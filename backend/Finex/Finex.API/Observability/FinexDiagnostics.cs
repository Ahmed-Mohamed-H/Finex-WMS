using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Finex.API.Observability
{
    public static class FinexDiagnostics
    {
        public const string ServiceName = "Finex.WMS.API";
        public const string BackgroundWorkersSourceName = "Finex.WMS.API.BackgroundWorkers";

        public static readonly string ServiceVersion =
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        public static readonly ActivitySource ActivitySource = new(ServiceName, ServiceVersion);

        public static readonly ActivitySource BackgroundActivitySource = new(BackgroundWorkersSourceName, ServiceVersion);

        public static readonly Meter Meter = new(ServiceName, ServiceVersion);

        public static readonly Counter<long> SecurityViolationsCounter = Meter.CreateCounter<long>(
            name: "finex.security.violations",
            unit: "{violation}",
            description: "Counts HMAC signature failures, replay attacks, and timestamp drifts.");

        public static readonly Counter<long> VerifiedRequestsCounter = Meter.CreateCounter<long>(
            name: "finex.security.verified_requests",
            unit: "{request}",
            description: "Counts successfully authenticated HMAC requests.");

        public static readonly Counter<long> UnhandledExceptionsCounter = Meter.CreateCounter<long>(
            name: "finex.exceptions.unhandled",
            unit: "{exception}",
            description: "Counts unhandled server exceptions caught by GlobalExceptionHandler.");
    }
}