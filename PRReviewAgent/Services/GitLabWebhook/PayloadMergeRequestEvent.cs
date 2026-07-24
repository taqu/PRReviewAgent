namespace PRReviewAgent.Services.GitLabWebhook
{
    public class PayloadMergeRequestObjectAttributes
    {
        public long id { get; set; }
        public long iid { get; set; }
        public string target_branch { get; set; } = string.Empty;
        public string source_branch { get; set; } = string.Empty;
        public int source_project_id { get; set; }
        public int target_project_id { get; set; }
        public string state { get; set; } = string.Empty;
        public string action { get; set; } = string.Empty;
        public string title { get; set; } = string.Empty;
        public string? description { get; set; }
    }

    public class PayloadMergeRequestEvent
    {
        public string object_kind { get; set; } = string.Empty;
        public string event_type { get; set; } = string.Empty;
        public PayloadUser? user { get; set; }
        public PayloadProject project { get; set; } = new();
        public PayloadMergeRequestObjectAttributes object_attributes { get; set; } = new();
    }
}
