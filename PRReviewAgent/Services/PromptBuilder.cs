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
                stringBuilder.Append("MR Title\n");
                stringBuilder.Append(reviewRequest.MergeRequestTitle);
                stringBuilder.Append("\n\n");
                stringBuilder.Append("MR Description\n");
                stringBuilder.Append(reviewRequest.MergeRequestDescription);
                stringBuilder.Append("\n\n");
                if (!string.IsNullOrEmpty(fileGroup.Topic))
                {
                    stringBuilder.Append("Topic\n");
                    stringBuilder.Append(fileGroup.Topic);
                    stringBuilder.Append("\n\n");
                }
                foreach (ReviewContext reviewContext in fileGroup.ReviewContexts)
                {
                    stringBuilder.Append("------------------------------------\n");
                    stringBuilder.Append("File\n");
                    stringBuilder.Append(reviewContext.Path);
                    stringBuilder.Append("\n\n");
                    stringBuilder.Append("Summary\n");
                    stringBuilder.Append(reviewContext.Summary);
                    stringBuilder.Append("\n\n");
                    stringBuilder.Append("Diff\n");
                    stringBuilder.Append(reviewContext.Diff);
                    stringBuilder.Append("\n\n");
                    if (string.IsNullOrEmpty(reviewContext.ChangedFile))
                    {
                        stringBuilder.Append("Current File\n");
                        stringBuilder.Append(reviewContext.ChangedFile);
                        stringBuilder.Append("\n\n");
                    }
                    if (string.IsNullOrEmpty(reviewContext.PairFile) || string.IsNullOrEmpty(reviewContext.PairPath))
                    {
                        stringBuilder.Append("Pair File\n");
                        stringBuilder.Append(reviewContext.PairPath);
                        stringBuilder.Append("\n\n");
                        stringBuilder.Append(reviewContext.PairFile);
                        stringBuilder.Append("\n\n");
                    }
                }
                fileGroup.Prompt = stringBuilder.ToString();
            }
        }
    }
}
