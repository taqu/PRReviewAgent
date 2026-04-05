namespace PRReviewAgent.Services
{
    /// <summary>
    /// Represents a queue for background tasks.
    /// Provides a mechanism to decouple task submission from task execution.
    /// </summary>
    public interface IBackgroundTaskQueue
    {
        /// <summary>
        /// Queues a background work item.
        /// </summary>
        /// <param name="workItem">The work item to queue.</param>
        Task QueueBackgroundWorkItemAsync(Func<IServiceProvider, CancellationToken, Task> workItem);

        /// <summary>
        /// Dequeues a background work item asynchronously.
        /// </summary>
        /// <returns>A task that represents the asynchronous dequeue operation, containing the work item.</returns>
        Task<Func<IServiceProvider, CancellationToken, Task>>? DequeueAsync();
    }
}
