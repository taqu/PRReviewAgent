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

            logger.LogInformation($"Merge Request: {Newtonsoft.Json.JsonConvert.SerializeObject(mergeRequest)}");

            NGitLab.IMergeRequestCommitClient mergeRequestCommitClient = mergeRequestClient.Commits(payload_.merge_request.iid);
            NGitLab.Models.Commit[] commits = mergeRequestCommitClient.All.ToArray();
            NGitLab.ICommitClient commitClient = gitLabClient.GetCommits(payload_.project.id);
            gitLabClient.GetCommitStatus
        }

        private PayloadComment payload_;
    }
}
