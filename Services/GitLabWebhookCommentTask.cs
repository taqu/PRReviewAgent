
using GitLabApiClient;
using GitLabApiClient.Models.MergeRequests.Responses;
using PRReviewAgent.Services.GitLabWebhook;
using System.Text.Json;

namespace PRReviewAgent.Services
{
    public class  GitLabWebhookCommentPayload
    {
        public string object_kind { get; set; }
    }

    public class GitLabWebhookCommentTask
    {
        public GitLabWebhookCommentTask(PayloadComment payload)
        {
            payload_ = payload;
        }

        public async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            ILogger<GitLabWebhookCommentTask>? logger = serviceProvider.GetService<ILogger<GitLabWebhookCommentTask>>();
            logger.LogInformation($"Comment: {JsonSerializer.Serialize(payload_)}");

            GitLabClient gitLabClient = serviceProvider.GetService<GitLabClientService>().GitLabClient;

            MergeRequest mergeRequest = await gitLabClient.MergeRequests.GetAsync(payload_.project.id, payload_.merge_request.id);
            logger.LogInformation($"Merge Request: {JsonSerializer.Serialize(mergeRequest)}");

        }

        private PayloadComment payload_;
    }
}
