
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TreeSitter;

namespace PRReviewAget.Prompt;

// ─────────────────────────────────────────────────────────────────────────────
// Model
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One semantically expanded region produced from a diff hunk.
/// All line numbers are 1-based.
/// </summary>
public sealed record ExpandedRegion(
    int StartLine,
    int EndLine,
    string Code,
    IReadOnlyList<int> ChangedLines
);

// ─────────────────────────────────────────────────────────────────────────────
// DiffExpander
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Expands a unified diff to semantically meaningful AST regions using Tree-Sitter.
/// </summary>
public static class DiffExpander
{
    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses <paramref name="fileText"/>, infers the language from the diff header,
    /// and returns the minimal AST-bounded regions that contain each changed line.
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
                          ParseChangedLines(diffText), GetRules(lang));
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
                          ParseChangedLines(diffText),
                          rules ?? GenericExpansionRules.Instance);
    }

    /// <summary>
    /// Uses a pre-parsed <paramref name="tree"/> to expand the diff.
    /// The caller retains ownership of <paramref name="tree"/> and must dispose it.
    /// <paramref name="rules"/> defaults to <see cref="GenericExpansionRules"/> when null.
    /// </summary>
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
        return DiffFormatter.Format(regions,diffPath);
    }

    // ── Core expansion algorithm ──────────────────────────────────────────────

    static IReadOnlyList<ExpandedRegion> ExpandCore(
        Node root, string[] lines, IReadOnlyList<int> changedLines, IExpansionRules rules)
    {
        if (changedLines.Count == 0)
            return Array.Empty<ExpandedRegion>();

        // Step 1: map each changed line to the extent of its innermost meaningful node
        List<(int Start, int End, int ChangedLine)> raw = new List<(int Start, int End, int ChangedLine)>(changedLines.Count);
        foreach (int cl in changedLines)
        {
            Node? node = FindInnermostMeaningful(root, cl, rules);
            raw.Add(node != null
                ? (node.StartPosition.Row + 1, node.EndPosition.Row + 1, cl)
                : (cl, cl, cl));
        }

        // Step 2: sort by region start, then by changed line
        raw.Sort(static (a, b) =>
            a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.ChangedLine.CompareTo(b.ChangedLine));

        // Step 3: merge overlapping regions, accumulating their changed lines
        List<(int Start, int End, List<int> Changed)> merged = new List<(int Start, int End, List<int> Changed)>();
        foreach ((int start, int end, int cl) in raw)
        {
            if (merged.Count > 0 && start <= merged[^1].End)
            {
                (int Start, int End, List<int> Changed) last = merged[^1];
                last.Changed.Add(cl);
                merged[^1] = (last.Start, Math.Max(last.End, end), last.Changed);
            }
            else
            {
                merged.Add((start, end, new List<int> { cl }));
            }
        }

        // Step 4: build final records
        return merged
            .Select(r => new ExpandedRegion(
                r.Start,
                r.End,
                ExtractCode(lines, r.Start, r.End),
                r.Changed.Distinct().OrderBy(static x => x).ToArray()))
            .ToList();
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

            // Prune: node does not contain the target line
            if (lineNum < startLine || lineNum > endLine) return;

            // Deeper DFS visits overwrite best → innermost wins
            if (rules.IsMeaningfulNode(node.Type))
                best = node;

            foreach (Node child in node.Children)
                Walk(child);
        }

        Walk(root);
        return best;
    }

    // ── Diff parsing ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the sorted set of 1-based new-file line numbers that appear as <c>+</c> lines
    /// in the unified diff.
    /// </summary>
    public static IReadOnlyList<int> ParseChangedLines(string diffText)
    {
        SortedSet<int> result = new SortedSet<int>();
        int lineNum = 0;
        bool inHunk = false;

        foreach (string line in SplitLines(diffText))
        {
            if (line.StartsWith("@@ "))
            {
                int plus = line.IndexOf('+');
                if (plus < 0) { inHunk = false; continue; }
                int end = line.IndexOfAny(new[] { ',', ' ' }, plus + 1);
                string startStr = end > plus + 1
                    ? line.Substring(plus + 1, end - plus - 1)
                    : line.Substring(plus + 1);
                inHunk = int.TryParse(startStr, out lineNum);
            }
            else if (inHunk)
            {
                if (line.StartsWith("+"))       { result.Add(lineNum++); }
                else if (line.StartsWith("-"))  { /* removed line — no new-file number */ }
                else if (line.StartsWith(" "))  { lineNum++; }
                else if (line.StartsWith("diff ") || line.StartsWith("index ")
                      || line.StartsWith("--- ") || line.StartsWith("+++ "))
                {
                    inHunk = false;
                }
            }
        }

        return result.ToList();
    }

    // ── Language inference ────────────────────────────────────────────────────

    internal static string InferLanguage(string diffPath)
    {
        return Path.GetExtension(diffPath).ToLowerInvariant() switch
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
        "Cpp"                       => "Cpp",
        "C"                         => "C",
        "CSharp"                    => "C-Sharp",
        "Rust"                      => "Rust",
        "Python"                    => "Python",
        "JavaScript"                => "JavaScript",
        "TypeScript"                => "TypeScript",
        _                           => "C"   // safe fallback: C grammar
    };

    static IExpansionRules GetRules(string lang) => lang switch
    {
        "Cpp" or "C"                => CppExpansionRules.Instance,
        "CSharp"                    => CSharpExpansionRules.Instance,
        "Rust"                      => RustExpansionRules.Instance,
        "Python"                    => PythonExpansionRules.Instance,
        "JavaScript" or "TypeScript" => JsExpansionRules.Instance,
        _                           => GenericExpansionRules.Instance,
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
