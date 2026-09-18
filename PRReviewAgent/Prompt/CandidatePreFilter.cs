using System.Text.RegularExpressions;

namespace PRReviewAgent.Prompt
{
    public static class CandidatePreFilter
    {
        public static (CandidateIssue[] Accepted, (CandidateIssue Candidate, string Reason)[] Rejected)
            Filter(CandidateIssue[] candidates)
        {
            List<CandidateIssue> accepted = new();
            List<(CandidateIssue, string)> rejected = new();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (CandidateIssue c in candidates)
            {
                string? reason = IsMalformed(c);
                if (reason != null)
                {
                    rejected.Add((c, reason));
                    continue;
                }

                string key = DeduplicationKey(c);
                if (!seen.Add(key))
                {
                    rejected.Add((c, "duplicate candidate"));
                    continue;
                }

                accepted.Add(c);
            }

            return (accepted.ToArray(), rejected.ToArray());
        }

        private static string? IsMalformed(CandidateIssue c)
        {
            if (string.IsNullOrWhiteSpace(c.location)) return "missing location";
            if (string.IsNullOrWhiteSpace(c.hypothesis)) return "empty hypothesis";
            if (string.IsNullOrWhiteSpace(c.trigger)) return "empty trigger";
            if (string.IsNullOrWhiteSpace(c.candidate_id)) return "missing candidate_id";
            return null;
        }

        private static string DeduplicationKey(CandidateIssue c)
        {
            string loc = (c.location ?? string.Empty).Trim();
            string cat = (c.category ?? string.Empty).Trim().ToLowerInvariant();
            string hyp = NormalizeText(c.hypothesis ?? string.Empty);
            return $"{loc}\0{cat}\0{hyp}";
        }

        private static string NormalizeText(string s) =>
            Regex.Replace(s.Trim().ToLowerInvariant(), @"\s+", " ");

        public static int GetCategoryPriority(string? category)
        {
            if (string.IsNullOrEmpty(category)) return 5;
            return category.Trim().ToLowerInvariant() switch
            {
                "memory safety" or "memory" or "correctness" or "lifetime" => 0,
                "concurrency" or "security" => 1,
                "api contract" or "api" or "resource management" or "resource" or "error handling" => 2,
                "performance" => 3,
                "maintainability" => 4,
                _ => 3,
            };
        }
    }
}
