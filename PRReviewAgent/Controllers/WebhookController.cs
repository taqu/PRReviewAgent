using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using PRReviewAgent.Services;
using PRReviewAgent.Services.GitLabWebhook;

namespace PRReviewAgent.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WebhookController : ControllerBase
    {
        private readonly ILogger<WebhookController> logger_;
        private readonly IBackgroundTaskQueue taskQueue_;

        public WebhookController(ILogger<WebhookController> logger, IBackgroundTaskQueue taskQueue)
        {
            logger_ = logger;
            taskQueue_ = taskQueue;
        }

        private const string GitlabTokenKey = "X-Gitlab-Token";
        private const string GitlabEvent = "X-Gitlab-Event";
        private const string GitlabEventUUIDKey = "X-Gitlab-Event-UUID";

        [HttpPost("gitlab")]
        public async Task<IActionResult> ReceiveGitLabWebhook([FromBody] dynamic payload)
        {
            lock (Context.Instance)
            {
                // Validate the token
                Tomlyn.Model.TomlTable? secrets = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Secrets["gitlab"];
                StringValues token;
                if (!HttpContext.Request.Headers.TryGetValue(GitlabTokenKey, out token))
                {
                    logger_.LogError($"Missing {GitlabTokenKey}");
                    return BadRequest();
                }
                if (!token.Any<string>((string x) => { return x == (string)secrets["shared_secret"]; }))
                {
                    logger_.LogError($"Invalid {GitlabTokenKey}");
                    return BadRequest();
                }
            }
            if(!HttpContext.Request.Headers.TryGetValue(GitlabEvent, out StringValues eventType))
            {
                return BadRequest();
            }

            if(!HttpContext.Request.Headers.TryGetValue(GitlabEventUUIDKey, out StringValues eventUUID))
            {
                return BadRequest();
            }
            try
            {
                switch (eventType)
                {
                    case "Note Hook":
                        {
                            PayloadComment payloadComment = JsonConvert.DeserializeObject<PayloadComment>(payload.ToString());
                            if (payloadComment.object_attributes.noteable_type == "MergeRequest" && null != payloadComment.merge_request)
                            {
                                ReadOnlySpan<char> line = payloadComment.object_attributes.note.AsSpan().Trim();
                                int index = line.IndexOfAny("\n\r".AsSpan());
                                if(0<=index)
                                {
                                    line = line.Slice(0, index);
                                }
                                if(line .Contains("/review", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Enqueue
                                    GitLabWebhookCommentTask gitLabWebhookTask = new GitLabWebhookCommentTask(payloadComment);
                                    taskQueue_.QueueBackgroundWorkItem(gitLabWebhookTask.RunAsync);
                                }
                            }
                        }
                        break;
                }
            }
            catch(Exception ex)
            {
                logger_.LogError(ex.ToString());
                return BadRequest();
            }
            return NotFound();
        }
    }
}
