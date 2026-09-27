namespace PRReviewAgent.Services.Coverage;

public sealed record CandidateMapping(
    string CandidateId,
    string Location,
    IReadOnlyList<string> RegionIds);
