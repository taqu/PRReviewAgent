using System.Text;

namespace PRReviewAgent.Services.AutoImprove
{
    public sealed class RuleRetrievalService
    {
        private readonly LocalEmbeddingProvider _embeddingProvider;
        private readonly RuleRepository _repository;

        public RuleRetrievalService(LocalEmbeddingProvider embeddingProvider, RuleRepository repository)
        {
            _embeddingProvider = embeddingProvider;
            _repository = repository;
        }

        public async Task<List<LearnedRule>> GetRelevantRulesAsync(
            string codeContext,
            float threshold = 0.75f,
            int topN = 5,
            CancellationToken cancellationToken = default)
        {
            List<float[]> queryEmbeddings = _embeddingProvider.GetEmbedding(codeContext);
            if (null == queryEmbeddings || queryEmbeddings.Count <= 0)
            {
                return new List<LearnedRule>();
            }
            List<LearnedRule> allRules = await _repository.GetAllActiveAsync(cancellationToken);

            List<(LearnedRule rule, float score)> scored = allRules.Select(r =>{
                float maxScore = queryEmbeddings
                    .Select(q => EmbeddingUtils.CosineSimilarity(q, r.Embedding))
                    .Max();

                return (rule: r, score: maxScore);
            })
                .Where(x => x.score >= threshold)
                .OrderByDescending(x => x.score)
                .ToList();
            if (!scored.Any()){
                return new List<LearnedRule>();
            }

            List<string> targetMergeRequestIds = scored
                .Select(x => x.rule.MergeRequestId)
                .Distinct()
                .Take(topN)
                .ToList();

            string dateTime = DateTime.UtcNow.ToString("O");
            foreach (string mergeRequestId in targetMergeRequestIds)
            {
                _ = _repository.UpdateLastHitByMergeRequestIdAsync(mergeRequestId, dateTime, cancellationToken);
            }
            List<LearnedRule> uniqueRules = scored.Where(x => targetMergeRequestIds.Contains(x.rule.MergeRequestId))
                .GroupBy(x => x.rule.MergeRequestId)
                .Select(g => g.First().rule)
                .ToList();
            return uniqueRules;
        }

        public static string FormatRulesForPrompt(IReadOnlyList<LearnedRule> rules)
        {
            if (rules.Count == 0) return string.Empty;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[IMPORTANT: PROJECT-SPECIFIC RULES]");
            sb.AppendLine("The following rules have been automatically learned from past modifications in this repository. Pay strict attention to these patterns:");
            foreach (LearnedRule rule in rules)
            {
                sb.AppendLine($"- Pattern: {rule.AstPattern}");
                sb.AppendLine($"  Rule: {rule.RuleDescription}");
                if (!string.IsNullOrEmpty(rule.BadPattern))
                    sb.AppendLine($"  Avoid: {rule.BadPattern}");
                if (!string.IsNullOrEmpty(rule.GoodPattern))
                    sb.AppendLine($"  Prefer: {rule.GoodPattern}");
            }
            return sb.ToString();
        }
    }
}
