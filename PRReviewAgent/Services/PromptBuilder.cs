using PRReviewAgent.Prompt;
using PRReviewAgent.Services.Coverage;
using PRReviewAgent.Services.Recovery;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace PRReviewAgent.Services
{
    public static class PromptBuilder
    {
        public const string RecoveryPromptVersion = "phase6-v1";
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

        /// <summary>
        /// Builds the Recovery Detection (Turn 1B) prompt.
        /// Focuses model attention on unreported changed regions while providing
        /// already-reported findings as exclusion context.
        /// </summary>
        public static string BuildTurn1Recovery(
            ReviewRequest reviewRequest,
            FileGroup fileGroup,
            ReviewCoverage primaryCoverage,
            IssuesResponse primaryCandidates,
            RecoveryContextResult recoveryContext,
            StringBuilder stringBuilder)
        {
            stringBuilder.Clear();
            stringBuilder.Append(reviewRequest.ReviewRulesTurn1Recovery);
            stringBuilder.Append("\n----\n");

            if (!string.IsNullOrEmpty(reviewRequest.MergeRequestTitle))
            {
                stringBuilder.Append("# MR Title\n");
                stringBuilder.Append(reviewRequest.MergeRequestTitle);
                stringBuilder.Append("\n\n");
            }

            // Compact exclusion context: location + problem only (token-efficient).
            if (primaryCandidates.issues.Length > 0)
            {
                stringBuilder.Append("# Already Reported Findings\n");
                foreach (Issue issue in primaryCandidates.issues)
                    stringBuilder.Append($"- {issue.location}: {issue.problem}\n");
                stringBuilder.Append("\n");
            }

            // Remaining changed regions — the focus for Recovery Detection.
            var unreported = primaryCoverage.ChangedRegions
                .Where(r => primaryCoverage.UnreportedRegionIds.Contains(r.RegionId))
                .ToList();
            stringBuilder.Append("# Remaining Changed Regions\n");
            foreach (ChangedRegion r in unreported)
            {
                string sym = r.ContainingSymbol != null ? $" ({r.ContainingSymbol})" : string.Empty;
                stringBuilder.Append($"- {r.FilePath}:{r.StartLine}-{r.EndLine}{sym}\n");
            }
            stringBuilder.Append("\n");

            // Files list.
            stringBuilder.Append("# Files\n");
            foreach (ReviewContext ctx in fileGroup.ReviewContexts)
                stringBuilder.Append($"{ctx.Filename}\n");
            stringBuilder.Append("\n");

            // AST structures (all files — provides semantic context per §10).
            int countAST = fileGroup.ReviewContexts.Count(x => !string.IsNullOrEmpty(x.AstJson));
            if (countAST > 0)
            {
                stringBuilder.Append("# Structures(JSON)\n");
                foreach (ReviewContext ctx in fileGroup.ReviewContexts)
                {
                    if (!string.IsNullOrEmpty(ctx.AstJson))
                    {
                        stringBuilder.Append($"```json:{ctx.Filename}\n");
                        stringBuilder.Append(ctx.AstJson);
                        stringBuilder.Append("\n```\n");
                    }
                }
            }

            // Phase 5: focused diff fragments — only the hunks for unreported regions.
            // Falls back to full file diffs if no fragments were built (e.g. missing ExpandedDiff).
            if (recoveryContext.Fragments.Count > 0)
            {
                var filenameLookup = fileGroup.ReviewContexts.ToDictionary(
                    c => ChangedRegionBuilder.NormalizePath(c.Path), c => c.Filename,
                    StringComparer.OrdinalIgnoreCase);

                stringBuilder.Append("# Recovery Target Diffs\n");
                string? lastFilePath = null;
                foreach (RecoveryFragment fragment in recoveryContext.Fragments)
                {
                    if (fragment.FilePath != lastFilePath)
                    {
                        if (lastFilePath != null) stringBuilder.Append("```\n");
                        string fn = filenameLookup.TryGetValue(fragment.FilePath, out string? name) ? name : fragment.FilePath;
                        stringBuilder.Append($"```diff:{fn}\n");
                        lastFilePath = fragment.FilePath;
                    }
                    stringBuilder.Append(fragment.DiffText);
                }
                if (lastFilePath != null) stringBuilder.Append("```\n");
            }
            else
            {
                // Fallback: include full diffs for files with unreported regions (Phase 4 behavior).
                var unreportedFiles = unreported
                    .Select(r => r.FilePath)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                bool anyDiff = fileGroup.ReviewContexts.Any(x =>
                    !string.IsNullOrEmpty(x.ExpandedDiff) &&
                    unreportedFiles.Contains(ChangedRegionBuilder.NormalizePath(x.Path)));
                if (anyDiff)
                {
                    stringBuilder.Append("# Diffs\n");
                    foreach (ReviewContext ctx in fileGroup.ReviewContexts)
                    {
                        if (string.IsNullOrEmpty(ctx.ExpandedDiff)) continue;
                        if (!unreportedFiles.Contains(ChangedRegionBuilder.NormalizePath(ctx.Path))) continue;
                        stringBuilder.Append($"```diff:{ctx.Filename}\n");
                        stringBuilder.Append(ctx.ExpandedDiff);
                        stringBuilder.Append("\n```\n");
                    }
                }
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
            // Attribution tracking: ask the model to append a hidden metadata line listing
            // the candidate_id values of findings it included. This is stripped before posting.
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
