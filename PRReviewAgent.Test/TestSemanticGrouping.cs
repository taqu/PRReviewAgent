using PRReviewAgent.Services;
using PRReviewAgent.Services.Grouping;

namespace PRReviewAgent.Test;

[TestClass]
public class TestSemanticGrouping
{
    // -----------------------------------------------------------------------
    // Path helpers
    // -----------------------------------------------------------------------

    [TestMethod]
    public void NormalizePath_ReplacesBackslashesAndTrimsSlashes()
    {
        Assert.AreEqual("src/foo/bar.cpp", SemanticReviewGroupBuilder.NormalizePath(@"src\foo\bar.cpp"));
        Assert.AreEqual("src/foo/bar.cpp", SemanticReviewGroupBuilder.NormalizePath("/src/foo/bar.cpp/"));
    }

    [TestMethod]
    public void ComputeModuleKey_StripsStructuralRoot()
    {
        Assert.AreEqual("render", SemanticReviewGroupBuilder.ComputeModuleKey("src/render/image.cpp"));
        Assert.AreEqual("render", SemanticReviewGroupBuilder.ComputeModuleKey("include/render/image.h"));
        Assert.AreEqual("render", SemanticReviewGroupBuilder.ComputeModuleKey("tests/render/image_test.cpp"));
    }

    [TestMethod]
    public void ComputeModuleKey_PreservesNonStandardRoot()
    {
        Assert.AreEqual("engine/render", SemanticReviewGroupBuilder.ComputeModuleKey("engine/render/foo.cpp"));
    }

    [TestMethod]
    public void ComputeModuleKey_EmptyForRootFile()
    {
        Assert.AreEqual(string.Empty, SemanticReviewGroupBuilder.ComputeModuleKey("foo.cpp"));
    }

    [TestMethod]
    public void SameModuleKey_TrueForSameModule()
    {
        Assert.IsTrue(SemanticReviewGroupBuilder.SameModuleKey("src/render/image.cpp", "include/render/image.h"));
        Assert.IsTrue(SemanticReviewGroupBuilder.SameModuleKey("src/render/image.cpp", "tests/render/image_test.cpp"));
    }

    [TestMethod]
    public void SameModuleKey_FalseForDifferentModules()
    {
        Assert.IsFalse(SemanticReviewGroupBuilder.SameModuleKey("src/network/config.cpp", "src/storage/config.cpp"));
    }

    // -----------------------------------------------------------------------
    // Header / implementation pair -> same group
    // -----------------------------------------------------------------------

    [TestMethod]
    public void PairRelationship_GroupedTogether()
    {
        ReviewContext header = new ReviewContext
        {
            Path = "include/foo.h",
            Filename = "foo.h",
            Diff = "+changed",
            PairPath = "src/foo.cpp",
        };
        ReviewContext impl = new ReviewContext
        {
            Path = "src/foo.cpp",
            Filename = "foo.cpp",
            Diff = "+changed",
            PairPath = "include/foo.h",
        };

        IReadOnlyList<FileGroup> groups = SemanticReviewGroupBuilder.Build(new[] { header, impl });

        Assert.AreEqual(1, groups.Count, "Pair files should be in same group");
        Assert.AreEqual(2, groups[0].ReviewContexts.Count);
    }

    // -----------------------------------------------------------------------
    // Same base name, different modules -> different groups
    // -----------------------------------------------------------------------

    [TestMethod]
    public void SameBaseName_DifferentModules_DifferentGroups()
    {
        ReviewContext netConfig = new ReviewContext
        {
            Path = "src/network/config.cpp",
            Filename = "config.cpp",
            Diff = "+changed",
        };
        ReviewContext storConfig = new ReviewContext
        {
            Path = "src/storage/config.cpp",
            Filename = "config.cpp",
            Diff = "+changed",
        };

        IReadOnlyList<FileGroup> groups = SemanticReviewGroupBuilder.Build(new[] { netConfig, storConfig });

        Assert.AreEqual(2, groups.Count, "Files in different modules must not be grouped by basename alone");
        Assert.IsTrue(groups.Any(g => g.Topic.Contains("network")), "Should have network group");
        Assert.IsTrue(groups.Any(g => g.Topic.Contains("storage")), "Should have storage group");
    }

