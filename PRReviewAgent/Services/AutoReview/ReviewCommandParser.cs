namespace PRReviewAgent.Services.AutoReview
{
    public static class ReviewCommandParser
    {
        public static ReviewCommand? Parse(string firstLine)
        {
            if (firstLine.Contains("/auto_review", StringComparison.OrdinalIgnoreCase))
            {
                string lang = GitLabWebhookCommentTask.FindLanguage(firstLine);
                int idx = firstLine.IndexOf("/auto_review", StringComparison.OrdinalIgnoreCase);
                string after = firstLine.Substring(idx + "/auto_review".Length).TrimStart();

                if (after.StartsWith("on", StringComparison.OrdinalIgnoreCase)
                    && (after.Length == 2 || !char.IsLetterOrDigit(after[2])))
                {
                    return new ReviewCommand(ReviewCommandType.AutoReviewOn,
                        string.IsNullOrEmpty(lang) ? null : lang);
                }

                if (after.StartsWith("off", StringComparison.OrdinalIgnoreCase)
                    && (after.Length == 3 || !char.IsLetterOrDigit(after[3])))
                {
                    return new ReviewCommand(ReviewCommandType.AutoReviewOff, null);
                }

                return null;
            }

            if (firstLine.Contains("/review", StringComparison.OrdinalIgnoreCase))
            {
                string lang = GitLabWebhookCommentTask.FindLanguage(firstLine);
                return new ReviewCommand(ReviewCommandType.Review,
                    string.IsNullOrEmpty(lang) ? null : lang);
            }

            return null;
        }
    }
}
