using PRReviewAgent.Prompt;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace PRReviewAgent.Services
{
    public static class PromptBuilder
    {
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

        public static string BuildTurn2(ReviewRequest reviewRequest, CandidateIssue candidate, VerificationContext verificationContext, StringBuilder stringBuilder)
        {
            stringBuilder.Clear();
            stringBuilder.Append(reviewRequest.ReviewRulesTurn2);
            stringBuilder.Append('\n');
            stringBuilder.Append(VerificationContextFormatter.Format(verificationContext, candidate));
            return stringBuilder.ToString();
        }

        public static string BuildTurn3(ReviewRequest reviewRequest, VerifiedIssue[] verifiedIssues, StringBuilder stringBuilder)
        {
            stringBuilder.Clear();
            stringBuilder.Append(reviewRequest.ReviewRulesTurn3);
            foreach (VerifiedIssue issue in verifiedIssues)
            {
                stringBuilder.Append($"\n### {issue.candidate_id}\n\n");
                if (!string.IsNullOrEmpty(issue.evidence))
                    stringBuilder.Append($"**Evidence:** {issue.evidence}\n\n");
                if (!string.IsNullOrEmpty(issue.impact))
                    stringBuilder.Append($"**Impact:** {issue.impact}\n\n");
                if (!string.IsNullOrEmpty(issue.suggested_fix))
                    stringBuilder.Append($"**Suggested fix:** {issue.suggested_fix}\n\n");
                if (!string.IsNullOrEmpty(issue.confidence))
                    stringBuilder.Append($"**Confidence:** {issue.confidence}\n\n");
            }
            bool hasCandidateIds = verifiedIssues.Any(i => i.candidate_id != null);
            if (hasCandidateIds)
            {
                stringBuilder.Append("\n\nAfter your review, append exactly one hidden metadata line on its own line at the very end in this exact format (no spaces around the colon, comma-separated, no extra text):\n");
                stringBuilder.Append("<!-- SELECTED_CANDIDATES: c0,c1 -->\n");
                stringBuilder.Append("Replace c0,c1 with the candidate_id values of findings you included. If you included none, omit the line entirely.");
            }
            return stringBuilder.ToString();
        }

        public static string BuildTurn2(ReviewRequest reviewRequest, CandidateResponse candidateResponse, StringBuilder stringBuilder)
        {
            stringBuilder.Clear();
            stringBuilder.Append(reviewRequest.ReviewRulesTurn2);
            foreach (CandidateIssue candidate in candidateResponse.issues)
            {
                string id = candidate.candidate_id ?? "?";
                stringBuilder.Append($"\n### Candidate {id}\n\n");
                stringBuilder.Append($"**Location:** {candidate.location}\n\n");
                stringBuilder.Append($"**Category:** {candidate.category}\n\n");
                stringBuilder.Append($"**Hypothesis:** {candidate.hypothesis}\n\n");
                stringBuilder.Append($"**Changed-code trigger:** {candidate.trigger}\n\n");
                if (candidate.verify_symbols?.Length > 0)
                {
                    stringBuilder.Append("**Suggested verification targets:**\n");
                    foreach (string sym in candidate.verify_symbols)
                        stringBuilder.Append($"- {sym}\n");
                    stringBuilder.Append("\n");
                }
            }
            bool hasCandidateIds = candidateResponse.issues.Any(i => i.candidate_id != null);
            if (hasCandidateIds)
            {
                stringBuilder.Append("\n\nAfter your review, append exactly one hidden metadata line on its own line at the very end in this exact format (no spaces around the colon, comma-separated, no extra text):\n");
                stringBuilder.Append("<!-- SELECTED_CANDIDATES: c0,c1 -->\n");
                stringBuilder.Append("Replace c0,c1 with the candidate_id values of findings you included. If you included none, omit the line entirely.");
            }
            return stringBuilder.ToString();
        }

        public static string BuildTurn2(ReviewRequest reviewRequest, IssuesResponse issuesResponse, StringBuilder stringBuilder)
        {
            stringBuilder.Clear();
            stringBuilder.Append(reviewRequest.ReviewRulesTurn2);
            System.Text.Json.JsonSerializerOptions options = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                WriteIndented = false
            };
            options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
            options.WriteIndented = true;
            string jsonText = System.Text.Json.JsonSerializer.Serialize<IssuesResponse>(issuesResponse, options);
            jsonText = jsonText.Replace("\r\n", "\n");
            stringBuilder.Append(jsonText);
            bool hasCandidateIds = issuesResponse.issues.Any(i => i.candidate_id != null);
            if (hasCandidateIds)
            {
                stringBuilder.Append("\n\nAfter your review, append exactly one hidden metadata line on its own line at the very end in this exact format (no spaces around the colon, comma-separated, no extra text):\n");
                stringBuilder.Append("<!-- SELECTED_CANDIDATES: c0,c1 -->\n");
                stringBuilder.Append("Replace c0,c1 with the candidate_id values of findings you included. If you included none, omit the line entirely.");
            }
            return stringBuilder.ToString();
        }

        private static readonly System.Text.RegularExpressions.Regex SelectedCandidatesPattern =
            new System.Text.RegularExpressions.Regex(
                @"<!--\s*SELECTED_CANDIDATES:\s*([^-]+?)\s*-->",
                System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Extracts selected candidate IDs from Selection output and strips the metadata line.
        /// Returns (cleanedText, selectedCandidateIds).
        /// </summary>
        public static (string CleanedText, IReadOnlyList<string> SelectedCandidateIds) ExtractSelectionMetadata(string selectionOutput)
        {
            System.Text.RegularExpressions.Match match = SelectedCandidatesPattern.Match(selectionOutput);
            if (!match.Success)
                return (selectionOutput, Array.Empty<string>());

            string[] ids = match.Groups[1].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string cleaned = SelectedCandidatesPattern.Replace(selectionOutput, string.Empty).TrimEnd();
            return (cleaned, ids);
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
