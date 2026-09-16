using Microsoft.Data.Sqlite;

namespace PRReviewAgent.Services.Statistics
{
    public sealed class StatisticsService : IStatisticsService, IDisposable
    {
        private readonly string _connectionString;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public StatisticsService(string dbPath)
        {
            _connectionString = $"Data Source={dbPath}";
        }

        internal StatisticsService(string dbPath, bool isConnectionString)
        {
            _connectionString = isConnectionString ? dbPath : $"Data Source={dbPath}";
        }

        public async Task<OverviewStatistics> GetOverviewAsync(StatisticsQuery query, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);

                string fromStr = query.From.ToString("O");
                string toStr = query.To.ToString("O");
                object projectIdParam = query.ProjectId.HasValue ? (object)query.ProjectId.Value : DBNull.Value;

                // Overview aggregates from review_executions
                int reviewCount = 0;
                int mergeRequestCount = 0;
                double? avgDurationMs = null;
                int failureCount = 0;
                long totalCandidates = 0;
                long totalFinal = 0;

                await using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT
                            COUNT(*) AS review_count,
                            COUNT(DISTINCT merge_request_id) AS mr_count,
                            AVG(duration_ms) AS avg_duration_ms,
                            SUM(CASE WHEN status = 'Failed' THEN 1 ELSE 0 END) AS failure_count,
                            SUM(COALESCE(candidate_finding_count, 0)) AS total_candidates,
                            SUM(COALESCE(selected_finding_count, 0)) AS total_final
                        FROM review_executions
                        WHERE started_at >= @from AND started_at <= @to
                          AND (@projectId IS NULL OR project_id = @projectId)";
                    cmd.Parameters.AddWithValue("@from", fromStr);
                    cmd.Parameters.AddWithValue("@to", toStr);
                    cmd.Parameters.AddWithValue("@projectId", projectIdParam);
                    await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    if (await reader.ReadAsync(cancellationToken))
                    {
                        reviewCount = reader.IsDBNull(0) ? 0 : (int)reader.GetInt64(0);
                        mergeRequestCount = reader.IsDBNull(1) ? 0 : (int)reader.GetInt64(1);
                        avgDurationMs = reader.IsDBNull(2) ? null : reader.GetDouble(2);
                        failureCount = reader.IsDBNull(3) ? 0 : (int)reader.GetInt64(3);
                        totalCandidates = reader.IsDBNull(4) ? 0 : reader.GetInt64(4);
                        totalFinal = reader.IsDBNull(5) ? 0 : reader.GetInt64(5);
                    }
                }

