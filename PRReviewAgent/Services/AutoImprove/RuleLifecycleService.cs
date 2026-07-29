using System.Collections.Concurrent;
using System.Threading;

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

        public async Task TrackReviewedRulesAsync(string prKey, IEnumerable<LearnedRule> rules, CancellationToken cancellationToken = default)
        {
            List<(string Id, string? BadPattern)> tracked = rules.Select(r => (r.Id, r.BadPattern)).ToList();
            _pendingReviews[prKey] = tracked;
            foreach (var r in rules)
            {
                await _repository.InsertPendingReviewAsync(prKey, r.Id, r.BadPattern, cancellationToken);
            }
        }

        public async Task OnPrMergedAsync(string prKey, string mergedDiff, CancellationToken cancellationToken = default)
        {
            List<(string RuleId, string? BadPattern)> trackedRules = await _repository.GetPendingReviewsAsync(prKey, cancellationToken);
            if (trackedRules.Count<=0){
                return;
            }
            try
            {
                foreach ((string ruleId, string? badPattern) in trackedRules)
                {
                    bool patternStillPresent = !string.IsNullOrEmpty(badPattern) && mergedDiff.Contains(badPattern, StringComparison.OrdinalIgnoreCase);

                    if (patternStillPresent) {
                        await _repository.DecrementConfidenceByChunkIdAsync(ruleId, cancellationToken);
                    }
                    else {
                        await _repository.IncrementConfidenceByChunkIdAsync(ruleId, cancellationToken);
                    }
                }
            }
            finally
            {
                await _repository.DeletePendingReviewsAsync(prKey, cancellationToken);
            }
        }
    }
}
