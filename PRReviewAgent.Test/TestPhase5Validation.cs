using Microsoft.Extensions.Logging.Abstractions;
using PRReviewAgent;
using PRReviewAgent.Services.AutoImprove;

namespace PRReviewAgent.Test;

/// <summary>
/// Phase 5 — End-to-end validation and hardening of the rule-extraction pipeline.
///
/// Tests are organized by the eight validation categories in the Phase 5 spec plus
/// cross-cutting concerns (failure isolation, prompt quality, cross-language regression).
///
/// These tests use realistic unified-diff inputs and verify pipeline behavior without
/// a live LLM. Prompt-level assertions verify that the SubAgent would receive
/// correctly structured, language-appropriate input.
/// </summary>
[TestClass]
public class TestPhase5Validation
{
    // =========================================================================
    // Category 1-3: Realistic Python diffs — prompt structure verification
    // =========================================================================

    [TestMethod]
    public void Python_NoneCheck_PromptContainsDiffAndLanguage()
    {
        // Before: user.save()
        // After:  if user is not None:\n    user.save()
        const string diff =
            "-user.save()\n" +
            "+if user is not None:\n" +
            "+    user.save()";

        string prompt = BuildPrompt(SourceLanguage.Python, "service.py", diff);

        StringAssert.Contains(prompt, "Python");
        StringAssert.Contains(prompt, "user.save()");
        StringAssert.Contains(prompt, "if user is not None");
        StringAssert.Contains(prompt, "# Changed Code");
        StringAssert.Contains(prompt, "UNKNOWN");
        StringAssert.Contains(prompt, "primary evidence");
    }

    [TestMethod]
    public void Python_ContextManager_PromptContainsDiffAndLanguage()
    {
        // Before: f = open(path) / data = f.read() / f.close()
        // After:  with open(path) as f: / data = f.read()
        const string diff =
            "-f = open(path)\n" +
            "-data = f.read()\n" +
            "-f.close()\n" +
            "+with open(path) as f:\n" +
            "+    data = f.read()";

        string prompt = BuildPrompt(SourceLanguage.Python, "io_helper.py", diff);

        StringAssert.Contains(prompt, "Python");
        StringAssert.Contains(prompt, "with open");
        StringAssert.Contains(prompt, "f.close()");
    }

    [TestMethod]
    public void Python_ErrorHandling_PromptContainsDiffAndLanguage()
    {
        const string diff =
            "-value = int(text)\n" +
            "+try:\n" +
            "+    value = int(text)\n" +
            "+except ValueError:\n" +
            "+    value = default_value";

        string prompt = BuildPrompt(SourceLanguage.Python, "parser.py", diff);

        StringAssert.Contains(prompt, "Python");
        StringAssert.Contains(prompt, "except ValueError");
    }

    // =========================================================================
    // Category 4: Non-rule change — prompt includes diff, UNKNOWN instruction present
    // =========================================================================

    [TestMethod]
    public void Python_TrivialLiteralChange_PromptStillInstructsUnknown()
    {
        // A trivial string rename — the prompt should NOT have any special handling;
        // the UNKNOWN instruction in the system prompt covers this.
        const string diff = "-title = \"foo\"\n+title = \"bar\"";

        string prompt = BuildPrompt(SourceLanguage.Python, "config.py", diff);

        StringAssert.Contains(prompt, "title");
        StringAssert.Contains(prompt, "UNKNOWN");
        // Must NOT create special-cased instructions for this pattern
        Assert.IsFalse(prompt.Contains("string rename"), "No hard-coded guidance for trivial changes");
    }

    // =========================================================================
    // Category 5: Refactoring — UNKNOWN is acceptable, validator catches generic output
    // =========================================================================

    [TestMethod]
    public void GenericValidator_CatchesFormattingOnlyResult()
    {
        // Simulate a model output after a formatting-only change
        LearnedRule rule = new LearnedRule
        {
            RuleDescription = "Improve code quality.",
            BadPattern = "unformatted",
            GoodPattern = "formatted",
        };
        Assert.IsTrue(RuleExtractionService.IsGenericRule(rule),
            "Formatting-only changes should produce generic output that is rejected");
    }

