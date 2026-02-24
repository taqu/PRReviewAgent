namespace PRReviewAgent.Services
{
    public class QueuedProcessorBackgroundService : BackgroundService
    {
        private readonly IBackgroundTaskQueue taskQueue_;
        private readonly IServiceProvider serviceProvider_;
        private readonly ILogger logger_;

        public QueuedProcessorBackgroundService(
            IBackgroundTaskQueue taskQueue,
            IServiceProvider serviceProvider,
            ILoggerFactory loggerFactory)
        {
            taskQueue_ = taskQueue;
            serviceProvider_ = serviceProvider;
            logger_ = loggerFactory.CreateLogger<QueuedProcessorBackgroundService>();
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            logger_.LogInformation("Queued Processor Background Service is starting.");
            while (!cancellationToken.IsCancellationRequested)
            {
                Func<IServiceProvider, CancellationToken, Task> workItem = await taskQueue_.DequeueAsync(cancellationToken);
                try
                {
                    await workItem(serviceProvider_, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger_.LogError(ex, $"Error occurred executing {nameof(workItem)}.");
                }
            }
            logger_.LogInformation("Queued Processor Background Service is stopping.");
        }
    }
}
