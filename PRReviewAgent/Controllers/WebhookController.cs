using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using PRReviewAgent.Services;
using PRReviewAgent.Services.AutoReview;
using PRReviewAgent.Services.GitHubWebhook;
using PRReviewAgent.Services.GitLabWebhook;

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
        private readonly IBackgroundTaskQueue taskQueueReview_;
        private readonly IBackgroundTaskQueue taskQueueImprove_;

        /// <summary>
        /// Initializes a new instance of the <see cref="WebhookController"/> class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <param name="taskQueue">The background task queue instance.</param>
        public WebhookController(ILogger<WebhookController> logger, [FromKeyedServices(Settings.TaskQueueReview)] IBackgroundTaskQueue taskQueueReview, [FromKeyedServices(Settings.TaskQueueImprove)] IBackgroundTaskQueue taskQueueImprove)
        {
            logger_ = logger;
            taskQueueReview_ = taskQueueReview;
            taskQueueImprove_ = taskQueueImprove;
        }

        private const string GitlabTokenKey = "X-Gitlab-Token";
        private const string GitlabEvent = "X-Gitlab-Event";
        private const string GitlabEventUUIDKey = "X-Gitlab-Event-UUID";
        private const string GitHubEventKey = "X-GitHub-Event";

        /// <summary>
        /// Receives and processes a GitLab webhook.
        /// </summary>
        /// <param name="payload">The webhook payload from GitLab.</param>
        /// <returns>An <see cref="IActionResult"/> representing the result of the operation.</returns>
        [HttpPost("gitlab")]
        public async Task<IActionResult> ReceiveGitLabWebhook([FromBody] dynamic payload)
        {
            logger_.LogInformation($"Received GitLab webhook {HttpContext.Connection.RemoteIpAddress}");
            lock (Context.Instance)
            {
                // Ensure the application is configured to handle GitLab webhooks
                if (Context.Instance.GitProvider != "gitlab")
                {
                    logger_.LogError("Not configured to handle GitLab webhooks");
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
                logger_.LogError($"Missing {GitlabEvent}");
                return BadRequest();
            }

            // Get the GitLab event UUID from the header
            if (!HttpContext.Request.Headers.TryGetValue(GitlabEventUUIDKey, out StringValues eventUUID))
            {
                logger_.LogError($"Missing {GitlabEventUUIDKey}");
                return BadRequest();
            }
            try
            {
                // Process the webhook payload based on the event type
                switch (eventType)
                {
                    case "Merge Request Hook":
                        try
                        {
                            // Parse the payload for merge-request-related events
                            GitLabMergeRequestWebhook payloadMergeRequest = GitLabWebhookParser.ParseAndValidateMergeRequest(payload.ToString());
                            if (payloadMergeRequest.ObjectAttributes.Action == "merge")
                            {
                                GitLabMergeMRTask mergeTask = new GitLabMergeMRTask(payloadMergeRequest);
                                await taskQueueImprove_.QueueBackgroundWorkItemAsync(mergeTask.RunAsync);
                                return Ok();
                            }
                            if (payloadMergeRequest.ObjectAttributes.Action == "open")
                            {
                                GitLabMROpenedTask openedTask = new GitLabMROpenedTask(payloadMergeRequest);
                                await taskQueueReview_.QueueBackgroundWorkItemAsync(openedTask.RunAsync);
                                return Ok();
                            }
                        }
                        catch (Exception ex)
                        {
                            logger_.LogError(ex.ToString());
                            return StatusCode(500);
                        }
                        break;
                    case "Note Hook":
                        try
                        {
                            // Parse the payload for comment-related events
                            GitLabMrNoteWebhook payloadComment = GitLabWebhookParser.ParseAndValidateNoteWebhook(payload.ToString());
                            // Get the first line of the comment
                            string noteLine = payloadComment.ObjectAttributes.Note ?? string.Empty;
                            int noteLineEnd = noteLine.IndexOfAny(new[] { '\n', '\r' });
                            string firstLine = noteLineEnd >= 0 ? noteLine.Substring(0, noteLineEnd).Trim() : noteLine.Trim();
                            ReviewCommand? command = ReviewCommandParser.Parse(firstLine);
                            if (command != null)
                            {
                                if (command.Type == ReviewCommandType.Review)
                                {
                                    GitLabWebhookCommentTask gitLabWebhookTask = new GitLabWebhookCommentTask(payloadComment);
                                    await taskQueueReview_.QueueBackgroundWorkItemAsync(gitLabWebhookTask.RunAsync);
                                    return Ok();
                                }
                                else if (command.Type == ReviewCommandType.AutoReviewOn || command.Type == ReviewCommandType.AutoReviewOff)
                                {
                                    GitLabAutoReviewCommandTask autoReviewTask = new GitLabAutoReviewCommandTask(payloadComment, command.Type);
                                    await taskQueueReview_.QueueBackgroundWorkItemAsync(autoReviewTask.RunAsync);
                                    if (command.Type == ReviewCommandType.AutoReviewOn)
                                    {
                                        GitLabWebhookCommentTask gitLabWebhookTask = new GitLabWebhookCommentTask(payloadComment);
                                        await taskQueueReview_.QueueBackgroundWorkItemAsync(gitLabWebhookTask.RunAsync);
                                    }
                                    return Ok();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger_.LogError(ex.ToString());
                            return StatusCode(500);
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
            logger_.LogInformation($"Received GitHub webhook {HttpContext.Connection.RemoteIpAddress}");
            lock (Context.Instance)
            {
                // Ensure the application is configured to handle GitHub webhooks
                if (Context.Instance.GitProvider != "github")
                {
                    logger_.LogError("Not configured to handle GitHub webhooks");
                    return NotFound();
                }
            }
            HttpContext.Request.Headers.TryGetValue(GitHubEventKey, out StringValues githubEventType);
            string githubEvent = githubEventType.FirstOrDefault() ?? "issue_comment";

            try
            {
                switch (githubEvent)
                {
                    case "pull_request":
                        {
                            Services.GitHubWebhook.PayloadPullRequestEvent prPayload =
                                JsonConvert.DeserializeObject<Services.GitHubWebhook.PayloadPullRequestEvent>(payload.ToString());
                            if (prPayload.action == "closed" && prPayload.pull_request.merged)
                            {
                                GitHubMergePRTask mergeTask = new GitHubMergePRTask(prPayload);
                                await taskQueueImprove_.QueueBackgroundWorkItemAsync(mergeTask.RunAsync);
                                return Ok();
                            }
                            if (prPayload.action == "opened")
                            {
                                GitHubMROpenedTask openedTask = new GitHubMROpenedTask(prPayload);
                                await taskQueueReview_.QueueBackgroundWorkItemAsync(openedTask.RunAsync);
                                return Ok();
                            }
                            break;
                        }
                    default:
                        {
                            // Parse the payload for issue-related comment events
                            Services.GitHubWebhook.PayloadIssueComment payloadIssueComment =
                                JsonConvert.DeserializeObject<Services.GitHubWebhook.PayloadIssueComment>(payload.ToString());
                            // Get the first line of the comment
                            string commentLine = payloadIssueComment.comment.body ?? string.Empty;
                            int commentLineEnd = commentLine.IndexOfAny(new[] { '\n', '\r' });
                            string firstLine = commentLineEnd >= 0 ? commentLine.Substring(0, commentLineEnd).Trim() : commentLine.Trim();
                            ReviewCommand? command = ReviewCommandParser.Parse(firstLine);
                            if (command != null)
                            {
                                if (command.Type == ReviewCommandType.Review)
                                {
                                    GitHubWebhookCommentTask gitHubWebhookCommentTask = new GitHubWebhookCommentTask(payloadIssueComment);
                                    await taskQueueReview_.QueueBackgroundWorkItemAsync(gitHubWebhookCommentTask.RunAsync);
                                    return Ok();
                                }
                                else if (command.Type == ReviewCommandType.AutoReviewOn || command.Type == ReviewCommandType.AutoReviewOff)
                                {
                                    GitHubAutoReviewCommandTask autoReviewTask = new GitHubAutoReviewCommandTask(payloadIssueComment, command.Type, command.Language);
                                    await taskQueueReview_.QueueBackgroundWorkItemAsync(autoReviewTask.RunAsync);
                                    if (command.Type == ReviewCommandType.AutoReviewOn)
                                    {
                                        GitHubWebhookCommentTask gitHubWebhookCommentTask = new GitHubWebhookCommentTask(payloadIssueComment);
                                        await taskQueueReview_.QueueBackgroundWorkItemAsync(gitHubWebhookCommentTask.RunAsync);
                                    }
                                    return Ok();
                                }
                            }
                            break;
                        }
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
