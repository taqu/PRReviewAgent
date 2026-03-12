using Octokit.Webhooks.Events.Package;

namespace PRReviewAgent
{
    public class Context
    {
        public static Context Instance => context_;
        public static bool Initialize()
        {
            bool result = context_.settigs_.Initialize();
            context_.agents_ = new Agents();
            return result;
        }

        private static Context context_ = new Context();

        public string GitProvider => (string)((Tomlyn.Model.TomlTable)settigs_.Config["common"])["git_provider"];
        public Settings Settings => settigs_;
        public Agents? Agents => agents_;
        public CancellationToken CancellationToken => CancellationTokenSource_.Token;

        private Context()
        {
        }

        public async Task WarmUpAsync()
        {
            foreach(string template in settigs_.GetReviewTemplates())
            {
                await agents_.RunExecutorAsync(template, CancellationToken);
            }
            foreach(string template in settigs_.GetOrganizeTemplates())
            {
                await agents_.RunExecutorAsync(template, CancellationToken);
            }
        }

        private Settings settigs_ = new Settings();
        private Agents? agents_;
        private CancellationTokenSource CancellationTokenSource_ = new CancellationTokenSource();
    }
}
