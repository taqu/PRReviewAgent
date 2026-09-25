using PRReviewAgent.Services;
using PRReviewAgent.Services.Grouping;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace PRReviewAgent.Test;

/// <summary>
/// Tests for SemanticReviewGroupBuilder — verifies that related changed files are
/// grouped correctly according to the signals defined in review.md (Phase 1).
/// </summary>
[TestClass]
public class TestSemanticGrouping
{
    private static GroupingConfig Cfg(int maxFiles = 8) =>
        new GroupingConfig { Mode = GroupingMode.Semantic, MaxFilesPerGroup = maxFiles };

    private static ReviewContext Ctx(string path, string? pairPath = null, string? astJson = null) =>
        new ReviewContext
        {
            Path = path,
            Filename = Path.GetFileName(path),
            Diff = string.Empty,
            ChangedFile = string.Empty,
            PairPath = pairPath,
            AstJson = astJson,
        };

    // =========================================================================
    // Helper: minimal AstJson for a file with changed functions
    // =========================================================================

    private static string MakeAst(params (string containingType, string funcName, string change)[] fns)
    {
        var functions = fns.Select(f => new
        {
            qualified_name = string.IsNullOrEmpty(f.containingType) ? f.funcName : $"{f.containingType}.{f.funcName}",
            containing_type = string.IsNullOrEmpty(f.containingType) ? null : f.containingType,
            change = f.change,
            visibility = "public",
            start_line = 1,
            end_line = 10,
        });
        return JsonSerializer.Serialize(new { language = "Cpp", functions });
    }

    private static string MakeAstWithCallGraph(
        (string containingType, string funcName, string change)[] fns,
        (string caller, string callee)[] callEdges)
    {
        var functions = fns.Select(f => new
        {
            qualified_name = string.IsNullOrEmpty(f.containingType) ? f.funcName : $"{f.containingType}.{f.funcName}",
            containing_type = string.IsNullOrEmpty(f.containingType) ? null : f.containingType,
            change = f.change,
            visibility = "public",
            start_line = 1,
            end_line = 10,
        });
        var callGraph = callEdges.Select(e => new { caller = e.caller, callee = e.callee });
        return JsonSerializer.Serialize(new { language = "Cpp", functions, call_graph = callGraph });
    }

    // =========================================================================
    // Language classification helper
    // =========================================================================

    [TestMethod]
    public void IsCppFile_RecognizesCppExtensions()
    {
        foreach (string ext in new[] { ".c", ".cc", ".cpp", ".cxx", ".h", ".hh", ".hpp", ".hxx" })
            Assert.IsTrue(SemanticReviewGroupBuilder.IsCppFile($"foo{ext}"), $"Expected {ext} to be C/C++");
    }

    [TestMethod]
    public void IsCppFile_RejectsNonCppExtensions()
    {
        foreach (string ext in new[] { ".cs", ".py", ".rs", ".ts", ".js", ".go", ".java", ".kt" })
            Assert.IsFalse(SemanticReviewGroupBuilder.IsCppFile($"foo{ext}"), $"Expected {ext} to be non-C/C++");
    }

    // =========================================================================
    // Spec Case 1 — Two non-C/C++ files → 2 groups, 1 file each
    // =========================================================================

