namespace PRReviewAgent.Services.Recovery;

public sealed class RecoveryConfig
{
    public RecoveryMode Mode { get; set; } = RecoveryMode.Auto;

    /// <summary>
    /// Minimum number of unreported changed regions required to justify a Recovery LLM call.
    /// Used only in Auto mode. 0 = no minimum (run whenever regions exist).
    /// </summary>
    public int MinimumChangedRegionCount { get; set; } = 2;
}
