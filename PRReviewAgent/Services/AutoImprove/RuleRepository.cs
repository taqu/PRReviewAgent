using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace PRReviewAgent.Services.AutoImprove
{
    public sealed class RuleRepository : IDisposable
    {
        private readonly string _connectionString;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public RuleRepository(string dbPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
            _connectionString = $"Data Source={dbPath}";
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                {
                    await using SqliteCommand cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS learned_rules (
                        id TEXT PRIMARY KEY,
                        merge_request_id TEXT NOT NULL,
                        ast_pattern TEXT NOT NULL,
                        rule_description TEXT NOT NULL,
                        bad_pattern TEXT,
                        good_pattern TEXT,
                        embedding BLOB NOT NULL,
                        confidence_score INTEGER NOT NULL DEFAULT 5,
                        last_hit_at TEXT NOT NULL,
                        created_at TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_confidence ON learned_rules(confidence_score);";
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
                {
                    await using SqliteCommand cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS pending_reviews (
                        pr_key TEXT NOT NULL,
                        rule_id TEXT NOT NULL,
                        bad_pattern TEXT,
                        PRIMARY KEY (pr_key, rule_id)
                    );";
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task InsertAsync(LearnedRule rule, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO learned_rules
                    (id, merge_request_id, ast_pattern, rule_description, bad_pattern, good_pattern, embedding, confidence_score, last_hit_at, created_at)
                    VALUES (@id, @ast_pattern, @rule_description, @bad_pattern, @good_pattern, @embedding, @confidence_score, @last_hit_at, @created_at)";
                cmd.Parameters.AddWithValue("@id", rule.Id);
                cmd.Parameters.AddWithValue("@merge_request_id", rule.MergeRequestId);
                cmd.Parameters.AddWithValue("@ast_pattern", rule.AstPattern);
                cmd.Parameters.AddWithValue("@rule_description", rule.RuleDescription);
                cmd.Parameters.AddWithValue("@bad_pattern", (object?)rule.BadPattern ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@good_pattern", (object?)rule.GoodPattern ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@embedding", EmbeddingUtils.ToBytes(rule.Embedding));
                cmd.Parameters.AddWithValue("@confidence_score", rule.ConfidenceScore);
                cmd.Parameters.AddWithValue("@last_hit_at", rule.LastHitAt.ToString("O"));
                cmd.Parameters.AddWithValue("@created_at", rule.CreatedAt.ToString("O"));
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<List<LearnedRule>> GetAllActiveAsync(CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT id, merge_request_id, ast_pattern, rule_description, bad_pattern, good_pattern,
                    embedding, confidence_score, last_hit_at, created_at
                    FROM learned_rules WHERE confidence_score > 0";
                await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
                List<LearnedRule> rules = new List<LearnedRule>();
                while (await reader.ReadAsync(cancellationToken))
                {
                    rules.Add(new LearnedRule
                    {
                        Id = (string)reader["id"],
                        MergeRequestId = (string)reader["merge_request_id"],
                        AstPattern = (string)reader["ast_pattern"],
                        RuleDescription = (string)reader["rule_description"],
                        BadPattern = reader["bad_pattern"] as string,
                        GoodPattern = reader["good_pattern"] as string,
                        Embedding = EmbeddingUtils.FromBytes((byte[])reader["embedding"]),
                        ConfidenceScore = (int)(long)reader["confidence_score"],
                        LastHitAt = DateTime.Parse((string)reader["last_hit_at"]),
                        CreatedAt = DateTime.Parse((string)reader["created_at"]),
                    });
                }
                return rules;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task UpdateLastHitByMergeRequestIdAsync(string mergeRequestId, string dataTime, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE learned_rules SET last_hit_at = @ts WHERE merge_request_id = @merge_request_id";
                cmd.Parameters.AddWithValue("@ts", dataTime);
                cmd.Parameters.AddWithValue("@merge_request_id", mergeRequestId);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task IncrementConfidenceAsync(string id, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE learned_rules SET confidence_score = MIN(10, confidence_score + 1) WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task DecrementConfidenceAsync(string id, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using System.Data.Common.DbTransaction tx = await conn.BeginTransactionAsync(cancellationToken);
                await using SqliteCommand updateCmd = conn.CreateCommand();
                updateCmd.Transaction = (SqliteTransaction)tx;
                updateCmd.CommandText = "UPDATE learned_rules SET confidence_score = confidence_score - 1 WHERE id = @id";
                updateCmd.Parameters.AddWithValue("@id", id);
                await updateCmd.ExecuteNonQueryAsync(cancellationToken);
                await using SqliteCommand deleteCmd = conn.CreateCommand();
                deleteCmd.Transaction = (SqliteTransaction)tx;
                deleteCmd.CommandText = "DELETE FROM learned_rules WHERE id = @id AND confidence_score <= 0";
                deleteCmd.Parameters.AddWithValue("@id", id);
                await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task PruneStaleAsync(CancellationToken cancellationToken = default)
        {
            int monthsThreshold = Context.Instance.Settings.StaleRuleMonthsThreshold;
            int minConfidence = Context.Instance.Settings.MinConfidenceScoreThreshold;
            string monthsParam = $"-{monthsThreshold} months";

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM learned_rules WHERE last_hit_at < DATE('now', @months) AND confidence_score < @minScore";
                cmd.Parameters.AddWithValue("@months", monthsParam);
                cmd.Parameters.AddWithValue("@minScore", minConfidence);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task InsertPendingReviewAsync(string prKey, string ruleId, string? badPattern, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using var cmd = conn.CreateCommand();

                cmd.CommandText = @"
                INSERT OR IGNORE INTO pending_reviews (pr_key, rule_id, bad_pattern)
                VALUES (@prKey, @ruleId, @badPattern);";

                cmd.Parameters.AddWithValue("@prKey", prKey);
                cmd.Parameters.AddWithValue("@ruleId", ruleId);
                cmd.Parameters.AddWithValue("@badPattern", (object?)badPattern ?? DBNull.Value);

                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }


        public async Task<List<(string RuleId, string? BadPattern)>> GetPendingReviewsAsync(string prKey, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using var cmd = conn.CreateCommand();

                cmd.CommandText = "SELECT rule_id, bad_pattern FROM pending_reviews WHERE pr_key = @prKey;";
                cmd.Parameters.AddWithValue("@prKey", prKey);

                var result = new List<(string RuleId, string? BadPattern)>();
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    string ruleId = reader.GetString(0);
                    string? badPattern = reader.IsDBNull(1) ? null : reader.GetString(1);
                    result.Add((ruleId, badPattern));
                }
                return result;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task DeletePendingReviewsAsync(string prKey, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using var cmd = conn.CreateCommand();

                cmd.CommandText = "DELETE FROM pending_reviews WHERE pr_key = @prKey;";
                cmd.Parameters.AddWithValue("@prKey", prKey);

                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task DecrementConfidenceByChunkIdAsync(string chunkId, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);

                // トランザクションを開始して一貫性を担保
                await using DbTransaction? transaction = await conn.BeginTransactionAsync(cancellationToken);

                try
                {
                    await using (var updateCmd = conn.CreateCommand())
                    {
                        updateCmd.Transaction = (SqliteTransaction)transaction;
                        updateCmd.CommandText = @"
                        UPDATE learned_rules 
                        SET confidence_score = confidence_score - 1 
                        WHERE merge_request_id = (SELECT merge_request_id FROM learned_rules WHERE id = @id);";
                        updateCmd.Parameters.AddWithValue("@id", chunkId);
                        await updateCmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await using (var deleteCmd = conn.CreateCommand())
                    {
                        deleteCmd.Transaction = (SqliteTransaction)transaction;
                        deleteCmd.CommandText = @"
                        DELETE FROM learned_rules 
                        WHERE merge_request_id IN (
                            SELECT merge_request_id FROM learned_rules WHERE confidence_score <= 0
                        );";
                        await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await transaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task IncrementConfidenceByChunkIdAsync(string chunkId, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using var cmd = conn.CreateCommand();

                cmd.CommandText = @"
                UPDATE learned_rules 
                SET confidence_score = MIN(confidence_score + 1, 10) 
                WHERE merge_request_id = (SELECT merge_request_id FROM learned_rules WHERE id = @id);";
                cmd.Parameters.AddWithValue("@id", chunkId);

                await cmd.ExecuteNonQueryAsync(cancellationToken);
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
