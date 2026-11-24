using Finex.API.Observability;
using Finex.Application.Interfaces.Production;
using Finex.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Serilog.Context;
using System.Buffers;
using System.Diagnostics;

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
        private readonly IProblemDetailsService _problemDetailsService;
        private readonly ILogger<ApiSecurityMiddleware> _logger;

        public ApiSecurityMiddleware(
            RequestDelegate next,
            IOptionsMonitor<ApiSecurityOptions> optionsMonitor,
            IReplayProtectionStore replayStore,
            IProblemDetailsService problemDetailsService,
            ILogger<ApiSecurityMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
            _replayStore = replayStore ?? throw new ArgumentNullException(nameof(replayStore));
            _problemDetailsService = problemDetailsService ?? throw new ArgumentNullException(nameof(problemDetailsService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var options = _optionsMonitor.CurrentValue;
            var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var requestPath = context.Request.Path.Value ?? "/";
            var requestTarget = $"{requestPath}{context.Request.QueryString.Value}";

            byte[] bodyBytes;
            Guid idempotencyGuid;
            string timestampRaw;
            string signatureRaw;

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
                    int initialCapacity = context.Request.ContentLength is > 0 and <= int.MaxValue
                        ? (int)context.Request.ContentLength.Value
                        : 4096;

                    bodyBytes = await ReadBoundedStreamAsync(
                        context.Request.Body,
                        options.MaxPayloadBytes,
                        initialCapacity,
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

                using (var securitySpan = FinexDiagnostics.ActivitySource.StartActivity("Security.VerifyHmacAndReplay"))
                {
                    bool isSignatureValid;
                    string? validSecret;

                    using (var hmacSpan = FinexDiagnostics.ActivitySource.StartActivity("Security.VerifyHmacSignature"))
                    {
                        isSignatureValid = HmacSignatureUtility.TryVerifyDualKey(
                            providedHexSignature: signatureRaw,
                            expectedHashFactory: secret => HmacSignatureUtility.ComputeCanonicalHmacBytes(
                                method: HttpMethods.Post,
                                pathAndQuery: requestTarget,
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

                Stream originalBodyStream = context.Response.Body;
                using var responseBuffer = new BoundedMemoryStream(options.MaxResponseBytes);
                context.Response.Body = responseBuffer;

                try
                {
                    await _next(context);

                    using (var signResponseSpan = FinexDiagnostics.ActivitySource.StartActivity("Security.SignAndFlushResponse"))
                    {
                        responseBuffer.Position = 0;
                        responseBuffer.TryGetBuffer(out ArraySegment<byte> responseSegment);
                        signResponseSpan?.SetTag("http.response.body.size", responseSegment.Count);

                        byte[] responseHmac = HmacSignatureUtility.ComputeResponseHmacBytes(
                            timestamp: timestampRaw,
                            idempotencyKey: normalizedIdempotencyKey,
                            statusCode: context.Response.StatusCode,
                            responseBodyBytes: responseSegment.AsSpan(),
                            secret: matchedSecret);

                        context.Response.Headers[HeaderResponseSignature] = Convert.ToHexStringLower(responseHmac);
                        signResponseSpan?.AddEvent(new ActivityEvent("ResponseSignatureAttached"));

                        if (responseSegment.Count > 0)
                        {
                            await originalBodyStream.WriteAsync(responseSegment.AsMemory(), context.RequestAborted);
                        }
                    }
                }
                catch (ResponseSizeLimitExceededException)
                {
                    context.Response.Body = originalBodyStream;
                    context.Response.Headers.Remove("Content-Type");
                    context.Response.Headers.Remove("Content-Length");

                    _logger.LogError(
                        "Signed response on {Path} exceeded MaxResponseBytes ({MaxBytes}).",
                        requestPath,
                        options.MaxResponseBytes);

                    await RejectAsync(context, StatusCodes.Status500InternalServerError, "Response size limit exceeded.");
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
            int initialCapacity,
            CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream(Math.Min(initialCapacity, maxBytes));
            byte[] chunk = ArrayPool<byte>.Shared.Rent(16 * 1024);
            try
            {
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
            finally
            {
                ArrayPool<byte>.Shared.Return(chunk);
            }
        }

        private static void RecordViolation(string reason)
        {
            Activity.Current?.SetTag("security.violation", reason);
            FinexDiagnostics.SecurityViolationsCounter.Add(
                1,
                new KeyValuePair<string, object?>("reason", reason));
        }

        private async Task RejectAsync(HttpContext context, int statusCode, string safePublicMessage)
        {
            context.Response.StatusCode = statusCode;

            await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails
                {
                    Status = statusCode,
                    Title = safePublicMessage
                }
            });
        }

        private sealed class ResponseSizeLimitExceededException : Exception;

        private sealed class BoundedMemoryStream(int maxBytes) : MemoryStream
        {
            public override void Write(byte[] buffer, int offset, int count)
            {
                if (Length + count > maxBytes)
                {
                    throw new ResponseSizeLimitExceededException();
                }

                base.Write(buffer, offset, count);
            }

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                if (Length + buffer.Length > maxBytes)
                {
                    throw new ResponseSizeLimitExceededException();
                }

                base.Write(buffer);
            }

            public override void WriteByte(byte value)
            {
                if (Length + 1 > maxBytes)
                {
                    throw new ResponseSizeLimitExceededException();
                }

                base.WriteByte(value);
            }

            public override void SetLength(long value)
            {
                if (value > maxBytes)
                {
                    throw new ResponseSizeLimitExceededException();
                }

                base.SetLength(value);
            }
        }
    }
}