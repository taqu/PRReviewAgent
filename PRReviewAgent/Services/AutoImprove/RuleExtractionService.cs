using System.Text;

namespace PRReviewAgent.Services.AutoImprove
{
    public sealed class RuleExtractionService
    {
        private readonly LocalEmbeddingProvider _embeddingProvider;
        private readonly RuleRepository _repository;
        private readonly ILogger<RuleExtractionService> _logger;

        private const string ExtractionSystemPrompt =
            "You are an expert static analysis bot for C/C++.\n" +
            "Analyze the provided code diff (Before/After), its AST structure, and file dependencies.\n" +
            "Extract the underlying engineering rule, coding standard, or bug-fix pattern that the developer applied.\n" +
            "Respond ONLY in the following JSON format without markdown code blocks:\n" +
            "{\n" +
            "  \"ast_pattern\": \"Short description of the affected AST node/pattern\",\n" +
            "  \"rule_description\": \"A clear, 1-sentence engineering rule applied here\",\n" +
            "  \"bad_pattern\": \"The code pattern to avoid\",\n" +
            "  \"good_pattern\": \"The corrected code pattern\"\n" +
            "}";

        public RuleExtractionService(LocalEmbeddingProvider embeddingProvider, RuleRepository repository, ILogger<RuleExtractionService> logger)
        {
            _embeddingProvider = embeddingProvider;
            _repository = repository;
            _logger = logger;
        }

        public async Task ExtractAndSaveRuleAsync(string astContext, string diff, string fileDependencies, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(diff)) return;
            try
            {
                string prompt = BuildExtractionPrompt(astContext, diff, fileDependencies);
                Microsoft.Agents.AI.AgentResponse agentResponse = await Context.Instance.Agents.RunAsync(prompt, cancellationToken);
                if (string.IsNullOrWhiteSpace(agentResponse.Text)) return;

                LearnedRule? extractedRule = ParseRuleJson(agentResponse.Text);
                if (extractedRule == null) return;

                float[] embedding = await _embeddingProvider.GetEmbeddingAsync(
                    $"{extractedRule.AstPattern} {extractedRule.RuleDescription}", cancellationToken);

                extractedRule.Id = Medo.Uuid7.NewGuid().ToString();
                extractedRule.Embedding = embedding;
                extractedRule.ConfidenceScore = 5;
                extractedRule.CreatedAt = DateTime.UtcNow;
                extractedRule.LastHitAt = DateTime.UtcNow;

                await _repository.InsertAsync(extractedRule, cancellationToken);
                _logger.LogInformation($"Learned new rule: {extractedRule.RuleDescription}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract and save rule");
            }
        }

        private static string BuildExtractionPrompt(string astContext, string diff, string fileDependencies)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(ExtractionSystemPrompt);
            sb.AppendLine("\n---");
            if (!string.IsNullOrEmpty(fileDependencies))
            {
                sb.AppendLine("# File Dependencies");
                sb.AppendLine(fileDependencies);
            }
            if (!string.IsNullOrEmpty(astContext))
            {
                sb.AppendLine("# AST Context");
                sb.AppendLine(astContext);
            }
            sb.AppendLine("# Code Diff");
            sb.AppendLine(diff);
            return sb.ToString();
        }

        private static LearnedRule? ParseRuleJson(string jsonText)
        {
            try
            {
                string text = jsonText.Trim();
                if (text.StartsWith("```"))
                {
                    int start = text.IndexOf('\n') + 1;
                    int end = text.LastIndexOf("```");
                    if (end > start) text = text.Substring(start, end - start).Trim();
                }
                RuleJsonPayload? obj = Newtonsoft.Json.JsonConvert.DeserializeObject<RuleJsonPayload>(text);
                if (obj == null || string.IsNullOrWhiteSpace(obj.rule_description)) return null;
                return new LearnedRule
                {
                    AstPattern = obj.ast_pattern ?? string.Empty,
                    RuleDescription = obj.rule_description,
                    BadPattern = obj.bad_pattern,
                    GoodPattern = obj.good_pattern,
                };
            }
            catch
            {
                return null;
            }
        }

        private sealed class RuleJsonPayload
        {
            public string? ast_pattern { get; set; }
            public string? rule_description { get; set; }
            public string? bad_pattern { get; set; }
            public string? good_pattern { get; set; }
        }
    }
}
