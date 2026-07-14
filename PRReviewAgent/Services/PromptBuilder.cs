using System.Text;

namespace PRReviewAgent.Services
{
    public static class PromptBuilder
    {
        public static void Build(ReviewRequest reviewRequest, StringBuilder stringBuilder)
        {
            foreach(FileGroup fileGroup in reviewRequest.FileGroups)
            {
                stringBuilder.Clear();
                stringBuilder.Append(reviewRequest.ReviewRules);
                stringBuilder.Append("\n\n");
                if (!string.IsNullOrEmpty(reviewRequest.MergeRequestTitle))
                {
                    stringBuilder.Append("MR Title\n");
                    stringBuilder.Append(reviewRequest.MergeRequestTitle);
                    stringBuilder.Append("\n\n");
                }
                if (!string.IsNullOrEmpty(reviewRequest.MergeRequestDescription))
                {
                    stringBuilder.Append("MR Description\n");
                    stringBuilder.Append(reviewRequest.MergeRequestDescription);
                    stringBuilder.Append("\n\n");
                }
                foreach (ReviewContext reviewContext in fileGroup.ReviewContexts)
                {
                    stringBuilder.Append($"## {reviewContext.Path}\n\n");
                    if (!string.IsNullOrEmpty(reviewContext.AstJson))
                    {
                        stringBuilder.Append("Semantic Context\n\n```json\n");
                        stringBuilder.Append(reviewContext.AstJson);
                        stringBuilder.Append("\n```\n\n");
                    }
                    if (!string.IsNullOrEmpty(reviewContext.ExpandedDiff))
                    {
                        stringBuilder.Append("Diff\n\n```\n");
                        stringBuilder.Append(reviewContext.ExpandedDiff);
                        stringBuilder.Append("\n```\n\n");
                    }
                }
                fileGroup.Prompt = stringBuilder.ToString();
            }
        }
    }
}
