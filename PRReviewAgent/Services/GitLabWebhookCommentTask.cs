using NGitLab;
using NGitLab.Models;
using Octokit;
using PRReviewAgent.Services.GitLabWebhook;
using System.ComponentModel;
using System.Text;

namespace PRReviewAgent.Services
{
    public class  GitLabWebhookCommentPayload
    {
        public string object_kind { get; set; }
    }

    public class GitLabWebhookCommentTask
    {
        public static string FindLanguage(string comment)
        {
            ReadOnlySpan<char> line = comment.AsSpan().Trim();
            int index = line.IndexOfAny("\n\r".AsSpan());
            if (0 <= index)
            {
                line = line.Slice(0, index);
            }
            for(int i = 0; i < line.Length;)
            {
                if ('/' != line[i])
                {
                    ++i;
                    continue;
                }

                if ((i + 3) <= line.Length)
                {
                    ReadOnlySpan<char> lang = line.Slice(i, 3);
                    if (!char.IsAsciiLetter(lang[1])
                        || !char.IsAsciiLetter(lang[2]))
                    {
                        i += 3;
                        continue;
                    }
                    if ((i + 4) <= line.Length)
                    {
                        ReadOnlySpan<char> rest = line.Slice(i + 4);
                        if (!char.IsWhiteSpace(rest[0]))
                        {
                            i += 4;
                            continue;
                        }
                    }
                    lang = lang.Slice(1);
                    return lang.ToString();
                }
            }
            return string.Empty;
        }

        public GitLabWebhookCommentTask(PayloadComment payloadComment)
        {
            payloadComment_ = payloadComment;
            language_ = FindLanguage(payloadComment_.object_attributes.note);
            if (string.IsNullOrEmpty(language_) || !Context.Instance.Settings.HasTemplate(language_))
            {
                Tomlyn.Model.TomlTable? commonTable = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["common"];
                language_ = (string)commonTable["default_language"];
            }
#if false
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(payload_, Newtonsoft.Json.Formatting.Indented);
            System.IO.File.WriteAllText("payload_comment.json", json);
#endif
        }

        public class Target
        {
            public NGitLab.Models.Diff Diff { get; set; }
            public string Path { get; set; }
            public string Summary { get; set; }
        }

        public class Change
        {
            [Description("path")]
            public string path { get;set; }
            [Description("summary")]
            public string summary { get; set; }
        }

        public record Changes(
            [Description("Changed file paths and summaries")]
            Change[] changes
        );

        public class Assign
        {
            public int reviewer_number { get; set; }
            public string[] paths { get; set; }
        }
        public record Assignments(
            Assign[] assigns
        );