                // Token totals from review_turns
                long inputTokens = 0;
                long outputTokens = 0;
                await using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT
                            SUM(COALESCE(rt.input_tokens, 0)) AS input_tokens,
                            SUM(COALESCE(rt.output_tokens, 0)) AS output_tokens
                        FROM review_turns rt
                        JOIN review_executions re ON re.id = rt.review_execution_id
                        WHERE re.started_at >= @from AND re.started_at <= @to
                          AND (@projectId IS NULL OR re.project_id = @projectId)";
                    cmd.Parameters.AddWithValue("@from", fromStr);
                    cmd.Parameters.AddWithValue("@to", toStr);
                    cmd.Parameters.AddWithValue("@projectId", projectIdParam);
                    await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    if (await reader.ReadAsync(cancellationToken))
                    {
                        inputTokens = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
                        outputTokens = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                    }
                }

                // Project count
                int projectCount = 0;
                await using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT COUNT(DISTINCT project_id)
                        FROM review_executions
                        WHERE started_at >= @from AND started_at <= @to
                          AND (@projectId IS NULL OR project_id = @projectId)";
                    cmd.Parameters.AddWithValue("@from", fromStr);
                    cmd.Parameters.AddWithValue("@to", toStr);
                    cmd.Parameters.AddWithValue("@projectId", projectIdParam);
                    object? scalar = await cmd.ExecuteScalarAsync(cancellationToken);
                    projectCount = scalar is long l ? (int)l : 0;
                }

                double? errorRate = reviewCount > 0 ? (double)failureCount / reviewCount : null;
                double? selectionRate = totalCandidates > 0 ? (double)totalFinal / totalCandidates : null;

                return new OverviewStatistics
                {
                    ProjectCount = projectCount,
                    ReviewCount = reviewCount,
                    MergeRequestCount = mergeRequestCount,
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    AvgReviewDurationMs = avgDurationMs,
                    ErrorRate = errorRate,
                    TotalCandidates = totalCandidates,
                    TotalFinalFindings = totalFinal,
                    SelectionRate = selectionRate,
                };
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<IReadOnlyList<ReviewTrendPoint>> GetReviewTrendAsync(StatisticsQuery query, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);

                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT
                        SUBSTR(started_at, 1, 10) AS day,
                        COUNT(*) AS review_count,
                        SUM(CASE WHEN status = 'Succeeded' THEN 1 ELSE 0 END) AS success_count,
                        SUM(CASE WHEN status = 'Failed' THEN 1 ELSE 0 END) AS failure_count
                    FROM review_executions
                    WHERE started_at >= @from AND started_at <= @to
                      AND (@projectId IS NULL OR project_id = @projectId)
                    GROUP BY day
                    ORDER BY day ASC";
                cmd.Parameters.AddWithValue("@from", query.From.ToString("O"));
                cmd.Parameters.AddWithValue("@to", query.To.ToString("O"));
                cmd.Parameters.AddWithValue("@projectId", query.ProjectId.HasValue ? (object)query.ProjectId.Value : DBNull.Value);

                await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
                List<ReviewTrendPoint> results = new();
                while (await reader.ReadAsync(cancellationToken))
                {
                    results.Add(new ReviewTrendPoint
                    {
                        Date = reader.GetString(0),
                        ReviewCount = (int)reader.GetInt64(1),
                        SuccessCount = reader.IsDBNull(2) ? 0 : (int)reader.GetInt64(2),
                        FailureCount = reader.IsDBNull(3) ? 0 : (int)reader.GetInt64(3),
                    });
                }
                return results;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<IReadOnlyList<TokenTrendPoint>> GetTokenTrendAsync(StatisticsQuery query, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);

                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT
                        SUBSTR(re.started_at, 1, 10) AS day,
                        SUM(COALESCE(rt.input_tokens, 0)) AS input_tokens,
                        SUM(COALESCE(rt.output_tokens, 0)) AS output_tokens
                    FROM review_turns rt
                    JOIN review_executions re ON re.id = rt.review_execution_id
                    WHERE re.started_at >= @from AND re.started_at <= @to
                      AND (@projectId IS NULL OR re.project_id = @projectId)
                    GROUP BY day
                    ORDER BY day ASC";
                cmd.Parameters.AddWithValue("@from", query.From.ToString("O"));
                cmd.Parameters.AddWithValue("@to", query.To.ToString("O"));
                cmd.Parameters.AddWithValue("@projectId", query.ProjectId.HasValue ? (object)query.ProjectId.Value : DBNull.Value);

                await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
                List<TokenTrendPoint> results = new();
                while (await reader.ReadAsync(cancellationToken))
                {
                    results.Add(new TokenTrendPoint
                    {
                        Date = reader.GetString(0),
                        InputTokens = reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
                        OutputTokens = reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                    });
                }
                return results;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<ReviewStatistics> GetReviewStatisticsAsync(StatisticsQuery query, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);

                string fromStr = query.From.ToString("O");
                string toStr = query.To.ToString("O");
                object projectIdParam = query.ProjectId.HasValue ? (object)query.ProjectId.Value : DBNull.Value;

                // Turn-level aggregates grouped by turn_type
                List<ReviewTurnStatistics> turnStats = new();
                await using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT
                            rt.turn_type,
                            COUNT(*) AS calls,
                            SUM(CASE WHEN rt.success = 1 THEN 1 ELSE 0 END) AS successful_calls,
                            SUM(CASE WHEN rt.success = 0 THEN 1 ELSE 0 END) AS failed_calls,
                            SUM(COALESCE(rt.input_tokens, 0)) AS input_tokens,
                            SUM(COALESCE(rt.output_tokens, 0)) AS output_tokens,
                            AVG(rt.duration_ms) AS avg_duration_ms,
                            AVG(rt.finding_count) AS avg_finding_count
                        FROM review_turns rt
                        JOIN review_executions re ON re.id = rt.review_execution_id
                        WHERE re.started_at >= @from AND re.started_at <= @to
                          AND (@projectId IS NULL OR re.project_id = @projectId)
                        GROUP BY rt.turn_type";
                    cmd.Parameters.AddWithValue("@from", fromStr);
                    cmd.Parameters.AddWithValue("@to", toStr);
                    cmd.Parameters.AddWithValue("@projectId", projectIdParam);
                    await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        turnStats.Add(new ReviewTurnStatistics
                        {
                            TurnType = (int)reader.GetInt64(0),
                            Calls = (int)reader.GetInt64(1),
                            SuccessfulCalls = reader.IsDBNull(2) ? 0 : (int)reader.GetInt64(2),
                            FailedCalls = reader.IsDBNull(3) ? 0 : (int)reader.GetInt64(3),
                            InputTokens = reader.IsDBNull(4) ? 0 : reader.GetInt64(4),
                            OutputTokens = reader.IsDBNull(5) ? 0 : reader.GetInt64(5),
                            AvgDurationMs = reader.IsDBNull(6) ? null : reader.GetDouble(6),
                            AvgFindingCount = reader.IsDBNull(7) ? null : reader.GetDouble(7),
                        });
                    }
                }

                // Finding counts from review_executions
                long totalCandidates = 0;
                long totalFinal = 0;
                long criticalCount = 0;
                long majorCount = 0;
                long minorCount = 0;
                await using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT
                            SUM(COALESCE(candidate_finding_count, 0)),
                            SUM(COALESCE(selected_finding_count, 0)),
                            SUM(COALESCE(critical_count, 0)),
                            SUM(COALESCE(major_count, 0)),
                            SUM(COALESCE(minor_count, 0))
                        FROM review_executions
                        WHERE started_at >= @from AND started_at <= @to
                          AND (@projectId IS NULL OR project_id = @projectId)";
                    cmd.Parameters.AddWithValue("@from", fromStr);
                    cmd.Parameters.AddWithValue("@to", toStr);
                    cmd.Parameters.AddWithValue("@projectId", projectIdParam);
                    await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    if (await reader.ReadAsync(cancellationToken))
                    {
                        totalCandidates = reader.IsDBNull(0) ? 0 : reader.GetInt64(0);
                        totalFinal = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                        criticalCount = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                        majorCount = reader.IsDBNull(3) ? 0 : reader.GetInt64(3);
                        minorCount = reader.IsDBNull(4) ? 0 : reader.GetInt64(4);
                    }
                }

                ReviewTurnStatistics? detection = turnStats.FirstOrDefault(t => t.TurnType == 1);
                ReviewTurnStatistics? selection = turnStats.FirstOrDefault(t => t.TurnType == 2);
                double? selectionRate = totalCandidates > 0 ? (double)totalFinal / totalCandidates : null;

                return new ReviewStatistics
                {
                    Detection = detection,
                    Selection = selection,
                    TotalCandidates = totalCandidates,
                    TotalFinalFindings = totalFinal,
                    SelectionRate = selectionRate,
                    CriticalCount = criticalCount,
                    MajorCount = majorCount,
                    MinorCount = minorCount,
                };
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<IReadOnlyList<ModelUsageStatistics>> GetModelUsageAsync(StatisticsQuery query, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);

                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT
                        rt.model,
                        COUNT(*) AS calls,
                        SUM(COALESCE(rt.input_tokens, 0)) AS input_tokens,
                        SUM(COALESCE(rt.output_tokens, 0)) AS output_tokens,
                        AVG(rt.duration_ms) AS avg_duration_ms,
                        SUM(CASE WHEN rt.success = 0 THEN 1 ELSE 0 END) AS failure_count
                    FROM review_turns rt
                    JOIN review_executions re ON re.id = rt.review_execution_id
                    WHERE re.started_at >= @from AND re.started_at <= @to
                      AND (@projectId IS NULL OR re.project_id = @projectId)
                      AND rt.model IS NOT NULL
                    GROUP BY rt.model
                    ORDER BY calls DESC";
                cmd.Parameters.AddWithValue("@from", query.From.ToString("O"));
                cmd.Parameters.AddWithValue("@to", query.To.ToString("O"));
                cmd.Parameters.AddWithValue("@projectId", query.ProjectId.HasValue ? (object)query.ProjectId.Value : DBNull.Value);

                await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
                List<ModelUsageStatistics> results = new();
                while (await reader.ReadAsync(cancellationToken))
                {
                    results.Add(new ModelUsageStatistics
                    {
                        Model = reader.GetString(0),
                        Calls = (int)reader.GetInt64(1),
                        InputTokens = reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                        OutputTokens = reader.IsDBNull(3) ? 0 : reader.GetInt64(3),
                        AvgDurationMs = reader.IsDBNull(4) ? null : reader.GetDouble(4),
                        FailureCount = reader.IsDBNull(5) ? 0 : (int)reader.GetInt64(5),
                    });
                }
                return results;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<RuleStatistics> GetRuleStatisticsAsync(StatisticsQuery query, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);

                string fromStr = query.From.ToString("O");
                string toStr = query.To.ToString("O");
                object projectIdParam = query.ProjectId.HasValue ? (object)query.ProjectId.Value : DBNull.Value;

                // Active rule count
                int activeRuleCount = 0;
                await using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT COUNT(*)
                        FROM learned_rules
                        WHERE confidence_score > 0
                          AND (@projectId IS NULL OR project_id = @projectId)";
                    cmd.Parameters.AddWithValue("@projectId", projectIdParam);
                    object? scalar = await cmd.ExecuteScalarAsync(cancellationToken);
                    activeRuleCount = scalar is long l ? (int)l : 0;
                }

                // Learning event counts within date range
                int createdCount = 0;
                int expiredCount = 0;
                int confidenceIncreaseCount = 0;
                int confidenceDecreaseCount = 0;
                await using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT
                            SUM(CASE WHEN event_type = 'Created' THEN 1 ELSE 0 END) AS created_count,
                            SUM(CASE WHEN event_type = 'Expired' THEN 1 ELSE 0 END) AS expired_count,
                            SUM(CASE WHEN event_type = 'ConfidenceIncreased' THEN 1 ELSE 0 END) AS confidence_increase_count,
                            SUM(CASE WHEN event_type = 'ConfidenceDecreased' THEN 1 ELSE 0 END) AS confidence_decrease_count
                        FROM rule_learning_events
                        WHERE created_at >= @from AND created_at <= @to
                          AND (@projectId IS NULL OR project_id = @projectId)";
                    cmd.Parameters.AddWithValue("@from", fromStr);
                    cmd.Parameters.AddWithValue("@to", toStr);
                    cmd.Parameters.AddWithValue("@projectId", projectIdParam);
                    await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    if (await reader.ReadAsync(cancellationToken))
                    {
                        createdCount = reader.IsDBNull(0) ? 0 : (int)reader.GetInt64(0);
                        expiredCount = reader.IsDBNull(1) ? 0 : (int)reader.GetInt64(1);
                        confidenceIncreaseCount = reader.IsDBNull(2) ? 0 : (int)reader.GetInt64(2);
                        confidenceDecreaseCount = reader.IsDBNull(3) ? 0 : (int)reader.GetInt64(3);
                    }
                }

                // Rule search statistics
                RuleSearchStatistics searchStats = new();
                await using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT
                            AVG(rse.total_rule_count) AS avg_scanned,
                            AVG(rse.candidate_rule_count) AS avg_candidate,
                            AVG(rse.selected_rule_count) AS avg_selected,
                            AVG(rse.search_duration_ms) AS avg_search_ms,
                            AVG(rse.embedding_duration_ms) AS avg_embedding_ms
                        FROM rule_search_executions rse
                        JOIN review_executions re ON re.id = rse.review_execution_id
                        WHERE re.started_at >= @from AND re.started_at <= @to
                          AND rse.success = 1
                          AND (@projectId IS NULL OR rse.project_id = @projectId)";
                    cmd.Parameters.AddWithValue("@from", fromStr);
                    cmd.Parameters.AddWithValue("@to", toStr);
                    cmd.Parameters.AddWithValue("@projectId", projectIdParam);
                    await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    if (await reader.ReadAsync(cancellationToken))
                    {
                        searchStats = new RuleSearchStatistics
                        {
                            AvgScannedCount = reader.IsDBNull(0) ? null : reader.GetDouble(0),
                            AvgCandidateCount = reader.IsDBNull(1) ? null : reader.GetDouble(1),
                            AvgSelectedCount = reader.IsDBNull(2) ? null : reader.GetDouble(2),
                            AvgSearchDurationMs = reader.IsDBNull(3) ? null : reader.GetDouble(3),
                            AvgEmbeddingDurationMs = reader.IsDBNull(4) ? null : reader.GetDouble(4),
                        };
                    }
                }

                return new RuleStatistics
                {
                    ActiveRuleCount = activeRuleCount,
                    CreatedCount = createdCount,
                    ExpiredCount = expiredCount,
                    ConfidenceIncreaseCount = confidenceIncreaseCount,
                    ConfidenceDecreaseCount = confidenceDecreaseCount,
                    SearchStats = searchStats,
                };
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<IReadOnlyList<RuleUsageStatistics>> GetRuleUsageAsync(StatisticsQuery query, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);

                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT rru.project_id, p.name AS project_name, rru.rule_id,
                           lr.rule_description AS rule_display_text,
                           COUNT(*) AS candidate_matches,
                           SUM(CASE WHEN rru.used_in_prompt = 1 THEN 1 ELSE 0 END) AS prompt_uses,
                           SUM(rru.produced_candidate) AS produced_candidate_count,
                           SUM(rru.produced_final_finding) AS produced_final_count
                    FROM review_rule_usage rru
                    JOIN review_executions re ON re.id = rru.review_execution_id
                    JOIN projects p ON p.id = rru.project_id
                    LEFT JOIN learned_rules lr ON lr.id = rru.rule_id
                    WHERE re.started_at >= @from AND re.started_at <= @to
                      AND (@projectId IS NULL OR rru.project_id = @projectId)
                    GROUP BY rru.project_id, p.name, rru.rule_id, lr.rule_description
                    ORDER BY prompt_uses DESC
                    LIMIT 100";
                cmd.Parameters.AddWithValue("@from", query.From.ToString("O"));
                cmd.Parameters.AddWithValue("@to", query.To.ToString("O"));
                cmd.Parameters.AddWithValue("@projectId", query.ProjectId.HasValue ? (object)query.ProjectId.Value : DBNull.Value);

                await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
                List<RuleUsageStatistics> results = new();
                while (await reader.ReadAsync(cancellationToken))
                {
                    long projectId = reader.GetInt64(0);
                    string projectName = reader.GetString(1);
                    string ruleId = reader.GetString(2);
                    string? ruleDisplayText = reader.IsDBNull(3) ? null : reader.GetString(3);
                    int candidateMatches = (int)reader.GetInt64(4);
                    int promptUses = reader.IsDBNull(5) ? 0 : (int)reader.GetInt64(5);
                    int producedCandidateCount = reader.IsDBNull(6) ? 0 : (int)reader.GetInt64(6);
                    int producedFinalFindingCount = reader.IsDBNull(7) ? 0 : (int)reader.GetInt64(7);

                    double? candidateHitRate = promptUses > 0 ? (double)producedCandidateCount / promptUses : null;
                    double? finalHitRate = promptUses > 0 ? (double)producedFinalFindingCount / promptUses : null;

                    results.Add(new RuleUsageStatistics
                    {
                        ProjectId = projectId,
                        ProjectName = projectName,
                        RuleId = ruleId,
                        RuleDisplayText = ruleDisplayText,
                        CandidateMatches = candidateMatches,
                        PromptUses = promptUses,
                        ProducedCandidateCount = producedCandidateCount,
                        ProducedFinalFindingCount = producedFinalFindingCount,
                        CandidateHitRate = candidateHitRate,
                        FinalHitRate = finalHitRate,
                    });
                }
                return results;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<IReadOnlyList<ProjectStatistics>> GetProjectStatisticsAsync(StatisticsQuery query, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);

                string fromStr = query.From.ToString("O");
                string toStr = query.To.ToString("O");
                object projectIdParam = query.ProjectId.HasValue ? (object)query.ProjectId.Value : DBNull.Value;

                // Query 1: review-level aggregates
                Dictionary<long, (string name, int reviewCount, double? avgDurationMs, int failureCount, long totalCandidates, long totalFinal)> reviewAgg = new();
                await using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT re.project_id, p.name AS project_name,
                               COUNT(*) AS review_count,
                               AVG(re.duration_ms) AS avg_duration_ms,
                               SUM(CASE WHEN re.status='Failed' THEN 1 ELSE 0 END) AS failure_count,
                               SUM(COALESCE(re.candidate_finding_count, 0)) AS total_candidates,
                               SUM(COALESCE(re.selected_finding_count, 0)) AS total_final
                        FROM review_executions re
                        JOIN projects p ON p.id = re.project_id
                        WHERE re.started_at >= @from AND re.started_at <= @to
                          AND (@projectId IS NULL OR re.project_id = @projectId)
                        GROUP BY re.project_id, p.name
                        ORDER BY review_count DESC";
                    cmd.Parameters.AddWithValue("@from", fromStr);
                    cmd.Parameters.AddWithValue("@to", toStr);
                    cmd.Parameters.AddWithValue("@projectId", projectIdParam);
                    await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        long pid = reader.GetInt64(0);
                        string name = reader.GetString(1);
                        int reviewCount = (int)reader.GetInt64(2);
                        double? avgDurationMs = reader.IsDBNull(3) ? null : reader.GetDouble(3);
                        int failureCount = reader.IsDBNull(4) ? 0 : (int)reader.GetInt64(4);
                        long totalCandidates = reader.IsDBNull(5) ? 0 : reader.GetInt64(5);
                        long totalFinal = reader.IsDBNull(6) ? 0 : reader.GetInt64(6);
                        reviewAgg[pid] = (name, reviewCount, avgDurationMs, failureCount, totalCandidates, totalFinal);
                    }
                }

                // Query 2: token totals per project
                Dictionary<long, (long inputTokens, long outputTokens)> tokenAgg = new();
                await using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT re.project_id,
                               SUM(COALESCE(rt.input_tokens, 0)) AS input_tokens,
                               SUM(COALESCE(rt.output_tokens, 0)) AS output_tokens
                        FROM review_turns rt
                        JOIN review_executions re ON re.id = rt.review_execution_id
                        WHERE re.started_at >= @from AND re.started_at <= @to
                          AND (@projectId IS NULL OR re.project_id = @projectId)
                        GROUP BY re.project_id";
                    cmd.Parameters.AddWithValue("@from", fromStr);
                    cmd.Parameters.AddWithValue("@to", toStr);
                    cmd.Parameters.AddWithValue("@projectId", projectIdParam);
                    await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        long pid = reader.GetInt64(0);
                        long inputTokens = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                        long outputTokens = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
                        tokenAgg[pid] = (inputTokens, outputTokens);
                    }
                }

                // Query 3: rule search avg duration per project
                Dictionary<long, double> searchDurationAgg = new();
                await using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT rse.project_id, AVG(rse.total_duration_ms) AS avg_search_ms
                        FROM rule_search_executions rse
                        JOIN review_executions re ON re.id = rse.review_execution_id
                        WHERE re.started_at >= @from AND re.started_at <= @to
                          AND rse.success = 1
                          AND (@projectId IS NULL OR rse.project_id = @projectId)
                        GROUP BY rse.project_id";
                    cmd.Parameters.AddWithValue("@from", fromStr);
                    cmd.Parameters.AddWithValue("@to", toStr);
                    cmd.Parameters.AddWithValue("@projectId", projectIdParam);
                    await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        long pid = reader.GetInt64(0);
                        if (!reader.IsDBNull(1))
                            searchDurationAgg[pid] = reader.GetDouble(1);
                    }
                }

                // Query 4: active rule count per project
                Dictionary<long, int> activeRuleAgg = new();
                await using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT project_id, COUNT(*) AS active_rules
                        FROM learned_rules
                        WHERE confidence_score > 0
                          AND (@projectId IS NULL OR project_id = @projectId)
                        GROUP BY project_id";
                    cmd.Parameters.AddWithValue("@projectId", projectIdParam);
                    await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        long pid = reader.GetInt64(0);
                        int count = (int)reader.GetInt64(1);
                        activeRuleAgg[pid] = count;
                    }
                }

                // Join in C#
                List<ProjectStatistics> results = new();
                foreach (KeyValuePair<long, (string name, int reviewCount, double? avgDurationMs, int failureCount, long totalCandidates, long totalFinal)> kvp in reviewAgg)
                {
                    long pid = kvp.Key;
                    (string name, int reviewCount, double? avgDurationMs, int failureCount, long totalCandidates, long totalFinal) = kvp.Value;

                    tokenAgg.TryGetValue(pid, out (long inputTokens, long outputTokens) tokens);
                    searchDurationAgg.TryGetValue(pid, out double avgSearchMs);
                    activeRuleAgg.TryGetValue(pid, out int activeRules);

                    double? errorRate = reviewCount > 0 ? (double)failureCount / reviewCount : null;
                    double? selectionRate = totalCandidates > 0 ? (double)totalFinal / totalCandidates : null;
                    double? avgSearchMsNullable = searchDurationAgg.ContainsKey(pid) ? avgSearchMs : null;

                    results.Add(new ProjectStatistics
                    {
                        ProjectId = pid,
                        ProjectName = name,
                        ReviewCount = reviewCount,
                        InputTokens = tokens.inputTokens,
                        OutputTokens = tokens.outputTokens,
                        AvgReviewDurationMs = avgDurationMs,
                        SelectionRate = selectionRate,
                        ErrorRate = errorRate,
                        ActiveRuleCount = activeRules,
                        AvgRuleSearchDurationMs = avgSearchMsNullable,
                    });
                }
                return results;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _semaphore.Dispose();
        }
    }
}
