using PRReviewAgent.Prompt;
using PRReviewAgent.Services;
using PRReviewAgent.Services.Coverage;
using Microsoft.Extensions.Logging.Abstractions;

namespace PRReviewAgent.Test;

[TestClass]
public class TestCoverageResolver
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ReviewContext Ctx(string path, string diff, string? astJson = null) =>
        new ReviewContext
        {
            Path = path,
            Filename = Path.GetFileName(path),
            Diff = diff,
            ChangedFile = string.Empty,
            AstJson = astJson,
        };

    // Minimal unified diff with one hunk containing the specified new-file lines.
    private static string MakeDiff(string path, int newStart, params string[] plusLines)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"--- a/{path}");
        sb.AppendLine($"+++ b/{path}");
        sb.AppendLine($"@@ -{newStart},{plusLines.Length} +{newStart},{plusLines.Length} @@");
        foreach (string l in plusLines)
            sb.AppendLine($"+{l}");
        return sb.ToString();
    }

    // Two-hunk diff: first hunk at h1Start (h1Count added lines), second at h2Start.
    private static string MakeDiffTwoHunks(string path,
        int h1Start, int h1Count, int h2Start, int h2Count)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"--- a/{path}");
        sb.AppendLine($"+++ b/{path}");
        sb.AppendLine($"@@ -{h1Start},{h1Count} +{h1Start},{h1Count} @@");
        for (int i = 0; i < h1Count; i++) sb.AppendLine($"+line{h1Start + i}");
        sb.AppendLine($"@@ -{h2Start},{h2Count} +{h2Start},{h2Count} @@");
        for (int i = 0; i < h2Count; i++) sb.AppendLine($"+line{h2Start + i}");
        return sb.ToString();
    }

    private static IssuesResponse Issues(params (string candidateId, string location)[] items)
    {
        var issues = items.Select(x => new Issue
        {
            location = x.location,
            problem = "problem",
            evidence = "evidence",
            impact = "impact",
            suggested_fix = "fix",
            confidence = "Major",
            candidate_id = x.candidateId,
        }).ToArray();
        return new IssuesResponse { issues = issues };
    }

    // -------------------------------------------------------------------------
    // ChangedRegionBuilder — ParseHunks
    // -------------------------------------------------------------------------

    [TestMethod]
    public void ParseHunks_SingleHunk_ReturnsChangedLines()
    {
        string diff = MakeDiff("foo.cpp", 100, "code A", "code B");
        var hunks = ChangedRegionBuilder.ParseHunks(diff);
        Assert.AreEqual(1, hunks.Count);
        CollectionAssert.AreEquivalent(new[] { 100, 101 }, hunks[0].ToList());
    }

    [TestMethod]
    public void ParseHunks_TwoHunks_ReturnsTwoLists()
    {
        string diff = MakeDiffTwoHunks("foo.cpp", 100, 2, 200, 3);
        var hunks = ChangedRegionBuilder.ParseHunks(diff);
        Assert.AreEqual(2, hunks.Count);
        Assert.AreEqual(2, hunks[0].Count);
        Assert.AreEqual(3, hunks[1].Count);
    }

    [TestMethod]
    public void ParseHunks_EmptyDiff_ReturnsEmpty()
    {
        Assert.AreEqual(0, ChangedRegionBuilder.ParseHunks(null).Count);
        Assert.AreEqual(0, ChangedRegionBuilder.ParseHunks(string.Empty).Count);
    }

    [TestMethod]
    public void ParseHunks_HunkWithOnlyRemovedLines_ReturnsEmpty()
    {
        string diff = "--- a/foo.cpp\n+++ b/foo.cpp\n@@ -100,2 +100,0 @@\n-removed A\n-removed B\n";
        var hunks = ChangedRegionBuilder.ParseHunks(diff);
        // Hunk has no '+' lines → excluded.
        Assert.AreEqual(0, hunks.Count);
    }

    // -------------------------------------------------------------------------
    // ChangedRegionBuilder — Build
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Build_SingleContext_ProducesRegionPerHunk()
    {
        string diff = MakeDiffTwoHunks("src/foo.cpp", 100, 2, 200, 1);
        var ctx = Ctx("src/foo.cpp", diff);

        var regions = ChangedRegionBuilder.Build(new[] { ctx });

        Assert.AreEqual(2, regions.Count);
        Assert.AreEqual("r0", regions[0].RegionId);
        Assert.AreEqual("r1", regions[1].RegionId);
        Assert.AreEqual("src/foo.cpp", regions[0].FilePath);
        Assert.AreEqual(100, regions[0].StartLine);
        Assert.AreEqual(101, regions[0].EndLine);
        Assert.AreEqual(200, regions[1].StartLine);
    }

    [TestMethod]
    public void Build_StableRegionIds_SortedByFilePath()
    {
        // File b comes before a alphabetically reversed — should sort by path.
        var ctxB = Ctx("src/b.cpp", MakeDiff("src/b.cpp", 50, "x"));
        var ctxA = Ctx("src/a.cpp", MakeDiff("src/a.cpp", 10, "y"));

        var regions = ChangedRegionBuilder.Build(new[] { ctxB, ctxA });

        Assert.AreEqual("r0", regions[0].RegionId);
        Assert.AreEqual("src/a.cpp", regions[0].FilePath); // a.cpp first
        Assert.AreEqual("r1", regions[1].RegionId);
        Assert.AreEqual("src/b.cpp", regions[1].FilePath);
    }

    [TestMethod]
    public void Build_PathNormalized()
    {
        var ctx = Ctx("src\\foo\\bar.cpp", MakeDiff("bar.cpp", 10, "x"));
        var regions = ChangedRegionBuilder.Build(new[] { ctx });
        Assert.AreEqual("src/foo/bar.cpp", regions[0].FilePath);
    }

    [TestMethod]
    public void Build_EmptyDiff_ProducesNoRegions()
    {
        var ctx = Ctx("foo.cpp", string.Empty);
        Assert.AreEqual(0, ChangedRegionBuilder.Build(new[] { ctx }).Count);
    }

    // -------------------------------------------------------------------------
    // ChangedRegionBuilder — FindContainingSymbol
    // -------------------------------------------------------------------------

    [TestMethod]
    public void FindContainingSymbol_LineInsideFunction_ReturnsName()
    {
        var fn = new PRReviewAget.Prompt.FunctionInfo { QualifiedName = "Foo.Bar", StartLine = 10, EndLine = 20 };
        string? sym = ChangedRegionBuilder.FindContainingSymbol(15, new List<PRReviewAget.Prompt.FunctionInfo> { fn });
        Assert.AreEqual("Foo.Bar", sym);
    }

    [TestMethod]
    public void FindContainingSymbol_LineOutsideAllFunctions_ReturnsNull()
    {
        var fn = new PRReviewAget.Prompt.FunctionInfo { QualifiedName = "Foo.Bar", StartLine = 10, EndLine = 20 };
        string? sym = ChangedRegionBuilder.FindContainingSymbol(50, new List<PRReviewAget.Prompt.FunctionInfo> { fn });
        Assert.IsNull(sym);
    }

    [TestMethod]
    public void FindContainingSymbol_NestedFunctions_ReturnsMostSpecific()
    {
        var outer = new PRReviewAget.Prompt.FunctionInfo { QualifiedName = "Outer", StartLine = 1, EndLine = 100 };
        var inner = new PRReviewAget.Prompt.FunctionInfo { QualifiedName = "Inner", StartLine = 10, EndLine = 20 };
        string? sym = ChangedRegionBuilder.FindContainingSymbol(15, new List<PRReviewAget.Prompt.FunctionInfo> { outer, inner });
        Assert.AreEqual("Inner", sym);
    }

    // =========================================================================
    // Spec Case 1 — Exact Line Match
    // =========================================================================

    [TestMethod]
    public void Case1_ExactLineMatch_Reported()
    {
        // Changed region: src/foo.cpp:100-102
        var ctx = Ctx("src/foo.cpp", MakeDiff("src/foo.cpp", 100, "a", "b", "c"));
        var regions = ChangedRegionBuilder.Build(new[] { ctx });

        // Candidate location: src/foo.cpp:101
        var response = Issues(("c0", "src/foo.cpp:101 doSomething"));
        var coverage = Turn1CoverageResolver.Resolve(regions, response, NullLogger.Instance);

        Assert.AreEqual(1, coverage.ReportedRegionIds.Count);
        Assert.IsTrue(coverage.ReportedRegionIds.Contains("r0"));
        Assert.AreEqual(0, coverage.UnreportedRegionIds.Count);
    }

    // =========================================================================
    // Spec Case 2 — Different Region in Same Function
    // =========================================================================

    [TestMethod]
    public void Case2_DifferentRegionSameFunction_OnlyMatchingRegionReported()
    {
        // Two separate hunks in the same function Foo::Run.
        // r0: lines 100-101, r1: lines 120-121
        string diff = MakeDiffTwoHunks("foo.cpp", 100, 2, 120, 2);
        var ctx = Ctx("foo.cpp", diff);
        var regions = ChangedRegionBuilder.Build(new[] { ctx });

        Assert.AreEqual(2, regions.Count); // r0, r1

        // Candidate at line 120 → should map to r1 only.
        var response = Issues(("c0", "foo.cpp:120 Foo::Run"));
        var coverage = Turn1CoverageResolver.Resolve(regions, response, NullLogger.Instance);

        Assert.IsTrue(coverage.ReportedRegionIds.Contains("r1"), "r1 must be reported");
        Assert.IsTrue(coverage.UnreportedRegionIds.Contains("r0"), "r0 must remain unreported");
    }

    // =========================================================================
    // Spec Case 3 — Symbol-Only With One Changed Region
    // =========================================================================

    [TestMethod]
    public void Case3_SymbolOnly_OneRegion_Reported()
    {
        // Single hunk in Foo::Open.
        string diff = MakeDiff("foo.cpp", 200, "x", "y", "z", "w", "v");
        string ast = MakeAstJson("Foo.Open", 198, 210);
        var ctx = Ctx("foo.cpp", diff, ast);
        var regions = ChangedRegionBuilder.Build(new[] { ctx });

        Assert.AreEqual(1, regions.Count);
        Assert.AreEqual("Foo.Open", regions[0].ContainingSymbol);

        // Candidate: symbol-only location.
        var response = Issues(("c0", "foo.cpp: Foo::Open"));
        var coverage = Turn1CoverageResolver.Resolve(regions, response, NullLogger.Instance);

        Assert.IsTrue(coverage.ReportedRegionIds.Contains("r0"), "Region must be reported via symbol match");
    }

    // =========================================================================
    // Spec Case 4 — Symbol-Only With Multiple Changed Regions → Conservative
    // =========================================================================

    [TestMethod]
    public void Case4_SymbolOnly_MultipleRegions_LeftUnmapped()
    {
        // Two separate hunks, both inside Foo::Open.
        string diff = MakeDiffTwoHunks("foo.cpp", 200, 2, 240, 2);
        string ast = MakeAstJson("Foo.Open", 198, 250);
        var ctx = Ctx("foo.cpp", diff, ast);
        var regions = ChangedRegionBuilder.Build(new[] { ctx });

        Assert.AreEqual(2, regions.Count);
        Assert.AreEqual("Foo.Open", regions[0].ContainingSymbol);
        Assert.AreEqual("Foo.Open", regions[1].ContainingSymbol);

        // Candidate: symbol-only → ambiguous, must not blindly mark both.
        var response = Issues(("c0", "foo.cpp: Foo::Open"));
        var coverage = Turn1CoverageResolver.Resolve(regions, response, NullLogger.Instance);

        Assert.AreEqual(0, coverage.ReportedRegionIds.Count, "Neither region should be reported");
        Assert.AreEqual(0, coverage.CandidateMappings[0].RegionIds.Count, "Candidate must be left unmapped");
    }

    // =========================================================================
    // Spec Case 5 — Unmapped Candidate
    // =========================================================================

    [TestMethod]
    public void Case5_UnmappedCandidate_PreservedIntact()
    {
        // One region in the file; candidate refers to a completely different file.
        var ctx = Ctx("src/foo.cpp", MakeDiff("src/foo.cpp", 10, "x"));
        var regions = ChangedRegionBuilder.Build(new[] { ctx });

        var response = Issues(
            ("c0", "src/other.cpp:99 doOther"),  // unmapped
            ("c1", "src/foo.cpp:10 doSomething")); // mapped
        var coverage = Turn1CoverageResolver.Resolve(regions, response, NullLogger.Instance);

        Assert.AreEqual(2, coverage.CandidateMappings.Count, "Both candidates must be present");
        var unmapped = coverage.CandidateMappings.Single(m => m.CandidateId == "c0");
        Assert.AreEqual(0, unmapped.RegionIds.Count, "c0 must be recorded as unmapped");
        Assert.AreEqual(1, coverage.CandidateMappings.Single(m => m.CandidateId == "c1").RegionIds.Count);
    }

    // =========================================================================
    // Spec Case 6 — Multiple Candidates for One Region
    // =========================================================================

    [TestMethod]
    public void Case6_MultipleCandidates_OneRegion_BothRetained()
    {
        var ctx = Ctx("foo.cpp", MakeDiff("foo.cpp", 420, "x"));
        var regions = ChangedRegionBuilder.Build(new[] { ctx });

        var response = Issues(
            ("c0", "foo.cpp:420 Environment::sample"),
            ("c1", "foo.cpp:420 sample"));
        var coverage = Turn1CoverageResolver.Resolve(regions, response, NullLogger.Instance);

        Assert.AreEqual(2, coverage.CandidateMappings.Count);
        Assert.IsTrue(coverage.CandidateMappings[0].RegionIds.Contains("r0"));
        Assert.IsTrue(coverage.CandidateMappings[1].RegionIds.Contains("r0"));
        // Region is reported (once, not duplicated).
        Assert.AreEqual(1, coverage.ReportedRegionIds.Count);
        Assert.IsTrue(coverage.ReportedRegionIds.Contains("r0"));
    }

    // =========================================================================
    // Spec Case 7 — One Candidate Covering Multiple Regions
    // =========================================================================

    [TestMethod]
    public void Case7_CandidateSpanningTwoRegions_MayMapToBoth()
    {
        // Two hunks in the same file. A candidate with a line in the middle of one region.
        string diff = MakeDiffTwoHunks("foo.cpp", 100, 2, 200, 2);
        var ctx = Ctx("foo.cpp", diff);
        var regions = ChangedRegionBuilder.Build(new[] { ctx });

        // Candidate points to line 101 (within r0) — maps to r0 only via line match.
        var response = Issues(("c0", "foo.cpp:101 doA"));
        var coverage = Turn1CoverageResolver.Resolve(regions, response, NullLogger.Instance);

        Assert.IsTrue(coverage.ReportedRegionIds.Contains("r0"));
        // r1 is unreported (only one candidate, which doesn't mention r1's range).
        Assert.IsTrue(coverage.UnreportedRegionIds.Contains("r1"));
    }

    // =========================================================================
    // Spec Case 8 — Path Normalization
    // =========================================================================

    [TestMethod]
    public void Case8a_PathNormalization_BackslashEquivalent()
    {
        var ctx = Ctx("src\\foo\\bar.cpp", MakeDiff("bar.cpp", 10, "x"));
        var regions = ChangedRegionBuilder.Build(new[] { ctx });

        // Candidate uses forward slashes.
        var response = Issues(("c0", "src/foo/bar.cpp:10 fn"));
        var coverage = Turn1CoverageResolver.Resolve(regions, response, NullLogger.Instance);

        Assert.IsTrue(coverage.ReportedRegionIds.Contains("r0"), "Backslash vs slash paths must match");
    }

    [TestMethod]
    public void Case8b_PathNormalization_SameBasenameInDifferentDirs_NotConfused()
    {
        // src/foo.cpp and tests/foo.cpp — different files with same basename.
        var ctxSrc   = Ctx("src/foo.cpp",   MakeDiff("src/foo.cpp",   10, "x"));
        var ctxTests = Ctx("tests/foo.cpp", MakeDiff("tests/foo.cpp", 50, "y"));
        var regions  = ChangedRegionBuilder.Build(new[] { ctxSrc, ctxTests });

        // Candidate refers to full path "src/foo.cpp:10" — must only match r0.
        var response = Issues(("c0", "src/foo.cpp:10 fn"));
        var coverage = Turn1CoverageResolver.Resolve(regions, response, NullLogger.Instance);

        Assert.AreEqual(1, coverage.ReportedRegionIds.Count);
        Assert.IsTrue(coverage.ReportedRegionIds.Contains("r0"), "Must match src/foo.cpp region only");
        Assert.IsTrue(coverage.UnreportedRegionIds.Contains("r1"), "tests/foo.cpp region must be unreported");
    }

    // =========================================================================
    // Spec Case 9 — Determinism
    // =========================================================================

    [TestMethod]
    public void Case9_Determinism_SameInputSameOutput()
    {
        string diff = MakeDiffTwoHunks("foo.cpp", 100, 2, 200, 2);
        var ctx = Ctx("foo.cpp", diff);
        var regions1 = ChangedRegionBuilder.Build(new[] { ctx });
        var regions2 = ChangedRegionBuilder.Build(new[] { ctx });

        Assert.AreEqual(regions1.Count, regions2.Count);
        for (int i = 0; i < regions1.Count; i++)
        {
            Assert.AreEqual(regions1[i].RegionId,         regions2[i].RegionId);
            Assert.AreEqual(regions1[i].FilePath,         regions2[i].FilePath);
            Assert.AreEqual(regions1[i].StartLine,        regions2[i].StartLine);
            Assert.AreEqual(regions1[i].EndLine,          regions2[i].EndLine);
            Assert.AreEqual(regions1[i].ContainingSymbol, regions2[i].ContainingSymbol);
            CollectionAssert.AreEqual(regions1[i].ChangedLines.ToList(), regions2[i].ChangedLines.ToList());
        }

        var response = Issues(("c0", "foo.cpp:101 fn"), ("c1", "foo.cpp:200 fn2"));
        var cov1 = Turn1CoverageResolver.Resolve(regions1, response, NullLogger.Instance);
        var cov2 = Turn1CoverageResolver.Resolve(regions2, response, NullLogger.Instance);

        CollectionAssert.AreEqual(
            cov1.ReportedRegionIds.OrderBy(x => x).ToList(),
            cov2.ReportedRegionIds.OrderBy(x => x).ToList());
        CollectionAssert.AreEqual(
            cov1.UnreportedRegionIds.OrderBy(x => x).ToList(),
            cov2.UnreportedRegionIds.OrderBy(x => x).ToList());
    }

    // =========================================================================
    // ParseLocation helper
    // =========================================================================

    [TestMethod]
    public void ParseLocation_LineAndSymbol()
    {
        var (file, line, sym) = Turn1CoverageResolver.ParseLocation("renderer.cpp:323 sampleFloat");
        Assert.AreEqual("renderer.cpp", file);
        Assert.AreEqual(323, line);
        Assert.AreEqual("sampleFloat", sym);
    }

    [TestMethod]
    public void ParseLocation_LineOnly()
    {
        var (file, line, sym) = Turn1CoverageResolver.ParseLocation("src/foo.cpp:101");
        Assert.AreEqual("src/foo.cpp", file);
        Assert.AreEqual(101, line);
        Assert.IsNull(sym);
    }

    [TestMethod]
    public void ParseLocation_SymbolOnly_SpaceAfterColon()
    {
        var (file, line, sym) = Turn1CoverageResolver.ParseLocation("renderer.cpp: Environment::sample");
        Assert.AreEqual("renderer.cpp", file);
        Assert.IsNull(line);
        Assert.AreEqual("Environment::sample", sym);
    }

    [TestMethod]
    public void ParseLocation_QualifiedSymbol_ColonColonSeparated()
    {
        var (file, line, sym) = Turn1CoverageResolver.ParseLocation("foo.cpp: Foo::Open");
        Assert.AreEqual("foo.cpp", file);
        Assert.IsNull(line);
        Assert.AreEqual("Foo::Open", sym);
    }

    // =========================================================================
    // SymbolsMatch helper
    // =========================================================================

    [TestMethod]
    public void SymbolsMatch_DotVsColonColon()
    {
        Assert.IsTrue(Turn1CoverageResolver.SymbolsMatch("Environment.sample", "Environment::sample"));
        Assert.IsTrue(Turn1CoverageResolver.SymbolsMatch("Foo.Open", "Foo::Open"));
    }

    [TestMethod]
    public void SymbolsMatch_CaseInsensitive()
    {
        Assert.IsTrue(Turn1CoverageResolver.SymbolsMatch("renderSphere", "RenderSphere"));
    }

    [TestMethod]
    public void SymbolsMatch_ParametersStripped()
    {
        Assert.IsTrue(Turn1CoverageResolver.SymbolsMatch("Foo.Bar", "Foo::Bar(int, float)"));
    }

    // =========================================================================
    // ReviewCoverage — reported/unreported split
    // =========================================================================

    [TestMethod]
    public void ReviewCoverage_EmptyRegions_EmptySets()
    {
        var cov = ReviewCoverage.Empty;
        Assert.AreEqual(0, cov.ChangedRegions.Count);
        Assert.AreEqual(0, cov.ReportedRegionIds.Count);
        Assert.AreEqual(0, cov.UnreportedRegionIds.Count);
    }

    [TestMethod]
    public void ReviewCoverage_UnmappedCandidate_AllRegionsUnreported()
    {
        var region = new ChangedRegion("r0", "foo.cpp", 10, 12,
            new[] { 10, 11, 12 }, "doSomething");
        var mapping = new CandidateMapping("c0", "bar.cpp:99 other", Array.Empty<string>());
        var cov = new ReviewCoverage(new[] { region }, new[] { mapping });

        Assert.AreEqual(0, cov.ReportedRegionIds.Count);
        Assert.IsTrue(cov.UnreportedRegionIds.Contains("r0"));
    }

    // =========================================================================
    // Full pipeline example matching the spec §14 scenario
    // =========================================================================

    [TestMethod]
    public void SpecExample_TenRegions_FiveReported()
    {
        // Simulate 10 separate single-line hunks in renderer.cpp.
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("--- a/renderer.cpp");
        sb.AppendLine("+++ b/renderer.cpp");
        int[] newLines = { 222, 323, 404, 420, 597, 679, 691, 721, 923, 973 };
        foreach (int l in newLines)
        {
            sb.AppendLine($"@@ -{l},1 +{l},1 @@");
            sb.AppendLine($"+code at {l}");
        }
        var ctx = Ctx("renderer.cpp", sb.ToString());
        var regions = ChangedRegionBuilder.Build(new[] { ctx });
        Assert.AreEqual(10, regions.Count);

        // Turn 1 reports 5 of them by line.
        var response = Issues(
            ("c0", "renderer.cpp:323 sampleFloat"),
            ("c1", "renderer.cpp:420 Environment::sample"),
            ("c2", "renderer.cpp:597 hitTriangle"),
            ("c3", "renderer.cpp:721 traceMesh"),
            ("c4", "renderer.cpp:973 renderMesh"));

        var coverage = Turn1CoverageResolver.Resolve(regions, response, NullLogger.Instance);

        Assert.AreEqual(5, coverage.ReportedRegionIds.Count);
        Assert.AreEqual(5, coverage.UnreportedRegionIds.Count);

        // Reported: r1 (323), r3 (420), r4 (597), r7 (721), r9 (973)
        Assert.IsTrue(coverage.ReportedRegionIds.Contains("r1"));
        Assert.IsTrue(coverage.ReportedRegionIds.Contains("r3"));
        Assert.IsTrue(coverage.ReportedRegionIds.Contains("r4"));
        Assert.IsTrue(coverage.ReportedRegionIds.Contains("r7"));
        Assert.IsTrue(coverage.ReportedRegionIds.Contains("r9"));

        // Unreported: r0 (222), r2 (404), r5 (679), r6 (691), r8 (923)
        Assert.IsTrue(coverage.UnreportedRegionIds.Contains("r0"));
        Assert.IsTrue(coverage.UnreportedRegionIds.Contains("r2"));
        Assert.IsTrue(coverage.UnreportedRegionIds.Contains("r5"));
        Assert.IsTrue(coverage.UnreportedRegionIds.Contains("r6"));
        Assert.IsTrue(coverage.UnreportedRegionIds.Contains("r8"));
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    // Minimal AstJson with one function at the given line range.
    private static string MakeAstJson(string qualifiedName, int startLine, int endLine)
    {
        var fn = new
        {
            qualified_name = qualifiedName,
            start_line = startLine,
            end_line = endLine,
            visibility = "public",
        };
        return System.Text.Json.JsonSerializer.Serialize(new { language = "Cpp", functions = new[] { fn } });
    }
}
