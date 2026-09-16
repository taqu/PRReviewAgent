namespace PRReviewAgent.Services.AutoImprove
{
    public sealed class Project
    {
        public long Id { get; init; }
        public string ExternalProjectId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? DisplayName { get; init; }
        public string? RepositoryUrl { get; init; }
        public bool IsActive { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
    }
}
