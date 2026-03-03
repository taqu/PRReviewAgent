using Microsoft.Extensions.Logging;
using NGitLab.Models;
using PRReviewAgent.Services.GitLabWebhook;

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
#if DEBUG
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(payload_, Newtonsoft.Json.Formatting.Indented);
            System.IO.File.WriteAllText("payload_comment.json", json);
#endif
        }

        public async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            ILogger<GitLabWebhookCommentTask>? logger = serviceProvider.GetService<ILogger<GitLabWebhookCommentTask>>();

            NGitLab.GitLabClient gitLabClient = serviceProvider.GetService<GitLabClientService>().GitLabClient;

            NGitLab.IMergeRequestClient mergeRequestClient = gitLabClient.GetMergeRequest(payload_.project.id);

            NGitLab.Models.MergeRequest mergeRequest = await mergeRequestClient.GetByIidAsync(
                iid:payload_.merge_request.iid,
                options: new NGitLab.Models.SingleMergeRequestQuery()
                {
                    IncludeDivergedCommitsCount = true,
                    IncludeRebaseInProgress = false,
                    RenderHtml = false,
                },
                cancellationToken: cancellationToken
                );
#if DEBUG
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(mergeRequest, Newtonsoft.Json.Formatting.Indented);
            logger.LogInformation($"Merge Request: {json}");
            System.IO.File.WriteAllText("mergerequest.json", json);
#endif
            NGitLab.IMergeRequestCommitClient mergeRequestCommitClient = mergeRequestClient.Commits(payload_.merge_request.iid);
            NGitLab.Models.Commit[] commits = mergeRequestCommitClient.All.ToArray();
            NGitLab.ICommitClient commitClient = gitLabClient.GetCommits(payload_.project.id);
            //gitLabClient.GetCommitStatus
        }

        private PayloadComment payload_;
    }
}
