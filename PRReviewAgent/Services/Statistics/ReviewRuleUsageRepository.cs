using Microsoft.Data.Sqlite;

namespace PRReviewAgent.Services.Statistics
{
    public sealed class ReviewRuleUsageRepository : IReviewRuleUsageRepository, IDisposable
    {
        private readonly string _connectionString;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public ReviewRuleUsageRepository(string dbPath)
        {
            _connectionString = $"Data Source={dbPath}";
        }

        internal ReviewRuleUsageRepository(string dbPath, bool isConnectionString)
        {
            _connectionString = isConnectionString ? dbPath : $"Data Source={dbPath}";
        }

        public async Task AddRangeAsync(IReadOnlyCollection<ReviewRuleUsage> usages, CancellationToken cancellationToken = default)
        {
            if (usages.Count == 0) return;
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteTransaction tx = conn.BeginTransaction();
                try
                {
                    foreach (ReviewRuleUsage u in usages)
                    {
                        await using SqliteCommand cmd = conn.CreateCommand();
                        cmd.Transaction = tx;
                        cmd.CommandText = @"
                            INSERT OR IGNORE INTO review_rule_usage
                                (review_execution_id, project_id, rule_id, similarity_score,
                                 used_in_prompt, produced_candidate, produced_final_finding, created_at)
                            VALUES
                                (@reviewExecutionId, @projectId, @ruleId, @similarityScore,
                                 @usedInPrompt, @producedCandidate, @producedFinalFinding, @createdAt)";
                        cmd.Parameters.AddWithValue("@reviewExecutionId", u.ReviewExecutionId);
                        cmd.Parameters.AddWithValue("@projectId", u.ProjectId);
                        cmd.Parameters.AddWithValue("@ruleId", u.RuleId);
                        cmd.Parameters.AddWithValue("@similarityScore", (object?)u.SimilarityScore ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@usedInPrompt", u.UsedInPrompt ? 1 : 0);
                        cmd.Parameters.AddWithValue("@producedCandidate", u.ProducedCandidate ? 1 : 0);
                        cmd.Parameters.AddWithValue("@producedFinalFinding", u.ProducedFinalFinding ? 1 : 0);
                        cmd.Parameters.AddWithValue("@createdAt", u.CreatedAt.ToString("O"));
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                    await tx.CommitAsync(cancellationToken);
                }
                catch
                {
                    await tx.RollbackAsync(cancellationToken);
                    throw;
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task MarkProducedCandidateAsync(
            long reviewExecutionId,
            long projectId,
            IReadOnlyCollection<string> ruleIds,
            CancellationToken cancellationToken = default)
        {
            if (ruleIds.Count == 0) return;
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                string placeholders = string.Join(",", ruleIds.Select((_, i) => $"@r{i}"));
                cmd.CommandText = $@"
                    UPDATE review_rule_usage
                    SET produced_candidate = 1
                    WHERE review_execution_id = @execId
                      AND project_id = @projectId
                      AND rule_id IN ({placeholders})";
                cmd.Parameters.AddWithValue("@execId", reviewExecutionId);
                cmd.Parameters.AddWithValue("@projectId", projectId);
                int idx = 0;
                foreach (string ruleId in ruleIds)
                    cmd.Parameters.AddWithValue($"@r{idx++}", ruleId);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task MarkProducedFinalFindingAsync(
            long reviewExecutionId,
            long projectId,
            IReadOnlyCollection<string> ruleIds,
            CancellationToken cancellationToken = default)
        {
            if (ruleIds.Count == 0) return;
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                string placeholders = string.Join(",", ruleIds.Select((_, i) => $"@r{i}"));
                cmd.CommandText = $@"
                    UPDATE review_rule_usage
                    SET produced_final_finding = 1
                    WHERE review_execution_id = @execId
                      AND project_id = @projectId
                      AND rule_id IN ({placeholders})";
                cmd.Parameters.AddWithValue("@execId", reviewExecutionId);
                cmd.Parameters.AddWithValue("@projectId", projectId);
                int idx = 0;
                foreach (string ruleId in ruleIds)
                    cmd.Parameters.AddWithValue($"@r{idx++}", ruleId);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<IReadOnlyList<ReviewRuleUsage>> GetByReviewExecutionIdAsync(
            long reviewExecutionId,
            CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, review_execution_id, project_id, rule_id, similarity_score,
                           used_in_prompt, produced_candidate, produced_final_finding, created_at
                    FROM review_rule_usage
                    WHERE review_execution_id = @execId
                    ORDER BY id ASC";
                cmd.Parameters.AddWithValue("@execId", reviewExecutionId);
                await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
                List<ReviewRuleUsage> results = new();
                while (await reader.ReadAsync(cancellationToken))
                {
                    results.Add(new ReviewRuleUsage
                    {
                        Id = reader.GetInt64(0),
                        ReviewExecutionId = reader.GetInt64(1),
                        ProjectId = reader.GetInt64(2),
                        RuleId = reader.GetString(3),
                        SimilarityScore = reader.IsDBNull(4) ? null : reader.GetDouble(4),
                        UsedInPrompt = reader.GetInt64(5) != 0,
                        ProducedCandidate = reader.GetInt64(6) != 0,
                        ProducedFinalFinding = reader.GetInt64(7) != 0,
                        CreatedAt = DateTimeOffset.Parse(reader.GetString(8)),
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
