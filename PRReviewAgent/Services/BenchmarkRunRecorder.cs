using Microsoft.Extensions.Logging;
using PRReviewAgent.Prompt;
using PRReviewAgent.Services.Coverage;
using PRReviewAgent.Services.Grouping;
using PRReviewAgent.Services.Recovery;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PRReviewAgent.Services;

// Temporary benchmark instrumentation for thinking-on/off comparison.
internal sealed class BenchmarkRunRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string runDir_;
    private readonly ILogger logger_;
    private readonly DateTimeOffset startedAt_;
    private readonly string? mergeRequestId_;
    private readonly string? model_;
    private readonly List<TurnGroupMetrics> turn1Metrics_ = new();
    private readonly List<RecoveryTurnMetrics> recoveryMetrics_ = new();
    private readonly List<TurnGroupMetrics> turn2Metrics_ = new();
    private readonly List<RecoveryPolicyRecord> recoveryPolicyRecords_ = new();
    private GroupingMetrics? groupingMetrics_;

    public string RunId { get; }

    private BenchmarkRunRecorder(string runId, string runDir, DateTimeOffset startedAt,
        string? mergeRequestId, string? model, ILogger logger)
    {
        RunId = runId;
        runDir_ = runDir;
        startedAt_ = startedAt;
        mergeRequestId_ = mergeRequestId;
        model_ = model;
        logger_ = logger;
    }

    public static BenchmarkRunRecorder Start(
        string benchmarkRoot, DateTimeOffset startedAt, ILogger logger,
        string? mergeRequestId = null, string? model = null)
    {
        string runId = GenerateRunId(startedAt);
        string runDir = Path.Combine(benchmarkRoot, runId);
        var recorder = new BenchmarkRunRecorder(runId, runDir, startedAt, mergeRequestId, model, logger);
        try
        {
            Directory.CreateDirectory(runDir);
            File.WriteAllText(
                Path.Combine(runDir, "metadata.json"),
                JsonSerializer.Serialize(new
                {
                    review_run_id = runId,
                    started_at_utc = startedAt.UtcDateTime.ToString("o"),
                    merge_request_id = mergeRequestId,
                    model,
                    status = "running",
                }, JsonOptions));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Benchmark: failed to initialize run directory {RunDir}", runDir);
        }
        return recorder;
    }

    public async Task RecordGroupsAsync(IReadOnlyList<FileGroup> groups, int tokenBudget = 0)
    {
        try
        {
            int[] groupTokens = groups
                .Select(g => GroupTokenEstimator.EstimateGroupTokens(g.ReviewContexts))
                .ToArray();

            string path = Path.Combine(runDir_, "groups.json");
            var data = groups.Select((g, i) =>
            {
                string strategy = g.GroupingReasons.FirstOrDefault(r => r.StartsWith("strategy:", StringComparison.OrdinalIgnoreCase)) ?? "unknown";
                return new
                {
                    group_id = $"g{i}",
                    strategy,
                    topic = g.Topic,
                    files = g.ReviewContexts.Select(rc => rc.Path).ToArray(),
                    file_count = g.ReviewContexts.Count,
                    estimated_tokens = groupTokens[i],
                    token_budget = tokenBudget > 0 ? (int?)tokenBudget : null,
                    reasons = g.GroupingReasons.Where(r => !r.StartsWith("strategy:", StringComparison.OrdinalIgnoreCase)).ToArray(),
                };
            }).ToArray();

            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { groups = data }, JsonOptions));

            groupingMetrics_ = new GroupingMetrics(
                FinalGroupCount: groups.Count,
                LargestGroupTokens: groupTokens.Length > 0 ? groupTokens.Max() : 0,
                AverageGroupTokens: groupTokens.Length > 0 ? (int)groupTokens.Average() : 0,
                TokenBudget: tokenBudget);
        }
        catch (Exception ex)
        {
            logger_.LogWarning(ex, "Benchmark: failed to write groups.json");
        }
    }

    public async Task RecordTurn1Async(string topic, IssuesResponse issuesResponse,
        long durationMs, int? inputTokens, int? outputTokens)
    {
        turn1Metrics_.Add(new TurnGroupMetrics(topic, durationMs, issuesResponse.issues.Length, inputTokens, outputTokens));
        try
        {
            string path = Path.Combine(runDir_, $"turn1_{Sanitize(topic)}.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
            {
                topic,
                duration_ms = durationMs,
                input_tokens = inputTokens,
                output_tokens = outputTokens,
                issues = issuesResponse.issues,
            }, JsonOptions));
        }
        catch (Exception ex)
        {
            logger_.LogWarning(ex, "Benchmark: failed to write turn1 for topic {Topic}", topic);
        }
    }

    public async Task RecordTurn1CoverageAsync(string groupId, ReviewCoverage coverage)
    {
        try
        {
            string path = Path.Combine(runDir_, $"coverage_{Sanitize(groupId)}.json");
            var regionsData = coverage.ChangedRegions.Select(r => new
            {
                region_id = r.RegionId,
                file = r.FilePath,
                start_line = r.StartLine,
                end_line = r.EndLine,
                symbol = r.ContainingSymbol,
                reported = coverage.ReportedRegionIds.Contains(r.RegionId),
            }).ToArray();
            var mappingsData = coverage.CandidateMappings.Select(m => new
            {
                candidate_id = m.CandidateId,
                location = m.Location,
                region_ids = m.RegionIds.ToArray(),
            }).ToArray();
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
            {
                coverage = new
                {
                    changed_region_count = coverage.ChangedRegions.Count,
                    reported_region_count = coverage.ReportedRegionIds.Count,
                    unreported_region_count = coverage.UnreportedRegionIds.Count,
                    unmapped_candidate_count = coverage.CandidateMappings.Count(m => m.RegionIds.Count == 0),
                    regions = regionsData,
                },
                candidate_mappings = mappingsData,
            }, JsonOptions));
        }
        catch (Exception ex)
        {
            logger_.LogWarning(ex, "Benchmark: failed to write coverage for group {GroupId}", groupId);
        }
    }

    public async Task RecordRecoveryTurnAsync(
        string groupId, string status,
        IssuesResponse? recoveryResponse,
        long durationMs, int? inputTokens, int? outputTokens,
        int newlyReportedRegionCount, int duplicateCandidateCount,
        RecoveryContextResult? contextResult = null,
        string? promptVersion = null,
        string? mode = null,
        string? decisionReason = null)
    {
        int candidateCount = recoveryResponse?.issues.Length ?? 0;
        bool executed = !status.StartsWith("skipped", StringComparison.OrdinalIgnoreCase)
                     && status != "failed";
        recoveryMetrics_.Add(new RecoveryTurnMetrics(
            groupId, status, durationMs, candidateCount,
            newlyReportedRegionCount, duplicateCandidateCount,
            inputTokens, outputTokens));
        if (mode != null || decisionReason != null)
            recoveryPolicyRecords_.Add(new RecoveryPolicyRecord(groupId, mode ?? "unknown", executed, decisionReason ?? status));
        try
        {
            string path = Path.Combine(runDir_, $"recovery_{Sanitize(groupId)}.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
            {
                group_id = groupId,
                status,
                recovery_policy = (mode != null || decisionReason != null) ? new
                {
                    mode,
                    executed,
                    decision_reason = decisionReason,
                } : null,
                prompt_version = promptVersion,
                duration_ms = durationMs > 0 ? (long?)durationMs : null,
                input_tokens = inputTokens,
                output_tokens = outputTokens,
                candidate_count = candidateCount > 0 ? (int?)candidateCount : null,
                newly_reported_region_count = newlyReportedRegionCount > 0 ? (int?)newlyReportedRegionCount : null,
                duplicate_candidate_count = duplicateCandidateCount > 0 ? (int?)duplicateCandidateCount : null,
                recovery_context = contextResult == null ? null : new
                {
                    target_region_count = contextResult.TargetRegionCount,
                    fragment_count = contextResult.Fragments.Count,
                    estimated_tokens = contextResult.EstimatedTokens,
                    excluded_reported_region_count = contextResult.ExcludedReportedRegionCount,
                    target_region_ids = contextResult.Fragments
                        .SelectMany(f => f.TargetRegionIds)
                        .ToArray(),
                    fragments = contextResult.Fragments.Select(f => new
                    {
                        file = f.FilePath,
                        symbol = f.ContainingSymbol,
                        start_line = f.StartLine,
                        end_line = f.EndLine,
                        target_region_ids = f.TargetRegionIds.ToArray(),
                    }).ToArray(),
                },
                issues = recoveryResponse?.issues,
            }, JsonOptions));
        }
        catch (Exception ex)
        {
            logger_.LogWarning(ex, "Benchmark: failed to write recovery turn for group {GroupId}", groupId);
        }
    }

    public async Task RecordRecoveryCoverageAsync(string groupId, ReviewCoverage combinedCoverage,
        int primaryReportedCount)
    {
        try
        {
            string path = Path.Combine(runDir_, $"coverage_recovery_{Sanitize(groupId)}.json");
            int newlyReported = combinedCoverage.ReportedRegionIds.Count - primaryReportedCount;
            var regionsData = combinedCoverage.ChangedRegions.Select(r => new
            {
                region_id = r.RegionId,
                file = r.FilePath,
                start_line = r.StartLine,
                end_line = r.EndLine,
                symbol = r.ContainingSymbol,
                reported = combinedCoverage.ReportedRegionIds.Contains(r.RegionId),
            }).ToArray();
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
            {
                coverage = new
                {
                    changed_region_count = combinedCoverage.ChangedRegions.Count,
                    reported_region_count = combinedCoverage.ReportedRegionIds.Count,
                    unreported_region_count = combinedCoverage.UnreportedRegionIds.Count,
                    newly_reported_by_recovery = newlyReported,
                    regions = regionsData,
                },
            }, JsonOptions));
        }
        catch (Exception ex)
        {
            logger_.LogWarning(ex, "Benchmark: failed to write recovery coverage for group {GroupId}", groupId);
        }
    }

    public async Task RecordTurn2Async(string topic, string reviewText,
        long durationMs, int? inputTokens, int? outputTokens)
    {
        turn2Metrics_.Add(new TurnGroupMetrics(topic, durationMs, 0, inputTokens, outputTokens));
        try
        {
            string path = Path.Combine(runDir_, $"turn2_{Sanitize(topic)}.txt");
            await File.WriteAllTextAsync(path, reviewText);
        }
        catch (Exception ex)
        {
            logger_.LogWarning(ex, "Benchmark: failed to write turn2 for topic {Topic}", topic);
        }
    }

    public async Task CompleteAsync(DateTimeOffset completedAt, long totalDurationMs)
    {
        await WriteSummaryAsync(completedAt, totalDurationMs);
        await UpdateMetadataAsync(completedAt, "completed");
    }

    public async Task MarkFailedAsync(string status, DateTimeOffset failedAt)
    {
        await UpdateMetadataAsync(failedAt, status);
    }

    private async Task WriteSummaryAsync(DateTimeOffset completedAt, long totalDurationMs)
    {
        try
        {
            long t1DurationMs = 0, t2DurationMs = 0, recDurationMs = 0;
            int? t1Input = null, t1Output = null, t2Input = null, t2Output = null;
            int? recInput = null, recOutput = null;
            int candidateCount = 0;
            int recCandidateCount = 0, recNewlyReported = 0, recDuplicates = 0;

            foreach (TurnGroupMetrics m in turn1Metrics_)
            {
                t1DurationMs += m.DurationMs;
                candidateCount += m.CandidateCount;
                if (m.InputTokens.HasValue) t1Input = (t1Input ?? 0) + m.InputTokens.Value;
                if (m.OutputTokens.HasValue) t1Output = (t1Output ?? 0) + m.OutputTokens.Value;
            }
            foreach (RecoveryTurnMetrics m in recoveryMetrics_)
            {
                recDurationMs += m.DurationMs;
                recCandidateCount += m.CandidateCount;
                recNewlyReported += m.NewlyReportedRegionCount;
                recDuplicates += m.DuplicateCandidateCount;
                if (m.InputTokens.HasValue) recInput = (recInput ?? 0) + m.InputTokens.Value;
                if (m.OutputTokens.HasValue) recOutput = (recOutput ?? 0) + m.OutputTokens.Value;
            }
            foreach (TurnGroupMetrics m in turn2Metrics_)
            {
                t2DurationMs += m.DurationMs;
                if (m.InputTokens.HasValue) t2Input = (t2Input ?? 0) + m.InputTokens.Value;
                if (m.OutputTokens.HasValue) t2Output = (t2Output ?? 0) + m.OutputTokens.Value;
            }

            int? totalInput = (t1Input.HasValue || recInput.HasValue || t2Input.HasValue)
                ? (t1Input ?? 0) + (recInput ?? 0) + (t2Input ?? 0) : (int?)null;
            int? totalOutput = (t1Output.HasValue || recOutput.HasValue || t2Output.HasValue)
                ? (t1Output ?? 0) + (recOutput ?? 0) + (t2Output ?? 0) : (int?)null;

            bool hasRecovery = recoveryMetrics_.Count > 0;
            string recoveryOverallStatus = hasRecovery
                ? (recoveryMetrics_.All(m => m.Status.StartsWith("skipped")) ? "skipped" : "executed")
                : "not_configured";

            // Policy aggregate metrics (Phase 7).
            object? policyMetrics = null;
            if (recoveryPolicyRecords_.Count > 0)
            {
                int policyExecuted = recoveryPolicyRecords_.Count(r => r.Executed);
                int policySkipped = recoveryPolicyRecords_.Count(r => !r.Executed);
                var skipReasonCounts = recoveryPolicyRecords_
                    .Where(r => !r.Executed)
                    .GroupBy(r => r.DecisionReason)
                    .ToDictionary(g => g.Key, g => g.Count());
                policyMetrics = new
                {
                    groups_total = recoveryPolicyRecords_.Count,
                    executed = policyExecuted,
                    skipped = policySkipped,
                    skip_reasons = skipReasonCounts.Count > 0 ? (object)skipReasonCounts : null,
                };
            }

            string path = Path.Combine(runDir_, "summary.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
            {
                review_run_id = RunId,
                started_at_utc = startedAt_.UtcDateTime.ToString("o"),
                completed_at_utc = completedAt.UtcDateTime.ToString("o"),
                primary_detection = new
                {
                    duration_ms = t1DurationMs,
                    candidate_count = candidateCount,
                    input_tokens = t1Input,
                    output_tokens = t1Output,
                },
                recovery_detection = new
                {
                    status = recoveryOverallStatus,
                    duration_ms = recDurationMs > 0 ? (long?)recDurationMs : null,
                    candidate_count = recCandidateCount > 0 ? (int?)recCandidateCount : null,
                    newly_reported_region_count = recNewlyReported,
                    duplicate_candidate_count = recDuplicates,
                    input_tokens = recInput,
                    output_tokens = recOutput,
                },
                combined = new
                {
                    primary_candidate_count = candidateCount,
                    recovery_candidate_count = recCandidateCount,
                    combined_candidate_count = candidateCount + recCandidateCount - recDuplicates,
                },
                turn2 = new
                {
                    duration_ms = t2DurationMs,
                    input_tokens = t2Input,
                    output_tokens = t2Output,
                },
                overall = new
                {
                    duration_ms = totalDurationMs,
                    input_tokens = totalInput,
                    output_tokens = totalOutput,
                },
                grouping = groupingMetrics_ == null ? null : new
                {
                    final_group_count    = groupingMetrics_.FinalGroupCount,
                    largest_group_tokens = groupingMetrics_.LargestGroupTokens,
                    average_group_tokens = groupingMetrics_.AverageGroupTokens,
                    token_budget         = groupingMetrics_.TokenBudget > 0 ? (int?)groupingMetrics_.TokenBudget : null,
                },
                recovery_policy = policyMetrics,
            }, JsonOptions));
        }
        catch (Exception ex)
        {
            logger_.LogWarning(ex, "Benchmark: failed to write summary.json");
        }
    }

    private async Task UpdateMetadataAsync(DateTimeOffset completedAt, string status)
    {
        try
        {
            string path = Path.Combine(runDir_, "metadata.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
            {
                review_run_id = RunId,
                started_at_utc = startedAt_.UtcDateTime.ToString("o"),
                completed_at_utc = completedAt.UtcDateTime.ToString("o"),
                merge_request_id = mergeRequestId_,
                model = model_,
                status,
            }, JsonOptions));
        }
        catch (Exception ex)
        {
            logger_.LogWarning(ex, "Benchmark: failed to update metadata.json");
        }
    }

    private static string GenerateRunId(DateTimeOffset ts)
    {
        string timestamp = ts.UtcDateTime.ToString("yyyyMMdd_HHmmss_fff");
        string suffix = Guid.NewGuid().ToString("N")[..4];
        return $"{timestamp}_{suffix}";
    }

    private static string Sanitize(string topic)
        => string.IsNullOrEmpty(topic) ? "unknown"
            : new string(topic.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());

    private record TurnGroupMetrics(string Topic, long DurationMs, int CandidateCount, int? InputTokens, int? OutputTokens);
    private record RecoveryTurnMetrics(string Topic, string Status, long DurationMs, int CandidateCount, int NewlyReportedRegionCount, int DuplicateCandidateCount, int? InputTokens, int? OutputTokens);
    private record GroupingMetrics(int FinalGroupCount, int LargestGroupTokens, int AverageGroupTokens, int TokenBudget);
    private record RecoveryPolicyRecord(string GroupId, string Mode, bool Executed, string DecisionReason);
}
