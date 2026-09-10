using Microsoft.Extensions.Logging.Abstractions;
using PRReviewAgent;
using PRReviewAgent.Services.AutoImprove;

namespace PRReviewAgent.Test;

/// <summary>
/// Stub ISubAgent that records calls and can simulate failure.
/// </summary>
internal sealed class TrackingSubAgent : ISubAgent
{
    public bool WasCalled { get; private set; }
    public string? LastPrompt { get; private set; }
    public string? ReturnValue { get; set; }
    public Exception? ThrowException { get; set; }

    public Task<string?> RunAsync(string prompt, CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        LastPrompt = prompt;
        if (ThrowException != null) throw ThrowException;
        return Task.FromResult(ReturnValue);
    }
}

[TestClass]
public class TestRuleExtractionSubAgent
{
    // =========================================================================
    // Phase 1: Settings default values
    // =========================================================================

    [TestMethod]
    public void Settings_Defaults_AreApplied()
    {
        RuleExtractionSubAgentSettings settings = new RuleExtractionSubAgentSettings();
        Assert.IsFalse(settings.Enabled);
        Assert.AreEqual(string.Empty, settings.Endpoint);
        Assert.AreEqual("RuleExtractor", settings.Name);
        Assert.AreEqual(string.Empty, settings.Model);
        Assert.AreEqual(1024, settings.MaxOutput);
        Assert.AreEqual(0.0, settings.Temperature);
        Assert.AreEqual(0.9, settings.TopP);
        Assert.AreEqual(120, settings.TimeoutSeconds);
    }

    [TestMethod]
    public void Settings_CustomValues_AreStored()
    {
        RuleExtractionSubAgentSettings settings = new RuleExtractionSubAgentSettings
        {
            Enabled = true,
            Endpoint = "http://192.168.128.152:9080/v1",
            Name = "RuleExtractor",
            Model = "gemma4-mini",
            MaxOutput = 2048,
            Temperature = 0.1,
            TopP = 0.8,
            TimeoutSeconds = 60,
        };

        Assert.IsTrue(settings.Enabled);
        Assert.AreEqual("http://192.168.128.152:9080/v1", settings.Endpoint);
        Assert.AreEqual("gemma4-mini", settings.Model);
        Assert.AreEqual(2048, settings.MaxOutput);
        Assert.AreEqual(0.1, settings.Temperature);
        Assert.AreEqual(0.8, settings.TopP);
        Assert.AreEqual(60, settings.TimeoutSeconds);
    }

    // =========================================================================
    // Phase 1: Disabled behavior
    // =========================================================================

    [TestMethod]
    public async Task SubAgent_WhenDisabled_ReturnsNull()
    {
        RuleExtractionSubAgentSettings settings = new RuleExtractionSubAgentSettings { Enabled = false };
        RuleExtractionSubAgent subAgent = new RuleExtractionSubAgent(
            settings, NullLogger<RuleExtractionSubAgent>.Instance);

        Assert.IsNull(await subAgent.RunAsync("any prompt"));
    }

    [TestMethod]
    public async Task SubAgent_WhenDisabled_NoHttpCallAttempted()
    {
        RuleExtractionSubAgentSettings settings = new RuleExtractionSubAgentSettings
        {
            Enabled = false,
            Endpoint = "http://127.0.0.1:9999/v1",
            Model = "test-model",
        };
        RuleExtractionSubAgent subAgent = new RuleExtractionSubAgent(
            settings, NullLogger<RuleExtractionSubAgent>.Instance);

        Assert.IsNull(await subAgent.RunAsync("any prompt"));
    }

    // =========================================================================
    // Phase 1: Routing
    // =========================================================================

    [TestMethod]
    public async Task RuleExtractionService_CallsSubAgent()
    {
        TrackingSubAgent stub = new TrackingSubAgent { ReturnValue = null };
        RuleExtractionService service = new RuleExtractionService(
            stub, null!, null!, NullLogger<RuleExtractionService>.Instance);

        await service.ExtractAndSaveRuleAsync(MakeCppContext("- old\n+ new"), CancellationToken.None);

        Assert.IsTrue(stub.WasCalled);
    }

    [TestMethod]
    public async Task RuleExtractionService_DoesNotUseMainAgents()
    {
        TrackingSubAgent stub = new TrackingSubAgent { ReturnValue = null };
        RuleExtractionService service = new RuleExtractionService(
            stub, null!, null!, NullLogger<RuleExtractionService>.Instance);

        // If production code still called Context.Instance.Agents it would throw here.
        await service.ExtractAndSaveRuleAsync(MakeCppContext("- removed\n+ added"), CancellationToken.None);

        Assert.IsTrue(stub.WasCalled);
    }

