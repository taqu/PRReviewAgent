
namespace PRReviewAgent.Services
{
    /// <summary>
    /// Service for interacting with the GitLab API.
    /// </summary>
    public class GitLabClientService
    {
        /// <summary>
        /// Gets the GitLab client instance.
        /// </summary>
        public NGitLab.GitLabClient GitLabClient => gitLabClient_;

        /// <summary>
        /// Initializes a new instance of the <see cref="GitLabClientService"/> class.
        /// </summary>
        /// <param name="url">The GitLab instance URL.</param>
        /// <param name="accessToken">The personal access token for authentication.</param>
        public GitLabClientService(string url, string accessToken)
        {
            // Initialize the NGitLab client with the instance URL and personal access token.
            gitLabClient_ = new NGitLab.GitLabClient(url, accessToken);
        }

        private NGitLab.GitLabClient gitLabClient_;
    }
}