    // -----------------------------------------------------------------------
    // Changed-symbol call -> same group
    // -----------------------------------------------------------------------

    [TestMethod]
    public void ChangedSymbolCall_GroupedTogether()
    {
        // imageLoader.cpp has changed function ImageLoader::Load that calls Image::Create
        // image.cpp has changed function Image::Create
        ReviewContext imageLoader = new ReviewContext
        {
            Path = "src/render/image_loader.cpp",
            Filename = "image_loader.cpp",
            Diff = "+changed",
            AstJson = BuildAstJson("ImageLoader::Load", callees: new[] { "Image::Create" }),
        };
        ReviewContext image = new ReviewContext
        {
            Path = "src/render/image.cpp",
            Filename = "image.cpp",
            Diff = "+changed",
            AstJson = BuildAstJson("Image::Create"),
        };

        IReadOnlyList<FileGroup> groups = SemanticReviewGroupBuilder.Build(new[] { imageLoader, image });

        Assert.AreEqual(1, groups.Count, "Files with direct changed-symbol call should be grouped");
        Assert.AreEqual(2, groups[0].ReviewContexts.Count);
    }

    // -----------------------------------------------------------------------
    // Unrelated files same directory -> not merged
    // -----------------------------------------------------------------------

    [TestMethod]
    public void UnrelatedFiles_SameDirectory_NotMerged()
    {
        ReviewContext image = new ReviewContext
        {
            Path = "src/render/image.cpp",
            Filename = "image.cpp",
            Diff = "+changed",
        };
        ReviewContext shader = new ReviewContext
        {
            Path = "src/render/shader.cpp",
            Filename = "shader.cpp",
            Diff = "+changed",
        };

        IReadOnlyList<FileGroup> groups = SemanticReviewGroupBuilder.Build(new[] { image, shader });

        // Same directory alone should not merge (no strong relationship)
        Assert.AreEqual(2, groups.Count, "Unrelated files in same dir must not be merged");
    }

    // -----------------------------------------------------------------------
    // Changed test + production file -> same group
    // -----------------------------------------------------------------------

    [TestMethod]
    public void TestFile_GroupedWithProductionFile_BySameModuleAndNaming()
    {
        ReviewContext prod = new ReviewContext
        {
            Path = "src/render/image.cpp",
            Filename = "image.cpp",
            Diff = "+changed",
        };
        ReviewContext test = new ReviewContext
        {
            Path = "tests/render/image_test.cpp",
            Filename = "image_test.cpp",
            Diff = "+changed",
        };

        IReadOnlyList<FileGroup> groups = SemanticReviewGroupBuilder.Build(new[] { prod, test });

        Assert.AreEqual(1, groups.Count, "Test and production file should be in same group when name + module match");
        Assert.AreEqual(2, groups[0].ReviewContexts.Count);
    }

    // -----------------------------------------------------------------------
    // Unchanged caller NOT added to group
    // -----------------------------------------------------------------------

    [TestMethod]
    public void UnchangedCaller_NotAddedToGroup()
    {
        ReviewContext foo = new ReviewContext
        {
            Path = "src/foo.cpp",
            Filename = "foo.cpp",
            Diff = "+changed",
        };
        // worker.cpp is NOT in reviewContexts -- it's unchanged
        IReadOnlyList<FileGroup> groups = SemanticReviewGroupBuilder.Build(new[] { foo });

        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual(1, groups[0].ReviewContexts.Count);
        Assert.AreEqual("src/foo.cpp", groups[0].ReviewContexts[0].Path);
    }

