namespace PRReviewAgent.Services.Statistics
{
    public interface IReviewExecutionRecorder
    {
        Task<long> StartAsync(long projectId, string mergeRequestId, DateTimeOffset startedAt, CancellationToken cancellationToken = default);
        Task CompleteSuccessAsync(long reviewExecutionId, ReviewExecutionResult result, CancellationToken cancellationToken = default);
        Task CompleteFailureAsync(long reviewExecutionId, string errorType, DateTimeOffset completedAt, long durationMs, CancellationToken cancellationToken = default);
    }
}
