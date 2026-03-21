using Octokit;

namespace PRReviewAgent.Services
{
    /// <summary>
    /// Service for interacting with the GitHub API using the Octokit library.
    /// </summary>
    public class GitHubClientService
    {
        /// <summary>
        /// Gets the underlying Octokit <see cref="Octokit.GitHubClient"/>.
        /// </summary>
        public Octokit.GitHubClient GitHubClient => gitHubClient_;

        /// <summary>
        /// Initializes a new instance of the <see cref="GitHubClientService"/> class.
        /// </summary>
        /// <param name="name">The name of the application for the product header.</param>
        /// <param name="accessToken">The personal access token for authentication.</param>
        public GitHubClientService(string name, string accessToken)
        {
            gitHubClient_ = new Octokit.GitHubClient(new Octokit.ProductHeaderValue(name));
            gitHubClient_.Credentials = new Credentials(accessToken);
        }

        private Octokit.GitHubClient gitHubClient_;
    }
}
