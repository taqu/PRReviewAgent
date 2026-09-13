namespace PRReviewAgent.Services.AutoImprove
{
    public static class SourceLanguageDetector
    {
        public static SourceLanguage Detect(string filePath, string? pairPath = null)
        {
            string ext = Path.GetExtension(filePath);
            return ext switch
            {
                ".c" => SourceLanguage.C,
                ".cpp" or ".cxx" or ".cc" or ".hpp" or ".inl" => SourceLanguage.Cpp,
                ".h" => DetectHeader(pairPath),
                ".cs" => SourceLanguage.CSharp,
                ".py" => SourceLanguage.Python,
                ".rs" => SourceLanguage.Rust,
                _ => SourceLanguage.Unknown,
            };
        }

        private static SourceLanguage DetectHeader(string? pairPath)
        {
            if (string.IsNullOrEmpty(pairPath)) return SourceLanguage.Cpp;
            string pairExt = Path.GetExtension(pairPath);
            return pairExt == ".c" ? SourceLanguage.C : SourceLanguage.Cpp;
        }

        public static string DisplayName(SourceLanguage language) => language switch
        {
            SourceLanguage.C => "C",
            SourceLanguage.Cpp => "C++",
            SourceLanguage.CSharp => "C#",
            SourceLanguage.Python => "Python",
            SourceLanguage.Rust => "Rust",
            _ => "Unknown",
        };
    }
}
