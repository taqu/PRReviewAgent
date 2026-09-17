using NGitLab;
using NGitLab.Models;

namespace PRReviewAgent.Services.ReviewStatus
{
    internal sealed class GitLabReviewCommentProvider : IReviewCommentProvider
    {
        private readonly IMergeRequestCommentClient _comments;

        public GitLabReviewCommentProvider(IMergeRequestCommentClient comments)
        {
            _comments = comments;
        }

        public Task<string> CreateCommentAsync(string body, CancellationToken cancellationToken)
        {
            MergeRequestCommentCreate create = new MergeRequestCommentCreate { Body = body };
            MergeRequestComment comment = _comments.Add(create);
            return Task.FromResult(comment.Id.ToString());
        }

        public Task UpdateCommentAsync(string commentId, string body, CancellationToken cancellationToken)
        {
            if (!long.TryParse(commentId, out long id))
                throw new ArgumentException($"Invalid GitLab comment ID: {commentId}", nameof(commentId));
            try
            {
                MergeRequestCommentEdit edit = new MergeRequestCommentEdit { Body = body };
                _comments.Edit(id, edit);
            }
            catch (Exception ex) when (IsNotFound(ex))
            {
                throw new ProviderNotFoundException(null, ex);
            }
            catch (Exception ex) when (IsForbidden(ex))
            {
                throw new ProviderForbiddenException(null, ex);
            }
            return Task.CompletedTask;
        }

        public Task TryAddReactionAsync(string commentId, CancellationToken cancellationToken)
        {
            // GitLab award emoji API is not exposed by NGitLab's IMergeRequestCommentClient; skip.
            return Task.CompletedTask;
        }

        private static bool IsNotFound(Exception ex) =>
            ex is GitLabException gle && gle.StatusCode == System.Net.HttpStatusCode.NotFound;

        private static bool IsForbidden(Exception ex) =>
            ex is GitLabException gle && gle.StatusCode == System.Net.HttpStatusCode.Forbidden;
    }
}
