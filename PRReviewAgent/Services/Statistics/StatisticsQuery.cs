namespace PRReviewAgent.Services.Statistics
{
    public sealed record StatisticsQuery(
        long? ProjectId,
        DateTimeOffset From,
        DateTimeOffset To);
}