    // -----------------------------------------------------------------------
    // Cross-module strong relationship (counterpart AST)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void CounterpartContext_MergesAcrossModules()
    {
        ReviewContext api = new ReviewContext
        {
            Path = "api/foo.h",
            Filename = "foo.h",
            Diff = "+changed",
            AstJson = BuildAstJsonWithCounterpart("backend/foo_impl.cpp"),
        };
        ReviewContext impl = new ReviewContext
        {
            Path = "backend/foo_impl.cpp",
            Filename = "foo_impl.cpp",
            Diff = "+changed",
        };

        IReadOnlyList<FileGroup> groups = SemanticReviewGroupBuilder.Build(new[] { api, impl });

        Assert.AreEqual(1, groups.Count, "Counterpart relationship should merge across modules");
    }

    // -----------------------------------------------------------------------
    // Oversized group -> deterministic split preserving pairs
    // -----------------------------------------------------------------------

    [TestMethod]
    public void OversizedGroup_SplitsDeterministically_PreservingPairs()
    {
        // Create 5 files all with pair relationships forming a chain, max=3
        ReviewContext h1 = new ReviewContext { Path = "include/a.h", Filename = "a.h", Diff = "+", PairPath = "src/a.cpp" };
        ReviewContext c1 = new ReviewContext { Path = "src/a.cpp", Filename = "a.cpp", Diff = "+", PairPath = "include/a.h" };
        ReviewContext h2 = new ReviewContext { Path = "include/b.h", Filename = "b.h", Diff = "+", PairPath = "src/b.cpp" };
        ReviewContext c2 = new ReviewContext { Path = "src/b.cpp", Filename = "b.cpp", Diff = "+", PairPath = "include/b.h" };
        ReviewContext c3 = new ReviewContext { Path = "src/c.cpp", Filename = "c.cpp", Diff = "+" };

        GroupingConfig cfg = new GroupingConfig { MaxFilesPerGroup = 3, Mode = GroupingMode.Semantic };
        IReadOnlyList<FileGroup> groups = SemanticReviewGroupBuilder.Build(
            new[] { h1, c1, h2, c2, c3 }, cfg);

        // All files should be present across all groups
        int total = groups.Sum(g => g.ReviewContexts.Count);
        Assert.AreEqual(5, total, "All files should be present across split groups");

        // Each group should be <= 3
        Assert.IsTrue(groups.All(g => g.ReviewContexts.Count <= 3), "No group should exceed max size");

        // Pairs should stay together
        foreach (FileGroup g in groups)
        {
            bool hasA_h = g.ReviewContexts.Any(c => c.Path == "include/a.h");
            bool hasA_cpp = g.ReviewContexts.Any(c => c.Path == "src/a.cpp");
            Assert.AreEqual(hasA_h, hasA_cpp, "Pair a.h/a.cpp must stay together");

            bool hasB_h = g.ReviewContexts.Any(c => c.Path == "include/b.h");
            bool hasB_cpp = g.ReviewContexts.Any(c => c.Path == "src/b.cpp");
            Assert.AreEqual(hasB_h, hasB_cpp, "Pair b.h/b.cpp must stay together");
        }
    }

    // -----------------------------------------------------------------------
    // Determinism -- same input -> same output
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Build_IsDeterministic_SameInputSameOutput()
    {
        ReviewContext[] contexts = new[]
        {
            new ReviewContext { Path = "src/network/config.cpp", Filename = "config.cpp", Diff = "+" },
            new ReviewContext { Path = "src/storage/config.cpp", Filename = "config.cpp", Diff = "+" },
            new ReviewContext { Path = "include/render/image.h", Filename = "image.h", Diff = "+", PairPath = "src/render/image.cpp" },
            new ReviewContext { Path = "src/render/image.cpp", Filename = "image.cpp", Diff = "+", PairPath = "include/render/image.h" },
        };

        IReadOnlyList<FileGroup> run1 = SemanticReviewGroupBuilder.Build(contexts);
        IReadOnlyList<FileGroup> run2 = SemanticReviewGroupBuilder.Build(contexts);

        Assert.AreEqual(run1.Count, run2.Count, "Group count must be deterministic");
        for (int i = 0; i < run1.Count; i++)
        {
            Assert.AreEqual(run1[i].Topic, run2[i].Topic, "Topics must be deterministic");
            Assert.AreEqual(run1[i].ReviewContexts.Count, run2[i].ReviewContexts.Count);
        }
    }

