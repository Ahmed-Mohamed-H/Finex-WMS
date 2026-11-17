using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Finex.API.Observability
{
    public static class FinexDiagnostics
    {
        public const string ServiceName = "Finex.WMS.API";
        public static readonly string ServiceVersion =
       Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        public static readonly ActivitySource ActivitySource = new(ServiceName, ServiceVersion);

        public static readonly Meter Meter = new(ServiceName, ServiceVersion);

        public static readonly Counter<long> SecurityViolationsCounter = Meter.CreateCounter<long>(
            name: "finex.security.violations",
            unit: "{violation}",
            description: "Counts HMAC signature failures, replay attacks, and timestamp drifts.");

        public static readonly Counter<long> VerifiedRequestsCounter = Meter.CreateCounter<long>(
            name: "finex.security.verified_requests",
            unit: "{request}",
            description: "Counts successfully authenticated HMAC requests.");
    }
}
