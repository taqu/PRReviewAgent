namespace PRReviewAgent
{
    public class Settings
    {
        public const string SecretsFileName = "secrets.toml";
        public const string ConfigFileName = "config.toml";

        public bool Initialize()
        {
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
                    secrets_ = Tomlyn.Toml.ToModel(text);
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
                    config_ = Tomlyn.Toml.ToModel(text);
                }
                catch(Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    return false;
                }

                {// parse target extensions
                    Tomlyn.Model.TomlTable? reviewTable = (Tomlyn.Model.TomlTable)config_["review"];
                    Tomlyn.Model.TomlArray? extensions = (Tomlyn.Model.TomlArray)reviewTable["target_extensions"];
                    extensions_ = new string[extensions.Count];
                    for (int i = 0; i < extensions.Count; i++)
                    {
                        extensions_[i] = (string)extensions[i];
                    }
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
                        reviewTemplates_.Add(key.ToString(), template);
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
                        organizeTemplates_.Add(key.ToString(), template);
                    }
                    catch
                    {
                    }
                }
            }
            return true;
        }

        public bool HasTemplate(string lang)
        {
            return reviewTemplates_.ContainsKey(lang) && organizeTemplates_.ContainsKey(lang);
        }

        public string? GetReviewTemplate(string lang)
        {
            string? template = null;
            reviewTemplates_.TryGetValue(lang, out template);
            return template;
        }

        public IEnumerable<string> GetReviewTemplates()
        {
            return reviewTemplates_.Values.AsEnumerable<string>();
        }

        public string? GetOrganizeTemplate(string lang)
        {
            string? template = null;
            organizeTemplates_.TryGetValue(lang, out template);
            return template;
        }

        public IEnumerable<string> GetOrganizeTemplates()
        {
            return organizeTemplates_.Values.AsEnumerable<string>();
        }

        private static string GetExtension(string path)
        {
            int index = path.LastIndexOf('.');
            if (index < 0)
            {
                return string.Empty;
            }
            return path.Substring(index+1);   
        }

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

        public Tomlyn.Model.TomlTable? Secrets => secrets_;
        public Tomlyn.Model.TomlTable? Config => config_;

        private Tomlyn.Model.TomlTable? secrets_;
        private Tomlyn.Model.TomlTable? config_;
        private Dictionary<string, string> reviewTemplates_ = new Dictionary<string, string>();
        private Dictionary<string, string> organizeTemplates_ = new Dictionary<string, string>();
        private string[] extensions_ = new string[0];
    }
}
