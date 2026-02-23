using GitLabApiClient;

namespace PRReviewAgent.Services
{
    public class GitLabClientService
    {
        public GitLabClient GitLabClient => gitLabClient_;
        public GitLabClientService(string url, string accessToken)
        {
            gitLabClient_ = new GitLabClient(url, accessToken);
        }

        private GitLabClient gitLabClient_;
    }
}
