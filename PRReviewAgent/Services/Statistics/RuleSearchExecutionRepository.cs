using Microsoft.Data.Sqlite;

namespace PRReviewAgent.Services.Statistics
{
    public sealed class RuleSearchExecutionRepository : IRuleSearchExecutionRecorder, IDisposable
    {
        private readonly string _connectionString;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public RuleSearchExecutionRepository(string dbPath)
        {
            _connectionString = $"Data Source={dbPath}";
        }

        internal RuleSearchExecutionRepository(string dbPath, bool isConnectionString)
        {
            _connectionString = isConnectionString ? dbPath : $"Data Source={dbPath}";
        }

        public async Task RecordAsync(RuleSearchExecution execution, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO rule_search_executions
                        (review_execution_id, project_id, total_rule_count, candidate_rule_count, selected_rule_count,
                         embedding_duration_ms, search_duration_ms, total_duration_ms,
                         success, error_type, started_at, completed_at)
                    VALUES
                        (@reviewExecutionId, @projectId, @totalRuleCount, @candidateRuleCount, @selectedRuleCount,
                         @embeddingDurationMs, @searchDurationMs, @totalDurationMs,
                         @success, @errorType, @startedAt, @completedAt)";
                cmd.Parameters.AddWithValue("@reviewExecutionId", execution.ReviewExecutionId);
                cmd.Parameters.AddWithValue("@projectId", execution.ProjectId);
                cmd.Parameters.AddWithValue("@totalRuleCount", execution.TotalRuleCount);
                cmd.Parameters.AddWithValue("@candidateRuleCount", (object?)execution.CandidateRuleCount ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@selectedRuleCount", (object?)execution.SelectedRuleCount ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@embeddingDurationMs", (object?)execution.EmbeddingDurationMs ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@searchDurationMs", (object?)execution.SearchDurationMs ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@totalDurationMs", (object?)execution.TotalDurationMs ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@success", execution.Success ? 1 : 0);
                cmd.Parameters.AddWithValue("@errorType", (object?)execution.ErrorType ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@startedAt", execution.StartedAt.ToString("O"));
                cmd.Parameters.AddWithValue("@completedAt", execution.CompletedAt.HasValue ? (object)execution.CompletedAt.Value.ToString("O") : DBNull.Value);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<IReadOnlyList<RuleSearchExecution>> GetByReviewExecutionIdAsync(long reviewExecutionId, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, review_execution_id, project_id, total_rule_count, candidate_rule_count, selected_rule_count,
                           embedding_duration_ms, search_duration_ms, total_duration_ms,
                           success, error_type, started_at, completed_at
                    FROM rule_search_executions
                    WHERE review_execution_id = @reviewExecutionId
                    ORDER BY started_at ASC";
                cmd.Parameters.AddWithValue("@reviewExecutionId", reviewExecutionId);
                await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
                List<RuleSearchExecution> results = new();
                while (await reader.ReadAsync(cancellationToken))
                {
                    results.Add(new RuleSearchExecution
                    {
                        Id = reader.GetInt64(0),
                        ReviewExecutionId = reader.GetInt64(1),
                        ProjectId = reader.GetInt64(2),
                        TotalRuleCount = (int)reader.GetInt64(3),
                        CandidateRuleCount = reader.IsDBNull(4) ? null : (int)reader.GetInt64(4),
                        SelectedRuleCount = reader.IsDBNull(5) ? null : (int)reader.GetInt64(5),
                        EmbeddingDurationMs = reader.IsDBNull(6) ? null : reader.GetInt64(6),
                        SearchDurationMs = reader.IsDBNull(7) ? null : reader.GetInt64(7),
                        TotalDurationMs = reader.IsDBNull(8) ? null : reader.GetInt64(8),
                        Success = reader.GetInt64(9) != 0,
                        ErrorType = reader.IsDBNull(10) ? null : reader.GetString(10),
                        StartedAt = DateTimeOffset.Parse(reader.GetString(11)),
                        CompletedAt = reader.IsDBNull(12) ? null : DateTimeOffset.Parse(reader.GetString(12)),
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
