namespace PRReviewAgent.Services
{
    public abstract class BackgroundService : IHostedService, IDisposable
    {
        private bool disposed_ = false;
        private Task? task_;
        private readonly CancellationTokenSource cancellationTokenSource_ = new CancellationTokenSource();

        protected abstract Task ExecuteAsync(CancellationToken stoppingToken);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

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

        public virtual Task StartAsync(CancellationToken cancellationToken)
        {
            // Store the task we're executing
            task_ = ExecuteAsync(cancellationTokenSource_.Token);

            // If the task is completed then return it,
            // this will bubble cancellation and failure to the caller
            if (task_.IsCompleted)
            {
                return task_;
            }

            // Otherwise it's running
            return Task.CompletedTask;
        }

        public virtual async Task StopAsync(CancellationToken cancellationToken)
        {
            // Stop called without start
            if (task_ == null)
            {
                return;
            }

            try
            {
                // Signal cancellation to the executing method
                cancellationTokenSource_.Cancel();
            }
            finally
            {
                // Wait until the task completes or the stop token triggers
                await Task.WhenAny(task_, Task.Delay(Timeout.Infinite, cancellationToken));
            }

        }
    }
}
