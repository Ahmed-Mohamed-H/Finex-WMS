using Finex.API.Observability;
using Finex.Application.Interfaces;
using Finex.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Serilog.Context;
using System.Diagnostics;
using System.Text;

namespace Finex.API.Middlewares
{
    public sealed class ApiSecurityMiddleware
    {
        private const string HeaderTimestamp = "X-Timestamp";
        private const string HeaderSignature = "X-Signature";
        private const string HeaderIdempotencyKey = "X-Idempotency-Key";
        private const string HeaderResponseSignature = "X-Response-Signature";

        private static readonly long MinValidUnixSeconds = DateTimeOffset.MinValue.ToUnixTimeSeconds();
        private static readonly long MaxValidUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

        private readonly RequestDelegate _next;
        private readonly IOptionsMonitor<ApiSecurityOptions> _optionsMonitor;
        private readonly IReplayProtectionStore _replayStore;
        private readonly ILogger<ApiSecurityMiddleware> _logger;

        public ApiSecurityMiddleware(
            RequestDelegate next,
            IOptionsMonitor<ApiSecurityOptions> optionsMonitor,
            IReplayProtectionStore replayStore,
            ILogger<ApiSecurityMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
            _replayStore = replayStore ?? throw new ArgumentNullException(nameof(replayStore));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var options = _optionsMonitor.CurrentValue;
            var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var requestPath = context.Request.Path.Value ?? "/";

            byte[] bodyBytes;
            Guid idempotencyGuid;
            string timestampRaw;
            string signatureRaw;

            // 1. الشريط الفرعي الأول: الفحوصات الأولية وقراءة الـ Body Stream
            using (var preCheckSpan = FinexDiagnostics.ActivitySource.StartActivity("Security.PreCheckAndReadBody"))
            {
                if (!HttpMethods.IsPost(context.Request.Method))
                {
                    RecordViolation("DisallowedMethod");
                    preCheckSpan?.SetStatus(ActivityStatusCode.Error, "DisallowedMethod");
                    _logger.LogWarning(
                        "Security violation [{SecurityEvent}]: Disallowed HTTP method {Method} on {Path} from {RemoteIp}.",
                        "DisallowedMethod", context.Request.Method, requestPath, remoteIp);

                    context.Response.Headers.Allow = HttpMethods.Post;
                    await RejectAsync(context, StatusCodes.Status405MethodNotAllowed, "Method not allowed.");
                    return;
                }

                if (!TryGetSingleHeader(context.Request.Headers, HeaderTimestamp, out timestampRaw) ||
                    !TryGetSingleHeader(context.Request.Headers, HeaderSignature, out signatureRaw) ||
                    !TryGetSingleHeader(context.Request.Headers, HeaderIdempotencyKey, out string idempotencyKeyRaw))
                {
                    RecordViolation("MissingHeaders");
                    preCheckSpan?.SetStatus(ActivityStatusCode.Error, "MissingHeaders");
                    _logger.LogWarning(
                        "Security violation [{SecurityEvent}]: Missing or duplicate required headers on {Path} from {RemoteIp}.",
                        "MissingHeaders", requestPath, remoteIp);

                    await RejectAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized.");
                    return;
                }

                if (!Guid.TryParseExact(idempotencyKeyRaw, "D", out idempotencyGuid))
                {
                    RecordViolation("MalformedIdempotencyKey");
                    preCheckSpan?.SetStatus(ActivityStatusCode.Error, "MalformedIdempotencyKey");
                    _logger.LogWarning(
                        "Security violation [{SecurityEvent}]: Malformed {Header} format on {Path} from {RemoteIp}.",
                        "MalformedIdempotencyKey", HeaderIdempotencyKey, requestPath, remoteIp);

                    await RejectAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized.");
                    return;
                }

                if (!long.TryParse(timestampRaw, out long unixTimestamp) ||
                    unixTimestamp < MinValidUnixSeconds ||
                    unixTimestamp > MaxValidUnixSeconds)
                {
                    RecordViolation("InvalidTimestamp");
                    preCheckSpan?.SetStatus(ActivityStatusCode.Error, "InvalidTimestamp");
                    _logger.LogWarning(
                        "Security violation [{SecurityEvent}]: Invalid {Header} value on {Path} from {RemoteIp}.",
                        "InvalidTimestamp", HeaderTimestamp, requestPath, remoteIp);

                    await RejectAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized.");
                    return;
                }

                var requestTime = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
                double driftSeconds = Math.Abs((DateTimeOffset.UtcNow - requestTime).TotalSeconds);
                preCheckSpan?.SetTag("security.clock_drift_sec", Math.Round(driftSeconds, 3));

                if (driftSeconds > options.AllowedClockSkewSeconds)
                {
                    RecordViolation("ClockSkewExceeded");
                    preCheckSpan?.SetStatus(ActivityStatusCode.Error, "ClockSkewExceeded");
                    _logger.LogWarning(
                        "Security violation [{SecurityEvent}]: Timestamp outside allowed window (Drift: {DriftSeconds:F1}s) on {Path} from {RemoteIp}.",
                        "ClockSkewExceeded", driftSeconds, requestPath, remoteIp);

                    await RejectAsync(context, StatusCodes.Status408RequestTimeout, "Request expired.");
                    return;
                }

                if (context.Request.ContentLength.HasValue && context.Request.ContentLength.Value > options.MaxPayloadBytes)
                {
                    RecordViolation("PayloadTooLarge");
                    preCheckSpan?.SetStatus(ActivityStatusCode.Error, "PayloadTooLarge");
                    _logger.LogWarning(
                        "Security violation [{SecurityEvent}]: Payload size {ContentLength} exceeds limit {MaxBytes} on {Path} from {RemoteIp}.",
                        "PayloadTooLarge", context.Request.ContentLength.Value, options.MaxPayloadBytes, requestPath, remoteIp);

                    await RejectAsync(context, StatusCodes.Status413PayloadTooLarge, "Payload too large.");
                    return;
                }

                preCheckSpan?.AddEvent(new ActivityEvent("HeadersAndTimestampValidated"));

                context.Request.EnableBuffering(
                    bufferThreshold: 32 * 1024,
                    bufferLimit: options.MaxPayloadBytes);

                try
                {
                    bodyBytes = await ReadBoundedStreamAsync(
                        context.Request.Body,
                        options.MaxPayloadBytes,
                        context.RequestAborted);

                    preCheckSpan?.SetTag("http.request.body.size", bodyBytes.Length);
                    preCheckSpan?.AddEvent(new ActivityEvent("RequestBodyBuffered"));
                }
                catch (InvalidOperationException)
                {
                    RecordViolation("StreamPayloadTooLarge");
                    preCheckSpan?.SetStatus(ActivityStatusCode.Error, "StreamPayloadTooLarge");
                    _logger.LogWarning(
                        "Security violation [{SecurityEvent}]: Streamed payload exceeded max limit {MaxBytes} on {Path} from {RemoteIp}.",
                        "StreamPayloadTooLarge", options.MaxPayloadBytes, requestPath, remoteIp);

                    await RejectAsync(context, StatusCodes.Status413PayloadTooLarge, "Payload too large.");
                    return;
                }
                finally
                {
                    if (context.Request.Body.CanSeek)
                    {
                        context.Request.Body.Position = 0;
                    }
                }
            }

            string normalizedIdempotencyKey = idempotencyGuid.ToString("D");
            var currentActivity = Activity.Current;
            currentActivity?.SetTag("finex.idempotency_key", normalizedIdempotencyKey);
            currentActivity?.SetTag("finex.client_ip", remoteIp);

            using (LogContext.PushProperty("IdempotencyKey", normalizedIdempotencyKey))
            using (LogContext.PushProperty("RemoteIp", remoteIp))
            {
                bool isUsingPreviousSecret;
                string matchedSecret;

                // 2. الشريط الأب لبوابة الأمان: وبداخله شريطان فرعيان للتمييز بين التشفير والكاش
                using (var securitySpan = FinexDiagnostics.ActivitySource.StartActivity("Security.VerifyHmacAndReplay"))
                {
                    bool isSignatureValid;
                    string? validSecret;

                    // 2.أ شريط فرعي لحساب ومقارنة HMAC-SHA256
                    using (var hmacSpan = FinexDiagnostics.ActivitySource.StartActivity("Security.VerifyHmacSignature"))
                    {
                        isSignatureValid = HmacSignatureUtility.TryVerifyDualKey(
                            providedHexSignature: signatureRaw,
                            expectedHashFactory: secret => HmacSignatureUtility.ComputeCanonicalHmacBytes(
                                method: HttpMethods.Post,
                                path: requestPath,
                                timestamp: timestampRaw,
                                idempotencyKey: normalizedIdempotencyKey,
                                bodyBytes: bodyBytes,
                                secret: secret),
                            currentSecret: options.CurrentSecret,
                            previousSecret: options.PreviousSecret,
                            out validSecret);

                        if (!isSignatureValid || validSecret is null)
                        {
                            hmacSpan?.SetStatus(ActivityStatusCode.Error, "Invalid HMAC signature");
                            securitySpan?.SetStatus(ActivityStatusCode.Error, "Invalid HMAC signature");
                            RecordViolation("InvalidSignature");

                            _logger.LogWarning(
                                "Security violation [{SecurityEvent}]: Invalid HMAC signature for {IdempotencyKey} on {Path} from {RemoteIp}.",
                                "InvalidSignature", normalizedIdempotencyKey, requestPath, remoteIp);

                            await RejectAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized.");
                            return;
                        }

                        matchedSecret = validSecret;
                        isUsingPreviousSecret = matchedSecret == options.PreviousSecret;
                        hmacSpan?.SetTag("finex.key_generation", isUsingPreviousSecret ? "Previous" : "Current");
                    }

                    securitySpan?.SetTag("finex.key_generation", isUsingPreviousSecret ? "Previous" : "Current");

                    if (isUsingPreviousSecret)
                    {
                        _logger.LogWarning(
                            "Security notice [{SecurityEvent}]: Request {IdempotencyKey} authenticated using rotated PreviousSecret on {Path} from {RemoteIp}.",
                            "RotatedKeyUsed", normalizedIdempotencyKey, requestPath, remoteIp);
                    }

                    // 2.ب شريط فرعي لفحص FusionCache / Redis لمنع التكرار
                    bool isNonceClaimed;
                    using (var replaySpan = FinexDiagnostics.ActivitySource.StartActivity("Security.CheckReplayNonce"))
                    {
                        replaySpan?.SetTag("cache.key_prefix", "hmac_nonce");

                        isNonceClaimed = await _replayStore.TryConsumeNonceAsync(
                            nonceKey: normalizedIdempotencyKey,
                            ttl: TimeSpan.FromSeconds(options.AllowedClockSkewSeconds * 2),
                            cancellationToken: context.RequestAborted);

                        replaySpan?.SetTag("security.nonce_claimed", isNonceClaimed);

                        if (!isNonceClaimed)
                        {
                            replaySpan?.SetStatus(ActivityStatusCode.Error, "Replay blocked");
                            securitySpan?.SetStatus(ActivityStatusCode.Error, "Replay blocked");
                            RecordViolation("ReplayBlocked");

                            _logger.LogWarning(
                                "Security violation [{SecurityEvent}]: Replay or concurrent duplicate blocked for {IdempotencyKey} on {Path} from {RemoteIp}.",
                                "ReplayBlocked", normalizedIdempotencyKey, requestPath, remoteIp);

                            await RejectAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized.");
                            return;
                        }
                    }

                    FinexDiagnostics.VerifiedRequestsCounter.Add(1, new KeyValuePair<string, object?>("key_generation", isUsingPreviousSecret ? "Previous" : "Current"));
                }

                // 3. تنفيذ الـ Controller وتوقيع الاستجابة
                Stream originalBodyStream = context.Response.Body;
                using var responseBuffer = new MemoryStream();
                context.Response.Body = responseBuffer;

                try
                {
                    await _next(context);

                    // 4. الشريط الفرعي الأخير: حساب بصمة الرد وإرساله
                    using (var signResponseSpan = FinexDiagnostics.ActivitySource.StartActivity("Security.SignAndFlushResponse"))
                    {
                        responseBuffer.Position = 0;
                        byte[] responseBytes = responseBuffer.ToArray();
                        signResponseSpan?.SetTag("http.response.body.size", responseBytes.Length);

                        byte[] responseHmac = HmacSignatureUtility.ComputeResponseHmacBytes(
                            timestamp: timestampRaw,
                            idempotencyKey: normalizedIdempotencyKey,
                            statusCode: context.Response.StatusCode,
                            responseBodyBytes: responseBytes,
                            secret: matchedSecret);

                        context.Response.Headers[HeaderResponseSignature] = Convert.ToHexString(responseHmac).ToLowerInvariant();
                        signResponseSpan?.AddEvent(new ActivityEvent("ResponseSignatureAttached"));

                        _logger.LogInformation(
                            "HMAC request verified [{SecurityEvent}] for {IdempotencyKey} using {KeyGeneration} secret; signed response dispatched with {StatusCode}.",
                            "RequestVerified",
                            normalizedIdempotencyKey,
                            isUsingPreviousSecret ? "Previous" : "Current",
                            context.Response.StatusCode);

                        if (responseBytes.Length > 0)
                        {
                            await originalBodyStream.WriteAsync(responseBytes, context.RequestAborted);
                        }
                    }
                }
                finally
                {
                    context.Response.Body = originalBodyStream;
                }
            }
        }

