using Finex.API.Observability;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Finex.API.Middlewares
{
    public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            Activity? activity = httpContext.Features.Get<IHttpActivityFeature>()?.Activity
                                 ?? Activity.Current;

            if (httpContext.Response.HasStarted)
            {
                logger.LogWarning(
                    exception,
                    "Exception occurred after response had already started for {Method} {Path}. Aborting connection gracefully.",
                    httpContext.Request.Method,
                    httpContext.Request.Path);

                return false;
            }

            if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
            {
                activity?.SetTag("http.client_closed", true);

                logger.LogInformation(
                    "Request was aborted by the client: {Method} {Path}",
                    httpContext.Request.Method,
                    httpContext.Request.Path);

                httpContext.Response.StatusCode = 499; 
                return true;
            }

            var (statusCode, title, detail, isServerFault) = exception switch
            {
                BadHttpRequestException badRequest => (
                    badRequest.StatusCode,
                    "Bad Request",
                    "The request payload or protocol framing is malformed.",
                    false),

                TimeoutException => (
                    StatusCodes.Status504GatewayTimeout,
                    "Gateway Timeout",
                    "The upstream operation timed out before completing.",
                    true),

                _ => (
                    StatusCodes.Status500InternalServerError,
                    "Internal Server Error",
                    "An unexpected error occurred. Please reference the traceId when contacting support.",
                    true)
            };

            if (isServerFault)
            {
                FinexDiagnostics.UnhandledExceptionsCounter.Add(1);
                activity?.SetStatus(ActivityStatusCode.Error, title);
                activity?.SetTag("exception.type", exception.GetType().FullName);
                activity?.SetTag("exception.message", exception.Message);

                logger.LogError(
                    exception,
                    "Unhandled server exception ({StatusCode}) on {Method} {Path}",
                    statusCode,
                    httpContext.Request.Method,
                    httpContext.Request.Path);
            }
            else
            {
                logger.LogWarning(
                    exception,
                    "Client protocol error ({StatusCode}) on {Method} {Path}",
                    statusCode,
                    httpContext.Request.Method,
                    httpContext.Request.Path);
            }

            httpContext.Response.StatusCode = statusCode;

            return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails = new ProblemDetails
                {
                    Status = statusCode,
                    Title = title,
                    Detail = detail
                }
            });
        }
    }
}
