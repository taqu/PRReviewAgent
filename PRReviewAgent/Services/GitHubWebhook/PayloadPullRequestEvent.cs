namespace PRReviewAgent.Services.GitHubWebhook
{
    public class PayloadPRHead
    {
        public string sha { get; set; } = string.Empty;
        public string ref_ { get; set; } = string.Empty;
    }

    public class PayloadPRDetail
    {
        public long id { get; set; }
        public int number { get; set; }
        public string state { get; set; } = string.Empty;
        public bool merged { get; set; }
        public string title { get; set; } = string.Empty;
        public string? body { get; set; }
        public PayloadPRHead head { get; set; } = new();
        public PayloadPRHead @base { get; set; } = new();
    }

    public class PayloadPullRequestEvent
    {
        public string action { get; set; } = string.Empty;
        public int number { get; set; }
        public PayloadPRDetail pull_request { get; set; } = new();
        public PayloadRepository repository { get; set; } = new();
        public PayloadSender? sender { get; set; }
    }
}