    // -----------------------------------------------------------------------
    // Missing AST -> path-aware fallback (no exception)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void MissingAst_UsesPathAwareFallback_NoException()
    {
        ReviewContext a = new ReviewContext { Path = "src/network/config.cpp", Filename = "config.cpp", Diff = "+" };
        ReviewContext b = new ReviewContext { Path = "src/storage/config.cpp", Filename = "config.cpp", Diff = "+" };

        // No AstJson -- should still work
        IReadOnlyList<FileGroup> groups = SemanticReviewGroupBuilder.Build(new[] { a, b });
        Assert.IsTrue(groups.Count >= 1, "Should return groups even without AST");
        Assert.AreEqual(2, groups.Sum(g => g.ReviewContexts.Count), "All files should be in some group");
    }

    // -----------------------------------------------------------------------
    // BaseFilename mode falls back to old behavior
    // -----------------------------------------------------------------------

    [TestMethod]
    public void BaseFilenameMode_GroupsByBasename()
    {
        ReviewContext a = new ReviewContext { Path = "src/network/config.cpp", Filename = "config.cpp", Diff = "+" };
        ReviewContext b = new ReviewContext { Path = "src/storage/config.cpp", Filename = "config.cpp", Diff = "+" };

        GroupingConfig cfg = new GroupingConfig { Mode = GroupingMode.BaseFilename };
        IReadOnlyList<FileGroup> groups = SemanticReviewGroupBuilder.Build(new[] { a, b }, cfg);

        Assert.AreEqual(1, groups.Count, "BaseFilename mode should group by basename (old behavior)");
        Assert.AreEqual("config", groups[0].Topic);
    }

    // -----------------------------------------------------------------------
    // Topic generation is path-aware
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Topic_IsPathAware_IncludesModule()
    {
        ReviewContext a = new ReviewContext { Path = "src/network/config.cpp", Filename = "config.cpp", Diff = "+" };
        IReadOnlyList<FileGroup> groups = SemanticReviewGroupBuilder.Build(new[] { a });

        Assert.AreEqual(1, groups.Count);
        Assert.IsTrue(groups[0].Topic.Contains("network"), $"Topic should include module, got: {groups[0].Topic}");
        Assert.IsTrue(groups[0].Topic.Contains("config"), $"Topic should include base name, got: {groups[0].Topic}");
    }

    // -----------------------------------------------------------------------
    // Integration: image/render/network/storage scenario
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Integration_SemanticScenario_CorrectGroups()
    {
        // image_loader calls Image::Create (changed in image.cpp)
        ReviewContext imageH = new ReviewContext
        {
            Path = "include/render/image.h",
            Filename = "image.h",
            Diff = "+",
            PairPath = "src/render/image.cpp",
        };
        ReviewContext imageCpp = new ReviewContext
        {
            Path = "src/render/image.cpp",
            Filename = "image.cpp",
            Diff = "+",
            PairPath = "include/render/image.h",
            AstJson = BuildAstJson("Image::Create"),
        };
        ReviewContext imageLoader = new ReviewContext
        {
            Path = "src/render/image_loader.cpp",
            Filename = "image_loader.cpp",
            Diff = "+",
            AstJson = BuildAstJson("ImageLoader::Load", callees: new[] { "Image::Create" }),
        };
        ReviewContext imageTest = new ReviewContext
        {
            Path = "tests/render/image_test.cpp",
            Filename = "image_test.cpp",
            Diff = "+",
        };
        ReviewContext netConfig = new ReviewContext
        {
            Path = "src/network/config.cpp",
            Filename = "config.cpp",
            Diff = "+",
        };
        ReviewContext storConfig = new ReviewContext
        {
            Path = "src/storage/config.cpp",
            Filename = "config.cpp",
            Diff = "+",
        };

        IReadOnlyList<FileGroup> groups = SemanticReviewGroupBuilder.Build(
            new[] { imageH, imageCpp, imageLoader, imageTest, netConfig, storConfig });

        // network/config and storage/config should be separate
        Assert.IsFalse(
            groups.Any(g => g.ReviewContexts.Any(c => c.Path == "src/network/config.cpp")
                         && g.ReviewContexts.Any(c => c.Path == "src/storage/config.cpp")),
            "network/config and storage/config must be in different groups");

        // image.h, image.cpp, image_loader.cpp, image_test.cpp should be in same group
        FileGroup? imageGroup = groups.FirstOrDefault(g =>
            g.ReviewContexts.Any(c => c.Path == "include/render/image.h"));

        Assert.IsNotNull(imageGroup, "Should have an image group");
        Assert.IsTrue(imageGroup.ReviewContexts.Any(c => c.Path == "src/render/image.cpp"),
            "image.cpp should be in image group");
        Assert.IsTrue(imageGroup.ReviewContexts.Any(c => c.Path == "src/render/image_loader.cpp"),
            "image_loader.cpp should be in image group (via changed-symbol call)");
        Assert.IsTrue(imageGroup.ReviewContexts.Any(c => c.Path == "tests/render/image_test.cpp"),
            "image_test.cpp should be in image group (test naming + module)");
    }

