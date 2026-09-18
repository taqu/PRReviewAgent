namespace PRReviewAgent.Services.Verification
{
    public sealed class VerificationExecutorMetrics
    {
        public int BatchCount { get; set; }
        public int BatchSuccessCount { get; set; }
        public int BatchFailureCount { get; set; }
        public int BatchRetryCount { get; set; }
        public int BatchSplitCount { get; set; }
        public int SingleCandidateFallbackCount { get; set; }
        public int TotalCandidates { get; set; }
        public int VerifiedCount { get; set; }
        public long WallClockMs { get; set; }
        public long TotalBatchDurationMs { get; set; }
        public int ConfiguredMaxConcurrency { get; set; }
    }
}
