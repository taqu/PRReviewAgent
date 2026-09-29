using PRReviewAgent.Services;

namespace PRReviewAgent.Test;

/// <summary>
/// Lightweight validation tests for the Phase 6 Recovery Detection prompt template.
/// Tests verify structural requirements without loading Settings (no secrets.toml needed).
/// </summary>
[TestClass]
public class TestRecoveryPromptTemplate
{
    private static string Template { get; } = LoadTemplate();

    private static string LoadTemplate()
    {
        // Walk up from the test binary checking both direct and project-subdirectory locations.
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10; i++)
        {
            if (dir == null) break;
            foreach (string sub in new[] { "Templates", System.IO.Path.Combine("PRReviewAgent", "Templates") })
            {
                string candidate = System.IO.Path.Combine(dir, sub, "review1recovery.en.md");
                if (System.IO.File.Exists(candidate))
                    return System.IO.File.ReadAllText(candidate);
            }
            dir = System.IO.Directory.GetParent(dir)?.FullName;
        }
        return string.Empty;
    }

    [TestMethod]
    public void Template_Loaded()
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(Template), "review1recovery.en.md should be loadable from the template directory");
    }

    // -------------------------------------------------------------------------
    // Case 1 — Output constraints present
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case1_OutputConstraints_JsonOnly()
    {
        Assert.IsTrue(Template.Contains("JSON only", StringComparison.OrdinalIgnoreCase),
            "template must require JSON-only output");
    }

    [TestMethod]
    public void Case1_OutputConstraints_MaximumCandidates()
    {
        Assert.IsTrue(Template.Contains("Maximum 10", StringComparison.OrdinalIgnoreCase) ||
                      Template.Contains("max 10", StringComparison.OrdinalIgnoreCase),
            "template must specify maximum 10 candidates");
    }

    [TestMethod]
    public void Case1_OutputConstraints_HighMediumConfidenceOnly()
    {
        Assert.IsTrue(Template.Contains("high") && Template.Contains("medium"),
            "template must mention high and medium confidence levels");
        Assert.IsTrue(
            Template.Contains("Do not output low-confidence", StringComparison.OrdinalIgnoreCase) ||
            Template.Contains("low-confidence candidates", StringComparison.OrdinalIgnoreCase),
            "template must prohibit low-confidence candidates");
    }

    [TestMethod]
    public void Case1_OutputConstraints_NoSeverity()
    {
        Assert.IsTrue(
            Template.Contains("Do not assign Critical, Major, or Minor", StringComparison.OrdinalIgnoreCase),
            "template must prohibit severity assignment");
    }

    // -------------------------------------------------------------------------
    // Case 2 — Remaining regions not assumed buggy
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case2_RemainingRegionsNotAssumedBuggy()
    {
        Assert.IsTrue(
            Template.Contains("not necessarily incorrect", StringComparison.OrdinalIgnoreCase) ||
            Template.Contains("not necessarily buggy", StringComparison.OrdinalIgnoreCase),
            "template must explicitly state that remaining regions are not necessarily incorrect");
    }

    // -------------------------------------------------------------------------
    // Case 3 — Duplicate avoidance
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case3_DuplicateAvoidance_DoNotRepeatFindings()
    {
        Assert.IsTrue(
            Template.Contains("Do not repeat", StringComparison.OrdinalIgnoreCase) ||
            Template.Contains("do not emit a candidate that describes the same", StringComparison.OrdinalIgnoreCase),
            "template must tell Recovery not to repeat already-reported findings");
    }

    [TestMethod]
    public void Case3_DuplicateAvoidance_DeduplicateByProblem()
    {
        Assert.IsTrue(
            Template.Contains("by problem", StringComparison.OrdinalIgnoreCase) ||
            Template.Contains("by underlying problem", StringComparison.OrdinalIgnoreCase),
            "template should instruct deduplication by problem, not symbol name");
    }

    // -------------------------------------------------------------------------
    // Case 4 — Specification-dependent caution
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case4_SpecDependentCaution_Explicit()
    {
        bool mentionsConvention =
            Template.Contains("convention", StringComparison.OrdinalIgnoreCase) ||
            Template.Contains("specification", StringComparison.OrdinalIgnoreCase);
        bool mentionsOmission =
            Template.Contains("omission", StringComparison.OrdinalIgnoreCase) ||
            Template.Contains("do not report", StringComparison.OrdinalIgnoreCase) ||
            Template.Contains("Prefer omission", StringComparison.OrdinalIgnoreCase);

        Assert.IsTrue(mentionsConvention,
            "template must mention project-specific conventions or specification");
        Assert.IsTrue(mentionsOmission,
            "template must prefer omission over speculation for spec-dependent cases");
    }

    // -------------------------------------------------------------------------
    // Case 5 — Changed-code focus
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case5_ChangedCodeFocus_EvidenceChain()
    {
        // Template should require building a concrete evidence chain from code.
        bool mentionsEvidence =
            Template.Contains("evidence", StringComparison.OrdinalIgnoreCase);
        bool mentionsChangedCode =
            Template.Contains("changed", StringComparison.OrdinalIgnoreCase);
        bool requiresConcrete =
            Template.Contains("concrete", StringComparison.OrdinalIgnoreCase);

        Assert.IsTrue(mentionsEvidence && mentionsChangedCode && requiresConcrete,
            "template must require concrete evidence from changed code");
    }

    // -------------------------------------------------------------------------
    // Case 6 — Same-type / order-sensitive heuristics present
    // -------------------------------------------------------------------------

    [TestMethod]
    public void Case6_SameTypeArgumentSwaps_Mentioned()
    {
        Assert.IsTrue(
            Template.Contains("same-type", StringComparison.OrdinalIgnoreCase) ||
            Template.Contains("argument order", StringComparison.OrdinalIgnoreCase) ||
            Template.Contains("order-sensitive", StringComparison.OrdinalIgnoreCase),
            "template must include guidance on same-type argument/value swaps");
    }

    [TestMethod]
    public void Case6_OrderSensitiveOperations_Mentioned()
    {
        // Should reference non-commutative operations like cross product, subtraction, etc.
        bool mentionsOrderSensitive =
            Template.Contains("order-sensitive", StringComparison.OrdinalIgnoreCase) ||
            Template.Contains("non-commutative", StringComparison.OrdinalIgnoreCase) ||
            Template.Contains("cross", StringComparison.OrdinalIgnoreCase);
        Assert.IsTrue(mentionsOrderSensitive,
            "template must address order-sensitive operations");
    }

    [TestMethod]
    public void Case6_DimensionCoordinate_Mentioned()
    {
        bool mentionsDimension =
            Template.Contains("dimension", StringComparison.OrdinalIgnoreCase) ||
            Template.Contains("width", StringComparison.OrdinalIgnoreCase) ||
            Template.Contains("height", StringComparison.OrdinalIgnoreCase);
        Assert.IsTrue(mentionsDimension,
            "template must include dimension/coordinate consistency checks");
    }

    [TestMethod]
    public void Case6_VariableRoleSubstitution_Mentioned()
    {
        Assert.IsTrue(
            Template.Contains("variable", StringComparison.OrdinalIgnoreCase) &&
            (Template.Contains("role", StringComparison.OrdinalIgnoreCase) ||
             Template.Contains("substitut", StringComparison.OrdinalIgnoreCase) ||
             Template.Contains("semantic", StringComparison.OrdinalIgnoreCase)),
            "template must address variable-role substitution");
    }

    // -------------------------------------------------------------------------
    // Prompt version — §31
    // -------------------------------------------------------------------------

    [TestMethod]
    public void PromptVersion_Recorded()
    {
        Assert.IsTrue(
            Template.Contains("phase6-v1", StringComparison.OrdinalIgnoreCase),
            "template must embed its version identifier");
    }

    [TestMethod]
    public void PromptVersion_ConstantMatchesTemplate()
    {
        Assert.IsTrue(
            Template.Contains(PromptBuilder.RecoveryPromptVersion, StringComparison.OrdinalIgnoreCase),
            $"PromptBuilder.RecoveryPromptVersion ('{PromptBuilder.RecoveryPromptVersion}') must match the version in the template");
    }

    // -------------------------------------------------------------------------
    // No chain-of-thought output — §21
    // -------------------------------------------------------------------------

    [TestMethod]
    public void NoChainOfThoughtOutput_Instructed()
    {
        Assert.IsTrue(
            Template.Contains("reasoning steps", StringComparison.OrdinalIgnoreCase) ||
            Template.Contains("scratchpad", StringComparison.OrdinalIgnoreCase) ||
            Template.Contains("analysis", StringComparison.OrdinalIgnoreCase),
            "template should tell the model not to output reasoning/scratchpad");
    }

    // -------------------------------------------------------------------------
    // Required output schema fields
    // -------------------------------------------------------------------------

    [TestMethod]
    public void OutputSchema_RequiredFields_Present()
    {
        foreach (string field in new[] { "location", "problem", "evidence", "impact", "suggested_fix", "confidence" })
        {
            Assert.IsTrue(Template.Contains($"\"{field}\"") || Template.Contains(field),
                $"template must require output field '{field}'");
        }
    }

    // -------------------------------------------------------------------------
    // Recovery is additive, not corrective — §4
    // -------------------------------------------------------------------------

    [TestMethod]
    public void RecoveryRole_Additive_NotCorrective()
    {
        bool mentionsAdditional =
            Template.Contains("additional", StringComparison.OrdinalIgnoreCase);
        bool doesNotAskToValidate =
            !Template.Contains("validate Primary", StringComparison.OrdinalIgnoreCase) &&
            !Template.Contains("reject Primary", StringComparison.OrdinalIgnoreCase);

        Assert.IsTrue(mentionsAdditional,
            "template must describe Recovery as finding additional issues");
        Assert.IsTrue(doesNotAskToValidate,
            "template must not ask Recovery to validate or reject Primary findings");
    }
}