    // -----------------------------------------------------------------------
    // GroupingReasons populated
    // -----------------------------------------------------------------------

    [TestMethod]
    public void GroupingReasons_PopulatedForPairEdge()
    {
        ReviewContext h = new ReviewContext { Path = "include/foo.h", Filename = "foo.h", Diff = "+", PairPath = "src/foo.cpp" };
        ReviewContext c = new ReviewContext { Path = "src/foo.cpp", Filename = "foo.cpp", Diff = "+", PairPath = "include/foo.h" };

        IReadOnlyList<FileGroup> groups = SemanticReviewGroupBuilder.Build(new[] { h, c });

        Assert.AreEqual(1, groups.Count);
        Assert.IsTrue(groups[0].GroupingReasons.Count > 0, "GroupingReasons should be populated for pair edge");
    }

    // -----------------------------------------------------------------------
    // GroupingConfig defaults
    // -----------------------------------------------------------------------

    [TestMethod]
    public void GroupingConfig_Defaults()
    {
        GroupingConfig cfg = new GroupingConfig();
        Assert.AreEqual(8, cfg.MaxFilesPerGroup);
        Assert.AreEqual(GroupingMode.Semantic, cfg.Mode);
    }

    // -----------------------------------------------------------------------
    // Empty input -> empty output
    // -----------------------------------------------------------------------

    [TestMethod]
    public void EmptyInput_ReturnsEmpty()
    {
        IReadOnlyList<FileGroup> groups = SemanticReviewGroupBuilder.Build(Array.Empty<ReviewContext>());
        Assert.AreEqual(0, groups.Count);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string BuildAstJson(string changedFn, string[]? callees = null)
    {
        string callGraphJson = "";
        if (callees != null && callees.Length > 0)
        {
            var edges = callees.Select(c => "{\"caller\":\"" + changedFn + "\",\"callee\":\"" + c + "\"}");
            callGraphJson = ",\"call_graph\":[" + string.Join(",", edges) + "]";
        }

        return "{\n  \"functions\": [\n    {\n      \"qualified_name\": \"" + changedFn + "\",\n      \"start_line\": 1,\n      \"end_line\": 10,\n      \"change\": \"modified\"\n    }\n  ]" + callGraphJson + "\n}";
    }

    private static string BuildAstJsonWithCounterpart(string counterpartFile)
    {
        return "{\n  \"functions\": [\n    {\n      \"qualified_name\": \"Foo::DoIt\",\n      \"start_line\": 1,\n      \"end_line\": 5,\n      \"change\": \"modified\"\n    }\n  ],\n  \"structural_dependencies\": {\n    \"counterpart_context\": {\n      \"file\": \"" + counterpartFile + "\",\n      \"functions\": [],\n      \"types\": []\n    }\n  }\n}";
    }
}
