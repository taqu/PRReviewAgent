using Microsoft.Extensions.Logging;
using PRReviewAgent.Prompt;
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
    private readonly List<TurnGroupMetrics> turn2Metrics_ = new();

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

    public async Task RecordGroupsAsync(IReadOnlyList<FileGroup> groups)
    {
        try
        {
            string path = Path.Combine(runDir_, "groups.json");
            var data = groups.Select((g, i) => new
            {
                group_id = $"g{i}",
                topic = g.Topic,
                files = g.ReviewContexts.Select(rc => rc.Path).ToArray(),
                file_count = g.ReviewContexts.Count,
                reasons = g.GroupingReasons.ToArray(),
            }).ToArray();
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { groups = data }, JsonOptions));
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
            long t1DurationMs = 0, t2DurationMs = 0;
            int? t1Input = null, t1Output = null, t2Input = null, t2Output = null;
            int candidateCount = 0;

            foreach (TurnGroupMetrics m in turn1Metrics_)
            {
                t1DurationMs += m.DurationMs;
                candidateCount += m.CandidateCount;
                if (m.InputTokens.HasValue) t1Input = (t1Input ?? 0) + m.InputTokens.Value;
                if (m.OutputTokens.HasValue) t1Output = (t1Output ?? 0) + m.OutputTokens.Value;
            }
            foreach (TurnGroupMetrics m in turn2Metrics_)
            {
                t2DurationMs += m.DurationMs;
                if (m.InputTokens.HasValue) t2Input = (t2Input ?? 0) + m.InputTokens.Value;
                if (m.OutputTokens.HasValue) t2Output = (t2Output ?? 0) + m.OutputTokens.Value;
            }

            int? totalInput = (t1Input.HasValue || t2Input.HasValue)
                ? (t1Input ?? 0) + (t2Input ?? 0) : (int?)null;
            int? totalOutput = (t1Output.HasValue || t2Output.HasValue)
                ? (t1Output ?? 0) + (t2Output ?? 0) : (int?)null;

            string path = Path.Combine(runDir_, "summary.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
            {
                review_run_id = RunId,
                started_at_utc = startedAt_.UtcDateTime.ToString("o"),
                completed_at_utc = completedAt.UtcDateTime.ToString("o"),
                turn1 = new
                {
                    duration_ms = t1DurationMs,
                    candidate_count = candidateCount,
                    input_tokens = t1Input,
                    output_tokens = t1Output,
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
}
