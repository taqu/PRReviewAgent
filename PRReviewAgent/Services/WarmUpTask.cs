namespace PRReviewAgent.Services
{
    /// <summary>
    /// Background service that performs warm-up tasks for the application.
    /// </summary>
    public class WarmUpTask : BackgroundService
    {
        /// <summary>
        /// Executes the warm-up process asynchronously.
        /// </summary>
        /// <param name="stoppingToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Perform application warm-up (e.g., pre-loading data or initializing components)
            // so that the first real request doesn't experience high latency.
            await Context.Instance.WarmUpAsync();
        }
    }
}

