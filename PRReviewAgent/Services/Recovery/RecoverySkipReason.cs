namespace PRReviewAgent.Services.Recovery;

public enum RecoverySkipReason
{
    None,
    Disabled,
    NoRemainingRegions,
    NoRecoverableContext,
    BelowMinimumRemainingWork,
}
