using Microsoft.AspNetCore.Http.Features;
using System.Diagnostics;

namespace Finex.API.Infrastructure.Observability;

public static class ProblemDetailsExtensions
{
    public static IServiceCollection AddUnifiedProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                HttpContext httpContext = context.HttpContext;
                Activity? activity = httpContext.Features.Get<IHttpActivityFeature>()?.Activity
                                     ?? Activity.Current;

                context.ProblemDetails.Extensions["traceId"] =
                    activity?.TraceId.ToString() ?? httpContext.TraceIdentifier;

                context.ProblemDetails.Instance ??=
                    $"{httpContext.Request.Method} {httpContext.Request.Path}";

                IHostEnvironment env = httpContext.RequestServices.GetRequiredService<IHostEnvironment>();
                if (!env.IsDevelopment())
                {
                    context.ProblemDetails.Extensions.Remove("exception");
                    context.ProblemDetails.Extensions.Remove("stackTrace");
                }
            };
        });

        return services;
    }
}