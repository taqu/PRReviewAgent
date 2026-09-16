using Microsoft.Data.Sqlite;

namespace PRReviewAgent.Services.AutoImprove
{
    public sealed class RuleLearningEventRepository : IRuleLearningEventRepository, IDisposable
    {
        private readonly string _connectionString;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public RuleLearningEventRepository(string dbPath)
        {
            _connectionString = $"Data Source={dbPath}";
        }

        internal RuleLearningEventRepository(string dbPath, bool isConnectionString)
        {
            _connectionString = isConnectionString ? dbPath : $"Data Source={dbPath}";
        }

        public async Task<IReadOnlyList<RuleLearningEvent>> GetByRuleAsync(long projectId, string ruleId, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, project_id, rule_id, merge_request_id, event_type,
                           confidence_before, confidence_after, created_at
                    FROM rule_learning_events
                    WHERE project_id = @projectId AND rule_id = @ruleId
                    ORDER BY created_at ASC, id ASC";
                cmd.Parameters.AddWithValue("@projectId", projectId);
                cmd.Parameters.AddWithValue("@ruleId", ruleId);
                await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
                List<RuleLearningEvent> results = new List<RuleLearningEvent>();
                while (await reader.ReadAsync(cancellationToken))
                    results.Add(MapRow(reader));
                return results;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private static RuleLearningEvent MapRow(SqliteDataReader reader)
        {
            return new RuleLearningEvent
            {
                Id = reader.GetInt64(reader.GetOrdinal("id")),
                ProjectId = reader.GetInt64(reader.GetOrdinal("project_id")),
                RuleId = reader.GetString(reader.GetOrdinal("rule_id")),
                MergeRequestId = reader.IsDBNull(reader.GetOrdinal("merge_request_id")) ? null : reader.GetString(reader.GetOrdinal("merge_request_id")),
                EventType = Enum.Parse<RuleLearningEventType>(reader.GetString(reader.GetOrdinal("event_type"))),
                ConfidenceBefore = reader.IsDBNull(reader.GetOrdinal("confidence_before")) ? null : reader.GetDouble(reader.GetOrdinal("confidence_before")),
                ConfidenceAfter = reader.IsDBNull(reader.GetOrdinal("confidence_after")) ? null : reader.GetDouble(reader.GetOrdinal("confidence_after")),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
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
