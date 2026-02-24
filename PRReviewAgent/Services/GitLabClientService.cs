
namespace PRReviewAgent.Services
{
    public class GitLabClientService
    {
        public NGitLab.GitLabClient GitLabClient => gitLabClient_;
        public GitLabClientService(string url, string accessToken)
        {
            gitLabClient_ = new NGitLab.GitLabClient(url, accessToken);
        }

        private NGitLab.GitLabClient gitLabClient_;
    }
}
