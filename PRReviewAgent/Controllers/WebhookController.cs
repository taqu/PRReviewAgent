using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using PRReviewAgent.Services;

namespace PRReviewAgent.Controllers
{
    /// <summary>
    /// Controller for handling incoming webhooks.
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    public class WebhookController : ControllerBase
    {
        private readonly ILogger<WebhookController> logger_;
        private readonly IBackgroundTaskQueue taskQueue_;

        /// <summary>
        /// Initializes a new instance of the <see cref="WebhookController"/> class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <param name="taskQueue">The background task queue instance.</param>
        public WebhookController(ILogger<WebhookController> logger, IBackgroundTaskQueue taskQueue)
        {
            logger_ = logger;
            taskQueue_ = taskQueue;
        }

        private const string GitlabTokenKey = "X-Gitlab-Token";
        private const string GitlabEvent = "X-Gitlab-Event";
        private const string GitlabEventUUIDKey = "X-Gitlab-Event-UUID";

        /// <summary>
        /// Receives and processes a GitLab webhook.
        /// </summary>
        /// <param name="payload">The webhook payload from GitLab.</param>
        /// <returns>An <see cref="IActionResult"/> representing the result of the operation.</returns>
        [HttpPost("gitlab")]
        public async Task<IActionResult> ReceiveGitLabWebhook([FromBody] dynamic payload)
        {
            lock (Context.Instance)
            {
                // Ensure the application is configured to handle GitLab webhooks
                if (Context.Instance.GitProvider != "gitlab")
                {
                    return NotFound();
                }
                // Validate the GitLab shared secret token from the request headers
                Tomlyn.Model.TomlTable? secrets = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Secrets["gitlab"];
                StringValues token;
                if (!HttpContext.Request.Headers.TryGetValue(GitlabTokenKey, out token))
                {
                    logger_.LogError($"Missing {GitlabTokenKey}");
                    return BadRequest();
                }
                // Verify if the provided token matches the configured shared secret
                if (!token.Any<string>((string x) => { return x == (string)secrets["shared_secret"]; }))
                {
                    logger_.LogError($"Invalid {GitlabTokenKey}");
                    return BadRequest();
                }
            }
            // Get the GitLab event type from the header
            if (!HttpContext.Request.Headers.TryGetValue(GitlabEvent, out StringValues eventType))
            {
                return BadRequest();
            }

            // Get the GitLab event UUID from the header
            if (!HttpContext.Request.Headers.TryGetValue(GitlabEventUUIDKey, out StringValues eventUUID))
            {
                return BadRequest();
            }
            try
            {
                // Process the webhook payload based on the event type
                switch (eventType)
                {
                    case "Note Hook":
                        {
                            // Parse the payload for comment-related events
                            Services.GitLabWebhook.PayloadComment payloadComment = JsonConvert.DeserializeObject<Services.GitLabWebhook.PayloadComment>(payload.ToString());
                            // Handle comments made on merge requests
                            if (payloadComment.object_attributes.noteable_type == "MergeRequest" && null != payloadComment.merge_request)
                            {
                                // Get the first line of the comment
                                ReadOnlySpan<char> line = payloadComment.object_attributes.note.AsSpan().Trim();
                                int index = line.IndexOfAny("\n\r".AsSpan());
                                if (0 <= index)
                                {
                                    line = line.Slice(0, index);
                                }
                                // Check if the comment contains the trigger command "/review"
                                if (line.Contains("/review", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Enqueue a background task to perform the review
                                    GitLabWebhookCommentTask gitLabWebhookTask = new GitLabWebhookCommentTask(payloadComment);
                                    await taskQueue_.QueueBackgroundWorkItemAsync(gitLabWebhookTask.RunAsync);
                                    return Ok();
                                }
                            }
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                logger_.LogError(ex.ToString());
                return BadRequest();
            }
            return NotFound();
        }

        /// <summary>
        /// Receives and processes a GitHub webhook.
        /// </summary>
        /// <param name="payload">The webhook payload from GitHub.</param>
        /// <returns>An <see cref="IActionResult"/> representing the result of the operation.</returns>
        [HttpPost("github")]
        public async Task<IActionResult> ReceiveGitHubWebhook([FromBody] dynamic payload)
        {
            lock (Context.Instance)
            {
                // Ensure the application is configured to handle GitHub webhooks
                if (Context.Instance.GitProvider != "github")
                {
                    return NotFound();
                }
            }
            try
            {
                // Parse the payload for issue-related comment events
                Services.GitHubWebhook.PayloadIssueComment payloadIssueComment = JsonConvert.DeserializeObject<Services.GitHubWebhook.PayloadIssueComment>(payload.ToString());
                // Get the first line of the comment
                ReadOnlySpan<char> line = payloadIssueComment.comment.body.AsSpan().Trim();
                int index = line.IndexOfAny("\n\r".AsSpan());
                if (0 <= index)
                {
                    line = line.Slice(0, index);
                }
                // Check if the comment body contains the trigger command "/review"
                if (line.Contains("/review", StringComparison.OrdinalIgnoreCase))
                {
                    // Enqueue a background task to perform the GitHub-specific review process
                    GitHubWebhookCommentTask gitHubWebhookCommentTask = new GitHubWebhookCommentTask(payloadIssueComment);
                    await taskQueue_.QueueBackgroundWorkItemAsync(gitHubWebhookCommentTask.RunAsync);
                    return Ok();
                }
            }
            catch (Exception ex)
            {
                logger_.LogError(ex.ToString());
                return BadRequest();
            }
            return NotFound();
        }
    }
}
