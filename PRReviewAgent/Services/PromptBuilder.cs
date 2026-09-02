using PRReviewAgent.Prompt;
using System.Text;
using System.Text.Encodings.Web;

namespace PRReviewAgent.Services
{
    public static class PromptBuilder
    {
#if false
        public static void Build(ReviewRequest reviewRequest, StringBuilder stringBuilder)
        {
            foreach(FileGroup fileGroup in reviewRequest.FileGroups)
            {
                stringBuilder.Clear();
                stringBuilder.Append(reviewRequest.ReviewRules);
                stringBuilder.Append("\n----\n");
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
                            stringBuilder.Append("\n```\n");
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
                            stringBuilder.Append("\n```\n");
                        }
                    }
                }
                if (!string.IsNullOrEmpty(reviewRequest.LearnedRules))
                {
                    stringBuilder.Append("\n----\n");
                    stringBuilder.Append(reviewRequest.LearnedRules);
                }
                fileGroup.Prompt = stringBuilder.ToString();
            }
        }
#endif

        public static string BuildTurn1(ReviewRequest reviewRequest, FileGroup fileGroup, StringBuilder stringBuilder)
        {
            stringBuilder.Clear();
            stringBuilder.Append(reviewRequest.ReviewRulesTurn1);
            stringBuilder.Append("\n----\n");
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

            int countAST = fileGroup.ReviewContexts.Count(x => !string.IsNullOrEmpty(x.AstJson));
            if (0 < countAST)
            {
                stringBuilder.Append("# Structures(JSON)\n");
                foreach (ReviewContext reviewContext in fileGroup.ReviewContexts)
                {
                    if (!string.IsNullOrEmpty(reviewContext.AstJson))
                    {
                        stringBuilder.Append($"```json:{reviewContext.Filename}\n");
                        stringBuilder.Append(reviewContext.AstJson);
                        stringBuilder.Append("\n```\n");
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
                        stringBuilder.Append("\n```\n");
                    }
                }
            }
            if (!string.IsNullOrEmpty(reviewRequest.LearnedRules))
            {
                stringBuilder.Append("\n----\n");
                stringBuilder.Append(reviewRequest.LearnedRules);
            }
            return stringBuilder.ToString();
        }

        public static string BuildTurn2(ReviewRequest reviewRequest, IssuesResponse issuesResponse, StringBuilder stringBuilder)
        {
            stringBuilder.Clear();
            stringBuilder.Append(reviewRequest.ReviewRulesTurn2);
            System.Text.Json.JsonSerializerOptions options = new System.Text.Json.JsonSerializerOptions();
            options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
            options.WriteIndented = true;
            string jsonText = System.Text.Json.JsonSerializer.Serialize<IssuesResponse>(issuesResponse, options);
            jsonText = jsonText.Replace("\r\n", "\n");
            stringBuilder.Append(jsonText);
            return stringBuilder.ToString();
        }

        public static void AddNotFound(FileGroup fileGroup, List<string> reviews, string language, StringBuilder stringBuilder)
        {
            string? template = Context.Instance.Settings.GetNoProblemTemplate(language);
            if (string.IsNullOrEmpty(template))
            {
                return;
            }
            stringBuilder.Clear();
            stringBuilder.Append($"# {fileGroup.Topic}\n\n");
            stringBuilder.Append(template);
            reviews.Add(stringBuilder.ToString());
        }
    }
}
