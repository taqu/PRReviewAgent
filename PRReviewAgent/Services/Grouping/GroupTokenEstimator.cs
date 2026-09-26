namespace PRReviewAgent.Services.Grouping
{
    /// <summary>
    /// Approximates the number of LLM input tokens contributed by a review context.
    /// Uses the variable per-file content (AstJson + ExpandedDiff) that PromptBuilder
    /// places into the Turn 1 prompt.  Fixed overhead (instructions, MR metadata,
    /// learned rules) is shared across groups and is not counted here.
    /// </summary>
    public static class GroupTokenEstimator
    {
        // 1 token ≈ 4 characters — industry standard rough approximation for code.
        private const int CharsPerToken = 4;

        public static int EstimateContextTokens(ReviewContext ctx)
        {
            int chars = 0;
            if (!string.IsNullOrEmpty(ctx.AstJson))      chars += ctx.AstJson.Length;
            if (!string.IsNullOrEmpty(ctx.ExpandedDiff)) chars += ctx.ExpandedDiff.Length;
            // Fallback to raw diff when AST/expanded diff are absent.
            if (chars == 0 && !string.IsNullOrEmpty(ctx.Diff)) chars += ctx.Diff.Length;
            return Math.Max(1, chars / CharsPerToken);
        }

        public static int EstimateGroupTokens(IEnumerable<ReviewContext> contexts)
            => contexts.Sum(EstimateContextTokens);
    }
}
