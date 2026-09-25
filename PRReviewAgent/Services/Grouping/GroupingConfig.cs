namespace PRReviewAgent.Services.Grouping
{
    public class GroupingConfig
    {
        public GroupingMode Mode { get; set; } = GroupingMode.Semantic;
        public int MaxFilesPerGroup { get; set; } = 8;
    }
}
