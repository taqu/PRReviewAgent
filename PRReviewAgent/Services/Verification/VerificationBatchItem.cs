namespace PRReviewAgent.Services.Verification
{
    public sealed class VerificationBatchItem
    {
        public required PRReviewAgent.Prompt.CandidateIssue Candidate { get; init; }
        public required PRReviewAgent.Prompt.VerificationContext Context { get; init; }
        public int EstimatedChars => Context.Items.Sum(i => i.Source.Length);
    }
}
