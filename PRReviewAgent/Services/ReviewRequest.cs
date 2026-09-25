namespace PRReviewAgent.Services
{
    public class FileGroup
    {
        public string Topic { get; set; } = string.Empty;
        public List<ReviewContext> ReviewContexts => reviewContexts;
        public List<string> GroupingReasons => groupingReasons;
        private List<ReviewContext> reviewContexts = new List<ReviewContext>();
        private List<string> groupingReasons = new List<string>();
    }

    public sealed class ReviewRequest
    {
        public string MergeRequestTitle { get; set; } = string.Empty;
        public string MergeRequestDescription { get; set; } = string.Empty;
        public string? ReviewRulesTurn1 { get; set; } = string.Empty;
        public string? ReviewRulesTurn2 { get; set; } = string.Empty;
        public string? LearnedRules { get; set; }
        public List<FileGroup> FileGroups => files_;
        private List<FileGroup> files_ = new List<FileGroup>();
    }
}