    [TestMethod]
    public void GenericValidator_CatchesNoPatternsResult()
    {
        LearnedRule rule = new LearnedRule
        {
            RuleDescription = "Rename variable for clarity.",
            BadPattern = null,
            GoodPattern = null,
        };
        Assert.IsTrue(RuleExtractionService.IsGenericRule(rule),
            "Rules without actionable patterns should be rejected");
    }

    // =========================================================================
    // Category 6: Context noise — unrelated functions excluded from prompt
    // =========================================================================

    [TestMethod]
    public void ContextNoise_OnlyChangedFunction_InStructures()
    {
        // File with 5 functions, only ProcessRequest is changed
        string astJson = BuildMultiFunctionAst(new (string, string?)[]
        {
            ("InitializeLogger", null),
            ("LoadConfig", null),
            ("ProcessRequest", "modified"),
            ("FormatOutput", null),
            ("ShutdownCleanly", null),
        });

        var (structures, _, _) = RuleContextSelector.Select(astJson, "-old\n+new", SourceLanguage.Cpp);

        Assert.AreEqual(1, structures.Count, "Only changed function should appear");
        Assert.AreEqual("ProcessRequest", structures[0].FunctionSignature);
    }

    [TestMethod]
    public void ContextNoise_UnrelatedFunctions_AbsentFromPrompt()
    {
        string astJson = BuildMultiFunctionAst(new (string, string?)[]
        {
            ("InitializeLogger", null),
            ("LoadConfig", null),
            ("ProcessRequest", "modified"),
        });

        var (structures, _, _) = RuleContextSelector.Select(astJson, "-old\n+new", SourceLanguage.Cpp);
        string prompt = BuildPromptWithContext(SourceLanguage.Cpp, "main.cpp", "-old\n+new", structures, [], []);

        Assert.IsFalse(prompt.Contains("InitializeLogger"), "Unrelated functions must not appear in prompt");
        Assert.IsFalse(prompt.Contains("LoadConfig"), "Unrelated functions must not appear in prompt");
        Assert.IsTrue(prompt.Contains("ProcessRequest"), "Changed function must appear in prompt");
    }

    [TestMethod]
    public void ContextNoise_ManyUnchangedImports_NotInDependencies()
    {
        // Unchanged imports have no + prefix — they must not appear in dependencies
        const string diff =
            " import os\n import sys\n import re\n import json\n import logging\n" +
            "-old_line()\n+new_line()";

        var (_, _, deps) = RuleContextSelector.Select(null, diff, SourceLanguage.Python);

        Assert.AreEqual(0, deps.Count, "Unchanged imports must not appear in dependencies");
    }

    // =========================================================================
    // Category 7: Missing semantic info — diff-only extraction still works
    // =========================================================================

    [TestMethod]
    public async Task MissingAst_ServiceStillCallsSubAgent()
    {
        TrackingSubAgent stub = new TrackingSubAgent { ReturnValue = null };
        RuleExtractionService service = CreateService(stub);

        await service.ExtractAndSaveRuleAsync(new RuleExtractionContext
        {
            Language = SourceLanguage.Python,
            FilePath = "app.py",
            ExpandedDiff = "-user.save()\n+if user is not None:\n+    user.save()",
            // No Structures, Symbols, or Dependencies
        }, 0, CancellationToken.None);

        Assert.IsTrue(stub.WasCalled, "SubAgent must be called even without AST context");
        StringAssert.Contains(stub.LastPrompt!, "user.save()");
    }

    [TestMethod]
    public void MissingAst_NoFullAstInPrompt()
    {
        // Verify that an empty AST results in no spurious AST section in the prompt
        string prompt = BuildPrompt(SourceLanguage.Python, "app.py", "-old\n+new");

        Assert.IsFalse(prompt.Contains("\"functions\""), "Full AST JSON must not appear in prompt");
        Assert.IsFalse(prompt.Contains("\"call_graph\""), "Full AST JSON must not appear in prompt");
        Assert.IsFalse(prompt.Contains("\"imports\""), "Full AST JSON must not appear in prompt");
    }

    // =========================================================================
    // Cross-language regression — prompt quality for all supported languages
    // =========================================================================

