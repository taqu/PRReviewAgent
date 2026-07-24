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
            float[] queryEmbedding = await _embeddingProvider.GetEmbeddingAsync(codeContext, cancellationToken);
            List<LearnedRule> allRules = await _repository.GetAllActiveAsync(cancellationToken);

            List<(LearnedRule rule, float score)> scored = allRules
                .Select(r => (rule: r, score: EmbeddingUtils.CosineSimilarity(queryEmbedding, r.Embedding)))
                .Where(x => x.score >= threshold)
                .OrderByDescending(x => x.score)
                .Take(topN)
                .ToList();

            foreach ((LearnedRule rule, float _) in scored)
            {
                _ = _repository.UpdateLastHitAsync(rule.Id, cancellationToken);
            }

            return scored.Select(x => x.rule).ToList();
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
