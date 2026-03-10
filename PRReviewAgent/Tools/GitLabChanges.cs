using System.ComponentModel;

namespace PRReviewAgent.Tools
{
    public class GitLabChanges
    {
        public class Change
        {
            [Description("path")]
            public string path { get;set; }
            [Description("summary")]
            public string summary { get; set; }
        }

        public class Difference
        {
            public Change Change { get; set; }
            public NGitLab.Models.Diff Diff { get; set; }
        }
        public record Changes(
            [Description("Changed file paths and summaries")]
            Change[] changes
        );

        [Description("Get changed file paths and summary of diffs for a pull request in json format.")]
        public string GetChangeFilePaths()
        {
            changes_.Clear();
            foreach(Difference diff in diffs_)
            {
                changes_.Add(diff.Change);
            }
            return Newtonsoft.Json.JsonConvert.SerializeObject(new Changes(changes_.ToArray()));
        }

        public List<Difference> Diffs => diffs_;

        public void ClearDiffs()
        {
            diffs_.Clear();
        }

        public void AddDiff(NGitLab.Models.Diff diff, Func<string, bool> isTarget)
        {
            if (diff.IsDeletedFile)
            {
                return;
            }
            if (diff.IsRenamedFile)
            {
                return;
            }
            if (string.IsNullOrEmpty(diff.Difference))
            {
                return;
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
                    return;
                }
            }
            else
            {
                path = diff.NewPath;
            }
            if (!isTarget(path))
            {
                return;
            }
            diffs_.Add(new Difference { Change = new Change { path = path, summary = string.Empty }, Diff = diff });
        }

        public Difference? Find(string path)
        {
            return diffs_.Find(d => d.Change.path == path);
        }

        public string? GetReviewDiffs(string[] paths, string language)
        {
            string? template = Context.Instance.Settings.GetReviewTemplate(language);
            if(null == template)
            {
                return null;
            }
            stringBuilder_.Clear();
            stringBuilder_.Append(template);
            stringBuilder_.Append("\n\n");
                
            foreach (string path in paths)
            {
                Difference? diff = Find(path);
                if (diff == null)
                {
                    continue;
                }
                stringBuilder_.Append("# ").Append(path).Append("\n");
                stringBuilder_.Append(diff.Diff.Difference).Append("\n");
            }
            return stringBuilder_.ToString();
        }

        private List<Change> changes_ = new List<Change>();
        private List<Difference> diffs_ = new List<Difference>();
        private System.Text.StringBuilder stringBuilder_ = new System.Text.StringBuilder();
    }
}
