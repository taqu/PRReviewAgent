using System.Text;

namespace PRReviewAgent.Services.AutoImprove
{
    public sealed class RuleExtractionService
    {
        private readonly PRReviewAgent.ISubAgent _subAgent;
        private readonly LocalEmbeddingProvider _embeddingProvider;
        private readonly RuleRepository _repository;
        private readonly ILogger<RuleExtractionService> _logger;

        private const string UnknownMarker = "UNKNOWN";

        private const string ExtractionSystemPrompt =
            "You are a static analysis rule-extraction system.\n\n" +
            "Analyze the provided code change in the specified programming language.\n\n" +
            "Extract the smallest reusable engineering rule directly demonstrated\n" +
            "by the Before/After change.\n\n" +
            "The changed code is the primary evidence.\n" +
            "Structural and semantic context are supporting evidence only.\n\n" +
            "Do not infer project-wide policy.\n" +
            "Do not infer unsupported developer intent.\n" +
            "Do not invent rules from unrelated surrounding context.\n\n" +
            "Do not merely restate the textual diff.\n\n" +
            "Generalize only enough for the rule to identify the same kind of issue\n" +
            "elsewhere in the codebase.\n\n" +
            "If no clear reusable rule is demonstrated by the change,\n" +
            "return UNKNOWN.\n\n" +
            "Respond only in the following JSON format without markdown code blocks:\n" +
            "{\n" +
            "  \"ast_pattern\": \"Short description of the affected AST node/pattern\",\n" +
            "  \"rule_description\": \"One concise sentence, or UNKNOWN\",\n" +
            "  \"bad_pattern\": \"Short reusable pattern to avoid\",\n" +
            "  \"good_pattern\": \"Short corrected pattern\"\n" +
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

        // Normalized lowercase deny-list (without trailing period).
        private static readonly HashSet<string> GenericPhraseDenyList = new(StringComparer.OrdinalIgnoreCase)
        {
            "follow best practices",
            "handle errors properly",
            "improve code quality",
            "use proper validation",
            "write safer code",
            "use correct coding standards",
            "follow coding standards",
            "write better code",
            "use best practices",
            "handle exceptions properly",
            "use proper error handling",
            "improve performance",
            "add proper documentation",
            "use meaningful names",
            "write cleaner code",
            "apply best practices",
        };

        public RuleExtractionService(PRReviewAgent.ISubAgent subAgent, LocalEmbeddingProvider embeddingProvider, RuleRepository repository, ILogger<RuleExtractionService> logger)
        {
            _subAgent = subAgent;
            _embeddingProvider = embeddingProvider;
            _repository = repository;
            _logger = logger;
        }

        public async Task ExtractAndSaveRuleAsync(RuleExtractionContext context, long projectId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(context.ExpandedDiff)) return;

            if (context.Language == SourceLanguage.Unknown)
            {
                _logger.LogWarning("Skipping rule extraction for {Path}: language could not be determined", context.FilePath);
                return;
            }

            try
            {
                _logger.LogInformation(
                    "Extracting rule for {Path} as {Language}",
                    context.FilePath, SourceLanguageDetector.DisplayName(context.Language));
                _logger.LogInformation(
                    "Rule extraction context for {Path}: structures={Structures}, symbols={Symbols}, dependencies={Dependencies}",
                    context.FilePath, context.Structures.Count, context.Symbols.Count, context.Dependencies.Count);

                string prompt = BuildExtractionPrompt(context);
                string? response = await _subAgent.RunAsync(prompt, cancellationToken);
                if (string.IsNullOrWhiteSpace(response)) return;

                LearnedRule? extractedRule = ParseRuleJson(response);
                if (extractedRule == null)
                {
                    _logger.LogWarning("Rule extraction invalid response for {Path}", context.FilePath);
                    return;
                }

                if (IsUnknown(extractedRule))
                {
                    _logger.LogInformation("Rule extraction skipped for {Path}: UNKNOWN", context.FilePath);
                    return;
                }

                if (IsGenericRule(extractedRule))
                {
                    _logger.LogInformation("Rule extraction skipped for {Path}: generic or low-value result", context.FilePath);
                    return;
                }

                List<float[]> embeddings = _embeddingProvider.GetEmbedding($"{extractedRule.AstPattern} {extractedRule.RuleDescription}");
                string currentMergeRequestId = Medo.Uuid7.NewGuid().ToString();
                foreach (float[] embedding in embeddings)
                {
                    LearnedRule ruleChunk = new LearnedRule
                    {
                        Id = Medo.Uuid7.NewGuid().ToString(),
                        ProjectId = projectId,
                        MergeRequestId = currentMergeRequestId,
                        AstPattern = extractedRule.AstPattern,
                        RuleDescription = extractedRule.RuleDescription,
                        BadPattern = extractedRule.BadPattern,
                        GoodPattern = extractedRule.GoodPattern,
                        Embedding = embedding,
                        CreatedAt = DateTime.UtcNow,
                        LastHitAt = DateTime.UtcNow
                    };
                    await _repository.InsertAsync(ruleChunk, _logger, cancellationToken);
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
            var sb = new StringBuilder();

            sb.AppendLine(ExtractionSystemPrompt);
            sb.AppendLine("\n---");

            sb.AppendLine("# Language");
            if (LanguageHints.TryGetValue(context.Language, out string? hint))
                sb.AppendLine($"{languageName}: {hint}");
            else
                sb.AppendLine(languageName);

            sb.AppendLine("# Changed Code");
            sb.AppendLine(context.ExpandedDiff);

            if (context.Structures.Count > 0)
            {
                sb.AppendLine("# Relevant Structure");
                foreach (StructuralContext s in context.Structures)
                {
                    string line = string.IsNullOrEmpty(s.ChangeKind)
                        ? $"function: {s.FunctionSignature}"
                        : $"function: {s.FunctionSignature} [{s.ChangeKind}]";
                    sb.AppendLine(line);
                    if (!string.IsNullOrEmpty(s.ContainingType))
                        sb.AppendLine($"type: {s.ContainingType}");
                }
            }

            if (context.Symbols.Count > 0 || context.Dependencies.Count > 0)
            {
                sb.AppendLine("# Relevant Semantic Context");
                foreach (SymbolContext sym in context.Symbols)
                    sb.AppendLine($"{sym.Kind}: {sym.Name}");
                foreach (DependencyContext dep in context.Dependencies)
                {
                    string change = dep.Change ?? string.Empty;
                    sb.AppendLine(string.IsNullOrEmpty(change)
                        ? $"{dep.Kind}: {dep.Name}"
                        : $"{change}: {dep.Kind} {dep.Name}");
                }
            }

            return sb.ToString();
        }

        public static bool IsUnknown(LearnedRule rule) =>
            string.Equals(rule.RuleDescription.Trim(), UnknownMarker, StringComparison.OrdinalIgnoreCase);

        public static bool IsGenericRule(LearnedRule rule)
        {
            if (IsUnknown(rule)) return false;

            string desc = rule.RuleDescription.Trim().TrimEnd('.').Trim();
            if (GenericPhraseDenyList.Contains(desc)) return true;

            // Both patterns missing — no actionable guidance
            bool badEmpty = string.IsNullOrWhiteSpace(rule.BadPattern);
            bool goodEmpty = string.IsNullOrWhiteSpace(rule.GoodPattern);
            if (badEmpty && goodEmpty) return true;

            // Identical non-empty patterns — model found no difference
            if (!badEmpty && !goodEmpty && rule.BadPattern == rule.GoodPattern) return true;

            return false;
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
                    RuleDescription = obj.rule_description.Trim(),
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
