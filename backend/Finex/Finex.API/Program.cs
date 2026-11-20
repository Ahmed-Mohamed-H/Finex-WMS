using Finex.API.Infrastructure.Observability;
using Finex.API.Middlewares;
using Finex.API.Observability.Extensions;
using Finex.API.Security;
using Finex.API.Security.Extensions;
using Finex.Application.Interfaces;
using Finex.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Enrichers.Span;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithSpan()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Finex WMS API host...");

    var builder = WebApplication.CreateBuilder(args);

    builder.HardenFinexServerHost();

    builder.Host.UseSerilog((context, services, configuration) =>
    {
        var seqUrl = context.Configuration["Seq:ServerUrl"] ?? "http://localhost:5341";
        var seqApiKey = context.Configuration["Seq:ApiKey"];

        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithSpan()
            .WriteTo.Seq(seqUrl, apiKey: seqApiKey);
    });

    builder.AddFinexOpenTelemetry();

    builder.Services.AddUnifiedProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    builder.Services
        .AddOptions<ApiSecurityOptions>()
        .Bind(builder.Configuration.GetSection(ApiSecurityOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    builder.Services.AddSingleton<IValidateOptions<ApiSecurityOptions>, ApiSecurityOptionsValidator>();

    builder.Services.AddFusionCache();
    builder.Services.AddSingleton<IReplayProtectionStore, FusionCacheReplayProtectionStore>();

    builder.Services.AddControllers();
    builder.Services.AddOpenApi();

    builder.Services.AddHttpClient(Options.DefaultName, client =>
    {
        client.Timeout = TimeSpan.FromSeconds(3);
    });

    var app = builder.Build();

    app.UseFinexSecurityHeaders(allowCrossSiteFrontend: false);
    app.UseExceptionHandler();
    app.UseHttpsRedirection();
    app.UseStatusCodePages();

    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("RemoteIp", httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
            if (httpContext.Request.Headers.TryGetValue("X-Idempotency-Key", out var key))
            {
                diagnosticContext.Set("IdempotencyKey", key.ToString());
            }
        };
    });

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference();
    }

    app.UseRouting();
    app.UseAuthorization();

    app.UseWhen(
        context => context.GetEndpoint()?.Metadata.GetMetadata<RequireHmacSignatureAttribute>() is not null,
        branch => branch.UseMiddleware<ApiSecurityMiddleware>());

    app.MapControllers();

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Finex WMS API terminated unexpectedly during startup.");
}
finally
{
    await Log.CloseAndFlushAsync();
}