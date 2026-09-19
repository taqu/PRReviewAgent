namespace PRReviewAgent.Services.Verification
{
    public static class ReviewWorkloadProfileBuilder
    {
        public const int DefaultLargeContextThreshold = 16_000;

        public static ReviewWorkloadProfile Build(
            IReadOnlyList<VerificationBatchItem> items,
            int groupCount = 1,
            int largeContextThreshold = DefaultLargeContextThreshold)
        {
            if (items.Count == 0)
                return new ReviewWorkloadProfile { GroupCount = groupCount };

            int total = 0, max = 0;
            int truncated = 0, large = 0;
            foreach (var item in items)
            {
                int chars = item.EstimatedChars;
                total += chars;
                if (chars > max) max = chars;
                if (item.Context.Truncated) truncated++;
                if (chars > largeContextThreshold) large++;
            }
            int avg = total / items.Count;

            return new ReviewWorkloadProfile
            {
                GroupCount = groupCount,
                CandidateCount = items.Count,
                EstimatedVerificationCharsTotal = total,
                EstimatedVerificationCharsMax = max,
                EstimatedVerificationCharsAverage = avg,
                LargeCandidateCount = large,
                TruncatedContextCount = truncated,
            };
        }
    }
}
