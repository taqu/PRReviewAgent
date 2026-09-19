using Microsoft.Extensions.DependencyInjection;
using PRReviewAgent.Services;
using PRReviewAgent.Services.AutoImprove;
using PRReviewAgent.Services.AutoReview;
using PRReviewAgent.Services.ReviewStatus;
using PRReviewAgent.Services.Statistics;
using Statistics = PRReviewAgent.Services.Statistics;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace PRReviewAgent
{
    /// <summary>
    /// The main entry point for the PRReviewAgent application.
    /// </summary>
    public class Program
    {
        private static RuleExtractionSubAgentSettings BuildRuleExtractionSubAgentSettings()
        {
            if (!Context.Instance.Settings.Config.TryGetValue("subagent", out object? subagentObj)
                || subagentObj is not Tomlyn.Model.TomlTable subagentTable
                || !subagentTable.TryGetValue("rule_extraction", out object? reObj)
                || reObj is not Tomlyn.Model.TomlTable re)
            {
                return new RuleExtractionSubAgentSettings { Enabled = false };
            }

            bool subEnabled = re.TryGetValue("enabled", out object? e) && e is bool eb && eb;
            string endpoint = re.TryGetValue("endpoint", out object? ep) ? (string)ep : string.Empty;
            string name = re.TryGetValue("name", out object? n) ? (string)n : "RuleExtractor";
            string model = re.TryGetValue("model", out object? m) ? (string)m : string.Empty;
            int maxOutput = re.TryGetValue("max_output", out object? mo) ? (int)(long)mo : 1024;
            double temperature = re.TryGetValue("temperature", out object? t) ? (double)t : 0.0;
            double topp = re.TryGetValue("topp", out object? tp) ? (double)tp : 0.9;
            int timeout = re.TryGetValue("timeout", out object? to) ? (int)(long)to : 120;

            return new RuleExtractionSubAgentSettings
            {
                Enabled = subEnabled,
                Endpoint = endpoint,
                Name = name,
                Model = model,
                MaxOutput = maxOutput,
                Temperature = temperature,
                TopP = topp,
                TimeoutSeconds = timeout,
            };
        }

        private static bool RemoteCertificateValidationCallback(
            Object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors)
        {
            // If there are no SSL policy errors, the certificate is valid
            if(SslPolicyErrors.None == sslPolicyErrors)
            {
                return true;
            }
            Tomlyn.Model.TomlTable config = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["server"];
            // Trust the certificate if explicitly configured in settings
            if ((bool)config["trust_certificate"])
            {
                return true;
            }
            // Check if the certificate subject matches any of the trusted certificates in the configuration
            string[] trusted_certificates = (string[])config["trusted_certificates"];
            foreach (string cert in trusted_certificates)
            {
                if(cert == certificate.Subject)
                {
                    return true;
                }
            }
            // Certificate is not trusted
            return false;
        }

        /// <summary>
        /// The main entry point of the application.
        /// </summary>
        /// <param name="args">The command-line arguments.</param>
        public static void Main(string[] args)
        {
            if (!Context.Initialize())
            {
                System.Console.WriteLine("Failed to initialize context");
                return;
            }
            System.Net.ServicePointManager.ServerCertificateValidationCallback += RemoteCertificateValidationCallback;

            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();

            {
                Tomlyn.Model.TomlTable? server = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["server"];
                builder.WebHost.UseUrls($"{server["url"]}");

                if(server.TryGetValue("log_level", out object? log_level))
                {
                    LogLevel level;
                    if(Enum.TryParse(log_level.ToString(), true, out level))
                    {
                        builder.Logging.SetMinimumLevel(level);
                    }
                }
            }

            // Add services to the container.

            builder.Services.AddControllers();
            builder.Services.AddRazorPages();
            builder.Services.AddKeyedSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>(Settings.TaskQueueReview);
            builder.Services.AddKeyedSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>(Settings.TaskQueueImprove);
            builder.Services.AddHostedService<QueuedProcessorBackgroundServiceReview>();
            builder.Services.AddHostedService<QueuedProcessorBackgroundServiceImprove>();
            bool ssl_verify = false;
            try
            {
                switch (Context.Instance.GitProvider) {
                case "github":
                        {
                            Tomlyn.Model.TomlTable? config = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["github"];
                            Tomlyn.Model.TomlTable? secrets = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Secrets["github"];
                            GitHubClientService gitHubClientService = new GitHubClientService((string)config["name"], (string)secrets["personal_access_token"]);
                            builder.Services.AddSingleton<GitHubClientService>(gitHubClientService);
                            ssl_verify = (bool)config["ssl_verify"];
                        }
                        break;
                case "gitlab":
                        {
                    Tomlyn.Model.TomlTable? config = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["gitlab"];
                    Tomlyn.Model.TomlTable? secrets = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Secrets["gitlab"];
                    GitLabClientService gitLabClientService = new GitLabClientService((string)config["url"], (string)secrets["personal_access_token"]);
                    builder.Services.AddSingleton<GitLabClientService>(gitLabClientService);
                    ssl_verify = (bool)config["ssl_verify"];
                    }
                        break;
                }
                if (ssl_verify)
                {
                    builder.Services.AddAuthentication(
                        Microsoft.AspNetCore.Authentication.Certificate.CertificateAuthenticationDefaults.AuthenticationScheme)
                    .AddCertificate();
                }

            }
            catch (Exception ex)
            {
                System.Console.WriteLine(ex.Message);
                return;
            }
            if ((bool)((Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["common"])["warm_up"])
            {
                builder.Services.AddHostedService<WarmUpTask>();
            }

            // Determine database path (used by both status comment services and auto-improve).
            string reviewDbPath = "AppData/review.db";
            Tomlyn.Model.TomlTable? autoImproveSection = null;
            if (Context.Instance.Settings.Config.TryGetValue("common", out object? aiCfgObj)
                && aiCfgObj is Tomlyn.Model.TomlTable aiCfgTbl)
            {
                autoImproveSection = aiCfgTbl;
                if (aiCfgTbl.TryGetValue("db_path", out object? dpObj)) reviewDbPath = (string)dpObj;
            }

            // Always register project repository and review status comment services.
            ProjectRepository sharedProjectRepository = new ProjectRepository(reviewDbPath);
            ReviewStatusCommentRepository reviewStatusCommentRepository = new ReviewStatusCommentRepository(reviewDbPath);
            reviewStatusCommentRepository.InitializeAsync().GetAwaiter().GetResult();
            builder.Services.AddSingleton(sharedProjectRepository);
            builder.Services.AddSingleton<IReviewStatusCommentRepository>(reviewStatusCommentRepository);
            builder.Services.AddSingleton<IReviewStatusCommentService, ReviewStatusCommentService>();
            System.Console.WriteLine("Review status comment service initialized.");

            // Register auto-review services.
            {
                bool arEnabled = false;
                AutoReviewMode arMode = AutoReviewMode.OptOut;
                if (Context.Instance.Settings.Config.TryGetValue("auto_review", out object? arCfgObj)
                    && arCfgObj is Tomlyn.Model.TomlTable arCfgTbl)
                {
                    arEnabled = arCfgTbl.TryGetValue("enabled", out object? arEnabledObj) && arEnabledObj is bool arEnabledBool && arEnabledBool;
                    if (arCfgTbl.TryGetValue("mode", out object? arModeObj) && arModeObj is string arModeStr)
                    {
                        arMode = string.Equals(arModeStr, "opt_in", StringComparison.OrdinalIgnoreCase)
                            ? AutoReviewMode.OptIn
                            : AutoReviewMode.OptOut;
                    }
                }
                AutoReviewOptions autoReviewOptions = new AutoReviewOptions { Enabled = arEnabled, Mode = arMode };
                AutoReviewUserSettingRepository autoReviewRepository = new AutoReviewUserSettingRepository(reviewDbPath);
                autoReviewRepository.InitializeAsync().GetAwaiter().GetResult();
                builder.Services.AddSingleton(autoReviewOptions);
                builder.Services.AddSingleton<IAutoReviewUserSettingRepository>(autoReviewRepository);
                builder.Services.AddSingleton<IAutoReviewPolicy, AutoReviewPolicy>();
                System.Console.WriteLine($"Auto-review service initialized (enabled={arEnabled}, mode={arMode}).");
            }

            // Register auto-improve services if configured.
            if (autoImproveSection != null
                && autoImproveSection.TryGetValue("enabled", out object? enabledObj)
                && enabledObj is bool enabled && enabled)
            {
                try
                {
                    string modelPath = autoImproveSection.TryGetValue("model_path", out object? mp) ? (string)mp : "Models/granite-embedding-97M-multilingual-r2-Q8_0.gguf";
                    long context_size = autoImproveSection.TryGetValue("context_size", out object? cs) ? (long)cs : 512;
                    long chunk_overlap = autoImproveSection.TryGetValue("chunk_overlap", out object? co) ? (long)co : 64;

                    LocalEmbeddingProvider embeddingProvider = new LocalEmbeddingProvider(modelPath, (uint)context_size, (int)chunk_overlap);
                    RuleRepository ruleRepository = new RuleRepository(reviewDbPath);
                    ruleRepository.InitializeAsync().GetAwaiter().GetResult();

                    ReviewExecutionRepository reviewExecutionRepository = new ReviewExecutionRepository(reviewDbPath);
                    Statistics.ReviewTurnRepository reviewTurnRepository = new Statistics.ReviewTurnRepository(reviewDbPath);
                    RuleLearningEventRepository ruleLearningEventRepository = new RuleLearningEventRepository(reviewDbPath);
                    Statistics.RuleSearchExecutionRepository ruleSearchExecutionRepository = new Statistics.RuleSearchExecutionRepository(reviewDbPath);
                    ReviewRuleUsageRepository reviewRuleUsageRepository = new ReviewRuleUsageRepository(reviewDbPath);
                    RuleExtractionSubAgentSettings subAgentSettings = BuildRuleExtractionSubAgentSettings();
                    builder.Services.AddSingleton(subAgentSettings);
                    builder.Services.AddSingleton(embeddingProvider);
                    builder.Services.AddSingleton(ruleRepository);
                    // sharedProjectRepository already registered above
                    builder.Services.AddSingleton(reviewExecutionRepository);
                    builder.Services.AddSingleton<IReviewExecutionRecorder>(reviewExecutionRepository);
                    builder.Services.AddSingleton(reviewTurnRepository);
                    builder.Services.AddSingleton<IReviewTurnRecorder>(reviewTurnRepository);
                    builder.Services.AddSingleton(ruleSearchExecutionRepository);
                    builder.Services.AddSingleton<IRuleSearchExecutionRecorder>(ruleSearchExecutionRepository);
                    builder.Services.AddSingleton(reviewRuleUsageRepository);
                    builder.Services.AddSingleton<IReviewRuleUsageRepository>(reviewRuleUsageRepository);
                    builder.Services.AddSingleton<RuleExtractionSubAgent>();
                    builder.Services.AddSingleton<ISubAgent>(sp => sp.GetRequiredService<RuleExtractionSubAgent>());
                    builder.Services.AddSingleton<RuleExtractionService>();
                    builder.Services.AddSingleton<RuleRetrievalService>();
                    builder.Services.AddSingleton(ruleLearningEventRepository);
                    builder.Services.AddSingleton<IRuleLearningEventRepository>(ruleLearningEventRepository);
                    builder.Services.AddSingleton<RuleLifecycleService>();
                    builder.Services.AddHostedService<RulePruningWorker>();
                    StatisticsService statisticsService = new StatisticsService(reviewDbPath);
                    builder.Services.AddSingleton<IStatisticsService>(statisticsService);
                    System.Console.WriteLine("Auto-improve module initialized.");
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"Auto-improve module disabled: {ex.Message}");
                }
            }

            WebApplication app = builder.Build();
            if (ssl_verify)
            {
                app.UseAuthentication();
            }

            app.UseStaticFiles();
            app.MapControllers();
            app.MapRazorPages();
            Context.Instance.AddLogger(app.Services.GetRequiredService<ILoggerFactory>());
            app.Run();
        }
    }
}
