using Microsoft.Net.Http.Headers;

namespace Finex.API.Security.Extensions;

public static class SecurityHeadersExtensions
{
    private static readonly PathString ScalarPath = new("/scalar");
    private static readonly PathString OpenApiPath = new("/openapi");

    private const int TwoYearsInSeconds = 63_072_000;

    public static WebApplicationBuilder HardenFinexServerHost(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WebHost.ConfigureKestrel(static options =>
        {
            options.AddServerHeader = false;
        });

        return builder;
    }

    public static IApplicationBuilder UseFinexSecurityHeaders(
        this WebApplication app,
        bool allowCrossSiteFrontend = false)
    {
        ArgumentNullException.ThrowIfNull(app);

        bool isDevelopment = app.Environment.IsDevelopment();

        HeaderPolicyCollection strictApiPolicy = BuildApiSecurityPolicy(
            isDevelopment: isDevelopment,
            allowCrossSiteFrontend: allowCrossSiteFrontend,
            isDocsEndpoint: false);

        app.Use(static (context, next) =>
        {
            context.Response.OnStarting(static state =>
            {
                var response = (HttpResponse)state;
                IHeaderDictionary headers = response.Headers;

                if (!headers.ContainsKey(HeaderNames.CacheControl))
                {
                    headers[HeaderNames.CacheControl] = "no-store, no-cache, must-revalidate";
                    headers[HeaderNames.Pragma] = "no-cache";
                }

                return Task.CompletedTask;
            }, context.Response);

            return next(context);
        });

        if (isDevelopment)
        {
            HeaderPolicyCollection docsDevPolicy = BuildApiSecurityPolicy(
                isDevelopment: true,
                allowCrossSiteFrontend: allowCrossSiteFrontend,
                isDocsEndpoint: true);

            app.UseWhen(
                static context => IsDocsRequest(context.Request.Path),
                branch => branch.UseSecurityHeaders(docsDevPolicy));

            app.UseWhen(
                static context => !IsDocsRequest(context.Request.Path),
                branch => branch.UseSecurityHeaders(strictApiPolicy));
        }
        else
        {
            app.UseSecurityHeaders(strictApiPolicy);
        }

        return app;
    }

    private static bool IsDocsRequest(PathString path) =>
        path.StartsWithSegments(ScalarPath, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments(OpenApiPath, StringComparison.OrdinalIgnoreCase);

    private static HeaderPolicyCollection BuildApiSecurityPolicy(
        bool isDevelopment,
        bool allowCrossSiteFrontend,
        bool isDocsEndpoint)
    {
        HeaderPolicyCollection policy = new HeaderPolicyCollection()
            .AddDefaultApiSecurityHeaders()
            .RemoveCustomHeader("X-Powered-By")
            .RemoveCustomHeader("X-AspNet-Version")
            .RemoveCustomHeader("X-AspNetMvc-Version");

        if (!isDevelopment)
        {
            policy.AddStrictTransportSecurityMaxAgeIncludeSubDomainsAndPreload(
                maxAgeInSeconds: TwoYearsInSeconds);
        }

        if (allowCrossSiteFrontend)
        {
            policy.AddCrossOriginResourcePolicy(static builder => builder.CrossOrigin());
            policy.AddCrossOriginEmbedderPolicy(static builder => builder.UnsafeNone());
        }
        else
        {
            policy.AddCrossOriginResourcePolicy(static builder => builder.SameSite());
        }

        if (isDevelopment && isDocsEndpoint)
        {
            policy.AddContentSecurityPolicy(static csp =>
            {
                csp.AddDefaultSrc().Self();
                csp.AddScriptSrc().Self().UnsafeInline();
                csp.AddStyleSrc().Self().UnsafeInline();
                csp.AddImgSrc().Self().From("data:").OverHttps();
                csp.AddFontSrc().Self().From("data:").OverHttps();
                csp.AddConnectSrc().Self();
                csp.AddWorkerSrc().Self().From("blob:");
                csp.AddObjectSrc().None();
                csp.AddFrameAncestors().None();
                csp.AddBaseUri().Self();
            });
        }

        return policy;
    }
}