        private static bool TryGetSingleHeader(IHeaderDictionary headers, string key, out string value)
        {
            value = string.Empty;
            if (!headers.TryGetValue(key, out var values) || values.Count != 1)
            {
                return false;
            }

            string? single = values[0];
            if (string.IsNullOrWhiteSpace(single))
            {
                return false;
            }

            value = single.Trim();
            return true;
        }

        private static async Task<byte[]> ReadBoundedStreamAsync(
            Stream stream,
            int maxBytes,
            CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[16 * 1024];
            int totalRead = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                totalRead += bytesRead;
                if (totalRead > maxBytes)
                {
                    throw new InvalidOperationException("Request body exceeded maximum allowed byte limit.");
                }

                buffer.Write(chunk, 0, bytesRead);
            }

            return buffer.ToArray();
        }

        private static void RecordViolation(string reason)
        {
            FinexDiagnostics.SecurityViolationsCounter.Add(
                1,
                new KeyValuePair<string, object?>("reason", reason));
        }
        private static async Task RejectAsync(HttpContext context, int statusCode, string safePublicMessage)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/problem+json";

            var payload = $$"""
            {
              "status": {{statusCode}},
              "title": "{{safePublicMessage}}"
            }
            """;

            await context.Response.WriteAsync(payload, context.RequestAborted);
        }
    }
}