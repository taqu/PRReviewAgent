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
    public string? ReturnValue { get; set; }
    public Exception? ThrowException { get; set; }

    public Task<string?> RunAsync(string prompt, CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        if (ThrowException != null) throw ThrowException;
        return Task.FromResult(ReturnValue);
    }
}

[TestClass]
public class TestRuleExtractionSubAgent
{
    // --- Phase 1: Settings default values ---

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

    // --- Phase 1: Disabled behavior ---

    [TestMethod]
    public async Task SubAgent_WhenDisabled_ReturnsNull()
    {
        RuleExtractionSubAgentSettings settings = new RuleExtractionSubAgentSettings { Enabled = false };
        RuleExtractionSubAgent subAgent = new RuleExtractionSubAgent(
            settings,
            NullLogger<RuleExtractionSubAgent>.Instance);

        string? result = await subAgent.RunAsync("any prompt");

        Assert.IsNull(result);
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
            settings,
            NullLogger<RuleExtractionSubAgent>.Instance);

        string? result = await subAgent.RunAsync("any prompt");
        Assert.IsNull(result);
    }

    // --- Phase 1: Routing tests ---

    [TestMethod]
    public async Task RuleExtractionService_CallsSubAgent()
    {
        TrackingSubAgent stub = new TrackingSubAgent { ReturnValue = null };
        RuleExtractionService service = new RuleExtractionService(
            stub, null!, null!, NullLogger<RuleExtractionService>.Instance);

        await service.ExtractAndSaveRuleAsync(MakeCppContext("- old\n+ new"), CancellationToken.None);

        Assert.IsTrue(stub.WasCalled, "RuleExtractionService must delegate to the injected ISubAgent");
    }

    [TestMethod]
    public async Task RuleExtractionService_DoesNotUseMainAgents()
    {
        // If production code still called Context.Instance.Agents it would throw here
        // because Context is not initialized. Must use only the injected SubAgent.
        TrackingSubAgent stub = new TrackingSubAgent { ReturnValue = null };
        RuleExtractionService service = new RuleExtractionService(
            stub, null!, null!, NullLogger<RuleExtractionService>.Instance);

        await service.ExtractAndSaveRuleAsync(MakeCppContext("- removed\n+ added"), CancellationToken.None);

        Assert.IsTrue(stub.WasCalled);
    }

    [TestMethod]
    public async Task RuleExtractionService_WhenSubAgentReturnsNull_ExitsEarly()
    {
        TrackingSubAgent stub = new TrackingSubAgent { ReturnValue = null };
        RuleExtractionService service = new RuleExtractionService(
            stub, null!, null!, NullLogger<RuleExtractionService>.Instance);

        // embeddingProvider/repository are null — must not be reached when response is null.
        await service.ExtractAndSaveRuleAsync(MakeCppContext("some diff"), CancellationToken.None);

        Assert.IsTrue(stub.WasCalled);
    }

    // --- Phase 1: Failure isolation ---

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
        // Must complete without throwing.
    }

    // --- Phase 2: SourceLanguageDetector ---

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

    // --- Phase 2: .h pairing ---

    [TestMethod]
    public void Detector_HeaderWithCPair_ReturnsC() =>
        Assert.AreEqual(SourceLanguage.C, SourceLanguageDetector.Detect("module.h", "module.c"));

    [TestMethod]
    public void Detector_HeaderWithCppPair_ReturnsCpp() =>
        Assert.AreEqual(SourceLanguage.Cpp, SourceLanguageDetector.Detect("module.h", "module.cpp"));

    [TestMethod]
    public void Detector_HeaderWithNoPair_ReturnsCpp() =>
        Assert.AreEqual(SourceLanguage.Cpp, SourceLanguageDetector.Detect("module.h", null));

    [TestMethod]
    public void Detector_HeaderWithEmptyPair_ReturnsCpp() =>
        Assert.AreEqual(SourceLanguage.Cpp, SourceLanguageDetector.Detect("module.h", string.Empty));

    // --- Phase 2: Prompt generation ---

    [TestMethod]
    public void Prompt_ContainsLanguage_Python()
    {
        string prompt = RuleExtractionService.BuildExtractionPrompt(new RuleExtractionContext
        {
            Language = SourceLanguage.Python,
            FilePath = "app.py",
            Diff = "- old\n+ new",
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
            Diff = "- old\n+ new",
        });

        StringAssert.Contains(prompt, "Rust");
    }

    [TestMethod]
    public void Prompt_ContainsLanguage_CSharp()
    {
        string prompt = RuleExtractionService.BuildExtractionPrompt(new RuleExtractionContext
        {
            Language = SourceLanguage.CSharp,
            FilePath = "Program.cs",
            Diff = "- old\n+ new",
        });

        StringAssert.Contains(prompt, "C#");
    }

    [TestMethod]
    public void Prompt_DoesNotContainCppSpecificRole_ForPython()
    {
        string prompt = RuleExtractionService.BuildExtractionPrompt(new RuleExtractionContext
        {
            Language = SourceLanguage.Python,
            FilePath = "app.py",
            Diff = "- old\n+ new",
        });

        Assert.IsFalse(prompt.Contains("expert static analysis bot for C/C++"),
            "Old C/C++-specific role must not appear in prompts for other languages");
    }

    [TestMethod]
    public void Prompt_DoesNotContainCppSpecificRole_ForRust()
    {
        string prompt = RuleExtractionService.BuildExtractionPrompt(new RuleExtractionContext
        {
            Language = SourceLanguage.Rust,
            FilePath = "main.rs",
            Diff = "- old\n+ new",
        });

        Assert.IsFalse(prompt.Contains("expert static analysis bot for C/C++"));
    }

    [TestMethod]
    public void Prompt_ContainsLanguageSectionHeader()
    {
        string prompt = RuleExtractionService.BuildExtractionPrompt(new RuleExtractionContext
        {
            Language = SourceLanguage.Cpp,
            FilePath = "foo.cpp",
            Diff = "- old\n+ new",
        });

        StringAssert.Contains(prompt, "# Language");
        StringAssert.Contains(prompt, "C++");
    }

    // --- Phase 2: Unknown language skipped ---

    [TestMethod]
    public async Task RuleExtractionService_UnknownLanguage_SubAgentNotCalled()
    {
        TrackingSubAgent stub = new TrackingSubAgent { ReturnValue = null };
        RuleExtractionService service = new RuleExtractionService(
            stub, null!, null!, NullLogger<RuleExtractionService>.Instance);

        await service.ExtractAndSaveRuleAsync(new RuleExtractionContext
        {
            Language = SourceLanguage.Unknown,
            FilePath = "build.gradle",
            Diff = "- old\n+ new",
        }, CancellationToken.None);

        Assert.IsFalse(stub.WasCalled, "SubAgent must not be called for unknown language");
    }

    // --- Phase 2: All supported languages use the same SubAgent ---

    [TestMethod]
    public async Task AllSupportedLanguages_UseSubAgent()
    {
        SourceLanguage[] languages =
        [
            SourceLanguage.C, SourceLanguage.Cpp, SourceLanguage.CSharp,
            SourceLanguage.Python, SourceLanguage.Rust
        ];

        foreach (SourceLanguage language in languages)
        {
            TrackingSubAgent stub = new TrackingSubAgent { ReturnValue = null };
            RuleExtractionService service = new RuleExtractionService(
                stub, null!, null!, NullLogger<RuleExtractionService>.Instance);

            await service.ExtractAndSaveRuleAsync(new RuleExtractionContext
            {
                Language = language,
                FilePath = "file",
                Diff = "- old\n+ new",
            }, CancellationToken.None);

            Assert.IsTrue(stub.WasCalled, $"SubAgent must be called for {language}");
        }
    }

    // --- Helpers ---

    private static RuleExtractionContext MakeCppContext(string diff) =>
        new RuleExtractionContext
        {
            Language = SourceLanguage.Cpp,
            FilePath = "foo.cpp",
            Diff = diff,
        };
}
