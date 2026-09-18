using System.Text.Json.Serialization;
namespace PRReviewAgent.Services.GitLabWebhook
{
    public class WebhookUser
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }
    }

    public class GitLabMrNoteWebhook
    {
        [JsonPropertyName("object_kind")]
        public string? ObjectKind { get; set; }

        [JsonPropertyName("user")]
        public WebhookUser? User { get; set; }

        [JsonPropertyName("project")]
        public WebhookProject? Project { get; set; }

        [JsonPropertyName("object_attributes")]
        public WebhookObjectAttributes? ObjectAttributes { get; set; }

        [JsonPropertyName("merge_request")]
        public WebhookMergeRequest? MergeRequest { get; set; }
    }

    public class WebhookProject
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; }
    }

    public class WebhookObjectAttributes
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; }

        [JsonPropertyName("note")]
        public string? Note { get; set; }

        [JsonPropertyName("noteable_type")]
        public string? NoteableType { get; set; }

        [JsonPropertyName("author_id")]
        public long? AuthorId { get; set; }
    }

    public class WebhookMergeRequest
    {
        [JsonPropertyName("iid")]
        public long? Iid { get; set; }

        [JsonPropertyName("target_project_id")]
        public long? TargetProjectId { get; set; }

        [JsonPropertyName("source_branch")]
        public string? SourceBranch { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    public class GitLabMergeRequestWebhook
    {
        [JsonPropertyName("object_kind")]
        public string? ObjectKind { get; set; }

        [JsonPropertyName("user")]
        public WebhookUser? User { get; set; }

        [JsonPropertyName("project")]
        public WebhookProject? Project { get; set; }

        [JsonPropertyName("object_attributes")]
        public WebhookMrAttributes? ObjectAttributes { get; set; }
    }

    public class WebhookMrAttributes
    {
        [JsonPropertyName("iid")]
        public long? Iid { get; set; }

        [JsonPropertyName("source_project_id")]
        public long? SourceProjectId { get; set; }

        [JsonPropertyName("source_branch")]
        public string? SourceBranch { get; set; }

        [JsonPropertyName("action")]
        public string? Action { get; set; }

        [JsonPropertyName("author_id")]
        public long? AuthorId { get; set; }

        [JsonPropertyName("target_project_id")]
        public long? TargetProjectId { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}
