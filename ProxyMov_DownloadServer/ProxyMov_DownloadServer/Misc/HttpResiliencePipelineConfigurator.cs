using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Timeout;
using System.Threading.RateLimiting;

namespace ProxyMov_DownloadServer.Misc
{
    public static class HttpResiliencePipelineConfigurator
    {
        public static void Configure(ResiliencePipelineBuilder<HttpResponseMessage> pipeline, ILoggerFactory loggerFactory, string clientName)
        {
            const int maxRetryAttempts = 3;
            ILogger logger = loggerFactory.CreateLogger($"HttpResilience.{clientName}");

            HttpRetryStrategyOptions retryOptions = new()
            {
                MaxRetryAttempts = maxRetryAttempts,
                Delay = TimeSpan.FromSeconds(10),
                UseJitter = true,
                OnRetry = args =>
                {
                    int retriesLeft = Math.Max(0, maxRetryAttempts - (args.AttemptNumber + 1));
                    string reason = args.Outcome.Exception?.Message
                        ?? (args.Outcome.Result is HttpResponseMessage response
                            ? $"HTTP {(int)response.StatusCode} {response.StatusCode}"
                            : "Unknown");

                    logger.LogWarning(
                        "HTTP retry for client {ClientName}. Attempt {Attempt}/{MaxAttempts}. Waiting {RetryDelayMs} ms before next try. Remaining retries: {RetriesLeft}. Attempt duration: {AttemptDurationMs} ms. Reason: {Reason}",
                        clientName,
                        args.AttemptNumber + 1,
                        maxRetryAttempts,
                        args.RetryDelay.TotalMilliseconds,
                        retriesLeft,
                        args.Duration.TotalMilliseconds,
                        reason);

                    return ValueTask.CompletedTask;
                }
            };
            retryOptions.DisableForUnsafeHttpMethods();

            HttpRateLimiterStrategyOptions rateLimiterOptions = new()
            {
                DefaultRateLimiterOptions = new ConcurrencyLimiterOptions
                {
                    PermitLimit = 1,
                    QueueLimit = 2,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                },
                OnRejected = args =>
                {
                    logger.LogWarning(
                        "HTTP request was rate-limited for client {ClientName}. The request exceeded the configured concurrency limit.",
                        clientName);

                    return ValueTask.CompletedTask;
                }
            };

            HttpCircuitBreakerStrategyOptions circuitBreakerOptions = new()
            {
                FailureRatio = 0.1,
                MinimumThroughput = 100,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(5),
                OnOpened = args =>
                {
                    logger.LogWarning(
                        "HTTP circuit breaker opened for client {ClientName}. Break duration: {BreakDurationMs} ms.",
                        clientName,
                        args.BreakDuration.TotalMilliseconds);

                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    logger.LogInformation(
                        "HTTP circuit breaker closed again for client {ClientName}.",
                        clientName);

                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = args =>
                {
                    logger.LogInformation(
                        "HTTP circuit breaker half-open for client {ClientName}. Next call will probe recovery.",
                        clientName);

                    return ValueTask.CompletedTask;
                }
            };

            pipeline
                .AddRateLimiter(rateLimiterOptions)
                .AddTimeout(new TimeoutStrategyOptions
                {
                    Timeout = TimeSpan.FromSeconds(90)
                })
                .AddRetry(retryOptions)
                .AddCircuitBreaker(circuitBreakerOptions)
                .AddTimeout(new TimeoutStrategyOptions
                {
                    Timeout = TimeSpan.FromSeconds(30)
                });
        }
    }
}
