using PRReviewAgent.Services.Coverage;

namespace PRReviewAgent.Services.Recovery;

/// <summary>
/// Deterministic policy that decides whether Recovery Detection should run for a review group.
/// No LLM calls, no AST traversal — uses only structural information already available.
/// </summary>
public static class RecoveryExecutionPolicy
{
    /// <summary>
    /// Evaluates whether Recovery Detection should run.
    ///
    /// Pass <c>context = null</c> for a pre-context check (before calling RecoveryContextBuilder.Build).
    /// When context is null, NoRecoverableContext is not tested — the caller must re-evaluate
    /// after building the context.
    ///
    /// Pass the real <c>RecoveryContextResult</c> for the full evaluation.
    /// </summary>
    public static RecoveryDecision Evaluate(
        RecoveryConfig config,
        ReviewCoverage coverage,
        RecoveryContextResult? context)
    {
        if (config.Mode == RecoveryMode.Never)
            return RecoveryDecision.Skip(RecoverySkipReason.Disabled);

        if (coverage.UnreportedRegionIds.Count == 0)
            return RecoveryDecision.Skip(RecoverySkipReason.NoRemainingRegions);

        // Context-dependent checks are only applied once the context has been built.
        if (context != null && context.TargetRegionCount == 0)
            return RecoveryDecision.Skip(RecoverySkipReason.NoRecoverableContext);

        // Always mode bypasses the minimum-work threshold but still respects mandatory skips above.
        if (config.Mode == RecoveryMode.Always)
            return RecoveryDecision.Eligible;

        // Auto: minimum changed-region threshold.
        if (config.MinimumChangedRegionCount > 0 &&
            coverage.UnreportedRegionIds.Count < config.MinimumChangedRegionCount)
            return RecoveryDecision.Skip(RecoverySkipReason.BelowMinimumRemainingWork);

        return RecoveryDecision.Eligible;
    }
}
