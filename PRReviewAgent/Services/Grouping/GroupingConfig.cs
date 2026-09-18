namespace PRReviewAgent.Services.Grouping
{
    public sealed class GroupingConfig
    {
        public int MaxFilesPerGroup { get; init; } = 8;
        public GroupingMode Mode { get; init; } = GroupingMode.Semantic;
    }
}