    // =========================================================================
    // Phase 1: Failure isolation
    // =========================================================================

    [TestMethod]
    public async Task RuleExtractionService_SubAgentThrows_ExceptionDoesNotPropagate()
    {
        TrackingSubAgent stub = new TrackingSubAgent
        {
            ThrowException = new HttpRequestException("Sub LLM unreachable"),
        };
        RuleExtractionService service = new RuleExtractionService(
            stub, null!, null!, NullLogger<RuleExtractionService>.Instance);

        await service.ExtractAndSaveRuleAsync(MakeCppContext("- old\n+ new"), CancellationToken.None);
    }

    // =========================================================================
    // Phase 2: SourceLanguageDetector
    // =========================================================================

    [TestMethod]
    public void Detector_CExtension_ReturnsC() =>
        Assert.AreEqual(SourceLanguage.C, SourceLanguageDetector.Detect("main.c"));

    [TestMethod]
    public void Detector_CppExtensions_ReturnCpp()
    {
        foreach (string path in new[] { "a.cpp", "a.cxx", "a.cc", "a.hpp", "a.inl" })
            Assert.AreEqual(SourceLanguage.Cpp, SourceLanguageDetector.Detect(path), path);
    }

    [TestMethod]
    public void Detector_CSharpExtension_ReturnsCSharp() =>
        Assert.AreEqual(SourceLanguage.CSharp, SourceLanguageDetector.Detect("Program.cs"));

    [TestMethod]
    public void Detector_PythonExtension_ReturnsPython() =>
        Assert.AreEqual(SourceLanguage.Python, SourceLanguageDetector.Detect("app.py"));

    [TestMethod]
    public void Detector_RustExtension_ReturnsRust() =>
        Assert.AreEqual(SourceLanguage.Rust, SourceLanguageDetector.Detect("main.rs"));

    [TestMethod]
    public void Detector_UnknownExtension_ReturnsUnknown() =>
        Assert.AreEqual(SourceLanguage.Unknown, SourceLanguageDetector.Detect("build.gradle"));

    [TestMethod]
    public void Detector_HeaderWithCPair_ReturnsC() =>
        Assert.AreEqual(SourceLanguage.C, SourceLanguageDetector.Detect("module.h", "module.c"));

    [TestMethod]
    public void Detector_HeaderWithCppPair_ReturnsCpp() =>
        Assert.AreEqual(SourceLanguage.Cpp, SourceLanguageDetector.Detect("module.h", "module.cpp"));

    [TestMethod]
    public void Detector_HeaderWithNoPair_ReturnsCpp() =>
        Assert.AreEqual(SourceLanguage.Cpp, SourceLanguageDetector.Detect("module.h", null));

    // =========================================================================
    // Phase 2: Prompt generation
    // =========================================================================

    [TestMethod]
    public void Prompt_ContainsLanguage_Python()
    {
        string prompt = RuleExtractionService.BuildExtractionPrompt(new RuleExtractionContext
        {
            Language = SourceLanguage.Python,
            FilePath = "app.py",
            ExpandedDiff = "- old\n+ new",
        });
        StringAssert.Contains(prompt, "Python");
    }

    [TestMethod]
    public void Prompt_ContainsLanguage_Rust()
    {
        string prompt = RuleExtractionService.BuildExtractionPrompt(new RuleExtractionContext
        {
            Language = SourceLanguage.Rust,
            FilePath = "main.rs",
            ExpandedDiff = "- old\n+ new",
        });
        StringAssert.Contains(prompt, "Rust");
    }

    [TestMethod]
    public void Prompt_DoesNotContainCppSpecificRole_ForPython()
    {
        string prompt = RuleExtractionService.BuildExtractionPrompt(new RuleExtractionContext
        {
            Language = SourceLanguage.Python,
            FilePath = "app.py",
            ExpandedDiff = "- old\n+ new",
        });
        Assert.IsFalse(prompt.Contains("expert static analysis bot for C/C++"));
    }

