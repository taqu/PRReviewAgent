namespace PRReviewAgent.Services
{
    /// <summary>
    /// Base class for implementing long-running <see cref="IHostedService"/> tasks.
    /// </summary>
    public abstract class BackgroundService : IHostedService, IDisposable
    {
        private bool disposed_ = false;
        private Task? task_;
        private readonly CancellationTokenSource cancellationTokenSource_ = new CancellationTokenSource();

        /// <summary>
        /// This method is called when the <see cref="IHostedService"/> starts. The implementation should return a task that represents the lifetime of the long running operation(s) being performed.
        /// </summary>
        /// <param name="stoppingToken">Triggered when <see cref="IHostedService.StopAsync(CancellationToken)"/> is called.</param>
        /// <returns>A <see cref="Task"/> that represents the long running operations.</returns>
        protected abstract Task ExecuteAsync(CancellationToken stoppingToken);

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="BackgroundService"/> and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposed_)
            {
                return;
            }

            if (disposing)
            {
                cancellationTokenSource_.Cancel();
            }

            // Free unmanaged resources.
            disposed_ = true;
        }

        /// <summary>
        /// Triggered when the application host is ready to start the service.
        /// </summary>
        /// <param name="cancellationToken">Indicates that the start process has been aborted.</param>
        /// <returns>A <see cref="Task"/> that represents the asynchronous Start operation.</returns>
        public virtual Task StartAsync(CancellationToken cancellationToken)
        {
            // Begin executing the background task using our internal cancellation token source.
            task_ = ExecuteAsync(cancellationTokenSource_.Token);

            // If the task has already completed, return it so the caller can handle its completion state.
            // This ensures that any startup failures or immediate cancellations are propagated.
            if (task_.IsCompleted)
            {
                return task_;
            }

            // If the task is still running, return Task.CompletedTask to signal that start-up was successful.
            return Task.CompletedTask;
        }

        /// <summary>
        /// Triggered when the application host is performing a graceful shutdown.
        /// </summary>
        /// <param name="cancellationToken">Indicates that the shutdown process should no longer be graceful.</param>
        /// <returns>A <see cref="Task"/> that represents the asynchronous Stop operation.</returns>
        public virtual async Task StopAsync(CancellationToken cancellationToken)
        {
            // If the task was never started, there is nothing to stop.
            if (task_ == null)
            {
                return;
            }

            try
            {
                // Signal to the background task that it should stop by cancelling the internal token.
                cancellationTokenSource_.Cancel();
            }
            finally
            {
                // Wait for the background task to finish or for the shutdown period to timeout.
                // Task.WhenAny returns as soon as either the task completes or the shutdown token is triggered.
                await Task.WhenAny(task_, Task.Delay(Timeout.Infinite, cancellationToken));
            }

        }
    }
}
