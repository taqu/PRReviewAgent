namespace PRReviewAgent.Services.Statistics
{
    public interface IStatisticsService
    {
        Task<OverviewStatistics> GetOverviewAsync(StatisticsQuery query, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ReviewTrendPoint>> GetReviewTrendAsync(StatisticsQuery query, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<TokenTrendPoint>> GetTokenTrendAsync(StatisticsQuery query, CancellationToken cancellationToken = default);
        Task<ReviewStatistics> GetReviewStatisticsAsync(StatisticsQuery query, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ModelUsageStatistics>> GetModelUsageAsync(StatisticsQuery query, CancellationToken cancellationToken = default);
        Task<RuleStatistics> GetRuleStatisticsAsync(StatisticsQuery query, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<RuleUsageStatistics>> GetRuleUsageAsync(StatisticsQuery query, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<ProjectStatistics>> GetProjectStatisticsAsync(StatisticsQuery query, CancellationToken cancellationToken = default);
    }
}