    [TestMethod]
    public void Prompt_UnknownLanguage_SubAgentNotCalled()
    {
        // Verified via service, not prompt builder
        TrackingSubAgent stub = new TrackingSubAgent();
        RuleExtractionService service = new RuleExtractionService(
            stub, null!, null!, NullLogger<RuleExtractionService>.Instance);

        service.ExtractAndSaveRuleAsync(new RuleExtractionContext
        {
            Language = SourceLanguage.Unknown,
            FilePath = "build.gradle",
            ExpandedDiff = "- old\n+ new",
        }, CancellationToken.None).GetAwaiter().GetResult();

        Assert.IsFalse(stub.WasCalled);
    }

    [TestMethod]
    public async Task AllSupportedLanguages_UseSubAgent()
    {
        foreach (SourceLanguage language in new[] {
            SourceLanguage.C, SourceLanguage.Cpp, SourceLanguage.CSharp,
            SourceLanguage.Python, SourceLanguage.Rust })
        {
            TrackingSubAgent stub = new TrackingSubAgent { ReturnValue = null };
            RuleExtractionService service = new RuleExtractionService(
                stub, null!, null!, NullLogger<RuleExtractionService>.Instance);

            await service.ExtractAndSaveRuleAsync(new RuleExtractionContext
            {
                Language = language,
                FilePath = "file",
                ExpandedDiff = "- old\n+ new",
            }, CancellationToken.None);

            Assert.IsTrue(stub.WasCalled, $"SubAgent must be called for {language}");
        }
    }

    // =========================================================================
    // Phase 3: RuleContextSelector — expanded diff preservation
    // =========================================================================

    [TestMethod]
    public void Prompt_ExpandedDiff_IsIncluded()
    {
        const string expandedDiff = "- user.save()\n+ if user is not None:\n+     user.save()";
        string prompt = RuleExtractionService.BuildExtractionPrompt(new RuleExtractionContext
        {
            Language = SourceLanguage.Python,
            FilePath = "app.py",
            ExpandedDiff = expandedDiff,
        });
        StringAssert.Contains(prompt, expandedDiff);
    }

    [TestMethod]
    public void Prompt_ChangedCode_AppearsBefore_Structure()
    {
        string prompt = RuleExtractionService.BuildExtractionPrompt(new RuleExtractionContext
        {
            Language = SourceLanguage.Cpp,
            FilePath = "foo.cpp",
            ExpandedDiff = "- old\n+ new",
            Structures = [new StructuralContext { FunctionSignature = "MyFunc()", ChangeKind = "modified" }],
        });

        int diffPos = prompt.IndexOf("# Changed Code", StringComparison.Ordinal);
        int structPos = prompt.IndexOf("# Relevant Structure", StringComparison.Ordinal);
        Assert.IsTrue(diffPos < structPos, "Changed Code section must precede Relevant Structure");
    }

    // =========================================================================
    // Phase 3: RuleContextSelector — full AST exclusion
    // =========================================================================

    [TestMethod]
    public void Selector_OnlyChangedFunctions_AreInStructures()
    {
        // AST with 4 functions: only one marked as modified
        string astJson = BuildAstJson(new (string, string?, string?)[]
        {
            ("FunctionA", null, null),
            ("FunctionB", null, null),
            ("ChangedFunc", "modified", "ContainerClass"),
            ("FunctionD", null, null),
        });

        var (structures, _, _) = RuleContextSelector.Select(astJson, "- old\n+ new", SourceLanguage.Cpp);

        Assert.AreEqual(1, structures.Count, "Only the modified function should be selected");
        Assert.AreEqual("ChangedFunc", structures[0].FunctionSignature);
    }

    [TestMethod]
    public void Selector_UnchangedFunctions_NotInStructures()
    {
        string astJson = BuildAstJson(new (string, string?, string?)[]
        {
            ("FunctionA", null, null),
            ("FunctionB", null, null),
            ("FunctionC", "modified", null),
        });

        var (structures, _, _) = RuleContextSelector.Select(astJson, "- old\n+ new", SourceLanguage.Cpp);

        Assert.IsFalse(structures.Any(s => s.FunctionSignature == "FunctionA"), "FunctionA must not appear");
        Assert.IsFalse(structures.Any(s => s.FunctionSignature == "FunctionB"), "FunctionB must not appear");
    }

    [TestMethod]
    public void Selector_ContainingType_Propagated()
    {
        string astJson = BuildAstJson(new[] { ("ProcessRequest", "modified", "RequestHandler") });

        var (structures, _, _) = RuleContextSelector.Select(astJson, "- old\n+ new", SourceLanguage.CSharp);

        Assert.AreEqual(1, structures.Count);
        Assert.AreEqual("RequestHandler", structures[0].ContainingType);
    }

