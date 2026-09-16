using Microsoft.Data.Sqlite;

namespace PRReviewAgent.Services.Statistics
{
    public sealed class ReviewTurnRepository : IReviewTurnRecorder, IDisposable
    {
        private readonly string _connectionString;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public ReviewTurnRepository(string dbPath)
        {
            _connectionString = $"Data Source={dbPath}";
        }

        internal ReviewTurnRepository(string dbPath, bool isConnectionString)
        {
            _connectionString = isConnectionString ? dbPath : $"Data Source={dbPath}";
        }

        public async Task<long> StartAsync(long reviewExecutionId, ReviewTurnType turnType, string model, DateTimeOffset startedAt, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO review_turns (review_execution_id, turn_type, model, success, started_at)
                    VALUES (@execId, @turnType, @model, 0, @startedAt);
                    SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@execId", reviewExecutionId);
                cmd.Parameters.AddWithValue("@turnType", turnType.ToString());
                cmd.Parameters.AddWithValue("@model", model);
                cmd.Parameters.AddWithValue("@startedAt", startedAt.ToString("O"));
                return (long)(await cmd.ExecuteScalarAsync(cancellationToken))!;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task CompleteSuccessAsync(long reviewTurnId, int? inputTokens, int? outputTokens, int findingCount, DateTimeOffset completedAt, long durationMs, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    UPDATE review_turns SET
                        input_tokens = @inputTokens,
                        output_tokens = @outputTokens,
                        finding_count = @findingCount,
                        success = 1,
                        completed_at = @completedAt,
                        duration_ms = @durationMs
                    WHERE id = @id";
                cmd.Parameters.AddWithValue("@inputTokens", inputTokens.HasValue ? (object)inputTokens.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@outputTokens", outputTokens.HasValue ? (object)outputTokens.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@findingCount", findingCount);
                cmd.Parameters.AddWithValue("@completedAt", completedAt.ToString("O"));
                cmd.Parameters.AddWithValue("@durationMs", durationMs);
                cmd.Parameters.AddWithValue("@id", reviewTurnId);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task CompleteFailureAsync(long reviewTurnId, int? inputTokens, int? outputTokens, string errorType, DateTimeOffset completedAt, long durationMs, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    UPDATE review_turns SET
                        input_tokens = @inputTokens,
                        output_tokens = @outputTokens,
                        success = 0,
                        error_type = @errorType,
                        completed_at = @completedAt,
                        duration_ms = @durationMs
                    WHERE id = @id";
                cmd.Parameters.AddWithValue("@inputTokens", inputTokens.HasValue ? (object)inputTokens.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@outputTokens", outputTokens.HasValue ? (object)outputTokens.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@errorType", errorType);
                cmd.Parameters.AddWithValue("@completedAt", completedAt.ToString("O"));
                cmd.Parameters.AddWithValue("@durationMs", durationMs);
                cmd.Parameters.AddWithValue("@id", reviewTurnId);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<IReadOnlyList<ReviewTurn>> GetByReviewExecutionIdAsync(long reviewExecutionId, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM review_turns WHERE review_execution_id = @execId ORDER BY started_at ASC";
                cmd.Parameters.AddWithValue("@execId", reviewExecutionId);
                await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
                List<ReviewTurn> results = new List<ReviewTurn>();
                while (await reader.ReadAsync(cancellationToken))
                    results.Add(MapRow(reader));
                return results;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private static ReviewTurn MapRow(SqliteDataReader reader)
        {
            return new ReviewTurn
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                ReviewExecutionId = reader.GetInt64(reader.GetOrdinal("review_execution_id")),
                TurnType = Enum.Parse<ReviewTurnType>(reader.GetString(reader.GetOrdinal("turn_type"))),
                Model = reader.GetString(reader.GetOrdinal("model")),
                InputTokens = reader.IsDBNull(reader.GetOrdinal("input_tokens")) ? null : reader.GetInt32(reader.GetOrdinal("input_tokens")),
                OutputTokens = reader.IsDBNull(reader.GetOrdinal("output_tokens")) ? null : reader.GetInt32(reader.GetOrdinal("output_tokens")),
                DurationMs = reader.IsDBNull(reader.GetOrdinal("duration_ms")) ? null : reader.GetInt64(reader.GetOrdinal("duration_ms")),
                FindingCount = reader.IsDBNull(reader.GetOrdinal("finding_count")) ? null : reader.GetInt32(reader.GetOrdinal("finding_count")),
                Success = reader.GetInt64(reader.GetOrdinal("success")) != 0,
                ErrorType = reader.IsDBNull(reader.GetOrdinal("error_type")) ? null : reader.GetString(reader.GetOrdinal("error_type")),
                StartedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("started_at"))),
                CompletedAt = reader.IsDBNull(reader.GetOrdinal("completed_at")) ? null : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("completed_at"))),
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
