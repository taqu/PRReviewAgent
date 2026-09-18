namespace PRReviewAgent.Prompt
{
    public enum CandidateState
    {
        Discovered = 0,
        Filtered = 1,
        SkippedBudget = 2,
        NoContext = 3,
        VerificationFailed = 4,
        Rejected = 5,
        Verified = 6,
        Reported = 7,
    }
}
