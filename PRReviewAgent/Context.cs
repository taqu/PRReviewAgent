using Octokit.Webhooks.Events.Package;
using System.Diagnostics;

namespace PRReviewAgent
{
    /// <summary>
    /// Provides a singleton context for the application, managing global state such as settings and agents.
    /// </summary>
    public class Context
    {
        /// <summary>
        /// Gets the singleton instance of the <see cref="Context"/> class.
        /// </summary>
        public static Context Instance => context_;

#if DEBUG
        private ILogger? logger_;
#endif
        [Conditional("DEBUG")]
        public void AddLogger(ILoggerFactory factory)
        {
            context_.logger_ = factory.CreateLogger("Context");
        }

        [Conditional("DEBUG")]
        public void Log(LogLevel logLevel, string message, params object[] args)
        {
            logger_?.Log(logLevel, message, args);
        }

        /// <summary>
        /// Initializes the application context, including settings and agents.
        /// </summary>
        /// <returns><c>true</c> if initialization was successful; otherwise, <c>false</c>.</returns>
        public static bool Initialize()
        {
            // Initialize application settings from configuration files
            bool result = context_.settigs_.Initialize();
            // Initialize AI agents
            context_.agents_ = new Agents();
            return result;
        }

        private static Context context_ = new Context();

        /// <summary>
        /// Gets the configured Git provider name.
        /// </summary>
        public string GitProvider => (string)((Tomlyn.Model.TomlTable)settigs_.Config["common"])["git_provider"];

        /// <summary>
        /// Gets the application settings.
        /// </summary>
        public Settings Settings => settigs_;

        /// <summary>
        /// Gets the AI agents managed by the context.
        /// </summary>
        public Agents? Agents => agents_;

        /// <summary>
        /// Gets the cancellation token for the application.
        /// </summary>
        public CancellationToken CancellationToken => CancellationTokenSource_.Token;

        /// <summary>
        /// Initializes a new instance of the <see cref="Context"/> class.
        /// </summary>
        private Context()
        {
        }

        /// <summary>
        /// Warms up the AI agents by running them with the configured templates.
        /// </summary>
        /// <returns>A task that represents the asynchronous warm-up operation.</returns>
        public async Task WarmUpAsync()
        {
            // Run each review template through the agents to warm them up
            foreach (string template in settigs_.GetReview1Templates())
            {
                await agents_.RunAsync(template, CancellationToken);
            }

            foreach (string template in settigs_.GetReview2Templates())
            {
                await agents_.RunAsync(template, CancellationToken);
            }
        }

        private Settings settigs_ = new Settings();
        private Agents? agents_;
        private CancellationTokenSource CancellationTokenSource_ = new CancellationTokenSource();
    }
}
