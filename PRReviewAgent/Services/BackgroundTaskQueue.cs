
using System.Collections.Concurrent;

namespace PRReviewAgent.Services
{
    public class BackgroundTaskQueue : IBackgroundTaskQueue
    {
        private readonly SemaphoreSlim signal_ = new(0);
        private readonly ConcurrentQueue<Func<IServiceProvider, CancellationToken, Task>> workItems_ = new();

        public async Task<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
        {
            await signal_.WaitAsync(cancellationToken);
            workItems_.TryDequeue(out var workItem);
            return workItem!;
        }

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
