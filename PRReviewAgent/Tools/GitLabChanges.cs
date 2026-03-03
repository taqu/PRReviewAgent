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

        public void AddDiff(NGitLab.Models.Diff diff)
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

            diffs_.Add(new Difference { Change = new Change { path = path, summary = string.Empty }, Diff = diff });
        }
        private List<Change> changes_ = new List<Change>();
        private List<Difference> diffs_ = new List<Difference>();
    }
}
