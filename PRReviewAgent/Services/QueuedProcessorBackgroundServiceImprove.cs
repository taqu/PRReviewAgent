using System.Threading;

namespace PRReviewAgent.Services
{
    /// <summary>
    /// Background service that processes work items from the <see cref="IBackgroundTaskQueue"/>.
    /// </summary>
    public class QueuedProcessorBackgroundServiceImprove : QueuedProcessorBackgroundServiceBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="QueuedProcessorBackgroundServiceImprove"/> class.
        /// </summary>
        /// <param name="taskQueue">The background task queue instance.</param>
        /// <param name="serviceProvider">The service provider instance.</param>
        /// <param name="loggerFactory">The logger factory instance.</param>
        public QueuedProcessorBackgroundServiceImprove(
            [FromKeyedServices(Settings.TaskQueueImprove)] IBackgroundTaskQueue taskQueue,
            IServiceProvider serviceProvider,
            ILoggerFactory loggerFactory)
            :base(taskQueue, serviceProvider, loggerFactory)
        {
        }
    }
}
