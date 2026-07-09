using System.Text;
using System.Text.RegularExpressions;

namespace PRReviewAgent.Services
{
    public static class ContextFormatter
    {
        private static readonly Regex HunkRegex = new(@"@@ -\d+(?:,\d+)? \+(\d+)", RegexOptions.Compiled);

        public static string Format(string path, string content, string diff)
        {
            string extension = Path.GetExtension(path);
            StringBuilder stringBuilder = new StringBuilder();

            string[] lines = content.Split('\n');

            foreach (string inc in GetIncludes(lines))
            {
                stringBuilder.AppendLine(inc);
            }
            if(extension == ".cs")
            {
                foreach(string us in GetUsings(lines))
                {
                    stringBuilder.AppendLine(us);
                }
            }

            foreach (string ns in GetNamespaces(lines))
            {
                stringBuilder.AppendLine(ns);
            }

            HashSet<(int, int)> scopes = new();

            foreach (int line in GetChangedLines(diff))
            {
                (int begin, int end) scope = FindScope(lines, line);

                if (!scopes.Add(scope))
                {
                    continue;
                }

                stringBuilder.AppendLine();

                for (int i = scope.begin; i <= scope.end; i++)
                {
                    stringBuilder.AppendLine(lines[i]);
                }
            }

            return stringBuilder.ToString();
        }

        private static List<int> GetChangedLines(string diff)
        {
            List<int> lines = new();

            foreach (Match m in HunkRegex.Matches(diff))
            {
                lines.Add(int.Parse(m.Groups[1].Value));
            }

            return lines;
        }

        private static IEnumerable<string> GetIncludes(string[] lines)
        {
            foreach (string line in lines)
            {
                string s = line.TrimStart();
                if (s.StartsWith("#include"))
                {
                    yield return line;
                }
                else if (!string.IsNullOrWhiteSpace(s))
                {
                    break;
                }
            }
        }

        private static IEnumerable<string> GetUsings(string[] lines)
        {
            foreach (string line in lines)
            {
                string s = line.TrimStart();
                if (s.StartsWith("using"))
                {
                    yield return line;
                }
                else if (!string.IsNullOrWhiteSpace(s))
                {
                    break;
                }
            }
        }

        private static IEnumerable<string> GetNamespaces(string[] lines)
        {
            foreach (string line in lines)
            {
                string s = line.Trim();
                if (s.StartsWith("namespace "))
                {
                    yield return line;
                }
            }
        }

        private static (int begin, int end) FindScope(string[] lines, int changedLine)
        {
            int start = changedLine;

            while (start > 0)
            {
                if (lines[start].Contains("{"))
                {
                    break;
                }

                start--;
            }

            int depth = 0;
            bool entered = false;

            for (int i = start; i < lines.Length; i++)
            {
                foreach (char c in lines[i])
                {
                    if (c == '{')
                    {
                        depth++;
                        entered = true;
                    }

                    else if (c == '}')
                    {
                        depth--;

                        if (entered && depth == 0)
                            return (start, i);
                    }
                }
            }
            return (Math.Max(0, changedLine - 20), Math.Min(lines.Length - 1, changedLine + 20));
        }
    }
}

