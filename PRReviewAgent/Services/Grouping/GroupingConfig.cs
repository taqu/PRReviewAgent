namespace PRReviewAgent.Services.Grouping
{
    public class GroupingConfig
    {
        public GroupingMode Mode { get; set; } = GroupingMode.Semantic;

        /// <summary>Secondary hard limit on files per C/C++ group.</summary>
        public int MaxFilesPerGroup { get; set; } = 8;

        /// <summary>
        /// Primary token budget for the variable content (AstJson + ExpandedDiff) of a
        /// C/C++ review group.  Groups that exceed this are split deterministically,
        /// preserving the strongest semantic relationships first.
        /// 0 = no token-based splitting.
        /// </summary>
        public int MaxGroupTokens { get; set; } = 12_000;
    }
}
