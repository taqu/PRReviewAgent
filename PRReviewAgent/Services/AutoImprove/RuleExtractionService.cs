using System.Text;

namespace PRReviewAgent.Services.AutoImprove
{
    public sealed class RuleExtractionService
    {
        private readonly PRReviewAgent.ISubAgent _subAgent;
        private readonly LocalEmbeddingProvider _embeddingProvider;
        private readonly RuleRepository _repository;
        private readonly ILogger<RuleExtractionService> _logger;

        private const string ExtractionSystemPrompt =
            "You are a static analysis extraction system.\n\n" +
            "Analyze the provided code change, AST structure, and relevant file dependencies.\n\n" +
            "Extract the underlying engineering rule, coding standard, or bug-fix pattern applied by the developer.\n\n" +
            "Use the programming language specified in the input when interpreting syntax and semantics.\n\n" +
            "Respond only in the following JSON format without markdown code blocks:\n" +
            "{\n" +
            "  \"ast_pattern\": \"Short description of the affected AST node/pattern\",\n" +
            "  \"rule_description\": \"A clear, 1-sentence engineering rule applied here\",\n" +
            "  \"bad_pattern\": \"The code pattern to avoid\",\n" +
            "  \"good_pattern\": \"The corrected code pattern\"\n" +
            "}";

        private static readonly IReadOnlyDictionary<SourceLanguage, string> LanguageHints =
            new Dictionary<SourceLanguage, string>
            {
                [SourceLanguage.C] =
                    "Interpret pointer lifetime, manual resource management, undefined behavior, " +
                    "and explicit error handling according to C semantics.",
                [SourceLanguage.Cpp] =
                    "Interpret ownership, lifetime, RAII, pointer/reference semantics, templates, " +
                    "and const correctness according to C++ semantics.",
                [SourceLanguage.CSharp] =
                    "Interpret nullability, IDisposable, async/await, exceptions, and task behavior " +
                    "according to C# semantics.",
                [SourceLanguage.Python] =
                    "Interpret None handling, exceptions, context managers, iteration, and dynamic typing " +
                    "according to Python semantics.",
                [SourceLanguage.Rust] =
                    "Interpret ownership, borrowing, Result/Option, traits, lifetimes, and unsafe code " +
                    "according to Rust semantics.",
            };

        public RuleExtractionService(PRReviewAgent.ISubAgent subAgent, LocalEmbeddingProvider embeddingProvider, RuleRepository repository, ILogger<RuleExtractionService> logger)
        {
            _subAgent = subAgent;
            _embeddingProvider = embeddingProvider;
            _repository = repository;
            _logger = logger;
        }

        public async Task ExtractAndSaveRuleAsync(RuleExtractionContext context, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(context.Diff)) return;

            if (context.Language == SourceLanguage.Unknown)
            {
                _logger.LogWarning("Skipping rule extraction for {Path}: language could not be determined", context.FilePath);
                return;
            }

            try
            {
                _logger.LogInformation("Extracting rule for {Path} as {Language}", context.FilePath, SourceLanguageDetector.DisplayName(context.Language));
                string prompt = BuildExtractionPrompt(context);
                string? response = await _subAgent.RunAsync(prompt, cancellationToken);
                if (string.IsNullOrWhiteSpace(response)) return;

                LearnedRule? extractedRule = ParseRuleJson(response);
                if (extractedRule == null) return;

                List<float[]> embeddings = _embeddingProvider.GetEmbedding($"{extractedRule.AstPattern} {extractedRule.RuleDescription}");
                string currentMergeRequestId = Medo.Uuid7.NewGuid().ToString();
                foreach (float[] embedding in embeddings)
                {
                    LearnedRule ruleChunk = new LearnedRule
                    {
                        Id = Medo.Uuid7.NewGuid().ToString(),
                        MergeRequestId = currentMergeRequestId,
                        AstPattern = extractedRule.AstPattern,
                        RuleDescription = extractedRule.RuleDescription,
                        BadPattern = extractedRule.BadPattern,
                        GoodPattern = extractedRule.GoodPattern,
                        Embedding = embedding,
                        CreatedAt = DateTime.UtcNow,
                        LastHitAt = DateTime.UtcNow
                    };
                    await _repository.InsertAsync(ruleChunk, cancellationToken);
                }
                _logger.LogInformation("Rule extraction completed for {Path}: {Rule}", context.FilePath, extractedRule.RuleDescription);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rule extraction SubAgent failed for {Path}", context.FilePath);
            }
        }

        public static string BuildExtractionPrompt(RuleExtractionContext context)
        {
            string languageName = SourceLanguageDetector.DisplayName(context.Language);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(ExtractionSystemPrompt);
            sb.AppendLine("\n---");
            sb.AppendLine("# Language");
            if (LanguageHints.TryGetValue(context.Language, out string? hint))
            {
                sb.AppendLine($"{languageName}: {hint}");
            }
            else
            {
                sb.AppendLine(languageName);
            }
            if (!string.IsNullOrEmpty(context.FileDependencies))
            {
                sb.AppendLine("# File Dependencies");
                sb.AppendLine(context.FileDependencies);
            }
            if (!string.IsNullOrEmpty(context.AstContext))
            {
                sb.AppendLine("# AST Context");
                sb.AppendLine(context.AstContext);
            }
            sb.AppendLine("# Code Diff");
            sb.AppendLine(context.Diff);
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