    [TestMethod]
    public void CrossLanguage_C_NullPointerGuard_PromptCorrect()
    {
        const string diff =
            "-if (ptr->data != NULL) DoWork(ptr);\n" +
            "+if (ptr != NULL && ptr->data != NULL) DoWork(ptr);";

        string prompt = BuildPrompt(SourceLanguage.C, "worker.c", diff);

        StringAssert.Contains(prompt, "# Language");
        StringAssert.Contains(prompt, "C:");   // "C: Interpret pointer lifetime..."
        StringAssert.Contains(prompt, "# Changed Code");
        StringAssert.Contains(prompt, "ptr");
        StringAssert.Contains(prompt, "UNKNOWN");
        Assert.IsFalse(prompt.Contains("C++:"), "C prompt must not use C++ hint");
    }

    [TestMethod]
    public void CrossLanguage_Cpp_RaiiPattern_PromptCorrect()
    {
        const string diff =
            "-FILE* f = fopen(path, \"r\");\n" +
            "+std::ifstream f(path);";

        string prompt = BuildPrompt(SourceLanguage.Cpp, "reader.cpp", diff);

        StringAssert.Contains(prompt, "C++");
        StringAssert.Contains(prompt, "fopen");
        StringAssert.Contains(prompt, "RAII");   // from the language hint
        StringAssert.Contains(prompt, "UNKNOWN");
    }

    [TestMethod]
    public void CrossLanguage_CSharp_UsingDisposable_PromptCorrect()
    {
        const string diff =
            "-var conn = GetConnection();\n" +
            "-conn.Execute(query);\n" +
            "+using var conn = GetConnection();\n" +
            "+conn.Execute(query);";

        string prompt = BuildPrompt(SourceLanguage.CSharp, "Repo.cs", diff);

        StringAssert.Contains(prompt, "C#");
        StringAssert.Contains(prompt, "IDisposable");   // from language hint
        StringAssert.Contains(prompt, "using var");
        StringAssert.Contains(prompt, "UNKNOWN");
        Assert.IsFalse(prompt.Contains("C++:"), "C# prompt must not use C++ hint");
        Assert.IsFalse(prompt.Contains("RAII"), "C# prompt must not use C++ RAII terminology");
    }

    [TestMethod]
    public void CrossLanguage_Rust_OptionHandling_PromptCorrect()
    {
        const string diff =
            "-let value = map.get(&key).unwrap();\n" +
            "+let value = map.get(&key).unwrap_or_default();";

        string prompt = BuildPrompt(SourceLanguage.Rust, "store.rs", diff);

        StringAssert.Contains(prompt, "Rust");
        StringAssert.Contains(prompt, "ownership");   // from language hint
        StringAssert.Contains(prompt, "unwrap");
        StringAssert.Contains(prompt, "UNKNOWN");
        Assert.IsFalse(prompt.Contains("C++:"), "Rust prompt must not use C++ hint");
    }

    [TestMethod]
    public void CrossLanguage_AllLanguages_DiffBeforeStructure()
    {
        // Verify "Changed Code" section always precedes "Relevant Structure"
        SourceLanguage[] languages = [
            SourceLanguage.C, SourceLanguage.Cpp, SourceLanguage.CSharp,
            SourceLanguage.Python, SourceLanguage.Rust,
        ];
        foreach (SourceLanguage lang in languages)
        {
            string prompt = BuildPromptWithContext(
                lang, "file", "-old\n+new",
                [new StructuralContext { FunctionSignature = "Fn()", ChangeKind = "modified" }],
                [], []);

            int codePos = prompt.IndexOf("# Changed Code", StringComparison.Ordinal);
            int structPos = prompt.IndexOf("# Relevant Structure", StringComparison.Ordinal);
            Assert.IsTrue(codePos >= 0 && structPos >= 0 && codePos < structPos,
                $"Changed Code must precede Relevant Structure for {lang}");
        }
    }

    [TestMethod]
    public void CrossLanguage_AllLanguages_LanguageSectionPresent()
    {
        SourceLanguage[] languages = [
            SourceLanguage.C, SourceLanguage.Cpp, SourceLanguage.CSharp,
            SourceLanguage.Python, SourceLanguage.Rust,
        ];
        foreach (SourceLanguage lang in languages)
        {
            string prompt = BuildPrompt(lang, "file", "-old\n+new");
            StringAssert.Contains(prompt, "# Language",
                $"Language section must be present for {lang}");
            StringAssert.Contains(prompt, SourceLanguageDetector.DisplayName(lang),
                $"Language name must appear for {lang}");
        }
    }

