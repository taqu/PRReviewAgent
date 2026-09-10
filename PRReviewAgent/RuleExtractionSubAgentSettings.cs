namespace PRReviewAgent
{
    public sealed record RuleExtractionSubAgentSettings
    {
        public bool Enabled { get; init; }
        public string Endpoint { get; init; } = string.Empty;
        public string Name { get; init; } = "RuleExtractor";
        public string Model { get; init; } = string.Empty;
        public int MaxOutput { get; init; } = 1024;
        public double Temperature { get; init; } = 0.0;
        public double TopP { get; init; } = 0.9;
        public int TimeoutSeconds { get; init; } = 120;
    }
}