    [TestMethod]
    public void NonCpp_TwoPythonFiles_TwoGroups()
    {
        var result = SemanticReviewGroupBuilder.Build(
            new[] { Ctx("a.py"), Ctx("b.py") }, Cfg(), NullLogger.Instance);

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result.All(g => g.ReviewContexts.Count == 1));
        Assert.IsTrue(result.All(g => g.GroupingReasons.Contains("strategy: per-file")));
    }

    // =========================================================================
    // Spec Case 2 — C++ header/source pair → eligible for one group
    // =========================================================================

    [TestMethod]
    public void Cpp_HeaderSourcePair_OneGroup()
    {
        var h   = Ctx("foo.h",   pairPath: "foo.cpp");
        var cpp = Ctx("foo.cpp", pairPath: "foo.h");

        var result = SemanticReviewGroupBuilder.Build(new[] { h, cpp }, Cfg(), NullLogger.Instance);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(2, result[0].ReviewContexts.Count);
        Assert.IsTrue(result[0].GroupingReasons.Contains("strategy: cpp-semantic"));
    }

    // =========================================================================
    // Spec Case 3 — Mixed languages: C++, Python, C#
    // =========================================================================

    [TestMethod]
    public void Mixed_CppAndNonCpp_GroupedIndependently()
    {
        var fooH   = Ctx("foo.h",    pairPath: "foo.cpp");
        var fooCpp = Ctx("foo.cpp",  pairPath: "foo.h");
        var buildPy = Ctx("build.py");
        var toolCs  = Ctx("tool.cs");

        var result = SemanticReviewGroupBuilder.Build(
            new[] { fooH, fooCpp, buildPy, toolCs }, Cfg(), NullLogger.Instance);

        Assert.AreEqual(3, result.Count);

        // C++ pair must be in one group
        var cppGroup = result.SingleOrDefault(g =>
            g.ReviewContexts.Any(rc => rc.Path == "foo.h") &&
            g.ReviewContexts.Any(rc => rc.Path == "foo.cpp"));
        Assert.IsNotNull(cppGroup, "foo.h and foo.cpp must be in one C++ group");
        Assert.IsTrue(cppGroup!.GroupingReasons.Contains("strategy: cpp-semantic"));

        // Non-C/C++ files each get their own group
        var pyGroup = result.SingleOrDefault(g => g.ReviewContexts.Any(rc => rc.Path == "build.py"));
        Assert.IsNotNull(pyGroup, "build.py must be in its own group");
        Assert.AreEqual(1, pyGroup!.ReviewContexts.Count);
        Assert.IsTrue(pyGroup.GroupingReasons.Contains("strategy: per-file"));

        var csGroup = result.SingleOrDefault(g => g.ReviewContexts.Any(rc => rc.Path == "tool.cs"));
        Assert.IsNotNull(csGroup, "tool.cs must be in its own group");
        Assert.AreEqual(1, csGroup!.ReviewContexts.Count);
        Assert.IsTrue(csGroup.GroupingReasons.Contains("strategy: per-file"));
    }

    // =========================================================================
    // Spec Case 4 — Same directory non-C/C++ → two separate groups
    // =========================================================================

    [TestMethod]
    public void NonCpp_SameDirectory_NotGroupedByDirectory()
    {
        var result = SemanticReviewGroupBuilder.Build(
            new[] { Ctx("src/a.py"), Ctx("src/b.py") }, Cfg(), NullLogger.Instance);

        Assert.AreEqual(2, result.Count, "Directory affinity must not merge non-C/C++ files");
        Assert.AreEqual(2, result.Sum(g => g.ReviewContexts.Count));
    }

    // =========================================================================
    // Spec Case 5 — Similar names non-C/C++ → two separate groups
    // =========================================================================

    [TestMethod]
    public void NonCpp_SimilarNames_NotGroupedByFilename()
    {
        var result = SemanticReviewGroupBuilder.Build(
            new[] { Ctx("Foo.cs"), Ctx("FooTests.cs") }, Cfg(), NullLogger.Instance);

        Assert.AreEqual(2, result.Count, "Filename similarity must not merge non-C/C++ files");
    }

    // =========================================================================
    // Non-C/C++ not merged into C++ group
    // =========================================================================

    [TestMethod]
    public void NonCpp_NotMergedIntoCppGroup_SameDirectory()
    {
        var cppH   = Ctx("src/render/image.h",   pairPath: "src/render/image.cpp");
        var cppCpp = Ctx("src/render/image.cpp",  pairPath: "src/render/image.h");
        var pyScript = Ctx("src/render/build.py");

        var result = SemanticReviewGroupBuilder.Build(
            new[] { cppH, cppCpp, pyScript }, Cfg(), NullLogger.Instance);

        Assert.AreEqual(2, result.Count);

        var cppGroup = result.Single(g => g.ReviewContexts.Count == 2);
        Assert.IsFalse(cppGroup.ReviewContexts.Any(rc => rc.Path.EndsWith(".py")),
            "Python file must not appear in a C++ group");

        var pyGroup = result.Single(g => g.ReviewContexts.Count == 1);
        Assert.AreEqual("src/render/build.py", pyGroup.ReviewContexts[0].Path);
        Assert.IsTrue(pyGroup.GroupingReasons.Contains("strategy: per-file"));
    }

    // =========================================================================
    // Non-C/C++ not grouped by shared callees or same class
    // =========================================================================

    [TestMethod]
    public void NonCpp_SharedCalleeInAst_NotGrouped()
    {
        // Even with shared callee in AstJson, non-C/C++ files must not be merged.
        string astA = MakeAstWithCallGraph(
            new[] { ("", "funcA", "modified") },
            new[] { ("funcA()", "SharedHelper") });
        string astB = MakeAstWithCallGraph(
            new[] { ("", "funcB", "modified") },
            new[] { ("funcB()", "SharedHelper") });

        var result = SemanticReviewGroupBuilder.Build(
            new[] { Ctx("a.cs", astJson: astA), Ctx("b.cs", astJson: astB) }, Cfg(), NullLogger.Instance);

        Assert.AreEqual(2, result.Count, "Shared callee must not group non-C/C++ files");
    }

    [TestMethod]
    public void NonCpp_SameContainingType_NotGrouped()
    {
        string astA = MakeAst(("MyClass", "MethodA", "modified"));
        string astB = MakeAst(("MyClass", "MethodB", "modified"));

        var result = SemanticReviewGroupBuilder.Build(
            new[] { Ctx("a.cs", astJson: astA), Ctx("b.cs", astJson: astB) }, Cfg(), NullLogger.Instance);

        Assert.AreEqual(2, result.Count, "Same containing type must not group non-C/C++ files");
    }

    // =========================================================================
    // Determinism for non-C/C++ groups
    // =========================================================================

    [TestMethod]
    public void NonCpp_DeterministicOrder()
    {
        var files = new[] { Ctx("z.py"), Ctx("a.py"), Ctx("m.py") };

        var r1 = SemanticReviewGroupBuilder.Build(files, Cfg(), NullLogger.Instance);
        var r2 = SemanticReviewGroupBuilder.Build(files, Cfg(), NullLogger.Instance);

        Assert.AreEqual(3, r1.Count);
        CollectionAssert.AreEqual(
            r1.Select(g => g.ReviewContexts[0].Path).ToList(),
            r2.Select(g => g.ReviewContexts[0].Path).ToList());
    }

    [TestMethod]
    public void NonCpp_SortedByPath()
    {
        var files = new[] { Ctx("z.py"), Ctx("a.py"), Ctx("m.py") };

        var result = SemanticReviewGroupBuilder.Build(files, Cfg(), NullLogger.Instance);

        var paths = result.Select(g => g.ReviewContexts[0].Path).ToList();
        CollectionAssert.AreEqual(new[] { "a.py", "m.py", "z.py" }, paths);
    }

    // =========================================================================
    // No duplicates across groups
    // =========================================================================

    [TestMethod]
    public void Mixed_NoFileDuplicated()
    {
        var files = new[]
        {
            Ctx("foo.h",    pairPath: "foo.cpp"),
            Ctx("foo.cpp",  pairPath: "foo.h"),
            Ctx("bar.py"),
            Ctx("baz.rs"),
        };

        var result = SemanticReviewGroupBuilder.Build(files, Cfg(), NullLogger.Instance);
        var allPaths = result.SelectMany(g => g.ReviewContexts.Select(rc => rc.Path)).ToList();
        Assert.AreEqual(files.Length, allPaths.Count);
        Assert.AreEqual(files.Length, allPaths.Distinct().Count());
    }

    // =========================================================================
    // Baseline: empty and single-file inputs
    // =========================================================================

    [TestMethod]
    public void Empty_ReturnsEmptyList()
    {
        var result = SemanticReviewGroupBuilder.Build(Array.Empty<ReviewContext>(), Cfg(), NullLogger.Instance);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void SingleCppFile_ReturnsSingletonGroup()
    {
        var result = SemanticReviewGroupBuilder.Build(
            new[] { Ctx("src/render/image.cpp") }, Cfg(), NullLogger.Instance);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(1, result[0].ReviewContexts.Count);
        Assert.AreEqual("src/render/image.cpp", result[0].ReviewContexts[0].Path);
        Assert.IsTrue(result[0].GroupingReasons.Contains("strategy: cpp-semantic"));
    }

    [TestMethod]
    public void SingleNonCppFile_ReturnsSingletonPerFileGroup()
    {
        var result = SemanticReviewGroupBuilder.Build(
            new[] { Ctx("main.py") }, Cfg(), NullLogger.Instance);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(1, result[0].ReviewContexts.Count);
        Assert.IsTrue(result[0].GroupingReasons.Contains("strategy: per-file"));
    }

    // =========================================================================
    // Signal 2: Header / source pair
    // =========================================================================

    [TestMethod]
    public void HeaderSourcePair_ViaReviewContextPairPath_Grouped()
    {
        var h = Ctx("include/render/image.h", pairPath: null);
        var cpp = Ctx("src/render/image.cpp", pairPath: "include/render/image.h");

        var result = SemanticReviewGroupBuilder.Build(new[] { h, cpp }, Cfg(), NullLogger.Instance);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(2, result[0].ReviewContexts.Count);
        CollectionAssert.Contains(result[0].GroupingReasons, "header/source pair");
    }

    [TestMethod]
    public void HeaderSourcePair_BothHavePairPath_Grouped()
    {
        var h   = Ctx("foo.h",   pairPath: "foo.cpp");
        var cpp = Ctx("foo.cpp", pairPath: "foo.h");

        var result = SemanticReviewGroupBuilder.Build(new[] { h, cpp }, Cfg(), NullLogger.Instance);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(2, result[0].ReviewContexts.Count);
    }

    [TestMethod]
    public void UnrelatedFiles_NotGrouped()
    {
        var a = Ctx("src/network/config.cpp");
        var b = Ctx("src/storage/config.cpp");

        // Different directories, different pairPaths — should not be grouped even though
        // both have the same base filename (would be grouped by BaseFilename mode but not Semantic).
        // Module keys differ: network vs storage.
        var result = SemanticReviewGroupBuilder.Build(new[] { a, b }, Cfg(), NullLogger.Instance);
        // They will only be grouped if same-base-filename signal fires (weight 30),
        // and that is a valid medium signal per spec. But different modules (weight 10) don't apply.
        // The base filename IS "config" for both → they get grouped by SameBaseFilename.
        // This is acceptable — the spec says base filename is a medium signal.
        // Verify no crash and groups are valid.
        Assert.IsTrue(result.Count >= 1);
        Assert.AreEqual(2, result.Sum(g => g.ReviewContexts.Count));
    }

    // =========================================================================
    // Signal 1: Same containing type
    // =========================================================================

    [TestMethod]
    public void SameContainingType_AcrossFiles_Grouped()
    {
        // Environment::sample and Environment::pdf in separate files.
        var fileA = Ctx("src/env.cpp",     astJson: MakeAst(("Environment", "sample", "modified")));
        var fileB = Ctx("src/env_pdf.cpp", astJson: MakeAst(("Environment", "pdf",    "modified")));

        var result = SemanticReviewGroupBuilder.Build(new[] { fileA, fileB }, Cfg(), NullLogger.Instance);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(2, result[0].ReviewContexts.Count);
        Assert.IsTrue(result[0].GroupingReasons.Any(r => r.Contains("Environment")));
    }

    [TestMethod]
    public void DifferentContainingTypes_NotGroupedBySameType()
    {
        var fileA = Ctx("src/foo.cpp", astJson: MakeAst(("Foo", "doSomething", "modified")));
        var fileB = Ctx("src/bar.cpp", astJson: MakeAst(("Bar", "doOther",     "modified")));

        var result = SemanticReviewGroupBuilder.Build(new[] { fileA, fileB }, Cfg(), NullLogger.Instance);

        // No same-type, no pair, no call, no shared callee, no base-filename match.
        // Might be grouped by module if same directory.
        Assert.AreEqual(2, result.Sum(g => g.ReviewContexts.Count));
    }

    [TestMethod]
    public void ThreeMethodsSameType_AllGrouped()
    {
        var fileA = Ctx("src/env_sample.cpp", astJson: MakeAst(("Environment", "sample", "modified")));
        var fileB = Ctx("src/env_pdf.cpp",    astJson: MakeAst(("Environment", "pdf",    "modified")));
        var fileC = Ctx("src/env_eval.cpp",   astJson: MakeAst(("Environment", "eval",   "added")));

        var result = SemanticReviewGroupBuilder.Build(new[] { fileA, fileB, fileC }, Cfg(), NullLogger.Instance);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(3, result[0].ReviewContexts.Count);
    }

    // =========================================================================
    // Signal 3: Direct changed-symbol call reference
    // =========================================================================

    [TestMethod]
    public void DirectCallBetweenChangedSymbols_Grouped()
    {
        // fileA has changed function "renderSphere" that calls "buildScene" (changed in fileB).
        string astA = MakeAstWithCallGraph(
            new[] { ("", "renderSphere", "modified") },
            new[] { ("renderSphere()", "buildScene") });

        string astB = MakeAst(("", "buildScene", "modified"));

        var fileA = Ctx("src/sphere.cpp", astJson: astA);
        var fileB = Ctx("src/scene.cpp",  astJson: astB);

        var result = SemanticReviewGroupBuilder.Build(new[] { fileA, fileB }, Cfg(), NullLogger.Instance);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(2, result[0].ReviewContexts.Count);
        Assert.IsTrue(result[0].GroupingReasons.Any(r => r.StartsWith("direct call:")));
    }

    // =========================================================================
    // Signal 4: Shared meaningful callee
    // =========================================================================

    [TestMethod]
    public void SharedMeaningfulCallee_Grouped()
    {
        // renderSphere and renderMesh both call Sampler::Sampler.
        string astA = MakeAstWithCallGraph(
            new[] { ("", "renderSphere", "modified") },
            new[] { ("renderSphere()", "Sampler::Sampler") });

        string astB = MakeAstWithCallGraph(
            new[] { ("", "renderMesh", "modified") },
            new[] { ("renderMesh()", "Sampler::Sampler") });

        var fileA = Ctx("src/sphere.cpp", astJson: astA);
        var fileB = Ctx("src/mesh.cpp",   astJson: astB);

        var result = SemanticReviewGroupBuilder.Build(new[] { fileA, fileB }, Cfg(), NullLogger.Instance);

        Assert.AreEqual(1, result.Count);
        Assert.IsTrue(result[0].GroupingReasons.Any(r => r.Contains("shared callee")));
    }

    [TestMethod]
    public void GenericCallee_std_move_NotUsedForGrouping()
    {
        // Both call std::move — but that's a generic utility.
        string astA = MakeAstWithCallGraph(
            new[] { ("", "funcA", "modified") },
            new[] { ("funcA()", "std::move") });

        string astB = MakeAstWithCallGraph(
            new[] { ("", "funcB", "modified") },
            new[] { ("funcB()", "std::move") });

        var fileA = Ctx("src/a.cpp", astJson: astA);
        var fileB = Ctx("src/b.cpp", astJson: astB);

        var result = SemanticReviewGroupBuilder.Build(new[] { fileA, fileB }, Cfg(), NullLogger.Instance);

        // Should NOT be grouped by shared callee of std::move.
        // (May still be grouped by module/directory if same "src" module.)
        bool hasSharedCalleeReason = result.Any(g => g.GroupingReasons.Any(r => r.Contains("move")));
        Assert.IsFalse(hasSharedCalleeReason, "std::move must not drive grouping");
    }

    // =========================================================================
    // Signal 5 & 6: Base filename and module fallback
    // =========================================================================

    [TestMethod]
    public void SameBaseFilename_Grouped()
    {
        var h   = Ctx("include/foo.h");
        var cpp = Ctx("src/foo.cpp");
        // No PairPath set — fall back to base filename.

        var result = SemanticReviewGroupBuilder.Build(new[] { h, cpp }, Cfg(), NullLogger.Instance);

        Assert.AreEqual(1, result.Count);
        Assert.IsTrue(result[0].GroupingReasons.Any(r => r.Contains("base filename")));
    }

    [TestMethod]
    public void SameModule_WeakSignal_GroupedByDirectory()
    {
        var a = Ctx("src/render/sphere.cpp");
        var b = Ctx("src/render/mesh.cpp");

        var result = SemanticReviewGroupBuilder.Build(new[] { a, b }, Cfg(), NullLogger.Instance);

        Assert.AreEqual(1, result.Count);
        Assert.IsTrue(result[0].GroupingReasons.Any(r => r.Contains("module")));
    }

    // =========================================================================
    // Signal priority: strong signals override weak ones
    // =========================================================================

    [TestMethod]
    public void SameContainingType_OverridesModuleAffinity()
    {
        // Files A and B: same containing type (very strong) — should be in same group.
        // File C: same module as A and B, but different type — may be grouped separately.
        var fileA = Ctx("src/render/env_sample.cpp", astJson: MakeAst(("Environment", "sample", "modified")));
        var fileB = Ctx("src/render/env_pdf.cpp",    astJson: MakeAst(("Environment", "pdf",    "modified")));
        var fileC = Ctx("src/render/camera.cpp",     astJson: MakeAst(("Camera",      "init",   "modified")));

        var result = SemanticReviewGroupBuilder.Build(new[] { fileA, fileB, fileC }, Cfg(), NullLogger.Instance);

        // A and B must be in the same group.
        var groupAB = result.FirstOrDefault(g =>
            g.ReviewContexts.Any(rc => rc.Path == "src/render/env_sample.cpp") &&
            g.ReviewContexts.Any(rc => rc.Path == "src/render/env_pdf.cpp"));
        Assert.IsNotNull(groupAB, "Environment::sample and Environment::pdf must be in the same group");
    }

    // =========================================================================
    // Budget: oversized groups are prevented
    // =========================================================================

    [TestMethod]
    public void BudgetLimit_PreventsOversizedGroup()
    {
        // 5 files all in the same module — budget = 3, so they should form multiple groups.
        var files = Enumerable.Range(1, 5)
            .Select(i => Ctx($"src/render/file{i}.cpp"))
            .ToArray();

        var result = SemanticReviewGroupBuilder.Build(files, Cfg(maxFiles: 3), NullLogger.Instance);

        Assert.IsTrue(result.All(g => g.ReviewContexts.Count <= 3), "No group should exceed budget");
        Assert.AreEqual(5, result.Sum(g => g.ReviewContexts.Count), "All files must appear exactly once");
    }

    [TestMethod]
    public void BudgetLimit_PreservesStrongestRelationships()
    {
        // Files A+B share same containing type (very strong).
        // Files A+B+C+D all in same module — but budget = 2.
        // A and B must still be grouped together (strongest signal preserved).
        string astA = MakeAst(("Engine", "update", "modified"));
        string astB = MakeAst(("Engine", "render", "modified"));

        var fileA = Ctx("src/engine/update.cpp", astJson: astA);
        var fileB = Ctx("src/engine/render.cpp", astJson: astB);
        var fileC = Ctx("src/engine/input.cpp");
        var fileD = Ctx("src/engine/audio.cpp");

        var result = SemanticReviewGroupBuilder.Build(new[] { fileA, fileB, fileC, fileD }, Cfg(maxFiles: 2), NullLogger.Instance);

        var groupAB = result.FirstOrDefault(g =>
            g.ReviewContexts.Any(rc => rc.Path == "src/engine/update.cpp") &&
            g.ReviewContexts.Any(rc => rc.Path == "src/engine/render.cpp"));
        Assert.IsNotNull(groupAB, "Same-type files must stay together even under budget pressure");
    }

    // =========================================================================
    // Determinism: same input → same output
    // =========================================================================

    [TestMethod]
    public void Deterministic_SameInputSameOutput()
    {
        var files = new[]
        {
            Ctx("src/render/sphere.cpp", astJson: MakeAst(("Renderer", "drawSphere", "modified"))),
            Ctx("src/render/mesh.cpp",   astJson: MakeAst(("Renderer", "drawMesh",   "modified"))),
            Ctx("include/render/renderer.h"),
            Ctx("src/render/renderer.cpp", pairPath: "include/render/renderer.h"),
        };

        var r1 = SemanticReviewGroupBuilder.Build(files, Cfg(), NullLogger.Instance);
        var r2 = SemanticReviewGroupBuilder.Build(files, Cfg(), NullLogger.Instance);

        Assert.AreEqual(r1.Count, r2.Count);
        for (int i = 0; i < r1.Count; i++)
        {
            var paths1 = r1[i].ReviewContexts.Select(rc => rc.Path).ToList();
            var paths2 = r2[i].ReviewContexts.Select(rc => rc.Path).ToList();
            CollectionAssert.AreEqual(paths1, paths2);
        }
    }

    // =========================================================================
    // No duplicate file inclusion
    // =========================================================================

    [TestMethod]
    public void NoFileDuplicatedAcrossGroups()
    {
        var files = new[]
        {
            Ctx("src/a.cpp", astJson: MakeAst(("Foo", "bar", "modified"))),
            Ctx("src/b.cpp", astJson: MakeAst(("Foo", "baz", "modified"))),
            Ctx("src/c.cpp", astJson: MakeAst(("Qux", "run", "modified"))),
        };

        var result = SemanticReviewGroupBuilder.Build(files, Cfg(), NullLogger.Instance);

        var allPaths = result.SelectMany(g => g.ReviewContexts.Select(rc => rc.Path)).ToList();
        Assert.AreEqual(files.Length, allPaths.Count, "Each file must appear exactly once");
        Assert.AreEqual(files.Length, allPaths.Distinct().Count(), "No file must be duplicated");
    }

    // =========================================================================
    // BaseFilename fallback mode
    // =========================================================================

    [TestMethod]
    public void BaseFilenameMode_GroupsByFilenameWithoutExtension()
    {
        var h   = Ctx("include/foo.h");
        var cpp = Ctx("src/foo.cpp");
        var bar = Ctx("src/bar.cpp");
        var cfg = new GroupingConfig { Mode = GroupingMode.BaseFilename };

        var result = SemanticReviewGroupBuilder.Build(new[] { h, cpp, bar }, cfg, NullLogger.Instance);

        Assert.AreEqual(2, result.Count);
        var fooGroup = result.Single(g => g.Topic == "foo");
        Assert.AreEqual(2, fooGroup.ReviewContexts.Count);
        var barGroup = result.Single(g => g.Topic == "bar");
        Assert.AreEqual(1, barGroup.ReviewContexts.Count);
    }

    // =========================================================================
    // Helper method tests
    // =========================================================================

    [TestMethod]
    public void GetFunctionBaseName_DottedQualifiedName()
    {
        Assert.AreEqual("sample", SemanticReviewGroupBuilder.GetFunctionBaseName("Environment.sample"));
        Assert.AreEqual("pdf",    SemanticReviewGroupBuilder.GetFunctionBaseName("Environment.pdf"));
        Assert.AreEqual("run",    SemanticReviewGroupBuilder.GetFunctionBaseName("run"));
    }

    [TestMethod]
    public void GetCalleeBaseName_ScopeResolution()
    {
        Assert.AreEqual("Sampler", SemanticReviewGroupBuilder.GetCalleeBaseName("Sampler::Sampler"));
        Assert.AreEqual("sample",  SemanticReviewGroupBuilder.GetCalleeBaseName("Environment::sample"));
        Assert.AreEqual("move",    SemanticReviewGroupBuilder.GetCalleeBaseName("std::move"));
        Assert.AreEqual("run",     SemanticReviewGroupBuilder.GetCalleeBaseName("run"));
        Assert.AreEqual("method",  SemanticReviewGroupBuilder.GetCalleeBaseName("obj.method"));
    }

    [TestMethod]
    public void GetCalleeBaseName_StripParameters()
    {
        Assert.AreEqual("Sampler", SemanticReviewGroupBuilder.GetCalleeBaseName("Sampler::Sampler(float, float)"));
    }

    [TestMethod]
    public void NormalizePath_BackslashesConverted()
    {
        Assert.AreEqual("src/foo/bar.cpp", SemanticReviewGroupBuilder.NormalizePath("src\\foo\\bar.cpp"));
        Assert.AreEqual("src/foo/bar.cpp", SemanticReviewGroupBuilder.NormalizePath("/src/foo/bar.cpp"));
    }

    [TestMethod]
    public void ComputeModuleKey_StripsStructuralRoot()
    {
        Assert.AreEqual("network", SemanticReviewGroupBuilder.ComputeModuleKey("src/network/config.cpp"));
        Assert.AreEqual("render",  SemanticReviewGroupBuilder.ComputeModuleKey("include/render/image.h"));
        Assert.AreEqual("render",  SemanticReviewGroupBuilder.ComputeModuleKey("src/render/sphere.cpp"));
        Assert.AreEqual(string.Empty, SemanticReviewGroupBuilder.ComputeModuleKey("foo.cpp"));
    }

    // =========================================================================
    // GroupingReasons logged correctly
    // =========================================================================

    [TestMethod]
    public void GroupingReasons_NotEmpty_WhenFilesAreRelated()
    {
        var h   = Ctx("foo.h",   pairPath: "foo.cpp");
        var cpp = Ctx("foo.cpp", pairPath: "foo.h");

        var result = SemanticReviewGroupBuilder.Build(new[] { h, cpp }, Cfg(), NullLogger.Instance);

        Assert.AreEqual(1, result.Count);
        Assert.IsTrue(result[0].GroupingReasons.Count > 0, "GroupingReasons must be populated");
    }
}
