namespace PRReviewAgent.Services.AutoImprove
{
    public interface IRuleLearningEventRepository
    {
        Task<IReadOnlyList<RuleLearningEvent>> GetByRuleAsync(long projectId, string ruleId, CancellationToken cancellationToken = default);
    }
}
