namespace PRReviewAgent.Services.Statistics
{
    public interface IReviewRuleUsageRepository
    {
        Task AddRangeAsync(IReadOnlyCollection<ReviewRuleUsage> usages, CancellationToken cancellationToken = default);

        Task MarkProducedCandidateAsync(
            long reviewExecutionId,
            long projectId,
            IReadOnlyCollection<string> ruleIds,
            CancellationToken cancellationToken = default);

        Task MarkProducedFinalFindingAsync(
            long reviewExecutionId,
            long projectId,
            IReadOnlyCollection<string> ruleIds,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ReviewRuleUsage>> GetByReviewExecutionIdAsync(
            long reviewExecutionId,
            CancellationToken cancellationToken = default);
    }
}
