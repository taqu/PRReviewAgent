using NGitLab;
using NGitLab.Models;
using PRReviewAgent.Services.GitLabWebhook;

namespace PRReviewAgent.Services
{
    public static class ContextCollector
    {
        public static async Task CollectAsync(
            GitLabClient client,
            PayloadMergeRequest mergeRequest,
            List<ReviewContext> reviewContexts)
        {
            IRepositoryClient repository = client.GetRepository(mergeRequest.target_project_id);

            foreach (ReviewContext reviewContext in reviewContexts)
            {
                string changedFile = string.Empty;
                try
                {
                    FileData file = await repository.Files.GetAsync(
                        reviewContext.Path,
                        mergeRequest.source_branch);

                    reviewContext.ChangedFile = file.Content;
                }
                catch
                {
                    continue;
                }
            }
            foreach (ReviewContext reviewContext in reviewContexts)
            {
                string? pairPath = GuessPair(reviewContext.Path);
                if (pairPath != null)
                {
                    ReviewContext? pair = FindPair(pairPath, reviewContexts);
                    if(null != pair)
                    {
                        reviewContext.PairPath = pair.Path;
                        reviewContext.PairDiff = pair.Diff;
                        reviewContext.PairFile = pair.ChangedFile;
                        continue;
                    }
                    try
                    {
                        FileData file = await repository.Files.GetAsync(
                            pairPath,
                            mergeRequest.source_branch);

                        reviewContext.PairPath = pairPath;
                        reviewContext.PairFile = file.Content;
                        continue;
                    }
                    catch
                    {
                    }
                }
                pairPath = GuessPair2(reviewContext.Path);
                if (pairPath != null)
                {
                    ReviewContext? pair = FindPair(pairPath, reviewContexts);
                    if (null != pair)
                    {
                        reviewContext.PairPath = pair.Path;
                        reviewContext.PairDiff = pair.Diff;
                        reviewContext.PairFile = pair.ChangedFile;
                        continue;
                    }
                    try
                    {
                        FileData file = await repository.Files.GetAsync(
                            pairPath,
                            mergeRequest.source_branch);

                        reviewContext.PairPath = pairPath;
                        reviewContext.PairFile = file.Content;
                        continue;
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static string? GuessPair(string path)
        {
            string ext = Path.GetExtension(path);
            string? pairPath = null;
            switch (ext)
            {
                case ".cpp":
                case ".cc":
                case ".cxx":
                case ".c":
                    pairPath = Path.ChangeExtension(path, ".h");
                    pairPath = pairPath.Replace("src", "include");
                    break;
                case ".h":
                case ".hpp":
                    pairPath = Path.ChangeExtension(path, ".cpp");
                    pairPath = pairPath.Replace("include", "src");
                    break;
                default:
                    return null;
            }
            return pairPath;
        }

        private static string? GuessPair2(string path)
        {
            string ext = Path.GetExtension(path);
            string? pairPath = null;
            switch (ext)
            {
                case ".cpp":
                case ".cc":
                case ".cxx":
                case ".c":
                    pairPath = Path.ChangeExtension(path, ".h");
                    pairPath = pairPath.Replace("src", "include");
                    break;
                case ".h":
                    pairPath = Path.ChangeExtension(path, ".c");
                    pairPath = pairPath.Replace("include", "src");
                    break;
                case ".hpp":
                    pairPath = Path.ChangeExtension(path, ".cpp");
                    pairPath = pairPath.Replace("include", "src");
                    break;
                default:
                    return null;
            }
            return pairPath;
        }


        private static ReviewContext? FindPair(string path, List<ReviewContext> reviewContexts)
        {
            foreach (ReviewContext reviewContext in reviewContexts)
            {
                if (reviewContext.Path == path)
                {
                    return reviewContext;
                }
            }
            return null;
        }
    }
}
