namespace PRReviewAgent.Prompt.Turn1
{
    public sealed class Turn1ContextMetrics
    {
        public int FileCount { get; set; }
        public int FullFileCount { get; set; }
        public int PartialFileCount { get; set; }
        public int DiffOnlyCount { get; set; }
        public int EstimatedSourceChars { get; set; }
        public int SemanticSummaryChars { get; set; }
        public bool Truncated { get; set; }
    }
}
