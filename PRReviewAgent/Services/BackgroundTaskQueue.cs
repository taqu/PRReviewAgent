
using PRReviewAgent.Controllers;
using System.Collections.Concurrent;
using System.Security.Cryptography.Xml;
using System.Threading.Channels;

namespace PRReviewAgent.Services
{
    /// <summary>
    /// Implementation of a background task queue that allows for enqueuing and dequeuing work items.
    /// </summary>
    public class BackgroundTaskQueue : IBackgroundTaskQueue
    {
        private const int TimeOutMilliSeconds = 3000;

        private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> workItems_;
        private ILogger<BackgroundTaskQueue> logger_;

        public BackgroundTaskQueue(ILogger<BackgroundTaskQueue> logger)
        {
            workItems_ = Channel.CreateUnbounded<Func<IServiceProvider, CancellationToken, Task>>();
            logger_ = logger;
        }

        /// <summary>
        /// Dequeues a work item from the queue asynchronously.
        /// </summary>
        /// <returns>A task that represents the asynchronous dequeue operation. The task result contains the work item.</returns>
        public async Task<Func<IServiceProvider, CancellationToken, Task>>? DequeueAsync()
        {
            // Attempt to retrieve the next work item from the concurrent queue.
            try
            {
                Func<IServiceProvider, CancellationToken, Task>? workItem = await workItems_.Reader.ReadAsync();
                return workItem;
            }
            catch (OperationCanceledException ex)
            {
                logger_.LogInformation("Failed to dequeue work item: " + ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                logger_.LogWarning("Failed to dequeue work item: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Enqueues a background work item.
        /// </summary>
        /// <param name="workItem">The work item to enqueue.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="workItem"/> is null.</exception>
        public async Task QueueBackgroundWorkItemAsync(Func<IServiceProvider, CancellationToken, Task> workItem)
        {
            ArgumentNullException.ThrowIfNull(workItem);
            await workItems_.Writer.WriteAsync(workItem);
        }
    }
}
