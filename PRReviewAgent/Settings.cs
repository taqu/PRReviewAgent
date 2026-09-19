namespace PRReviewAgent
{
    /// <summary>
    /// Manages the application settings, including secrets, configuration, and templates.
    /// </summary>
    public class Settings
    {
        public const string TaskQueueReview = "review";
        public const string TaskQueueImprove = "improve";

        /// <summary>
        /// The name of the secrets file.
        /// </summary>
        public const string SecretsFileName = "secrets.toml";

        /// <summary>
        /// The name of the configuration file.
        /// </summary>
        public const string ConfigFileName = "config.toml";

        public const string TemplatesDirectory = "Templates";

        /// <summary>
        /// Initializes the settings by loading secrets, configuration, and templates.
        /// </summary>
        /// <returns>True if initialization was successful; otherwise, false.</returns>
        public bool Initialize()
        {
            string current = System.IO.Directory.GetCurrentDirectory();
            
            // Step 1: Load secrets (e.g., API keys, tokens) from the secrets.toml file.
            {
                string secretsPath = System.IO.Path.Combine(current, SecretsFileName);
                if (!System.IO.File.Exists(secretsPath))
                {
                    Console.WriteLine($"{SecretsFileName} not found");
                    return false;
                }
                try
                {
                    string text = System.IO.File.ReadAllText(secretsPath);
                    secrets_ = Tomlyn.Toml.ToModel(text);
                }
                catch(Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    return false;
                }
            }

            // Step 2: Load general configuration from the config.toml file.
            {
                string configPath = System.IO.Path.Combine(current, ConfigFileName);
                if (!System.IO.File.Exists(configPath))
                {
                    Console.WriteLine($"{ConfigFileName} not found");
                    return false;
                }
                try
                {
                    string text = System.IO.File.ReadAllText(configPath);
                    config_ = Tomlyn.Toml.ToModel(text);
                }
                catch(Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    return false;
                }

                // Parse the target file extensions that should be reviewed.
                {
                    Tomlyn.Model.TomlTable? reviewTable = (Tomlyn.Model.TomlTable)config_["review"];
                    Tomlyn.Model.TomlArray? extensions = (Tomlyn.Model.TomlArray)reviewTable["target_extensions"];
                    extensions_ = new string[extensions.Count];
                    for (int i = 0; i < extensions.Count; i++)
                    {
                        extensions_[i] = (string)extensions[i];
                    }
                }
            }

            // Step 3: Enumerate and load Markdown templates from the Templates directory.
            if (System.IO.Directory.Exists("Templates"))
            {
                review1Templates_ = LoadTemplate("review1");
                review2Templates_ = LoadTemplate("review2");
                review3Templates_ = LoadTemplate("review3");
                learnedTemplates_ = LoadTemplate("learned");
                noproblemTemplates_ = LoadTemplate("noproblem");
                localPolicy_ = LoadOneTemplate("localpolicy");
            }
            return true;
        }

        /// <summary>
        /// Load review templates (e.g., review.en.md, review.ja.md).
        /// </summary>
        /// <param name="prefix"></param>
        /// <returns></returns>
        private static Dictionary<string, string> LoadTemplate(string prefix)
        {
            Dictionary<string, string> templates = new();
            foreach (string file in System.IO.Directory.EnumerateFiles(TemplatesDirectory, $"{prefix}.*.md"))
            {
                string filename = System.IO.Path.GetFileName(file);
                // Extract the language code from the filename.
                ReadOnlySpan<char> key = filename.AsSpan().Slice(prefix.Length + 1, filename.Length - prefix.Length - 1 - ".md".Length);
                try
                {
                    string template = System.IO.File.ReadAllText(file);
                    templates.Add(key.ToString(), template);
                }
                catch
                {
                }
            }
            return templates;
        }

        private static string LoadOneTemplate(string prefix)
        {
            string path = System.IO.Path.Combine(TemplatesDirectory, $"{prefix}.md");
            if (!System.IO.File.Exists(path))
            {
                return string.Empty;
            }
            try
            {
                return System.IO.File.ReadAllText(path);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Determines if a template exists for the specified language.
        /// </summary>
        /// <param name="lang">The language code.</param>
        /// <returns>True if both review and organize templates exist; otherwise, false.</returns>
        public bool HasTemplate(string lang)
        {
            return review1Templates_.ContainsKey("en") && review2Templates_.ContainsKey("en") && review3Templates_.ContainsKey(lang) && learnedTemplates_.ContainsKey(lang);
        }

        /// <summary>
        /// Gets the review template for the specified language.
        /// </summary>
        /// <param name="lang">The language code.</param>
        /// <returns>The review template text, or null if not found.</returns>
        public string? GetReview1Template(string lang)
        {
            string? template = null;
            review1Templates_.TryGetValue(lang, out template);
            return template;
        }

        /// <summary>
        /// Gets the review template for the specified language.
        /// </summary>
        /// <param name="lang">The language code.</param>
        /// <returns>The review template text, or null if not found.</returns>
        public string? GetReview2Template(string lang)
        {
            string? template = null;
            review2Templates_.TryGetValue(lang, out template);
            return template;
        }

        /// <summary>
        /// Gets the finalization template for the specified language.
        /// </summary>
        /// <param name="lang">The language code.</param>
        /// <returns>The review template text, or null if not found.</returns>
        public string? GetReview3Template(string lang)
        {
            string? template = null;
            review3Templates_.TryGetValue(lang, out template);
            return template;
        }

        /// <summary>
        /// Gets the review template for the specified language.
        /// </summary>
        /// <param name="lang">The language code.</param>
        /// <returns>The review template text, or null if not found.</returns>
        public string? GetLearnedTemplate(string lang)
        {
            string? template = null;
            learnedTemplates_.TryGetValue(lang, out template);
            return template;
        }

        /// <summary>
        /// Gets the review template for the specified language.
        /// </summary>
        /// <param name="lang">The language code.</param>
        /// <returns>The review template text, or null if not found.</returns>
        public string? GetNoProblemTemplate(string lang)
        {
            string? template = null;
            noproblemTemplates_.TryGetValue(lang, out template);
            return template;
        }

        /// <summary>
        /// Gets all review templates.
        /// </summary>
        /// <returns>An enumerable of review template texts.</returns>
        public IEnumerable<string> GetReview1Templates()
        {
            return review1Templates_.Values.AsEnumerable<string>();
        }

        /// <summary>
        /// Gets all review templates.
        /// </summary>
        /// <returns>An enumerable of review template texts.</returns>
        public IEnumerable<string> GetReview2Templates()
        {
            return review2Templates_.Values.AsEnumerable<string>();
        }

        /// <summary>
        /// Gets all finalization templates.
        /// </summary>
        /// <returns>An enumerable of review template texts.</returns>
        public IEnumerable<string> GetReview3Templates()
        {
            return review3Templates_.Values.AsEnumerable<string>();
        }

        /// <summary>
        /// Get project specific conding policy
        /// </summary>
        /// <returns></returns>
        public string GetLocalPolicy()
        {
            return localPolicy_;
        }


        /// <summary>
        /// Gets the extension from the specified file path.
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <returns>The file extension without the dot.</returns>
        private static string GetExtension(string path)
        {
            int index = path.LastIndexOf('.');
            if (index < 0)
            {
                return string.Empty;
            }
            return path.Substring(index+1);   
        }

        /// <summary>
        /// Determines if the specified file path has a target extension for review.
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <returns>True if the extension is a target; otherwise, false.</returns>
        public bool IsTargetExtension(string path)
        {
            string ext = GetExtension(path);
            foreach (string extension in extensions_)
            {
                if (ext == extension)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Gets the number of months after which an unhit rule is considered stale.
        /// Reads from [auto_improve] stale_rule_months in config.toml; defaults to 3.
        /// </summary>
        public int StaleRuleMonthsThreshold
        {
            get
            {
                if (config_ != null
                    && config_.TryGetValue("auto_improve", out object? section)
                    && section is Tomlyn.Model.TomlTable table
                    && table.TryGetValue("stale_rule_months", out object? value)
                    && value is long months)
                {
                    return (int)months;
                }
                return 3;
            }
        }

        /// <summary>
        /// Gets the minimum confidence score below which a stale rule is pruned.
        /// Reads from [auto_improve] min_confidence_score in config.toml; defaults to 7.
        /// </summary>
        public int MinConfidenceScoreThreshold
        {
            get
            {
                if (config_ != null
                    && config_.TryGetValue("auto_improve", out object? section)
                    && section is Tomlyn.Model.TomlTable table
                    && table.TryGetValue("min_confidence_score", out object? value)
                    && value is long score)
                {
                    return (int)score;
                }
                return 7;
            }
        }

        /// <summary>
        /// Gets the secrets table.
        /// </summary>
        public Tomlyn.Model.TomlTable? Secrets => secrets_;

        /// <summary>
        /// Gets the configuration table.
        /// </summary>
        public Tomlyn.Model.TomlTable? Config => config_;

        public PRReviewAgent.Prompt.Turn1.ReviewBudgetConfig GetReviewBudgetConfig()
        {
            int turn1MaxSource = 128_000;
            int turn1FullFile = 32_000;
            int turn1SemSummary = 8_000;
            int verMaxChars = 32_000;
            int verMaxItems = 16;
            int verMaxCallers = 5;
            int verMaxCallees = 5;
            int verMaxTypes = 3;
            int maxCandidates = 8;
            int maxVerCandidates = 8;
            int maxBatchCandidates = 1;
            int maxBatchChars = 32_000;
            int maxConcurrentBatches = 1;

            if (config_ != null
                && config_.TryGetValue("review", out object? reviewObj)
                && reviewObj is Tomlyn.Model.TomlTable reviewTable)
            {
                if (reviewTable.TryGetValue("turn1", out object? t1Obj)
                    && t1Obj is Tomlyn.Model.TomlTable t1)
                {
                    turn1MaxSource = GetIntSetting(t1, "max_source_chars", turn1MaxSource);
                    turn1FullFile = GetIntSetting(t1, "full_file_threshold_chars", turn1FullFile);
                    turn1SemSummary = GetIntSetting(t1, "semantic_summary_max_chars", turn1SemSummary);
                }
                if (reviewTable.TryGetValue("verification", out object? verObj)
                    && verObj is Tomlyn.Model.TomlTable ver)
                {
                    verMaxChars = GetIntSetting(ver, "max_context_chars", verMaxChars);
                    verMaxItems = GetIntSetting(ver, "max_context_items", verMaxItems);
                    verMaxCallers = GetIntSetting(ver, "max_direct_callers", verMaxCallers);
                    verMaxCallees = GetIntSetting(ver, "max_direct_callees", verMaxCallees);
                    verMaxTypes = GetIntSetting(ver, "max_referenced_types", verMaxTypes);
                }
                if (reviewTable.TryGetValue("candidates", out object? candObj)
                    && candObj is Tomlyn.Model.TomlTable cand)
                {
                    maxCandidates = GetIntSetting(cand, "max_candidates_per_group", maxCandidates);
                    maxVerCandidates = GetIntSetting(cand, "max_verification_candidates_per_group", maxVerCandidates);
                }
                if (reviewTable.TryGetValue("batching", out object? batchObj) && batchObj is Tomlyn.Model.TomlTable batchTable)
                {
                    maxBatchCandidates = GetIntSetting(batchTable, "max_candidates_per_batch", maxBatchCandidates);
                    maxBatchChars = GetIntSetting(batchTable, "max_batch_input_chars", maxBatchChars);
                    maxConcurrentBatches = GetIntSetting(batchTable, "max_concurrent_batches", maxConcurrentBatches);
                }
            }

            return new PRReviewAgent.Prompt.Turn1.ReviewBudgetConfig
            {
                Turn1MaxSourceChars = turn1MaxSource,
                Turn1FullFileThresholdChars = turn1FullFile,
                Turn1SemanticSummaryMaxChars = turn1SemSummary,
                VerificationMaxContextChars = verMaxChars,
                VerificationMaxContextItems = verMaxItems,
                VerificationMaxDirectCallers = verMaxCallers,
                VerificationMaxDirectCallees = verMaxCallees,
                VerificationMaxReferencedTypes = verMaxTypes,
                MaxCandidatesPerGroup = maxCandidates,
                MaxVerificationCandidatesPerGroup = maxVerCandidates,
                MaxCandidatesPerBatch = maxBatchCandidates,
                MaxBatchInputChars = maxBatchChars,
                MaxConcurrentBatches = maxConcurrentBatches,
            };
        }

        public PRReviewAgent.Services.Grouping.GroupingConfig GetGroupingConfig()
        {
            int maxFiles = 8;
            PRReviewAgent.Services.Grouping.GroupingMode mode = PRReviewAgent.Services.Grouping.GroupingMode.Semantic;

            if (config_ != null
                && config_.TryGetValue("review", out object? reviewObj)
                && reviewObj is Tomlyn.Model.TomlTable reviewTable
                && reviewTable.TryGetValue("grouping", out object? gObj)
                && gObj is Tomlyn.Model.TomlTable gTable)
            {
                maxFiles = GetIntSetting(gTable, "max_files_per_group", maxFiles);
                if (gTable.TryGetValue("mode", out object? modeVal) && modeVal is string modeStr
                    && System.Enum.TryParse<PRReviewAgent.Services.Grouping.GroupingMode>(modeStr, true, out var parsed))
                    mode = parsed;
            }

            return new PRReviewAgent.Services.Grouping.GroupingConfig
            {
                MaxFilesPerGroup = maxFiles,
                Mode = mode,
            };
        }

        public PRReviewAgent.Services.Verification.AdaptiveVerificationConfig GetAdaptiveVerificationConfig()
        {
            var policy = PRReviewAgent.Services.Verification.VerificationPolicy.Fixed;
            int hardMaxBatch = 8, hardMaxConcurrent = 4;
            int smallCount = 3, smallChars = 24_000, largeChars = 16_000;
            int prefSmallBatch = 2, prefMedBatch = 2, prefConcurrency = 2;

            if (config_ != null
                && config_.TryGetValue("review", out object? reviewObj)
                && reviewObj is Tomlyn.Model.TomlTable reviewTable
                && reviewTable.TryGetValue("adaptive", out object? adaptObj)
                && adaptObj is Tomlyn.Model.TomlTable adaptTable)
            {
                if (adaptTable.TryGetValue("policy", out object? policyVal) && policyVal is string policyStr
                    && System.Enum.TryParse<PRReviewAgent.Services.Verification.VerificationPolicy>(policyStr, true, out var parsedPolicy))
                    policy = parsedPolicy;
                hardMaxBatch = GetIntSetting(adaptTable, "hard_max_candidates_per_batch", hardMaxBatch);
                hardMaxConcurrent = GetIntSetting(adaptTable, "hard_max_concurrent_batches", hardMaxConcurrent);
                smallCount = GetIntSetting(adaptTable, "small_candidate_count_threshold", smallCount);
                smallChars = GetIntSetting(adaptTable, "small_total_chars_threshold", smallChars);
                largeChars = GetIntSetting(adaptTable, "large_candidate_chars_threshold", largeChars);
                prefSmallBatch = GetIntSetting(adaptTable, "preferred_small_batch_size", prefSmallBatch);
                prefMedBatch = GetIntSetting(adaptTable, "preferred_medium_batch_size", prefMedBatch);
                prefConcurrency = GetIntSetting(adaptTable, "preferred_concurrency", prefConcurrency);
            }

            return new PRReviewAgent.Services.Verification.AdaptiveVerificationConfig
            {
                Policy = policy,
                HardMaxCandidatesPerBatch = Math.Max(1, hardMaxBatch),
                HardMaxConcurrentBatches = Math.Max(1, hardMaxConcurrent),
                SmallCandidateCountThreshold = Math.Max(1, smallCount),
                SmallTotalCharsThreshold = Math.Max(1, smallChars),
                LargeCandidateCharsThreshold = Math.Max(1, largeChars),
                PreferredSmallBatchSize = Math.Max(1, prefSmallBatch),
                PreferredMediumBatchSize = Math.Max(1, prefMedBatch),
                PreferredConcurrency = Math.Max(1, prefConcurrency),
            };
        }

        private static int GetIntSetting(Tomlyn.Model.TomlTable table, string key, int defaultValue)
        {
            if (table.TryGetValue(key, out object? val) && val is long l) return (int)l;
            return defaultValue;
        }

        private Tomlyn.Model.TomlTable? secrets_;
        private Tomlyn.Model.TomlTable? config_;
        private Dictionary<string, string> review1Templates_ = new();
        private Dictionary<string, string> review2Templates_ = new();
        private Dictionary<string, string> review3Templates_ = new();
        private Dictionary<string, string> learnedTemplates_ = new();
        private Dictionary<string, string> noproblemTemplates_ = new();
        private string[] extensions_ = new string[0];
        private string localPolicy_ = string.Empty;
    }
}
