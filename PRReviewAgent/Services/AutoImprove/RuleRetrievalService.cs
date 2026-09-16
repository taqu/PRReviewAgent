using System.Diagnostics;
using System.Text;
using PRReviewAgent.Services.Statistics;

namespace PRReviewAgent.Services.AutoImprove
{
    public sealed class RuleRetrievalService
    {
        private readonly LocalEmbeddingProvider _embeddingProvider;
        private readonly RuleRepository _repository;
        private readonly ILogger<RuleRetrievalService> _logger;

        public RuleRetrievalService(LocalEmbeddingProvider embeddingProvider, RuleRepository repository, ILogger<RuleRetrievalService> logger)
        {
            _embeddingProvider = embeddingProvider;
            _repository = repository;
            _logger = logger;
        }

        public async Task<RuleSearchResult> GetRelevantRulesAsync(
            string codeContext,
            long projectId,
            float threshold = 0.75f,
            int topN = 5,
            long? reviewExecutionId = null,
            IRuleSearchExecutionRecorder? searchRecorder = null,
            CancellationToken cancellationToken = default)
        {
            DateTimeOffset searchStartedAt = DateTimeOffset.UtcNow;
            Stopwatch totalSw = Stopwatch.StartNew();

            // Stage 1: Generate query embeddings and measure embedding duration.
            Stopwatch embeddingSw = Stopwatch.StartNew();
            List<float[]> queryEmbeddings = _embeddingProvider.GetEmbedding(codeContext);
            embeddingSw.Stop();

            if (queryEmbeddings == null || queryEmbeddings.Count <= 0)
            {
                // No usable query embedding — no vector search was performed; skip telemetry.
                return new RuleSearchResult();
            }

            long embeddingDurationMs = embeddingSw.ElapsedMilliseconds;

            // Stage 2: Load project-local rules.
            List<LearnedRule> allRules = await _repository.GetAllActiveAsync(projectId, _logger, cancellationToken);
            int totalRuleCount = allRules.Count;
            _logger.LogDebug("Loaded {RuleCount} learned rules for project {ProjectId}", totalRuleCount, projectId);

            // Stage 3: Vector similarity search — cosine comparison, threshold, ranking, top-k selection.
            Stopwatch searchSw = Stopwatch.StartNew();
            List<(LearnedRule rule, float score)> scored = allRules.Select(r =>
            {
                float maxScore = queryEmbeddings
                    .Select(q => EmbeddingUtils.CosineSimilarity(q, r.Embedding))
                    .Max();
                return (rule: r, score: maxScore);
            })
                .Where(x => x.score >= threshold)
                .OrderByDescending(x => x.score)
                .ToList();

            int candidateRuleCount = scored.Count;

            List<LearnedRule> uniqueRules;
            if (candidateRuleCount == 0)
            {
                uniqueRules = new List<LearnedRule>();
            }
            else
            {
                List<string> targetMergeRequestIds = scored
                    .Select(x => x.rule.MergeRequestId)
                    .Distinct()
                    .Take(topN)
                    .ToList();

                string dateTime = DateTime.UtcNow.ToString("O");
                foreach (string mergeRequestId in targetMergeRequestIds)
                {
                    _ = _repository.UpdateLastHitByMergeRequestIdAsync(projectId, mergeRequestId, dateTime, cancellationToken);
                }
                uniqueRules = scored.Where(x => targetMergeRequestIds.Contains(x.rule.MergeRequestId))
                    .GroupBy(x => x.rule.MergeRequestId)
                    .Select(g => g.First().rule)
                    .ToList();

                if (uniqueRules.Any(r => r.ProjectId != projectId))
                {
                    throw new InvalidOperationException(
                        $"Cross-project learned rule detected during retrieval for project {projectId}.");
                }
            }

            searchSw.Stop();
            totalSw.Stop();
            int selectedRuleCount = uniqueRules.Count;

            _logger.LogDebug(
                "Rule search for project {ProjectId} scanned {TotalRuleCount} rules, produced {CandidateRuleCount} candidates, and selected {SelectedRuleCount}",
                projectId, totalRuleCount, candidateRuleCount, selectedRuleCount);

            if (searchRecorder != null && reviewExecutionId.HasValue)
            {
                try
                {
                    await searchRecorder.RecordAsync(new RuleSearchExecution
                    {
                        ReviewExecutionId = reviewExecutionId.Value,
                        ProjectId = projectId,
                        TotalRuleCount = totalRuleCount,
                        CandidateRuleCount = candidateRuleCount,
                        SelectedRuleCount = selectedRuleCount,
                        EmbeddingDurationMs = embeddingDurationMs,
                        SearchDurationMs = searchSw.ElapsedMilliseconds,
                        TotalDurationMs = totalSw.ElapsedMilliseconds,
                        Success = true,
                        StartedAt = searchStartedAt,
                        CompletedAt = DateTimeOffset.UtcNow,
                    }, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to record rule search telemetry");
                }
            }

            return new RuleSearchResult
            {
                Candidates = scored,
                Selected = uniqueRules,
            };
        }

        public static string FormatRulesForPrompt(IReadOnlyList<LearnedRule> rules, string language)
        {
            if (rules.Count == 0) return string.Empty;
            string? template = Context.Instance.Settings.GetLearnedTemplate(language);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("[IMPORTANT: PROJECT-SPECIFIC RULES]");
            if (!string.IsNullOrEmpty(template))
            {
                sb.AppendLine(template);
            }
            foreach (LearnedRule rule in rules)
            {
                sb.AppendLine($"- [rule-id: {rule.Id}] Pattern: {rule.AstPattern}");
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
