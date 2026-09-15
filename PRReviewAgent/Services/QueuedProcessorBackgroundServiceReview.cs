using System.Threading;

namespace PRReviewAgent.Services
{
    /// <summary>
    /// Background service that processes work items from the <see cref="IBackgroundTaskQueue"/>.
    /// </summary>
    public class QueuedProcessorBackgroundServiceReview : QueuedProcessorBackgroundServiceBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="QueuedProcessorBackgroundServiceReview"/> class.
        /// </summary>
        /// <param name="taskQueue">The background task queue instance.</param>
        /// <param name="serviceProvider">The service provider instance.</param>
        /// <param name="loggerFactory">The logger factory instance.</param>
        public QueuedProcessorBackgroundServiceReview(
            [FromKeyedServices(Settings.TaskQueueReview)] IBackgroundTaskQueue taskQueue,
            IServiceProvider serviceProvider,
            ILoggerFactory loggerFactory)
            :base(taskQueue, serviceProvider, loggerFactory)
        {
        }
    }
}