        public static Target? IsTarget(NGitLab.Models.Diff diff, Func<string, bool> isTarget)
        {
            if (diff.IsDeletedFile)
            {
                return null;
            }
            if (diff.IsRenamedFile)
            {
                return null;
            }
            if (string.IsNullOrEmpty(diff.Difference))
            {
                return null;
            }
            string path;
            if (string.IsNullOrEmpty(diff.NewPath))
            {
                if (!string.IsNullOrEmpty(diff.OldPath))
                {
                    path = diff.OldPath;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                path = diff.NewPath;
            }
            if (!isTarget(path))
            {
                return null;
            }
            return new Target{ Diff = diff, Path = path, Summary = string.Empty };
        }

        public string? GetReviewDiffs(string[] paths, List<Target> targets)
        {
            string? template = Context.Instance.Settings.GetReviewTemplate(language_);
            if(null == template)
            {
                return null;
            }
            stringBuilder_.Clear();
            stringBuilder_.Append(template);
            stringBuilder_.Append("\n\n");
                
            foreach (string path in paths)
            {
                string diff = string.Empty;
                foreach(Target target in targets)
                {
                    if(target.Path == path)
                    {
                        diff = target.Diff.Difference;
                        break;
                    }
                }
                if(string.IsNullOrEmpty(diff))
                {
                    continue;
                }
                stringBuilder_.Append("# ").Append(path).Append("\n");
                stringBuilder_.Append(diff).Append("\n");
            }
            return stringBuilder_.ToString();
        }

        public async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            ILogger<GitLabWebhookCommentTask>? logger = serviceProvider.GetService<ILogger<GitLabWebhookCommentTask>>();

            Context context = Context.Instance;

            NGitLab.GitLabClient gitLabClient = serviceProvider.GetService<GitLabClientService>().GitLabClient;

            NGitLab.IMergeRequestClient mergeRequestClient = gitLabClient.GetMergeRequest(payloadComment_.project.id);
            GitLabCollectionResponse<NGitLab.Models.Diff> response = mergeRequestClient.GetDiffsAsync(payloadComment_.merge_request.iid);
            List<Target> targets = new List<Target>();

            await foreach (NGitLab.Models.Diff diff in response)
            {
                Target? target = IsTarget(diff, context.Settings.IsTargetExtension);
                if (null != target)
                {
                    targets.Add(target);
                }
            }

            foreach (Target target in targets)
            {
                try
                {
                    Microsoft.Agents.AI.AgentResponse agentResponse = await context.Agents.RunAsync(Agents.Type.Assistant, $"Summarize a next diff briefly in few lines.\n{target.Path}\n----\n{target.Diff.Difference}", context.CancellationToken);
                    target.Summary = agentResponse.Text;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex.ToString());
                }
            }
            List<string> reviews = new List<string>();
            {
                List<Change> changes = new List<Change>(targets.Count);
                foreach (Target target in targets)
                {
                    if (string.IsNullOrEmpty(target.Summary))
                    {
                        continue;
                    }
                    changes.Add(new Change() { path = target.Path, summary = target.Summary });
                }

                string changeFilePaths = Newtonsoft.Json.JsonConvert.SerializeObject(new Changes(changes.ToArray()));
                Assignments? assignments = null;
                try
                {
                    assignments = await context.Agents.RunAsync<Assignments>(Agents.Type.Planner, $"Group next diffs in the pull request by relevance and assign file paths to each reviewers.\n```json\n{changeFilePaths}```", context.CancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex.ToString());
                    return;
                }
                foreach (Assign assign in assignments.assigns)
                {
                    string? reviewText = GetReviewDiffs(assign.paths, targets);
                    if (string.IsNullOrEmpty(reviewText))
                    {
                        continue;
                    }
                    try
                    {
                        Microsoft.Agents.AI.AgentResponse agentResponse = await context.Agents.RunAsync(Agents.Type.Executor, reviewText, context.CancellationToken);
                        if (string.IsNullOrEmpty(agentResponse.Text))
                        {
                            continue;
                        }
                        reviews.Add(agentResponse.Text);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex.ToString());
                        continue;
                    }
                }
            }

            stringBuilder_.Clear();
            string organizedReview = string.Empty;
            if (reviews.Count == 1)
            {
                organizedReview = reviews[0];
            }
            else if(1<reviews.Count)
            {
                string? organizeTemplate = Context.Instance.Settings.GetOrganizeTemplate(language_);
                if (null != organizeTemplate)
                {
                    stringBuilder_.Append(organizeTemplate);
                }
                else
                {
                    stringBuilder_.Append("Organize the following reviews:\n");
                }
                foreach (string review in reviews)
                {
                    stringBuilder_.Append(review).Append("\n\n");
                }
                try
                {
                    Microsoft.Agents.AI.AgentResponse agentResponse = await context.Agents.RunAsync(Agents.Type.Executor, stringBuilder_.ToString(), context.CancellationToken);
                    if (!string.IsNullOrEmpty(agentResponse.Text))
                    {
                        organizedReview = agentResponse.Text;
                    }
                }
                catch
                {
                }
            }

            if (!string.IsNullOrEmpty(organizedReview))
            {
                IMergeRequestCommentClient mergeRequestCommentClient = mergeRequestClient.Comments(payloadComment_.merge_request.iid);
                MergeRequestCommentEdit mergeRequestCommentEdit = new MergeRequestCommentEdit();
                stringBuilder_.Clear();
                stringBuilder_.Append(organizedReview);
                mergeRequestCommentEdit.Body = stringBuilder_.ToString();
                try
                {
                    MergeRequestComment _ = mergeRequestCommentClient.Edit(payloadComment_.object_attributes.id, mergeRequestCommentEdit);
                }
                catch(Exception ex)
                {
                    logger.LogError(ex.Message);
                }
            }
            else
            {
                IMergeRequestCommentClient mergeRequestCommentClient = mergeRequestClient.Comments(payloadComment_.merge_request.iid);
                MergeRequestCommentEdit mergeRequestCommentEdit = new MergeRequestCommentEdit();
                stringBuilder_.Clear();
                stringBuilder_.Append("no reviews are generated.");
                mergeRequestCommentEdit.Body = stringBuilder_.ToString();
                try
                {
                    MergeRequestComment _ = mergeRequestCommentClient.Edit(payloadComment_.object_attributes.id, mergeRequestCommentEdit);
                }
                catch(Exception ex)
                {
                    logger.LogError(ex.Message);
                }
            }
        }

        private PayloadComment payloadComment_;
        private string language_;
        private StringBuilder stringBuilder_ = new StringBuilder();
    }
}
