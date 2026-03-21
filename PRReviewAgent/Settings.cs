namespace PRReviewAgent
{
    /// <summary>
    /// Manages the application settings, including secrets, configuration, and templates.
    /// </summary>
    public class Settings
    {
        /// <summary>
        /// The name of the secrets file.
        /// </summary>
        public const string SecretsFileName = "secrets.toml";

        /// <summary>
        /// The name of the configuration file.
        /// </summary>
        public const string ConfigFileName = "config.toml";

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
                // Load review templates (e.g., review.en.md, review.ja.md).
                foreach (string file in System.IO.Directory.EnumerateFiles("Templates", "review.*.md"))
                {
                    string filename = System.IO.Path.GetFileName(file);
                    // Extract the language code from the filename.
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

                // Load organization templates (e.g., organize.en.md, organize.ja.md).
                foreach (string file in System.IO.Directory.EnumerateFiles("Templates", "organize.*.md"))
                {
                    string filename = System.IO.Path.GetFileName(file);
                    // Extract the language code from the filename.
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

        /// <summary>
        /// Determines if a template exists for the specified language.
        /// </summary>
        /// <param name="lang">The language code.</param>
        /// <returns>True if both review and organize templates exist; otherwise, false.</returns>
        public bool HasTemplate(string lang)
        {
            return reviewTemplates_.ContainsKey(lang) && organizeTemplates_.ContainsKey(lang);
        }

        /// <summary>
        /// Gets the review template for the specified language.
        /// </summary>
        /// <param name="lang">The language code.</param>
        /// <returns>The review template text, or null if not found.</returns>
        public string? GetReviewTemplate(string lang)
        {
            string? template = null;
            reviewTemplates_.TryGetValue(lang, out template);
            return template;
        }

        /// <summary>
        /// Gets all review templates.
        /// </summary>
        /// <returns>An enumerable of review template texts.</returns>
        public IEnumerable<string> GetReviewTemplates()
        {
            return reviewTemplates_.Values.AsEnumerable<string>();
        }

        /// <summary>
        /// Gets the organize template for the specified language.
        /// </summary>
        /// <param name="lang">The language code.</param>
        /// <returns>The organize template text, or null if not found.</returns>
        public string? GetOrganizeTemplate(string lang)
        {
            string? template = null;
            organizeTemplates_.TryGetValue(lang, out template);
            return template;
        }

        /// <summary>
        /// Gets all organize templates.
        /// </summary>
        /// <returns>An enumerable of organize template texts.</returns>
        public IEnumerable<string> GetOrganizeTemplates()
        {
            return organizeTemplates_.Values.AsEnumerable<string>();
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
        /// Gets the secrets table.
        /// </summary>
        public Tomlyn.Model.TomlTable? Secrets => secrets_;

        /// <summary>
        /// Gets the configuration table.
        /// </summary>
        public Tomlyn.Model.TomlTable? Config => config_;

        private Tomlyn.Model.TomlTable? secrets_;
        private Tomlyn.Model.TomlTable? config_;
        private Dictionary<string, string> reviewTemplates_ = new Dictionary<string, string>();
        private Dictionary<string, string> organizeTemplates_ = new Dictionary<string, string>();
        private string[] extensions_ = new string[0];
    }
}
