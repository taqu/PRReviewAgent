using Octokit;

namespace PRReviewAgent.Services
{
    public class GitHubClientService
    {
        public Octokit.GitHubClient GitHubClient => gitHubClient_;

        public GitHubClientService(string name, string accessToken)
        {
            gitHubClient_ = new Octokit.GitHubClient(new Octokit.ProductHeaderValue(name));
            gitHubClient_.Credentials = new Credentials(accessToken);
        }

        private Octokit.GitHubClient gitHubClient_;
    }
}
