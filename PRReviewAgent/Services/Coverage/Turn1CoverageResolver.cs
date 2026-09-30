using Microsoft.Extensions.Logging;
using PRReviewAgent.Prompt;

namespace PRReviewAgent.Services.Coverage;

public static class Turn1CoverageResolver
{
    /// <summary>
    /// Maps Turn 1 candidates to changed regions using deterministic location-based matching.
    /// Does not modify the candidates or alter review behavior.
    /// </summary>
    public static ReviewCoverage Resolve(
        IReadOnlyList<ChangedRegion> regions,
        IssuesResponse issuesResponse,
        ILogger logger)
    {
        if (regions.Count == 0 || issuesResponse.issues.Length == 0)
            return new ReviewCoverage(regions, Array.Empty<CandidateMapping>());

        var mappings = new List<CandidateMapping>(issuesResponse.issues.Length);

        foreach (Issue issue in issuesResponse.issues)
        {
            string candidateId = issue.candidate_id ?? string.Empty;
            string location = issue.location ?? string.Empty;
            var (filePath, lineNumber, symbol) = ParseLocation(location);
            string normalizedFile = NormalizePathForMatching(filePath);

            List<string> regionIds = new();

            // 1. Exact line overlap (preferred).
            if (lineNumber.HasValue)
                regionIds = MatchByLine(regions, normalizedFile, lineNumber.Value);

            // 2. Symbol-level fallback when no line match resolved.
            if (regionIds.Count == 0 && !string.IsNullOrEmpty(symbol))
                regionIds = MatchBySymbol(regions, normalizedFile, symbol);

            mappings.Add(new CandidateMapping(candidateId, location, regionIds));
        }

        return new ReviewCoverage(regions, mappings);
    }

    // Returns region IDs whose changed-line range overlaps the candidate line.
    internal static List<string> MatchByLine(
        IReadOnlyList<ChangedRegion> regions, string normalizedFile, int line)
    {
        var result = new List<string>();
        foreach (ChangedRegion r in regions)
        {
            if (!PathsMatch(r.FilePath, normalizedFile)) continue;
            if (r.StartLine <= line && line <= r.EndLine)
                result.Add(r.RegionId);
        }
        return result;
    }

    // Conservative symbol match: only resolves when exactly one region carries that symbol.
    internal static List<string> MatchBySymbol(
        IReadOnlyList<ChangedRegion> regions, string normalizedFile, string symbol)
    {
        var candidates = regions
            .Where(r => string.IsNullOrEmpty(normalizedFile) || PathsMatch(r.FilePath, normalizedFile))
            .Where(r => r.ContainingSymbol != null && SymbolsMatch(r.ContainingSymbol, symbol))
            .ToList();

        // Multiple regions in same symbol → leave unmapped (spec §8, §9).
        return candidates.Count == 1
            ? new List<string> { candidates[0].RegionId }
            : new List<string>();
    }

    /// <summary>
    /// Parses a candidate location string such as:
    ///   "renderer.cpp:323 sampleFloat"
    ///   "renderer.cpp: Environment::sample"
    ///   "src/foo.cpp:101"
    ///   "foo.cpp: Foo::Open"
    /// </summary>
    internal static (string FilePath, int? LineNumber, string? Symbol) ParseLocation(string location)
    {
        string s = (location ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(s)) return (string.Empty, null, null);

        int colonIdx = s.IndexOf(':');
        if (colonIdx < 0) return (s, null, null);

        string filePart = s[..colonIdx].Trim();
        string rest = s[(colonIdx + 1)..].Trim();
        if (string.IsNullOrEmpty(rest)) return (filePart, null, null);

        // Determine if rest starts with a decimal line number.
        int spaceIdx = rest.IndexOfAny(new[] { ' ', '\t' });
        string firstToken = spaceIdx >= 0 ? rest[..spaceIdx] : rest;

        if (int.TryParse(firstToken, out int lineNum) && lineNum > 0)
        {
            string? sym = null;
            if (spaceIdx >= 0)
            {
                sym = rest[(spaceIdx + 1)..].Trim();
                if (string.IsNullOrEmpty(sym)) sym = null;
            }
            return (filePart, lineNum, sym);
        }

        // rest is a symbol name.
        return (filePart, null, string.IsNullOrEmpty(rest) ? null : rest);
    }

    internal static bool SymbolsMatch(string a, string b)
        => NormalizeSymbol(a).Equals(NormalizeSymbol(b), StringComparison.OrdinalIgnoreCase);

    internal static string NormalizeSymbol(string sym)
    {
        int paren = sym.IndexOf('(');
        if (paren >= 0) sym = sym[..paren].Trim();
        return sym.Replace("::", ".").Replace("->", ".").Trim();
    }

    internal static string NormalizePathForMatching(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        return path.Replace('\\', '/').TrimStart('/');
    }

    // True if the two (already normalized) paths refer to the same file.
    // Handles: exact match, basename-only candidate, suffix match.
    internal static bool PathsMatch(string regionPath, string candidatePath)
    {
        if (string.IsNullOrEmpty(candidatePath)) return false;
        if (regionPath.Equals(candidatePath, StringComparison.OrdinalIgnoreCase)) return true;

        // Basename-only candidate (no slash) → match by filename.
        // Guarded against confusing src/foo.cpp and tests/foo.cpp.
        if (!candidatePath.Contains('/'))
        {
            string regionBasename = Path.GetFileName(regionPath);
            return regionBasename.Equals(candidatePath, StringComparison.OrdinalIgnoreCase);
        }

        // Partial path: region = "src/render/foo.cpp", candidate = "render/foo.cpp"
        if (regionPath.EndsWith("/" + candidatePath, StringComparison.OrdinalIgnoreCase)) return true;
        if (candidatePath.EndsWith("/" + regionPath, StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }
}
