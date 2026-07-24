using System.Collections.Concurrent;

namespace PRReviewAgent.Services.AutoImprove
{
    public sealed class RuleLifecycleService
    {
        private readonly RuleRepository _repository;
        private readonly ConcurrentDictionary<string, List<(string RuleId, string? BadPattern)>> _pendingReviews = new();

        public RuleLifecycleService(RuleRepository repository)
        {
            _repository = repository;
        }

        public void TrackReviewedRules(string prKey, IEnumerable<LearnedRule> rules)
        {
            List<(string Id, string? BadPattern)> tracked = rules.Select(r => (r.Id, r.BadPattern)).ToList();
            _pendingReviews[prKey] = tracked;
        }

        public async Task OnPrMergedAsync(string prKey, string mergedDiff, CancellationToken cancellationToken = default)
        {
            if (!_pendingReviews.TryRemove(prKey, out List<(string RuleId, string? BadPattern)>? trackedRules)) return;

            foreach ((string ruleId, string badPattern) in trackedRules)
            {
                bool patternStillPresent = !string.IsNullOrEmpty(badPattern)
                    && mergedDiff.Contains(badPattern, StringComparison.OrdinalIgnoreCase);

                if (patternStillPresent)
                    await _repository.DecrementConfidenceAsync(ruleId, cancellationToken);
                else
                    await _repository.IncrementConfidenceAsync(ruleId, cancellationToken);
            }
        }
    }
}
