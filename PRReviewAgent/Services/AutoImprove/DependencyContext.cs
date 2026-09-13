namespace PRReviewAgent.Services.AutoImprove
{
    public sealed record DependencyContext
    {
        public required string Name { get; init; }
        public string Kind { get; init; } = string.Empty;
        public string? Change { get; init; }
    }
}
