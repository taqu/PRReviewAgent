namespace PRReviewAgent.Services.AutoReview
{
    public sealed record ReviewCommand(ReviewCommandType Type, string? Language);
}
