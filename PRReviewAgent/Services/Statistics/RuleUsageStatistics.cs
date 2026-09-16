namespace PRReviewAgent.Services.Statistics
{
    public sealed class RuleUsageStatistics
    {
        public long ProjectId { get; init; }
        public string ProjectName { get; init; } = string.Empty;
        public string RuleId { get; init; } = string.Empty;
        public string? RuleDisplayText { get; init; }
        public int CandidateMatches { get; init; }
        public int PromptUses { get; init; }
        public int ProducedCandidateCount { get; init; }
        public int ProducedFinalFindingCount { get; init; }
        public double? CandidateHitRate { get; init; }
        public double? FinalHitRate { get; init; }
    }
}
