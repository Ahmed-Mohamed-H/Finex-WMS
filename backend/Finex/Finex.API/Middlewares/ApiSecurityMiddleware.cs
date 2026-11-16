using Finex.Application.Interfaces;
using Finex.Infrastructure.Security;
using Microsoft.Extensions.Options;

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

            if (!HttpMethods.IsPost(context.Request.Method))
            {
                _logger.LogWarning(
                    "Security violation: Disallowed HTTP method {Method} on {Path} from {RemoteIp}.",
                    context.Request.Method, requestPath, remoteIp);

                context.Response.Headers.Allow = HttpMethods.Post;
                await RejectAsync(context, StatusCodes.Status405MethodNotAllowed, "Method not allowed.");
                return;
            }

            if (!TryGetSingleHeader(context.Request.Headers, HeaderTimestamp, out string timestampRaw) ||
                !TryGetSingleHeader(context.Request.Headers, HeaderSignature, out string signatureRaw) ||
                !TryGetSingleHeader(context.Request.Headers, HeaderIdempotencyKey, out string idempotencyKeyRaw))
            {
                _logger.LogWarning(
                    "Security violation: Missing or duplicate required security headers on {Path} from {RemoteIp}.",
                    requestPath, remoteIp);

                await RejectAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized.");
                return;
            }

            if (!Guid.TryParseExact(idempotencyKeyRaw, "D", out Guid idempotencyGuid))
            {
                _logger.LogWarning(
                    "Security violation: Malformed {Header} format on {Path} from {RemoteIp}.",
                    HeaderIdempotencyKey, requestPath, remoteIp);

                await RejectAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized.");
                return;
            }

            if (!long.TryParse(timestampRaw, out long unixTimestamp) ||
                unixTimestamp < MinValidUnixSeconds ||
                unixTimestamp > MaxValidUnixSeconds)
            {
                _logger.LogWarning(
                    "Security violation: Invalid {Header} value on {Path} from {RemoteIp}.",
                    HeaderTimestamp, requestPath, remoteIp);

                await RejectAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized.");
                return;
            }

            var requestTime = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
            double driftSeconds = Math.Abs((DateTimeOffset.UtcNow - requestTime).TotalSeconds);

            if (driftSeconds > options.AllowedClockSkewSeconds)
            {
                _logger.LogWarning(
                    "Security violation: Request timestamp outside allowed window (Drift: {DriftSeconds:F1}s) on {Path} from {RemoteIp}.",
                    driftSeconds, requestPath, remoteIp);

                await RejectAsync(context, StatusCodes.Status408RequestTimeout, "Request expired.");
                return;
            }

            if (context.Request.ContentLength.HasValue && context.Request.ContentLength.Value > options.MaxPayloadBytes)
            {
                _logger.LogWarning(
                    "Security violation: Payload size {ContentLength} exceeds limit {MaxBytes} on {Path} from {RemoteIp}.",
                    context.Request.ContentLength.Value, options.MaxPayloadBytes, requestPath, remoteIp);

                await RejectAsync(context, StatusCodes.Status413PayloadTooLarge, "Payload too large.");
                return;
            }

            context.Request.EnableBuffering(
                bufferThreshold: 32 * 1024,
                bufferLimit: options.MaxPayloadBytes);

            byte[] bodyBytes;
            try
            {
                bodyBytes = await ReadBoundedStreamAsync(
                    context.Request.Body,
                    options.MaxPayloadBytes,
                    context.RequestAborted);
            }
            catch (InvalidOperationException)
            {
                _logger.LogWarning(
                    "Security violation: Streamed payload exceeded max limit {MaxBytes} on {Path} from {RemoteIp}.",
                    options.MaxPayloadBytes, requestPath, remoteIp);

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

            string normalizedIdempotencyKey = idempotencyGuid.ToString("D");

            bool isSignatureValid = HmacSignatureUtility.TryVerifyDualKey(
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
                out string? matchedSecret);

            if (!isSignatureValid || matchedSecret is null)
            {
                _logger.LogWarning(
                    "Security violation: Invalid HMAC signature for IdempotencyKey {IdempotencyKey} on {Path} from {RemoteIp}.",
                    normalizedIdempotencyKey, requestPath, remoteIp);

                await RejectAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized.");
                return;
            }

            bool isNonceClaimed = await _replayStore.TryConsumeNonceAsync(
                nonceKey: normalizedIdempotencyKey,
                ttl: TimeSpan.FromSeconds(options.AllowedClockSkewSeconds * 2),
                cancellationToken: context.RequestAborted);

            if (!isNonceClaimed)
            {
                _logger.LogWarning(
                    "Security violation: Replay or concurrent duplicate blocked for IdempotencyKey {IdempotencyKey} on {Path} from {RemoteIp}.",
                    normalizedIdempotencyKey, requestPath, remoteIp);

                await RejectAsync(context, StatusCodes.Status401Unauthorized, "Unauthorized.");
                return;
            }

            Stream originalBodyStream = context.Response.Body;
            using var responseBuffer = new MemoryStream();
            context.Response.Body = responseBuffer;

            try
            {
                await _next(context);

                responseBuffer.Position = 0;
                byte[] responseBytes = responseBuffer.ToArray();

                byte[] responseHmac = HmacSignatureUtility.ComputeResponseHmacBytes(
                    timestamp: timestampRaw,
                    idempotencyKey: normalizedIdempotencyKey,
                    statusCode: context.Response.StatusCode,
                    responseBodyBytes: responseBytes,
                    secret: matchedSecret);

                context.Response.Headers[HeaderResponseSignature] = Convert.ToHexString(responseHmac).ToLowerInvariant();

                if (responseBytes.Length > 0)
                {
                    await originalBodyStream.WriteAsync(responseBytes, context.RequestAborted);
                }
            }
            finally
            {
                context.Response.Body = originalBodyStream;
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
