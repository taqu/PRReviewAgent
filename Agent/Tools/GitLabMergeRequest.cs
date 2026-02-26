using NGitLab.Models;
using System.ComponentModel;

namespace Agent.Tools
{
    public class GitLabMergeRequest
    {
        public GitLabMergeRequest(MergeRequest mergeRequest, Commit[] commits)
        {
            mergeRequest_ = mergeRequest;
            commits_ = commits;
        }

        [Description("Get the description of a merge request to review.")]
        public string GetMergeRequest()
        {
            return mergeRequest_.Description;
        }

        //[Description("List commits to review.")]
        //public string GetMergeRequest()
        //{
        //    return mergeRequest_.Description;
        //}

        private MergeRequest mergeRequest_;
        private Commit[] commits_;
    }
}
