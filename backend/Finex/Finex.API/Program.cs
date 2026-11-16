using Finex.API.Middlewares;
using Finex.API.Security;
using Finex.Application.Interfaces;
using Finex.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Finex WMS API host...");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

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

    var app = builder.Build();

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
    app.UseHttpsRedirection();
    app.UseAuthorization();

    app.UseWhen(
        context => context.GetEndpoint()?.Metadata.GetMetadata<RequireHmacSignatureAttribute>() is not null,
        branch => branch.UseMiddleware<ApiSecurityMiddleware>());

    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Finex WMS API terminated unexpectedly during startup.");
}
finally
{
    Log.CloseAndFlush();
}