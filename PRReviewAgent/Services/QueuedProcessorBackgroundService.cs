namespace PRReviewAgent.Services
{
    /// <summary>
    /// Background service that processes work items from the <see cref="IBackgroundTaskQueue"/>.
    /// </summary>
    public class QueuedProcessorBackgroundService : BackgroundService
    {
        private readonly IBackgroundTaskQueue taskQueue_;
        private readonly IServiceProvider serviceProvider_;
        private readonly ILogger logger_;

        /// <summary>
        /// Initializes a new instance of the <see cref="QueuedProcessorBackgroundService"/> class.
        /// </summary>
        /// <param name="taskQueue">The background task queue instance.</param>
        /// <param name="serviceProvider">The service provider instance.</param>
        /// <param name="loggerFactory">The logger factory instance.</param>
        public QueuedProcessorBackgroundService(
            IBackgroundTaskQueue taskQueue,
            IServiceProvider serviceProvider,
            ILoggerFactory loggerFactory)
        {
            taskQueue_ = taskQueue;
            serviceProvider_ = serviceProvider;
            logger_ = loggerFactory.CreateLogger<QueuedProcessorBackgroundService>();
        }

        /// <summary>
        /// Executes the background processing loop.
        /// </summary>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            logger_.LogInformation("Queued Processor Background Service is starting.");

            // Loop until cancellation is requested.
            while (!cancellationToken.IsCancellationRequested)
            {
                // Wait for the next work item to be available in the queue.
                Func<IServiceProvider, CancellationToken, Task> workItem = await taskQueue_.DequeueAsync(cancellationToken);
                try
                {
                    // Execute the work item using the provided service provider and cancellation token.
                    await workItem(serviceProvider_, cancellationToken);
                }
                catch (Exception ex)
                {
                    // Log any errors that occur during the execution of a background task.
                    logger_.LogError(ex, $"Error occurred executing {nameof(workItem)}.");
                }
            }
            logger_.LogInformation("Queued Processor Background Service is stopping.");
        }
    }
}
