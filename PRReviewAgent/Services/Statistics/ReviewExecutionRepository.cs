using Microsoft.Data.Sqlite;

namespace PRReviewAgent.Services.Statistics
{
    public sealed class ReviewExecutionRepository : IReviewExecutionRecorder, IDisposable
    {
        private readonly string _connectionString;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public ReviewExecutionRepository(string dbPath)
        {
            _connectionString = $"Data Source={dbPath}";
        }

        internal ReviewExecutionRepository(string dbPath, bool isConnectionString)
        {
            _connectionString = isConnectionString ? dbPath : $"Data Source={dbPath}";
        }

        public async Task<long> StartAsync(long projectId, string mergeRequestId, DateTimeOffset startedAt, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO review_executions (project_id, merge_request_id, started_at, status)
                    VALUES (@projectId, @mergeRequestId, @startedAt, @status);
                    SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@projectId", projectId);
                cmd.Parameters.AddWithValue("@mergeRequestId", mergeRequestId);
                cmd.Parameters.AddWithValue("@startedAt", startedAt.ToString("O"));
                cmd.Parameters.AddWithValue("@status", ReviewExecutionStatus.Running.ToString());
                return (long)(await cmd.ExecuteScalarAsync(cancellationToken))!;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task CompleteSuccessAsync(long reviewExecutionId, ReviewExecutionResult result, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    UPDATE review_executions SET
                        completed_at = @completedAt,
                        duration_ms = @durationMs,
                        candidate_finding_count = @candidateCount,
                        selected_finding_count = @selectedCount,
                        critical_count = @criticalCount,
                        major_count = @majorCount,
                        minor_count = @minorCount,
                        status = @status
                    WHERE id = @id";
                cmd.Parameters.AddWithValue("@completedAt", result.CompletedAt.ToString("O"));
                cmd.Parameters.AddWithValue("@durationMs", result.DurationMs);
                cmd.Parameters.AddWithValue("@candidateCount", result.CandidateFindingCount);
                cmd.Parameters.AddWithValue("@selectedCount", result.SelectedFindingCount);
                cmd.Parameters.AddWithValue("@criticalCount", result.CriticalCount);
                cmd.Parameters.AddWithValue("@majorCount", result.MajorCount);
                cmd.Parameters.AddWithValue("@minorCount", result.MinorCount);
                cmd.Parameters.AddWithValue("@status", ReviewExecutionStatus.Succeeded.ToString());
                cmd.Parameters.AddWithValue("@id", reviewExecutionId);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task CompleteFailureAsync(long reviewExecutionId, string errorType, DateTimeOffset completedAt, long durationMs, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    UPDATE review_executions SET
                        completed_at = @completedAt,
                        duration_ms = @durationMs,
                        status = @status,
                        error_type = @errorType
                    WHERE id = @id";
                cmd.Parameters.AddWithValue("@completedAt", completedAt.ToString("O"));
                cmd.Parameters.AddWithValue("@durationMs", durationMs);
                cmd.Parameters.AddWithValue("@status", ReviewExecutionStatus.Failed.ToString());
                cmd.Parameters.AddWithValue("@errorType", errorType);
                cmd.Parameters.AddWithValue("@id", reviewExecutionId);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<ReviewExecution?> GetByIdAsync(long reviewExecutionId, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM review_executions WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", reviewExecutionId);
                await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken)) return null;
                return MapRow(reader);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<List<ReviewExecution>> GetByProjectAsync(long projectId, int limit = 100, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM review_executions WHERE project_id = @projectId ORDER BY started_at DESC LIMIT @limit";
                cmd.Parameters.AddWithValue("@projectId", projectId);
                cmd.Parameters.AddWithValue("@limit", limit);
                await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
                List<ReviewExecution> results = new List<ReviewExecution>();
                while (await reader.ReadAsync(cancellationToken))
                    results.Add(MapRow(reader));
                return results;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private static ReviewExecution MapRow(SqliteDataReader reader)
        {
            return new ReviewExecution
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                ProjectId = reader.GetInt64(reader.GetOrdinal("project_id")),
                MergeRequestId = reader.GetString(reader.GetOrdinal("merge_request_id")),
                StartedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("started_at"))),
                CompletedAt = reader.IsDBNull(reader.GetOrdinal("completed_at")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("completed_at"))),
                DurationMs = reader.IsDBNull(reader.GetOrdinal("duration_ms")) ? null : reader.GetInt64(reader.GetOrdinal("duration_ms")),
                CandidateFindingCount = reader.IsDBNull(reader.GetOrdinal("candidate_finding_count")) ? null : reader.GetInt32(reader.GetOrdinal("candidate_finding_count")),
                SelectedFindingCount = reader.IsDBNull(reader.GetOrdinal("selected_finding_count")) ? null : reader.GetInt32(reader.GetOrdinal("selected_finding_count")),
                CriticalCount = reader.IsDBNull(reader.GetOrdinal("critical_count")) ? null : reader.GetInt32(reader.GetOrdinal("critical_count")),
                MajorCount = reader.IsDBNull(reader.GetOrdinal("major_count")) ? null : reader.GetInt32(reader.GetOrdinal("major_count")),
                MinorCount = reader.IsDBNull(reader.GetOrdinal("minor_count")) ? null : reader.GetInt32(reader.GetOrdinal("minor_count")),
                Status = Enum.Parse<ReviewExecutionStatus>(reader.GetString(reader.GetOrdinal("status"))),
                ErrorType = reader.IsDBNull(reader.GetOrdinal("error_type")) ? null : reader.GetString(reader.GetOrdinal("error_type")),
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _semaphore.Dispose();
        }
    }
}
