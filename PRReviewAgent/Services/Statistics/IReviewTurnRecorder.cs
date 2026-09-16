namespace PRReviewAgent.Services.Statistics
{
    public interface IReviewTurnRecorder
    {
        Task<long> StartAsync(long reviewExecutionId, ReviewTurnType turnType, string model, DateTimeOffset startedAt, CancellationToken cancellationToken = default);
        Task CompleteSuccessAsync(long reviewTurnId, int? inputTokens, int? outputTokens, int findingCount, DateTimeOffset completedAt, long durationMs, CancellationToken cancellationToken = default);
        Task CompleteFailureAsync(long reviewTurnId, int? inputTokens, int? outputTokens, string errorType, DateTimeOffset completedAt, long durationMs, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ReviewTurn>> GetByReviewExecutionIdAsync(long reviewExecutionId, CancellationToken cancellationToken = default);
    }
}