    // =========================================================================
    // Phase 3: RuleContextSelector — symbol selection (one-hop)
    // =========================================================================

    [TestMethod]
    public void Selector_FieldReads_OfChangedFunction_AreInSymbols()
    {
        string astJson = BuildAstJsonWithSymbols(
            name: "ProcessData",
            changeKind: "modified",
            fieldReads: ["_config", "_logger"],
            fieldWrites: [],
            objectCreations: []);

        var (_, symbols, _) = RuleContextSelector.Select(astJson, "- old\n+ new", SourceLanguage.Cpp);

        Assert.IsTrue(symbols.Any(s => s.Name == "_config" && s.Kind == "field_read"));
        Assert.IsTrue(symbols.Any(s => s.Name == "_logger" && s.Kind == "field_read"));
    }

    [TestMethod]
    public void Selector_UnchangedFunction_FieldReads_NotInSymbols()
    {
        string astJson = BuildAstJsonWithSymbols(
            name: "UnchangedFunc",
            changeKind: null,
            fieldReads: ["_secret"],
            fieldWrites: [],
            objectCreations: []);

        var (_, symbols, _) = RuleContextSelector.Select(astJson, "- old\n+ new", SourceLanguage.Cpp);

        Assert.IsFalse(symbols.Any(s => s.Name == "_secret"), "Fields from unchanged functions must not appear");
    }

    // =========================================================================
    // Phase 3: RuleContextSelector — dependency filtering
    // =========================================================================

    [TestMethod]
    public void Selector_AddedCppInclude_IsInDependencies()
    {
        string diff = "@@ -1,3 +1,4 @@\n #include <vector>\n+#include \"guard.h\"\n int main() {}";

        var (_, _, deps) = RuleContextSelector.Select(null, diff, SourceLanguage.Cpp);

        Assert.IsTrue(deps.Any(d => d.Name == "guard.h" && d.Change == "added"));
    }

    [TestMethod]
    public void Selector_UnchangedIncludes_NotInDependencies()
    {
        // Unchanged includes appear without + prefix in the diff
        string diff = " #include <vector>\n #include <string>\n- int old() {}\n+ int new_fn() {}";

        var (_, _, deps) = RuleContextSelector.Select(null, diff, SourceLanguage.Cpp);

        Assert.IsFalse(deps.Any(d => d.Name == "vector"), "Unchanged includes must not appear");
        Assert.IsFalse(deps.Any(d => d.Name == "string"), "Unchanged includes must not appear");
    }

    [TestMethod]
    public void Selector_AddedPythonImport_IsInDependencies()
    {
        string diff = "@@ -1 +1,2 @@\n import os\n+from contextlib import closing";

        var (_, _, deps) = RuleContextSelector.Select(null, diff, SourceLanguage.Python);

        Assert.IsTrue(deps.Any(d => d.Change == "added" && d.Name.Contains("contextlib")));
    }

    [TestMethod]
    public void Selector_AddedCSharpUsing_IsInDependencies()
    {
        string diff = "+using System.IO;\n using System;";

        var (_, _, deps) = RuleContextSelector.Select(null, diff, SourceLanguage.CSharp);

        Assert.IsTrue(deps.Any(d => d.Name == "System.IO" && d.Change == "added"));
    }

    [TestMethod]
    public void Selector_AddedRustUse_IsInDependencies()
    {
        string diff = "+use std::io::Write;\n use std::fmt;";

        var (_, _, deps) = RuleContextSelector.Select(null, diff, SourceLanguage.Rust);

        Assert.IsTrue(deps.Any(d => d.Name == "std::io::Write" && d.Change == "added"));
    }

    // =========================================================================
    // Phase 3: RuleContextSelector — degradation behavior
    // =========================================================================

    [TestMethod]
    public void Selector_NullAst_ReturnsEmptyStructuresAndSymbols()
    {
        var (structures, symbols, _) = RuleContextSelector.Select(null, "- old\n+ new", SourceLanguage.Cpp);

        Assert.AreEqual(0, structures.Count);
        Assert.AreEqual(0, symbols.Count);
    }

    [TestMethod]
    public void Selector_MalformedAst_ReturnsEmptyStructuresAndSymbols()
    {
        var (structures, symbols, _) = RuleContextSelector.Select("{ not valid json }", "- old\n+ new", SourceLanguage.Cpp);

        Assert.AreEqual(0, structures.Count);
        Assert.AreEqual(0, symbols.Count);
    }

