namespace PRReviewAgent.Services.ReviewStatus
{
    public interface IReviewCommentProvider
    {
        /// <summary>Returns the new comment ID.</summary>
        Task<string> CreateCommentAsync(string body, CancellationToken cancellationToken);

        /// <summary>
        /// Updates the comment with the given ID.
        /// Throws <see cref="ProviderNotFoundException"/> on 404.
        /// Throws <see cref="ProviderForbiddenException"/> on 403.
        /// </summary>
        Task UpdateCommentAsync(string commentId, string body, CancellationToken cancellationToken);

        /// <summary>Adds a reaction to indicate review is in progress. Failures are swallowed.</summary>
        Task TryAddReactionAsync(string commentId, CancellationToken cancellationToken);
    }
}
