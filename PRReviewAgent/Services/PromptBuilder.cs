using Microsoft.AspNetCore.Mvc.Rendering;
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
                stringBuilder.Append("\n\n----\n");
                if (!string.IsNullOrEmpty(reviewRequest.MergeRequestTitle))
                {
                    stringBuilder.Append("# MR Title\n");
                    stringBuilder.Append(reviewRequest.MergeRequestTitle);
                    stringBuilder.Append("\n\n");
                }
                if (!string.IsNullOrEmpty(reviewRequest.MergeRequestDescription))
                {
                    stringBuilder.Append("# MR Description\n");
                    stringBuilder.Append(reviewRequest.MergeRequestDescription);
                    stringBuilder.Append("\n\n");
                }
                stringBuilder.Append("# Files\n");
                foreach (ReviewContext reviewContext in fileGroup.ReviewContexts)
                {
                    stringBuilder.Append($"{reviewContext.Filename}\n");
                }
                stringBuilder.Append("\n");

                int countAST = fileGroup.ReviewContexts.Count(x=>!string.IsNullOrEmpty(x.AstJson));
                if (0 < countAST)
                {
                    stringBuilder.Append("# Structures(JSON)\n");
                    foreach (ReviewContext reviewContext in fileGroup.ReviewContexts)
                    {
                        if (!string.IsNullOrEmpty(reviewContext.AstJson))
                        {
                            stringBuilder.Append($"```json:{reviewContext.Filename}\n");
                            stringBuilder.Append(reviewContext.AstJson);
                            stringBuilder.Append("\n```\n\n");
                        }
                    }
                }

                int countDiff = fileGroup.ReviewContexts.Count(x => !string.IsNullOrEmpty(x.ExpandedDiff));
                if (0 < countAST)
                {
                    stringBuilder.Append("# Diffs\n");
                    foreach (ReviewContext reviewContext in fileGroup.ReviewContexts)
                    {
                        if (!string.IsNullOrEmpty(reviewContext.ExpandedDiff))
                        {
                            stringBuilder.Append($"```diff:{reviewContext.Filename}\n");
                            stringBuilder.Append(reviewContext.ExpandedDiff);
                            stringBuilder.Append("\n```\n\n");
                        }
                    }
                }
                fileGroup.Prompt = stringBuilder.ToString();
            }
        }
    }
}
