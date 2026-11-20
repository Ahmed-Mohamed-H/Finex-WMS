using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Finex.Infrastructure.Background
{
    public abstract class ResilientBackgroundService(
    ILogger logger,
    TimeSpan? failureBackoff = null) : BackgroundService
    {

        private static readonly ActivitySource BackgroundActivitySource = new("Finex.WMS.API.BackgroundWorkers");
        private readonly TimeSpan _backoff = failureBackoff ?? TimeSpan.FromSeconds(5);

        protected abstract string WorkerName { get; }



        protected abstract Task ExecuteIterationAsync(CancellationToken stoppingToken);

        protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("Background worker {WorkerName} started.", WorkerName);

            while (!stoppingToken.IsCancellationRequested)
            {

                using Activity? activity = BackgroundActivitySource.StartActivity(
                    $"{WorkerName}.Iteration",
                    ActivityKind.Internal);

                try
                {
                    await ExecuteIterationAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {

                    break;
                }
                catch (Exception ex)
                {

                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    activity?.SetTag("exception.type", ex.GetType().FullName);

                    logger.LogError(
                        ex,
                        "Unhandled error in background worker {WorkerName}. Retrying after {BackoffSeconds}s.",
                        WorkerName,
                        _backoff.TotalSeconds);

                    try
                    {
                        await Task.Delay(_backoff, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }

            logger.LogInformation("Background worker {WorkerName} stopped gracefully.", WorkerName);
        }
    }
}
