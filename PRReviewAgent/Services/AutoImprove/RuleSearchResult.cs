namespace PRReviewAgent.Services.AutoImprove
{
    public sealed class RuleSearchResult
    {
        /// <summary>
        /// All rules that survived the similarity threshold, ordered by score descending.
        /// Usage rows are created for each candidate.
        /// </summary>
        public List<(LearnedRule Rule, float Score)> Candidates { get; init; } = new();

        /// <summary>
        /// The top-N subset actually included in the review prompt.
        /// </summary>
        public List<LearnedRule> Selected { get; init; } = new();
    }
}
