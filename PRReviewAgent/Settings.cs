namespace PRReviewAgent
{
    public class Settings
    {
        public const string SecretsFileName = "secrets.toml";
        public const string ConfigFileName = "config.toml";

        public static Settings Instance { get; set; }

        public static bool Initialize()
        {
            Settings settings = new Settings();
            string current = System.IO.Directory.GetCurrentDirectory();
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
                    settings.secrets_ = Tomlyn.Toml.ToModel(text);
                }
                catch(Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    return false;
                }
            }

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
                    settings.config_ = Tomlyn.Toml.ToModel(text);
                }
                catch(Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    return false;
                }
            }

            if (System.IO.Directory.Exists("Templates"))
            {
                foreach (string file in System.IO.Directory.EnumerateFiles("Templates", "review.*.md"))
                {
                    string filename = System.IO.Path.GetFileName(file);
                    ReadOnlySpan<char> key = filename.AsSpan().Slice("review.".Length, filename.Length - "review.".Length - ".md".Length);
                    try
                    {
                        string template = System.IO.File.ReadAllText(file);
                        settings.reviewTemplates_.Add(key.ToString(), template);
                    }
                    catch
                    {
                    }
                }

                foreach (string file in System.IO.Directory.EnumerateFiles("Templates", "organize.*.md"))
                {
                    string filename = System.IO.Path.GetFileName(file);
                    ReadOnlySpan<char> key = filename.AsSpan().Slice("organize.".Length, filename.Length - "organize.".Length - ".md".Length);
                    try
                    {
                        string template = System.IO.File.ReadAllText(file);
                        settings.organizeTemplates_.Add(key.ToString(), template);
                    }
                    catch
                    {
                    }
                }
            }
            Instance = settings;
            return true;
        }

        public string? GetReviewTemplate(string lang)
        {
            string? template = null;
            reviewTemplates_.TryGetValue(lang, out template);
            return template;
        }

        public string? GetOrganizeTemplate(string lang)
        {
            string? template = null;
            organizeTemplates_.TryGetValue(lang, out template);
            return template;
        }

        public Tomlyn.Model.TomlTable? Secrets => secrets_;
        public Tomlyn.Model.TomlTable? Config => config_;

        private Tomlyn.Model.TomlTable? secrets_;
        private Tomlyn.Model.TomlTable? config_;
        private Dictionary<string, string> reviewTemplates_ = new Dictionary<string, string>();
        private Dictionary<string, string> organizeTemplates_ = new Dictionary<string, string>();
    }
}
