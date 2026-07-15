using TreeSitter;

namespace PRReviewAget.Prompt;

// ─────────────────────────────────────────────────────────────────────────────
// Model
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One line in a unified diff hunk: marker is ' ', '+', or '-'.</summary>
public sealed record DiffHunkLine(char Marker, string Content);

/// <summary>
/// One semantically expanded diff hunk produced from the original unified diff.
/// All line numbers are 1-based.
/// </summary>
public sealed record ExpandedRegion(
    int OldStart,
    int OldCount,
    int NewStart,
    int NewCount,
    IReadOnlyList<DiffHunkLine> Lines)
{
    // Backward-compatible convenience properties
    public int StartLine => NewStart;
    public int EndLine   => NewStart + NewCount - 1;

    // Content of all lines (including removed), joined with '\n'
    public string Code => string.Join("\n", Lines.Select(static l => l.Content));

    // New-file line numbers of '+' (added) lines
    public IReadOnlyList<int> ChangedLines
    {
        get
        {
            var result = new List<int>();
            int n = NewStart;
            foreach (DiffHunkLine line in Lines)
            {
                if      (line.Marker == '+') result.Add(n++);
                else if (line.Marker == ' ') n++;
                // '-' does not advance the new-file counter
            }
            return result;
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DiffExpander
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Expands a unified diff to semantically meaningful AST regions using Tree-Sitter,
/// preserving the original '+'/'-'/' ' line markers throughout.
/// </summary>
public static class DiffExpander
{
    // ── Internal hunk representation ──────────────────────────────────────────

    sealed class ParsedHunk
    {
        public int OldStart;
        public int OldCount;
        public int NewStart;
        public int NewCount;
        public List<DiffHunkLine> Lines = new();

        public int NewEnd => NewStart + NewCount - 1;

        // New-file line numbers of '+' lines in this hunk
        public IEnumerable<int> ChangedNewLines()
        {
            int n = NewStart;
            foreach (DiffHunkLine line in Lines)
            {
                if      (line.Marker == '+') yield return n++;
                else if (line.Marker == ' ') n++;
            }
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses <paramref name="fileText"/>, infers the language from the diff header,
    /// and returns the minimal AST-bounded regions that contain each changed hunk.
    /// </summary>
    public static IReadOnlyList<ExpandedRegion> Expand(string fileText, string diffText, string diffPath)
    {
        string lang = InferLanguage(diffPath);
        string tsLang = GetTreeSitterName(lang);

        using Language language = new Language(tsLang);
        using Parser parser = new Parser(language);
        using Tree tree = parser.Parse(fileText)
            ?? throw new InvalidOperationException($"Tree-Sitter failed to parse source as {lang}");

        return ExpandCore(tree.RootNode, SplitLines(fileText),
                          ParseHunks(diffText), GetRules(lang));
    }

    /// <summary>
    /// Uses a pre-parsed <paramref name="tree"/> to expand the diff.
    /// The caller retains ownership of <paramref name="tree"/> and must dispose it.
    /// <paramref name="rules"/> defaults to <see cref="GenericExpansionRules"/> when null.
    /// </summary>
    public static IReadOnlyList<ExpandedRegion> Expand(
        Tree tree, string diffText, IExpansionRules? rules = null)
    {
        string fileText = tree.RootNode.Text;
        return ExpandCore(tree.RootNode, SplitLines(fileText),
                          ParseHunks(diffText),
                          rules ?? GenericExpansionRules.Instance);
    }

    /// <summary>Uses a pre-parsed <paramref name="tree"/> to expand the diff and serialize to unified diff.</summary>
    public static string ExpandDiff(Tree tree, string diffText, string diffPath)
    {
        string lang = InferLanguage(diffPath);
        IReadOnlyList<ExpandedRegion> regions = Expand(tree, diffText, GetRules(lang));
        return DiffFormatter.Format(regions, diffPath);
    }

    /// <summary>Wraps <see cref="Expand(string,string)"/> and serializes back to unified diff.</summary>
    public static string ExpandDiff(string fileText, string diffText, string diffPath)
    {
        IReadOnlyList<ExpandedRegion> regions = Expand(fileText, diffText, diffPath);
        return DiffFormatter.Format(regions, diffPath);
    }

    /// <summary>Wraps <see cref="Expand(Tree,string,IExpansionRules?)"/> and serializes back to unified diff.</summary>
    public static string ExpandDiff(Tree tree, string diffText, string diffPath, IExpansionRules? rules = null)
    {
        IReadOnlyList<ExpandedRegion> regions = Expand(tree, diffText, rules);
        return DiffFormatter.Format(regions, diffPath);
    }

    // ── Core expansion algorithm ──────────────────────────────────────────────

    static IReadOnlyList<ExpandedRegion> ExpandCore(
        Node root, string[] newFileLines, List<ParsedHunk> hunks, IExpansionRules rules)
    {
        if (hunks.Count == 0)
            return Array.Empty<ExpandedRegion>();

        // Step 1: For each hunk, find the enclosing AST region using '+' line positions.
        var hunkRegions = new List<(int AstStart, int AstEnd, ParsedHunk Hunk)>(hunks.Count);

        foreach (ParsedHunk hunk in hunks)
        {
            int astStart = hunk.NewStart;
            int astEnd   = Math.Max(hunk.NewStart, hunk.NewEnd);

            // Use '+' lines for AST lookup; for pure-deletion hunks fall back to NewStart.
            List<int> refLines = hunk.ChangedNewLines().ToList();
            if (refLines.Count == 0)
                refLines.Add(hunk.NewStart);

            foreach (int refLine in refLines)
            {
                Node? node = FindInnermostMeaningful(root, refLine, rules);
                if (node != null)
                {
                    astStart = Math.Min(astStart, node.StartPosition.Row + 1);
                    astEnd   = Math.Max(astEnd,   node.EndPosition.Row + 1);
                }
            }

            astStart = Math.Max(1, astStart);
            astEnd   = Math.Min(newFileLines.Length, astEnd);

            hunkRegions.Add((astStart, astEnd, hunk));
        }

        // Step 2: Sort by AST start.
        hunkRegions.Sort(static (a, b) => a.AstStart.CompareTo(b.AstStart));

        // Step 3: Merge overlapping AST regions, accumulating their hunks.
        var mergedGroups = new List<(int AstStart, int AstEnd, List<ParsedHunk> Hunks)>();

        foreach ((int astStart, int astEnd, ParsedHunk hunk) in hunkRegions)
        {
            if (mergedGroups.Count > 0 && astStart <= mergedGroups[^1].AstEnd)
            {
                (int s, int e, List<ParsedHunk> hs) = mergedGroups[^1];
                hs.Add(hunk);
                mergedGroups[^1] = (s, Math.Max(e, astEnd), hs);
            }
            else
            {
                mergedGroups.Add((astStart, astEnd, new List<ParsedHunk> { hunk }));
            }
        }

        // Step 4: Build an expanded ExpandedRegion for each merged group.
        var result = new List<ExpandedRegion>(mergedGroups.Count);
        foreach ((int astStart, int astEnd, List<ParsedHunk> groupHunks) in mergedGroups)
        {
            groupHunks.Sort(static (a, b) => a.NewStart.CompareTo(b.NewStart));
            result.Add(BuildExpandedRegion(newFileLines, astStart, astEnd, groupHunks));
        }

        return result;
    }

    // Builds a single ExpandedRegion by sandwiching the original diff hunks with
    // context lines drawn from the new file, according to the AST-determined boundaries.
    static ExpandedRegion BuildExpandedRegion(
        string[] newFileLines, int astStart, int astEnd, List<ParsedHunk> hunks)
    {
        var lines  = new List<DiffHunkLine>();
        int oldCount = 0;
        int newCount = 0;
        int cursor   = astStart; // next new-file line to emit

        // The old-file start is the new-file start adjusted by the first hunk's line delta.
        ParsedHunk first = hunks[0];
        int oldStart = astStart + (first.OldStart - first.NewStart);

        foreach (ParsedHunk hunk in hunks)
        {
            // Pre-hunk or inter-hunk context (identical in both file versions).
            while (cursor < hunk.NewStart)
            {
                lines.Add(new DiffHunkLine(' ', GetLine(newFileLines, cursor)));
                cursor++;
                oldCount++;
                newCount++;
            }

            // Original hunk lines — preserve every '+', '-', and ' ' marker.
            foreach (DiffHunkLine line in hunk.Lines)
            {
                lines.Add(line);
                if      (line.Marker == '+') newCount++;
                else if (line.Marker == '-') oldCount++;
                else { oldCount++; newCount++; }
            }

            cursor = hunk.NewEnd + 1;
        }

        // Post-hunk context.
        while (cursor <= astEnd)
        {
            lines.Add(new DiffHunkLine(' ', GetLine(newFileLines, cursor)));
            cursor++;
            oldCount++;
            newCount++;
        }

        return new ExpandedRegion(oldStart, oldCount, astStart, newCount, lines);
    }

    static string GetLine(string[] lines, int lineNum1Based)
    {
        int idx = lineNum1Based - 1;
        return idx >= 0 && idx < lines.Length ? lines[idx] : "";
    }

    // ── Tree navigation ───────────────────────────────────────────────────────

    /// <summary>
    /// DFS from <paramref name="root"/>: returns the innermost node whose type satisfies
    /// <paramref name="rules"/> and whose line span contains <paramref name="lineNum"/>.
    /// Returns null when no meaningful node covers the line.
    /// </summary>
    static Node? FindInnermostMeaningful(Node root, int lineNum, IExpansionRules rules)
    {
        Node? best = null;

        void Walk(Node node)
        {
            int startLine = node.StartPosition.Row + 1;
            int endLine   = node.EndPosition.Row + 1;

            if (lineNum < startLine || lineNum > endLine) return;

            if (rules.IsMeaningfulNode(node.Type))
                best = node;

            foreach (Node child in node.Children)
                Walk(child);
        }

        Walk(root);
        return best;
    }

    // ── Diff parsing ──────────────────────────────────────────────────────────

    static List<ParsedHunk> ParseHunks(string diffText)
    {
        var hunks = new List<ParsedHunk>();
        ParsedHunk? current = null;

        foreach (string line in SplitLines(diffText))
        {
            if (line.StartsWith("@@ "))
            {
                current = new ParsedHunk();
                ParseHunkHeader(line, current);
                hunks.Add(current);
            }
            else if (current != null)
            {
                if (line.StartsWith("diff ") || line.StartsWith("index ")
                 || line.StartsWith("--- ") || line.StartsWith("+++ "))
                {
                    current = null;
                }
                else if (line.Length > 0)
                {
                    char marker = line[0];
                    if (marker == '+' || marker == '-' || marker == ' ')
                        current.Lines.Add(new DiffHunkLine(marker, line.Substring(1)));
                }
            }
        }

        return hunks;
    }

    static void ParseHunkHeader(string line, ParsedHunk hunk)
    {
        // @@ -OldStart[,OldCount] +NewStart[,NewCount] @@
        int minus = line.IndexOf('-');
        int plus  = minus >= 0 ? line.IndexOf('+', minus) : -1;
        if (minus < 0 || plus < 0) return;

        ParseLineRange(line, minus + 1, out hunk.OldStart, out hunk.OldCount);
        ParseLineRange(line, plus  + 1, out hunk.NewStart, out hunk.NewCount);
    }

    static void ParseLineRange(string line, int start, out int lineStart, out int count)
    {
        int comma = line.IndexOf(',', start);
        int space = line.IndexOf(' ', start);

        int startEnd = comma >= 0 && (space < 0 || comma < space) ? comma : space;
        if (startEnd < 0) startEnd = line.Length;

        lineStart = int.TryParse(line.Substring(start, startEnd - start), out int s) ? s : 1;

        if (comma >= 0 && comma < (space >= 0 ? space : line.Length))
        {
            int countEnd = space >= 0 ? space : line.Length;
            count = int.TryParse(line.Substring(comma + 1, countEnd - comma - 1), out int c) ? c : 1;
        }
        else
        {
            count = 1;
        }
    }

    /// <summary>
    /// Returns the sorted set of 1-based new-file line numbers that appear as <c>+</c> lines
    /// in the unified diff.
    /// </summary>
    public static IReadOnlyList<int> ParseChangedLines(string diffText) =>
        ParseHunks(diffText)
            .SelectMany(static h => h.ChangedNewLines())
            .Distinct()
            .OrderBy(static x => x)
            .ToList();

    /// <summary>
    /// Extracts the target filename from the <c>+++ b/…</c> line of the diff.
    /// Returns <c>"file"</c> when the header is absent.
    /// </summary>
    public static string GetFilenameFromDiff(string diffText)
    {
        foreach (string line in SplitLines(diffText))
        {
            if (line.StartsWith("+++ "))
            {
                string path = line.Substring(4).TrimEnd();
                if (path.StartsWith("b/")) path = path.Substring(2);
                return path;
            }
        }
        return "file";
    }

    // ── Language inference ────────────────────────────────────────────────────

    internal static string InferLanguage(string diffPath)
    {
        string fileextension = Path.GetExtension(diffPath);
        return fileextension.ToLowerInvariant() switch
        {
            ".cpp" or ".cc" or ".cxx" or ".h" or ".hpp" or ".hxx" => "Cpp",
            ".c"                                                    => "C",
            ".cs"                                                   => "CSharp",
            ".rs"                                                   => "Rust",
            ".py"                                                   => "Python",
            ".js" or ".mjs" or ".cjs"                              => "JavaScript",
            ".ts" or ".tsx"                                        => "TypeScript",
            _                                                       => "Unknown"
        };
    }

    static string GetTreeSitterName(string lang) => lang switch
    {
        "Cpp"        => "Cpp",
        "C"          => "C",
        "CSharp"     => "C-Sharp",
        "Rust"       => "Rust",
        "Python"     => "Python",
        "JavaScript" => "JavaScript",
        "TypeScript" => "TypeScript",
        _            => "C"
    };

    static IExpansionRules GetRules(string lang) => lang switch
    {
        "Cpp" or "C"                 => CppExpansionRules.Instance,
        "CSharp"                     => CSharpExpansionRules.Instance,
        "Rust"                       => RustExpansionRules.Instance,
        "Python"                     => PythonExpansionRules.Instance,
        "JavaScript" or "TypeScript" => JsExpansionRules.Instance,
        _                            => GenericExpansionRules.Instance,
    };

    // ── Utilities ─────────────────────────────────────────────────────────────

    static string[] SplitLines(string text) =>
        text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

    /// <summary>Extracts lines [startLine..endLine] (1-based, inclusive) from the array.</summary>
    internal static string ExtractCode(string[] lines, int startLine, int endLine)
    {
        int from = Math.Max(0, startLine - 1);
        int to   = Math.Min(lines.Length - 1, endLine - 1);
        if (from > to) return "";
        return string.Join("\n", lines, from, to - from + 1);
    }
}