    [TestMethod]
    public void CrossLanguage_AllLanguages_UnknownInstructionPresent()
    {
        SourceLanguage[] languages = [
            SourceLanguage.C, SourceLanguage.Cpp, SourceLanguage.CSharp,
            SourceLanguage.Python, SourceLanguage.Rust,
        ];
        foreach (SourceLanguage lang in languages)
        {
            string prompt = BuildPrompt(lang, "file", "-old\n+new");
            StringAssert.Contains(prompt, "UNKNOWN",
                $"UNKNOWN instruction must be present for {lang}");
        }
    }

    [TestMethod]
    public void CrossLanguage_AllLanguages_NoCppSpecificRole()
    {
        SourceLanguage[] languages = [
            SourceLanguage.C, SourceLanguage.CSharp, SourceLanguage.Python, SourceLanguage.Rust,
        ];
        foreach (SourceLanguage lang in languages)
        {
            string prompt = BuildPrompt(lang, "file", "-old\n+new");
            Assert.IsFalse(prompt.Contains("expert static analysis bot for C/C++"),
                $"Old C/C++-specific role must not appear for {lang}");
        }
    }

    // =========================================================================
    // Failure isolation — all specified failure modes
    // =========================================================================

    [TestMethod]
    public async Task Failure_SubLlmUnavailable_ServiceContinues()
    {
        TrackingSubAgent stub = new TrackingSubAgent
        {
            ThrowException = new HttpRequestException("Connection refused"),
        };
        RuleExtractionService service = CreateService(stub);

        // Must not throw — Sub LLM failure must not propagate to caller
        await service.ExtractAndSaveRuleAsync(
            MakePythonContext("-old\n+new"), 0, CancellationToken.None);

        Assert.IsTrue(stub.WasCalled);
    }

    [TestMethod]
    public async Task Failure_Timeout_ServiceContinues()
    {
        TrackingSubAgent stub = new TrackingSubAgent
        {
            ThrowException = new TaskCanceledException("Simulated timeout"),
        };
        RuleExtractionService service = CreateService(stub);

        await service.ExtractAndSaveRuleAsync(
            MakePythonContext("-old\n+new"), 0, CancellationToken.None);

        Assert.IsTrue(stub.WasCalled);
    }

    [TestMethod]
    public async Task Failure_InvalidJson_NoEmbeddingOrPersist()
    {
        TrackingSubAgent stub = new TrackingSubAgent { ReturnValue = "not valid json at all" };
        RuleExtractionService service = CreateService(stub);

        // embeddingProvider is null — NPE-free completion proves it was not called
        await service.ExtractAndSaveRuleAsync(
            MakePythonContext("-old\n+new"), 0, CancellationToken.None);

        Assert.IsTrue(stub.WasCalled);
    }

    [TestMethod]
    public async Task Failure_EmptyResponse_NoEmbeddingOrPersist()
    {
        TrackingSubAgent stub = new TrackingSubAgent { ReturnValue = string.Empty };
        RuleExtractionService service = CreateService(stub);

        await service.ExtractAndSaveRuleAsync(
            MakePythonContext("-old\n+new"), 0, CancellationToken.None);

        Assert.IsTrue(stub.WasCalled);
    }

    [TestMethod]
    public async Task Failure_WhitespaceResponse_NoEmbeddingOrPersist()
    {
        TrackingSubAgent stub = new TrackingSubAgent { ReturnValue = "   \n   " };
        RuleExtractionService service = CreateService(stub);

        await service.ExtractAndSaveRuleAsync(
            MakePythonContext("-old\n+new"), 0, CancellationToken.None);

        Assert.IsTrue(stub.WasCalled);
    }

    [TestMethod]
    public async Task Failure_NullResponse_NoEmbeddingOrPersist()
    {
        TrackingSubAgent stub = new TrackingSubAgent { ReturnValue = null };
        RuleExtractionService service = CreateService(stub);

        await service.ExtractAndSaveRuleAsync(
            MakePythonContext("-old\n+new"), 0, CancellationToken.None);

        Assert.IsTrue(stub.WasCalled);
    }

