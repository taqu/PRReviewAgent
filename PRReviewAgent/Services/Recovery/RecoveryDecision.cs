namespace PRReviewAgent.Services.Recovery;

public sealed class RecoveryDecision
{
    public static readonly RecoveryDecision Eligible =
        new(true, RecoverySkipReason.None, "eligible");

    public bool ShouldRun { get; }
    public RecoverySkipReason SkipReason { get; }

    /// <summary>Machine-readable snake_case reason string for benchmark logs.</summary>
    public string Reason { get; }

    private RecoveryDecision(bool shouldRun, RecoverySkipReason skipReason, string reason)
    {
        ShouldRun = shouldRun;
        SkipReason = skipReason;
        Reason = reason;
    }

    public static RecoveryDecision Skip(RecoverySkipReason reason) =>
        new(false, reason, ToReasonString(reason));

    private static string ToReasonString(RecoverySkipReason reason) => reason switch
    {
        RecoverySkipReason.Disabled => "disabled",
        RecoverySkipReason.NoRemainingRegions => "no_remaining_regions",
        RecoverySkipReason.NoRecoverableContext => "no_recoverable_context",
        RecoverySkipReason.BelowMinimumRemainingWork => "below_minimum_remaining_work",
        _ => "unknown",
    };
}
