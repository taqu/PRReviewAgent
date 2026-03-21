
using System.Collections.Concurrent;
using System.Security.Cryptography.Xml;

namespace PRReviewAgent.Services
{
    /// <summary>
    /// Implementation of a background task queue that allows for enqueuing and dequeuing work items.
    /// </summary>
    public class BackgroundTaskQueue : IBackgroundTaskQueue
    {
        private const int MaxRetryCount = 3;
        private readonly SemaphoreSlim signal_ = new(0);
        private readonly ConcurrentQueue<Func<IServiceProvider, CancellationToken, Task>> workItems_ = new();

        /// <summary>
        /// Dequeues a work item from the queue asynchronously.
        /// </summary>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous dequeue operation. The task result contains the work item.</returns>
        public async Task<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
        {
            // Attempt to retrieve the next work item from the concurrent queue.
            TimeSpan timeout = new TimeSpan(0, 0, 0, 0, 100);
            for(int i=0; i<MaxRetryCount; ++i) {
                bool success = await signal_.WaitAsync(timeout, cancellationToken);
                if (!success)
                {
                    return null;
                }
            }

            if(!workItems_.TryDequeue(out var workItem))
            {
                return null;
            }

            return workItem!;
        }

        /// <summary>
        /// Enqueues a background work item.
        /// </summary>
        /// <param name="workItem">The work item to enqueue.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="workItem"/> is null.</exception>
        public void QueueBackgroundWorkItem(Func<IServiceProvider, CancellationToken, Task> workItem)
        {
            if (workItem == null)
            {
                throw new ArgumentNullException(nameof(workItem));
            }

            workItems_.Enqueue(workItem);

            signal_.Release();
        }
    }
}
