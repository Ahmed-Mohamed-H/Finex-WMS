using Microsoft.Extensions.Primitives;
using Serilog;
using Serilog.Events;
using System.Net;
using System.Net.Sockets;

namespace Finex.API.Observability.Extensions;

public static class RequestLoggingExtensions
{
    public static WebApplication UseFinexRequestLogging(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate =
                "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";

            options.GetLevel = (httpContext, _, ex) =>
            {
                if (ex is not null || httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError)
                    return LogEventLevel.Error;

                if (httpContext.Response.StatusCode >= StatusCodes.Status400BadRequest)
                    return LogEventLevel.Warning;

                var path = httpContext.Request.Path.Value ?? string.Empty;
                if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/scalar", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase))
                {
                    return LogEventLevel.Verbose;
                }

                return LogEventLevel.Information;
            };

            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                if (httpContext.Response.StatusCode >= StatusCodes.Status400BadRequest)
                {
                    diagnosticContext.Set("RemoteIpClass", GetIpClass(httpContext.Connection.RemoteIpAddress));

                    bool hasIdempotencyKey = httpContext.Request.Headers.TryGetValue("X-Idempotency-Key", out var key) &&
                                             !StringValues.IsNullOrEmpty(key);

                    diagnosticContext.Set("IdempotencyKeyPresent", hasIdempotencyKey);
                }
            };
        });

        return app;
    }

    public static string GetIpClass(IPAddress? address)
    {
        if (address is null)
            return "unknown";

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            Span<byte> bytes = stackalloc byte[4];
            if (address.TryWriteBytes(bytes, out _))
                return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0/24";
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return "IPv6";

        return "other";
    }
}