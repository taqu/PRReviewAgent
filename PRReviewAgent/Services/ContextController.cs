using NGitLab;
using NGitLab.Models;
using PRReviewAgent.Services.GitLabWebhook;

namespace PRReviewAgent.Services
{
    public static class ContextCollector
    {
        /// <summary>
        /// Find pair based on filenames
        /// </summary>
        /// <param name="reviewContexts"></param>
        public static void FindPair(List<ReviewContext> reviewContexts)
        {
            foreach (ReviewContext reviewContext in reviewContexts)
            {
                string? pairFilename = GuessPairFileName1(reviewContext.Path);
                if (pairFilename == null)
                {
                    continue;
                }
                ReviewContext? pair = reviewContexts.Find(c => Path.GetFileName(c.Path) == pairFilename);
                if (null != pair)
                {
                    reviewContext.PairPath = pair.Path;
                    reviewContext.PairDiff = pair.Diff;
                    reviewContext.PairFile = pair.ChangedFile;
                    continue;
                }
                pairFilename = GuessPairFileName2(reviewContext.Path);
                if (pairFilename == null)
                {
                    continue;
                }
                if (null != pair)
                {
                    reviewContext.PairPath = pair.Path;
                    reviewContext.PairDiff = pair.Diff;
                    reviewContext.PairFile = pair.ChangedFile;
                    continue;
                }
            }
        }

        private static string? GuessPairFileName1(string path)
        {
            string ext = Path.GetExtension(path);
            string filename = Path.GetFileName(path);
            switch (ext)
            {
                case ".cpp":
                case ".cc":
                case ".cxx":
                case ".c":
                    return Path.ChangeExtension(filename, ".h");
                case ".h":
                case ".hpp":
                    return Path.ChangeExtension(filename, ".cpp");
                default:
                    return null;
            }
        }

        private static string? GuessPairFileName2(string path)
        {
            string ext = Path.GetExtension(path);
            string filename = Path.GetFileName(path);
            switch (ext)
            {
                case ".cpp":
                case ".cc":
                case ".cxx":
                case ".c":
                    return Path.ChangeExtension(filename, ".h");
                case ".h":
                    return Path.ChangeExtension(filename, ".c");
                case ".hpp":
                    return Path.ChangeExtension(filename, ".cpp");
                default:
                    return null;
            }
        }
    }
}