    [TestMethod]
    public void Selector_MalformedAst_ChangedImportsStillReturned()
    {
        string diff = "+#include \"new_header.h\"";
        var (_, _, deps) = RuleContextSelector.Select("not valid json", diff, SourceLanguage.Cpp);

        // Even on AST parse failure, changed imports should be detected from diff
        Assert.IsTrue(deps.Any(d => d.Name == "new_header.h"));
    }

    [TestMethod]
    public void Selector_NoAst_DiffStillPassedThrough()
    {
        const string expandedDiff = "- user.save()\n+ if user is not None:\n+     user.save()";
        TrackingSubAgent stub = new TrackingSubAgent { ReturnValue = null };
        RuleExtractionService service = new RuleExtractionService(
            stub, null!, null!, NullLogger<RuleExtractionService>.Instance);

        service.ExtractAndSaveRuleAsync(new RuleExtractionContext
        {
            Language = SourceLanguage.Python,
            FilePath = "app.py",
            ExpandedDiff = expandedDiff,
            // No structures, symbols, or dependencies
        }, CancellationToken.None).GetAwaiter().GetResult();

        Assert.IsTrue(stub.WasCalled);
        Assert.IsTrue(stub.LastPrompt!.Contains(expandedDiff), "Expanded diff must still be in prompt");
    }

    // =========================================================================
    // Phase 3: Prompt structure — empty sections omitted
    // =========================================================================

    [TestMethod]
    public void Prompt_EmptyStructure_SectionOmitted()
    {
        string prompt = RuleExtractionService.BuildExtractionPrompt(new RuleExtractionContext
        {
            Language = SourceLanguage.Python,
            FilePath = "app.py",
            ExpandedDiff = "- old\n+ new",
            // Structures is empty
        });

        Assert.IsFalse(prompt.Contains("# Relevant Structure"), "Empty Structures section must be omitted");
    }

    [TestMethod]
    public void Prompt_EmptySemanticContext_SectionOmitted()
    {
        string prompt = RuleExtractionService.BuildExtractionPrompt(new RuleExtractionContext
        {
            Language = SourceLanguage.Python,
            FilePath = "app.py",
            ExpandedDiff = "- old\n+ new",
            // Symbols and Dependencies are empty
        });

        Assert.IsFalse(prompt.Contains("# Relevant Semantic Context"), "Empty Semantic Context section must be omitted");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static RuleExtractionContext MakeCppContext(string diff) =>
        new RuleExtractionContext
        {
            Language = SourceLanguage.Cpp,
            FilePath = "foo.cpp",
            ExpandedDiff = diff,
        };

    /// <summary>Builds a minimal AST JSON with the given functions.</summary>
    private static string BuildAstJson(
        IEnumerable<(string name, string? change, string? containingType)> functions)
    {
        var funcs = string.Join(",", functions.Select(f =>
        {
            string change = f.change != null ? $",\"change\":\"{f.change}\"" : "";
            string ct = f.containingType != null ? $",\"containing_type\":\"{f.containingType}\"" : "";
            return $"{{\"qualified_name\":\"{f.name}\"{ct}{change}}}";
        }));
        return $"{{\"language\":\"Cpp\",\"functions\":[{funcs}]}}";
    }

    /// <summary>Builds a minimal AST JSON for a single function with symbol info.</summary>
    private static string BuildAstJsonWithSymbols(
        string name,
        string? changeKind,
        string[] fieldReads,
        string[] fieldWrites,
        string[] objectCreations)
    {
        string change = changeKind != null ? $",\"change\":\"{changeKind}\"" : "";
        string reads = fieldReads.Length > 0
            ? $",\"field_reads\":[{string.Join(",", fieldReads.Select(r => $"\"{r}\""))}]"
            : "";
        string writes = fieldWrites.Length > 0
            ? $",\"field_writes\":[{string.Join(",", fieldWrites.Select(w => $"\"{w}\""))}]"
            : "";
        string creates = objectCreations.Length > 0
            ? $",\"object_creations\":[{string.Join(",", objectCreations.Select(o => $"\"{o}\""))}]"
            : "";
        return $"{{\"language\":\"Cpp\",\"functions\":[{{\"qualified_name\":\"{name}\"{change}{reads}{writes}{creates}}}]}}";
    }
}