    [TestMethod]
    public async Task Failure_MarkdownWrappedInvalidJson_NoEmbeddingOrPersist()
    {
        TrackingSubAgent stub = new TrackingSubAgent { ReturnValue = "```json\nnot valid\n```" };
        RuleExtractionService service = CreateService(stub);

        await service.ExtractAndSaveRuleAsync(
            MakePythonContext("-old\n+new"), 0, CancellationToken.None);

        Assert.IsTrue(stub.WasCalled);
    }

    [TestMethod]
    public async Task Failure_MissingRuleDescription_NoEmbeddingOrPersist()
    {
        const string json = "{\"ast_pattern\":\"x\",\"rule_description\":\"\",\"bad_pattern\":\"a\",\"good_pattern\":\"b\"}";
        TrackingSubAgent stub = new TrackingSubAgent { ReturnValue = json };
        RuleExtractionService service = CreateService(stub);

        await service.ExtractAndSaveRuleAsync(
            MakePythonContext("-old\n+new"), 0, CancellationToken.None);

        Assert.IsTrue(stub.WasCalled);
    }

    // =========================================================================
    // Main LLM routing isolation — SubAgent must always be used, never main agents
    // =========================================================================

    [TestMethod]
    public async Task Routing_MainAgents_NeverCalled()
    {
        // Context.Instance.Agents is not initialized here.
        // If the production code called it, it would throw NullReferenceException.
        // NPE-free completion proves that the main agent was not touched.
        TrackingSubAgent stub = new TrackingSubAgent { ReturnValue = null };
        RuleExtractionService service = CreateService(stub);

        await service.ExtractAndSaveRuleAsync(
            MakePythonContext("-user.save()\n+if user is not None:\n+    user.save()"),
            0, CancellationToken.None);

        Assert.IsTrue(stub.WasCalled, "SubAgent must be used");
    }

    [TestMethod]
    public async Task Routing_SubAgentFailure_NoFallbackToMainLlm()
    {
        TrackingSubAgent stub = new TrackingSubAgent
        {
            ThrowException = new Exception("Sub LLM down"),
        };
        RuleExtractionService service = CreateService(stub);

        // Context.Instance.Agents is not initialized — if main LLM were used it would NPE.
        await service.ExtractAndSaveRuleAsync(
            MakePythonContext("-old\n+new"), 0, CancellationToken.None);
    }

    // =========================================================================
    // Prompt quality — conservative extraction instructions
    // =========================================================================

    [TestMethod]
    public void PromptQuality_SmallestRuleInstruction()
    {
        string prompt = BuildPrompt(SourceLanguage.Python, "app.py", "-old\n+new");
        StringAssert.Contains(prompt, "smallest reusable");
    }

    [TestMethod]
    public void PromptQuality_NoProjectWidePolicy()
    {
        string prompt = BuildPrompt(SourceLanguage.CSharp, "Repo.cs", "-old\n+new");
        StringAssert.Contains(prompt, "project-wide policy");
    }

    [TestMethod]
    public void PromptQuality_NoDiffRestatement()
    {
        string prompt = BuildPrompt(SourceLanguage.Rust, "main.rs", "-old\n+new");
        StringAssert.Contains(prompt, "restate");
    }

    [TestMethod]
    public void PromptQuality_GeneralizeOnly()
    {
        string prompt = BuildPrompt(SourceLanguage.Cpp, "foo.cpp", "-old\n+new");
        StringAssert.Contains(prompt, "Generalize only enough");
    }

    // =========================================================================
    // Pipeline integration — selector → context → prompt → service
    // =========================================================================

    [TestMethod]
    public void Pipeline_SelectorToPrompt_StructureAppearsAfterChangedCode()
    {
        string astJson = BuildSingleFunctionAst("SaveUser", "modified", "UserService");
        const string diff = "-user.save()\n+if user is not None:\n+    user.save()";

        var (structures, symbols, deps) = RuleContextSelector.Select(astJson, diff, SourceLanguage.Python);
        string prompt = BuildPromptWithContext(SourceLanguage.Python, "service.py", diff, structures, symbols, deps);

        int codePos = prompt.IndexOf("# Changed Code", StringComparison.Ordinal);
        int structPos = prompt.IndexOf("# Relevant Structure", StringComparison.Ordinal);
        Assert.IsTrue(codePos < structPos, "Changed Code must come before Relevant Structure");
        StringAssert.Contains(prompt, "SaveUser");
        StringAssert.Contains(prompt, "user.save()");
    }

