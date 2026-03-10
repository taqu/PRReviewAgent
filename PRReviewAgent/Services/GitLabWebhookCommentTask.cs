using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.FileSystemGlobbing.Internal;
using Microsoft.Extensions.Logging;
using NGitLab;
using NGitLab.Models;
using PRReviewAgent.Services.GitLabWebhook;
using System.Text;
using System.Text.RegularExpressions;
using static PRReviewAgent.Tools.GitLabChanges;

namespace PRReviewAgent.Services
{
    public class  GitLabWebhookCommentPayload
    {
        public string object_kind { get; set; }
    }

    public class GitLabWebhookCommentTask
    {
        public string FindLanguage()
        {
            ReadOnlySpan<char> line = payloadComment_.object_attributes.note.AsSpan().Trim();
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
                    return lang.ToString();
                }
            }
            return string.Empty;
        }

        public GitLabWebhookCommentTask(PayloadComment payloadComment)
        {
            payloadComment_ = payloadComment;
            language_ = FindLanguage();
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

        public class Assign
        {
            public int reviewer_number { get; set; }
            public string[] paths { get; set; }
        }
        public record Assignments(
            Assign[] assigns
        );

        public async Task RunAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            ILogger<GitLabWebhookCommentTask>? logger = serviceProvider.GetService<ILogger<GitLabWebhookCommentTask>>();

            NGitLab.GitLabClient gitLabClient = serviceProvider.GetService<GitLabClientService>().GitLabClient;

            NGitLab.IMergeRequestClient mergeRequestClient = gitLabClient.GetMergeRequest(payloadComment_.project.id);
            GitLabCollectionResponse<NGitLab.Models.Diff> response = mergeRequestClient.GetDiffsAsync(payloadComment_.merge_request.iid);
            List<NGitLab.Models.Diff> diffs = new List<NGitLab.Models.Diff>();

            Context context = Context.Instance;
            context.Agents.GitLabChanges.ClearDiffs();
            await foreach (NGitLab.Models.Diff diff in response)
            {
                context.Agents.GitLabChanges.AddDiff(diff, context.Settings.IsTargetExtension);
            }

            foreach (Difference diff in context.Agents.GitLabChanges.Diffs)
            {
                Microsoft.Agents.AI.AgentResponse agentResponse = await context.Agents.RunExecutorAsync($"Summarize a next diff briefly in few lines.\n{diff.Change.path}\n----\n{diff.Diff.Difference}", context.CancellationToken);
                diff.Change.summary = agentResponse.Text;
            }
            string changeFilePaths = context.Agents.GitLabChanges.GetChangeFilePaths();
            Assignments? assignments = await context.Agents.RunExecutorAsync<Assignments>($"Group next diffs in the pull request by relevance and assign file paths to each reviewer.\n```json\n{changeFilePaths}```", context.CancellationToken);
            if (null == assignments)
            {
                return;
            }
            List<string> reviews = new List<string>();
            foreach (Assign assign in assignments.assigns)
            {
                string? reviewText = context.Agents.GitLabChanges.GetReviewDiffs(assign.paths, "ja");
                if (string.IsNullOrEmpty(reviewText))
                {
                    continue;
                }
                try
                {
                    Microsoft.Agents.AI.AgentResponse agentResponse = await context.Agents.RunExecutorAsync(reviewText, context.CancellationToken);
                    if (string.IsNullOrEmpty(agentResponse.Text))
                    {
                        continue;
                    }
                    reviews.Add(agentResponse.Text);
                }
                catch
                {
                    continue;
                }
            }

            StringBuilder stringBuilder = new StringBuilder();
            if (reviews.Count == 1)
            {
                stringBuilder.Append(reviews[0]);
            }
            else
            {
                string? organizeTemplate = Context.Instance.Settings.GetOrganizeTemplate("ja");
                if (null != organizeTemplate)
                {
                    stringBuilder.Append(organizeTemplate);
                }
                else
                {
                    stringBuilder.Append("Organize the following reviews:\n");
                }
                foreach (string review in reviews)
                {
                    stringBuilder.Append(review).Append("\n\n");
                }
            }

            if (0 < stringBuilder.Length)
            {
                Microsoft.Agents.AI.AgentResponse agentResponse = await context.Agents.RunExecutorAsync(stringBuilder.ToString(), context.CancellationToken);
                if(!string.IsNullOrEmpty(agentResponse.Text))
                {
                    IMergeRequestCommentClient mergeRequestCommentClient = mergeRequestClient.Comments(payloadComment_.merge_request.iid);
                    MergeRequestCommentEdit mergeRequestCommentEdit = new MergeRequestCommentEdit();
                    stringBuilder.Clear();
                    stringBuilder.Append($"{payloadComment_.object_attributes.note}\n\n");
                    stringBuilder.Append(agentResponse.Text);
                    mergeRequestCommentEdit.Body = stringBuilder.ToString();
                    try
                    {
                        mergeRequestCommentClient.Edit(payloadComment_.object_attributes.id, mergeRequestCommentEdit);
                    }
                    catch
                    {
                    }
                }
            }
            else
            {
                IMergeRequestCommentClient mergeRequestCommentClient = mergeRequestClient.Comments(payloadComment_.merge_request.iid);
                MergeRequestCommentEdit mergeRequestCommentEdit = new MergeRequestCommentEdit();
                stringBuilder.Clear();
                stringBuilder.Append($"{payloadComment_.object_attributes.note}\n\n");
                stringBuilder.Append("no reviews are generated.");
                mergeRequestCommentEdit.Body = stringBuilder.ToString();
                try
                {
                    mergeRequestCommentClient.Edit(payloadComment_.object_attributes.id, mergeRequestCommentEdit);
                }
                catch
                {
                }
            }
        }

        private PayloadComment payloadComment_;
        private string language_;
    }
}
