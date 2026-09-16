namespace PRReviewAgent.Services.Statistics
{
    public interface IRuleSearchExecutionRecorder
    {
        Task RecordAsync(RuleSearchExecution execution, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<RuleSearchExecution>> GetByReviewExecutionIdAsync(long reviewExecutionId, CancellationToken cancellationToken = default);
    }
}
