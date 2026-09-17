using Octokit;

namespace PRReviewAgent.Services.ReviewStatus
{
    internal sealed class GitHubReviewCommentProvider : IReviewCommentProvider
    {
        private readonly GitHubClient _client;
        private readonly long _repositoryId;
        private readonly int _issueNumber;

        public GitHubReviewCommentProvider(GitHubClient client, long repositoryId, int issueNumber)
        {
            _client = client;
            _repositoryId = repositoryId;
            _issueNumber = issueNumber;
        }

        public async Task<string> CreateCommentAsync(string body, CancellationToken cancellationToken)
        {
            IssueComment comment = await _client.Issue.Comment.Create(_repositoryId, _issueNumber, body);
            return comment.Id.ToString();
        }

        public async Task UpdateCommentAsync(string commentId, string body, CancellationToken cancellationToken)
        {
            if (!long.TryParse(commentId, out long id))
                throw new ArgumentException($"Invalid GitHub comment ID: {commentId}", nameof(commentId));
            try
            {
                await _client.Issue.Comment.Update(_repositoryId, id, body);
            }
            catch (NotFoundException ex)
            {
                throw new ProviderNotFoundException(null, ex);
            }
            catch (ApiException ex) when ((int)ex.StatusCode == 403)
            {
                throw new ProviderForbiddenException(null, ex);
            }
        }

        public async Task TryAddReactionAsync(string commentId, CancellationToken cancellationToken)
        {
            if (!long.TryParse(commentId, out long id)) return;
            try
            {
                await _client.Reaction.IssueComment.Create(_repositoryId, id, new NewReaction(ReactionType.Eyes));
            }
            catch { }
        }
    }
}
