namespace PRReviewAgent.Services.Verification
{
    public sealed class ReviewWorkloadProfile
    {
        public int GroupCount { get; init; }
        public int CandidateCount { get; init; }
        public int EstimatedVerificationCharsTotal { get; init; }
        public int EstimatedVerificationCharsMax { get; init; }
        public int EstimatedVerificationCharsAverage { get; init; }
        public int LargeCandidateCount { get; init; }
        public int TruncatedContextCount { get; init; }
    }
}