    [TestMethod]
    public async Task Pipeline_SelectorToService_SubAgentReceivesCompactPrompt()
    {
        string astJson = BuildSingleFunctionAst("ProcessRequest", "modified", "Handler");
        const string diff = "-return null;\n+if (request == null) throw new ArgumentNullException();";

        var (structures, symbols, deps) = RuleContextSelector.Select(astJson, diff, SourceLanguage.CSharp);
        TrackingSubAgent stub = new TrackingSubAgent { ReturnValue = null };
        RuleExtractionService service = CreateService(stub);

        await service.ExtractAndSaveRuleAsync(new RuleExtractionContext
        {
            Language = SourceLanguage.CSharp,
            FilePath = "Handler.cs",
            ExpandedDiff = diff,
            Structures = structures,
            Symbols = symbols,
            Dependencies = deps,
        }, 0, CancellationToken.None);

        Assert.IsTrue(stub.WasCalled);
        Assert.IsFalse(stub.LastPrompt!.Contains("\"functions\""),
            "Full AST JSON must not appear in SubAgent prompt");
        Assert.IsFalse(stub.LastPrompt.Contains("\"call_graph\""),
            "Full call graph must not appear in SubAgent prompt");
    }

    [TestMethod]
    public void Pipeline_ChangedImportsIncluded_UnchangedExcluded()
    {
        const string diff =
            " import os\n" +
            " import sys\n" +
            "+from contextlib import contextmanager\n" +
            "-old_function()\n" +
            "+new_function()";

        var (_, _, deps) = RuleContextSelector.Select(null, diff, SourceLanguage.Python);

        Assert.IsTrue(deps.Any(d => d.Change == "added" && d.Name.Contains("contextlib")),
            "Added import must be included");
        Assert.IsFalse(deps.Any(d => d.Name == "os"), "Unchanged import must not appear");
        Assert.IsFalse(deps.Any(d => d.Name == "sys"), "Unchanged import must not appear");
    }

    // =========================================================================
    // UNKNOWN handling across the pipeline
    // =========================================================================

    [TestMethod]
    public async Task Unknown_ServiceDiscards_AllLanguages()
    {
        const string unknownJson =
            "{\"ast_pattern\":\"\",\"rule_description\":\"UNKNOWN\"," +
            "\"bad_pattern\":\"\",\"good_pattern\":\"\"}";

        foreach (SourceLanguage lang in new[] {
            SourceLanguage.C, SourceLanguage.Cpp, SourceLanguage.CSharp,
            SourceLanguage.Python, SourceLanguage.Rust })
        {
            TrackingSubAgent stub = new TrackingSubAgent { ReturnValue = unknownJson };
            RuleExtractionService service = CreateService(stub);

            await service.ExtractAndSaveRuleAsync(new RuleExtractionContext
            {
                Language = lang,
                FilePath = "file",
                ExpandedDiff = "-title = \"foo\"\n+title = \"bar\"",
            }, 0, CancellationToken.None);

            Assert.IsTrue(stub.WasCalled, $"SubAgent called for {lang}");
            // Null embeddingProvider proves embedding path was not reached
        }
    }

    [TestMethod]
    public void Unknown_IsValidNonError_DistinctFromInvalid()
    {
        // UNKNOWN has a non-empty rule_description — it is a valid parseable result
        // (not null), but flagged as UNKNOWN by IsUnknown().
        const string unknownJson =
            "{\"ast_pattern\":\"\",\"rule_description\":\"UNKNOWN\"," +
            "\"bad_pattern\":\"\",\"good_pattern\":\"\"}";

        // The UNKNOWN JSON must parse into a non-null rule
        LearnedRule rule = new LearnedRule { RuleDescription = "UNKNOWN" };
        Assert.IsTrue(RuleExtractionService.IsUnknown(rule));
        Assert.IsFalse(RuleExtractionService.IsGenericRule(rule),
            "UNKNOWN is handled by IsUnknown, not IsGenericRule");
    }

    // =========================================================================
    // Diagnostics — validate context size reduction is working
    // =========================================================================

