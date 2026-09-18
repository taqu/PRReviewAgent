namespace PRReviewAgent.Services.ReviewStatus
{
    public sealed class ReviewStatusCommentService : IReviewStatusCommentService
    {
        private const string ReviewingBody = "🤖 Review in progress...\n\n⏳ Analyzing this change.";
        private const string FailedBody = "⚠️ Review failed.\n\nThe review could not be completed.";

        private readonly IReviewStatusCommentRepository _repository;
        private readonly ILogger<ReviewStatusCommentService> _logger;

        public ReviewStatusCommentService(
            IReviewStatusCommentRepository repository,
            ILogger<ReviewStatusCommentService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public async Task BeginReviewAsync(
            IReviewCommentProvider provider,
            long projectId,
            string mergeRequestId,
            CancellationToken cancellationToken = default)
        {
            string? commentId = await _repository.GetCommentIdAsync(projectId, mergeRequestId, cancellationToken);
            if (commentId == null)
            {
                string newId = await provider.CreateCommentAsync(ReviewingBody, cancellationToken);
                _logger.LogInformation(
                    "Created review status comment {CommentId} for project {ProjectId}, merge request {MergeRequestId}",
                    newId, projectId, mergeRequestId);
                await provider.TryAddReactionAsync(newId, cancellationToken);
                await _repository.UpsertAsync(projectId, mergeRequestId, newId, cancellationToken);
            }
            else
            {
                _logger.LogDebug(
                    "Using review status comment {CommentId} for project {ProjectId}, merge request {MergeRequestId}",
                    commentId, projectId, mergeRequestId);
                await UpdateWithRecoveryAsync(provider, projectId, mergeRequestId, commentId, ReviewingBody, addReaction: true, cancellationToken);
            }
        }

        public async Task CompleteReviewAsync(
            IReviewCommentProvider provider,
            long projectId,
            string mergeRequestId,
            string reviewText,
            CancellationToken cancellationToken = default)
        {
            string body = string.IsNullOrWhiteSpace(reviewText)
                ? "✅ Review completed.\n\nNo issues found."
                : reviewText;
            string? commentId = await _repository.GetCommentIdAsync(projectId, mergeRequestId, cancellationToken);
            if (commentId == null)
            {
                string newId = await provider.CreateCommentAsync(body, cancellationToken);
                _logger.LogInformation(
                    "Created review status comment {CommentId} for project {ProjectId}, merge request {MergeRequestId}",
                    newId, projectId, mergeRequestId);
                await _repository.UpsertAsync(projectId, mergeRequestId, newId, cancellationToken);
            }
            else
            {
                await UpdateWithRecoveryAsync(provider, projectId, mergeRequestId, commentId, body, addReaction: false, cancellationToken);
            }
        }

        public async Task FailReviewAsync(
            IReviewCommentProvider provider,
            long projectId,
            string mergeRequestId,
            CancellationToken cancellationToken = default)
        {
            string? commentId = await _repository.GetCommentIdAsync(projectId, mergeRequestId, cancellationToken);
            if (commentId == null)
            {
                string newId = await provider.CreateCommentAsync(FailedBody, cancellationToken);
                _logger.LogInformation(
                    "Created review status comment {CommentId} for project {ProjectId}, merge request {MergeRequestId}",
                    newId, projectId, mergeRequestId);
                await _repository.UpsertAsync(projectId, mergeRequestId, newId, cancellationToken);
            }
            else
            {
                await UpdateWithRecoveryAsync(provider, projectId, mergeRequestId, commentId, FailedBody, addReaction: false, cancellationToken);
            }
        }

        private async Task UpdateWithRecoveryAsync(
            IReviewCommentProvider provider,
            long projectId,
            string mergeRequestId,
            string commentId,
            string body,
            bool addReaction,
            CancellationToken cancellationToken)
        {
            try
            {
                await provider.UpdateCommentAsync(commentId, body, cancellationToken);
            }
            catch (ProviderNotFoundException)
            {
                _logger.LogWarning(
                    "Stored review status comment {CommentId} was not found for project {ProjectId}, merge request {MergeRequestId}; creating a replacement",
                    commentId, projectId, mergeRequestId);
                string newId = await provider.CreateCommentAsync(body, cancellationToken);
                _logger.LogInformation(
                    "Created replacement review status comment {CommentId} for project {ProjectId}, merge request {MergeRequestId}",
                    newId, projectId, mergeRequestId);
                if (addReaction) await provider.TryAddReactionAsync(newId, cancellationToken);
                await _repository.UpsertAsync(projectId, mergeRequestId, newId, cancellationToken);
            }
            catch (ProviderForbiddenException ex)
            {
                _logger.LogError(ex,
                    "Review status comment update was forbidden for comment {CommentId}, project {ProjectId}, merge request {MergeRequestId}. Not creating a replacement.",
                    commentId, projectId, mergeRequestId);
                throw;
            }
        }
    }
}
