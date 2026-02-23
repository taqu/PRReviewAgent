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
            Instance = settings;
            return true;
        }

        public Tomlyn.Model.TomlTable? Secrets => secrets_;
        public Tomlyn.Model.TomlTable? Config => config_;

        private Tomlyn.Model.TomlTable? secrets_;
        private Tomlyn.Model.TomlTable? config_;
    }
}
