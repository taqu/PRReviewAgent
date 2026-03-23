using Newtonsoft.Json;

namespace PRReviewAgent.Services.GitLabWebhook
{
    /// <summary>
    /// Represents a user in the GitLab webhook payload.
    /// </summary>
    public class PayloadUser
    {
        public long id { get;set; }
        public string name { get; set; }
        public string username { get;set; }
        public string avatar_url { get; set; }
        public string email { get;set; }
    }

    /// <summary>
    /// Represents a project in the GitLab webhook payload.
    /// Contains project identification, URLs, and visibility details.
    /// </summary>
    public class PayloadProject
    {
        public long id { get;set; }
        public string name { get; set; }
        public string description { get;set; }
        public string web_url { get; set; }
        public string avatar_url { get;set; }
        public string git_ssh_url { get;set; }
        public string git_http_url { get; set; }
        [JsonProperty("namespace")]
        public string Namespace { get;set; }
        public int visibility_level { get; set; }
        public string path_with_namespace { get;set; }

        public string default_branch { get; set; }
        public string homepage { get;set; }
        public string url { get; set; }
        public string ssh_url { get;set; }
        public string http_url { get;set; }
    }

    /// <summary>
    /// Represents a repository in the GitLab webhook payload.
    /// </summary>
    public class PayloadRepository
    {
        public string name { get; set; }
        public string url { get;set; }
        public string description { get; set; }
        public string homepage { get;set; }
    }

    /// <summary>
    /// Represents a diff in the GitLab webhook payload.
    /// Provides details about file changes, including paths and modes.
    /// </summary>
    public class st_diff
    {
        public string diff { get; set; }
        public string new_path { get;set; }
        public string old_path { get; set; }
        public string a_mode { get;set; }
        public string b_mode { get;set; }
        public bool new_file { get; set; }
        public bool renamed_file { get;set; }
        public bool deleted_file { get;set; }
    }

    /// <summary>
    /// Represents object attributes in the GitLab webhook payload.
    /// Contains the core details of the event (e.g., the note content and metadata).
    /// </summary>
    public class PayloadObjectAttributes
    {
        public long id { get;set; }
        [JsonProperty("internal")]
        public string Internal { get; set; }
        public string note { get;set; }
        public string noteable_type { get;set; }
        public int author_id { get;set; }
        public string created_at { get;set; }
        public string updated_at { get;set; }
        public int project_id { get;set; }
        public object? attachment { get;set; }
        public string line_code { get;set; }
        public string commit_id { get;set; }
        public object? noteable_id { get;set; }
        public bool system { get;set; }
        st_diff st_diff { get; set; }
        public string action { get;set; }
        public string url { get;set; }
    }

    /// <summary>
    /// Represents an author in the GitLab webhook payload.
    /// </summary>
    public class author
    {
        public string name { get; set; }
        public string email { get; set; }
    }

    /// <summary>
    /// Represents a commit in the GitLab webhook payload.
    /// </summary>
    public class PayloadCommit
    {
        public string id { get;set; }
        public string message { get; set; }
        public string timestamp { get;set; }
        public string url { get;set; }
        public author author { get; set; }
    }

    /// <summary>
    /// Represents a label in the GitLab webhook payload.
    /// </summary>
    public struct label
    {
        public long id { get; set; }
        public string title { get; set; }
        public string color { get; set; }
        public string? project_id { get; set; }
        public string created_at { get; set; }
        public string updated_at { get; set; }
        public bool template { get; set; }
        public string? description { get; set; }
        public string type { get; set; }
        public string group_id { get; set; }
    }

    /// <summary>
    /// Represents a source or target in the GitLab webhook payload.
    /// </summary>
    public struct source_target
    {
        public string name { get; set; }
        public string description { get; set; }
        public string web_url { get; set; }
        public string? avatar_url { get; set; }
        public string git_ssh_url { get; set; }
        public string git_http_url { get; set; }
        [JsonProperty("namespace")]
        public string Namespace { get; set; }
        public int visibility_level { get; set; }
        public string path_with_namespace { get; set; }
        public string default_branch { get; set; }
        public string homepage { get; set; }
        public string url { get; set; }
        public string ssh_url { get; set; }
        public string http_url { get; set; }
    }

    /// <summary>
    /// Represents the last commit in the GitLab webhook payload.
    /// </summary>
    public struct last_commit
    {
        public string id { get; set; }
        public string message { get; set; }
        public string timestamp { get; set; }
        public string url { get; set; }
        public author author { get; set; }
    }

    /// <summary>
    /// Represents an assignee in the GitLab webhook payload.
    /// </summary>
    public struct assignee
    {
        public string name { get; set; }
        public string username { get; set; }
        public string avatar_url { get; set; }
    }

    /// <summary>
    /// Represents a merge request in the GitLab webhook payload.
    /// </summary>
    public class PayloadMergeRequest
    {
        public long id { get;set; }
        public string target_branch { get; set; }
        public string source_branch { get; set; }
        public int source_project_id { get; set; }
        public long author_id { get; set; }
        public long assignee_id { get; set; }

        public string title { get; set; }
        public string created_at { get; set; }
        public string updated_at { get; set; }
        public int? milestone_id { get; set; }
        public string state { get; set; }
        public string merge_status { get; set; }
        public int target_project_id { get; set; }
        public long iid { get; set; }
        public string description { get; set; }
        public int position { get; set; }
        public label[] labels { get; set; }
        public source_target source { get; set; }
        public source_target target { get; set; }
        public last_commit last_commit { get; set; }
        public bool work_in_progress { get; set; }
        public bool draft { get; set; }
        public assignee assignee { get; set; }
        public string detailed_merge_status { get;set; }
    }

    /// <summary>
    /// Represents an issue in the GitLab webhook payload.
    /// </summary>
    public class PayloadIssue
    {
        public long id { get;set; }
        public string title { get; set; }
        public int[] assignee_ids { get; set; }
        public string? assignee_id { get; set; }
        public int author_id { get; set; }
        public int project_id { get; set; }

        public string created_at { get; set; }
        public string updated_at { get; set; }
        public int position { get; set; }
        public string? branch_name { get; set; }
        public string description { get; set; }
        public int? milestone_id { get; set; }
        public string state { get; set; }
        public long iid { get; set; }
        public label[] labels { get; set; }
    }

    /// <summary>
    /// Represents a snippet in the GitLab webhook payload.
    /// </summary>
    public class PayloadSnippet
    {
        public long id { get;set; }
        public string title { get; set; }
        public string description { get; set; }
        public string content { get; set; }
        public int author_id { get; set; }
        public int project_id { get; set; }
        public string created_at { get; set; }
        public string updated_at { get; set; }
        public string file_name { get; set; }
        public string type { get; set; }
        public int visibility_level { get; set; }
        public string url { get; set; }
    }

    /// <summary>
    /// Represents the overall comment payload for a GitLab webhook.
    /// </summary>
    public class PayloadComment
    {
        public string object_kind { get;set; }
        public string event_type { get; set; }
        public PayloadUser user { get; set; }
        public int project_id { get; set; }
        public PayloadProject project { get; set; }
        public PayloadRepository repository { get; set;}
        public PayloadObjectAttributes object_attributes { get; set; }
        public PayloadCommit? commit { get; set; }
        public PayloadMergeRequest? merge_request { get; set; }
        public PayloadIssue? issue { get; set; }
        public PayloadSnippet? snippet { get; set; }
    }
}
