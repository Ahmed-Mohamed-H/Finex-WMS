using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Finex.API.Observability.Extensions
{
    public static class ObservabilityExtensions
    {
        public static WebApplicationBuilder AddFinexOpenTelemetry(this WebApplicationBuilder builder)
        {
            var seqIngestUrl = builder.Configuration["Serilog:WriteTo:1:Args:serverUrl"] ?? "http://localhost:5341";
            var seqApiKey = builder.Configuration["Serilog:WriteTo:1:Args:apiKey"];

            builder.Services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(
                        serviceName: FinexDiagnostics.ServiceName,
                        serviceVersion: FinexDiagnostics.ServiceVersion,
                        serviceInstanceId: Environment.MachineName)
                    .AddAttributes(new Dictionary<string, object>
                    {
                        ["deployment.environment"] = builder.Environment.EnvironmentName
                    }))

                .WithTracing(tracing =>
                {
                    tracing
                        .AddSource(FinexDiagnostics.ServiceName)
                        .AddAspNetCoreInstrumentation(options =>
                        {
                            options.RecordException = true;
                            options.Filter = httpContext =>
                            {
                                var path = httpContext.Request.Path.Value ?? string.Empty;
                                return !path.StartsWith("/scalar", StringComparison.OrdinalIgnoreCase) &&
                                       !path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase) &&
                                       !path.StartsWith("/health", StringComparison.OrdinalIgnoreCase);
                            };
                        })
                       .AddHttpClientInstrumentation(options =>
                       {
                           options.RecordException = true;

                           options.EnrichWithHttpRequestMessage = (activity, httpRequestMessage) =>
                           {
                               if (httpRequestMessage.RequestUri != null)
                               {
                                   activity.SetTag("http.request.uri", httpRequestMessage.RequestUri.ToString());
                                   activity.SetTag("http.target.host", httpRequestMessage.RequestUri.Host);
                               }
                           };
                       })
                        .AddSqlClientInstrumentation(options =>
                        {
                            options.RecordException = true;
                        })
                        .AddOtlpExporter(otlp =>
                        {
                            otlp.Endpoint = new Uri($"{seqIngestUrl.TrimEnd('/')}/ingest/otlp/v1/traces");
                            otlp.Protocol = OtlpExportProtocol.HttpProtobuf;
                            otlp.TimeoutMilliseconds = 1000;
                            if (!string.IsNullOrWhiteSpace(seqApiKey))
                            {
                                otlp.Headers = $"X-Seq-ApiKey={seqApiKey}";
                            }
                        });
                })

                .WithMetrics(metrics =>
                {
                    metrics
                        .AddMeter(FinexDiagnostics.ServiceName)
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddRuntimeInstrumentation()
                        .AddOtlpExporter(otlp =>
                        {
                            otlp.Endpoint = new Uri("http://localhost:4317");
                            otlp.Protocol = OtlpExportProtocol.Grpc;
                            otlp.TimeoutMilliseconds = 5000;
                            otlp.Headers = $"X-Seq-ApiKey={seqApiKey}";
                        });
                });

            return builder;
        }
    }
}