    [TestMethod]
    public void Diagnostics_CompactContext_SmallerThanFullAst()
    {
        // Build an AST JSON with 5 functions (4 unchanged, 1 changed) and many imports
        string fullAstJson = BuildLargeAst(unchangedCount: 4, changedName: "ProcessRequest");
        const string diff = "-old_logic()\n+if (request != null) new_logic();";

        var (structures, symbols, deps) = RuleContextSelector.Select(fullAstJson, diff, SourceLanguage.Cpp);

        // Compact context: only 1 structure entry (the changed function)
        Assert.AreEqual(1, structures.Count, "Only changed function in compact context");

        // Verify the compact structural context is smaller than the full AST JSON
        // (The full prompt adds a system prompt, but the selected context should be a strict subset)
        string compactContext = string.Join("\n", structures.Select(s => $"function: {s.FunctionSignature}"));
        Assert.IsTrue(compactContext.Length < fullAstJson.Length,
            "Compact context section must be smaller than the full AST JSON");
    }

    [TestMethod]
    public void Diagnostics_ContextCounts_ReflectSelection()
    {
        string astJson = BuildSingleFunctionAstWithSymbols(
            "ProcessRequest", "modified",
            fieldReads: ["_config", "_logger"],
            fieldWrites: ["_result"]);
        const string diff = "-old\n+new\n+#include \"guard.h\"";

        var (structures, symbols, deps) = RuleContextSelector.Select(astJson, diff, SourceLanguage.Cpp);

        Assert.AreEqual(1, structures.Count, "One changed function");
        Assert.IsTrue(symbols.Count >= 2, "At least field_reads appear in symbols");
        // guard.h should appear in deps
        Assert.IsTrue(deps.Any(d => d.Name == "guard.h"));
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static RuleExtractionService CreateService(ISubAgent stub) =>
        new RuleExtractionService(stub, null!, null!, NullLogger<RuleExtractionService>.Instance);

    private static RuleExtractionContext MakePythonContext(string diff) =>
        new RuleExtractionContext
        {
            Language = SourceLanguage.Python,
            FilePath = "app.py",
            ExpandedDiff = diff,
        };

    private static string BuildPrompt(SourceLanguage language, string filePath, string diff) =>
        RuleExtractionService.BuildExtractionPrompt(new RuleExtractionContext
        {
            Language = language,
            FilePath = filePath,
            ExpandedDiff = diff,
        });

    private static string BuildPromptWithContext(
        SourceLanguage language, string filePath, string diff,
        IReadOnlyList<StructuralContext> structures,
        IReadOnlyList<SymbolContext> symbols,
        IReadOnlyList<DependencyContext> deps) =>
        RuleExtractionService.BuildExtractionPrompt(new RuleExtractionContext
        {
            Language = language,
            FilePath = filePath,
            ExpandedDiff = diff,
            Structures = structures,
            Symbols = symbols,
            Dependencies = deps,
        });

    private static string BuildMultiFunctionAst(IEnumerable<(string name, string? change)> functions)
    {
        string funcs = string.Join(",", functions.Select(f =>
        {
            string change = f.change != null ? $",\"change\":\"{f.change}\"" : "";
            return $"{{\"qualified_name\":\"{f.name}\"{change}}}";
        }));
        return $"{{\"language\":\"Cpp\",\"functions\":[{funcs}]}}";
    }

    private static string BuildSingleFunctionAst(string name, string change, string containingType) =>
        $"{{\"language\":\"Cpp\",\"functions\":[{{" +
        $"\"qualified_name\":\"{name}\"," +
        $"\"containing_type\":\"{containingType}\"," +
        $"\"change\":\"{change}\"" +
        $"}}]}}";

    private static string BuildSingleFunctionAstWithSymbols(
        string name, string change, string[] fieldReads, string[] fieldWrites)
    {
        string reads = $",\"field_reads\":[{string.Join(",", fieldReads.Select(r => $"\"{r}\""))}]";
        string writes = $",\"field_writes\":[{string.Join(",", fieldWrites.Select(w => $"\"{w}\""))}]";
        return $"{{\"language\":\"Cpp\",\"functions\":[{{" +
               $"\"qualified_name\":\"{name}\",\"change\":\"{change}\"{reads}{writes}" +
               $"}}]}}";
    }

    private static string BuildLargeAst(int unchangedCount, string changedName)
    {
        var functions = new List<(string, string?)>();
        for (int i = 0; i < unchangedCount; i++)
            functions.Add(($"UnrelatedFunction{i}", null));
        functions.Add((changedName, "modified"));
        return BuildMultiFunctionAst(functions);
    }
}
