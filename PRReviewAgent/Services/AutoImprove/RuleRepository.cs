using Microsoft.Data.Sqlite;
using System.Data.Common;
using TreeSitter;

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

        internal RuleRepository(string dbPath, bool isConnectionString)
        {
            _connectionString = isConnectionString ? dbPath : $"Data Source={dbPath}";
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);

                // Create projects table
                {
                    await using SqliteCommand cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS projects (
                        id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                        external_project_id TEXT NOT NULL,
                        name                TEXT NOT NULL,
                        display_name        TEXT,
                        repository_url      TEXT,
                        is_active           INTEGER NOT NULL DEFAULT 1,
                        created_at          TEXT NOT NULL,
                        updated_at          TEXT NOT NULL
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS ux_projects_external_project_id
                        ON projects(external_project_id);";
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                // Ensure legacy project exists for pre-migration data
                long legacyProjectId;
                {
                    await using SqliteCommand cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT id FROM projects WHERE external_project_id = @id";
                    cmd.Parameters.AddWithValue("@id", ProjectRepository.LegacyExternalProjectId);
                    object? result = await cmd.ExecuteScalarAsync(cancellationToken);
                    if (result != null)
                    {
                        legacyProjectId = (long)result;
                    }
                    else
                    {
                        string now = DateTime.UtcNow.ToString("O");
                        await using SqliteCommand insertCmd = conn.CreateCommand();
                        insertCmd.CommandText = @"
                            INSERT INTO projects (external_project_id, name, display_name, repository_url, is_active, created_at, updated_at)
                            VALUES (@externalId, @name, NULL, NULL, 1, @now, @now);
                            SELECT last_insert_rowid();";
                        insertCmd.Parameters.AddWithValue("@externalId", ProjectRepository.LegacyExternalProjectId);
                        insertCmd.Parameters.AddWithValue("@name", ProjectRepository.LegacyName);
                        insertCmd.Parameters.AddWithValue("@now", now);
                        legacyProjectId = (long)(await insertCmd.ExecuteScalarAsync(cancellationToken))!;
                    }
                }

                // Create learned_rules table (without project_id index — added after migration)
                {
                    await using SqliteCommand cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS learned_rules (
                        id TEXT PRIMARY KEY,
                        project_id INTEGER NOT NULL,
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

                // Migrate existing installations: add project_id column if missing
                {
                    await using SqliteCommand pragmaCmd = conn.CreateCommand();
                    pragmaCmd.CommandText = "PRAGMA table_info(learned_rules)";
                    await using SqliteDataReader reader = await pragmaCmd.ExecuteReaderAsync(cancellationToken);
                    bool hasProjectId = false;
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        if (string.Equals((string)reader["name"], "project_id", StringComparison.OrdinalIgnoreCase))
                        {
                            hasProjectId = true;
                            break;
                        }
                    }

                    if (!hasProjectId)
                    {
                        await using SqliteCommand alterCmd = conn.CreateCommand();
                        alterCmd.CommandText = "ALTER TABLE learned_rules ADD COLUMN project_id INTEGER";
                        await alterCmd.ExecuteNonQueryAsync(cancellationToken);

                        await using SqliteCommand updateCmd = conn.CreateCommand();
                        updateCmd.CommandText = "UPDATE learned_rules SET project_id = @legacyId WHERE project_id IS NULL";
                        updateCmd.Parameters.AddWithValue("@legacyId", legacyProjectId);
                        await updateCmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    // Create project_id index after migration (safe whether column pre-existed or was just added)
                    await using SqliteCommand indexCmd = conn.CreateCommand();
                    indexCmd.CommandText = "CREATE INDEX IF NOT EXISTS ix_learned_rules_project_id ON learned_rules(project_id)";
                    await indexCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                // Create pending_reviews table
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

                // Create rule_learning_events table
                {
                    await using SqliteCommand cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS rule_learning_events (
                        id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                        project_id          INTEGER NOT NULL,
                        rule_id             TEXT NOT NULL,
                        merge_request_id    TEXT NULL,
                        event_type          TEXT NOT NULL,
                        confidence_before   REAL NULL,
                        confidence_after    REAL NULL,
                        created_at          TEXT NOT NULL,
                        FOREIGN KEY(project_id) REFERENCES projects(id)
                    );
                    CREATE INDEX IF NOT EXISTS ix_rule_learning_events_project_rule_created
                        ON rule_learning_events(project_id, rule_id, created_at);
                    CREATE INDEX IF NOT EXISTS ix_rule_learning_events_project_created
                        ON rule_learning_events(project_id, created_at);";
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                // Create review_turns table
                {
                    await using SqliteCommand cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS review_turns (
                        id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                        review_execution_id INTEGER NOT NULL,
                        turn_type           TEXT NOT NULL,
                        model               TEXT NOT NULL,
                        input_tokens        INTEGER NULL,
                        output_tokens       INTEGER NULL,
                        duration_ms         INTEGER NULL,
                        finding_count       INTEGER NULL,
                        success             INTEGER NOT NULL DEFAULT 0,
                        error_type          TEXT NULL,
                        started_at          TEXT NOT NULL,
                        completed_at        TEXT NULL,
                        FOREIGN KEY(review_execution_id) REFERENCES review_executions(id)
                    );
                    CREATE INDEX IF NOT EXISTS ix_review_turns_review_execution_id ON review_turns(review_execution_id);";
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                // Create review_executions table
                {
                    await using SqliteCommand cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS review_executions (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        project_id INTEGER NOT NULL,
                        merge_request_id TEXT NOT NULL,
                        started_at TEXT NOT NULL,
                        completed_at TEXT NULL,
                        duration_ms INTEGER NULL,
                        candidate_finding_count INTEGER NULL,
                        selected_finding_count INTEGER NULL,
                        critical_count INTEGER NULL,
                        major_count INTEGER NULL,
                        minor_count INTEGER NULL,
                        status TEXT NOT NULL,
                        error_type TEXT NULL,
                        FOREIGN KEY(project_id) REFERENCES projects(id)
                    );
                    CREATE INDEX IF NOT EXISTS ix_review_executions_project_id ON review_executions(project_id);";
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                // Create rule_search_executions table
                {
                    await using SqliteCommand cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS rule_search_executions (
                        id                      INTEGER PRIMARY KEY AUTOINCREMENT,
                        review_execution_id     INTEGER NOT NULL,
                        project_id              INTEGER NOT NULL,
                        total_rule_count        INTEGER NOT NULL,
                        candidate_rule_count    INTEGER NULL,
                        selected_rule_count     INTEGER NULL,
                        embedding_duration_ms   INTEGER NULL,
                        search_duration_ms      INTEGER NULL,
                        total_duration_ms       INTEGER NULL,
                        success                 INTEGER NOT NULL,
                        error_type              TEXT NULL,
                        started_at              TEXT NOT NULL,
                        completed_at            TEXT NULL,
                        FOREIGN KEY(review_execution_id) REFERENCES review_executions(id),
                        FOREIGN KEY(project_id) REFERENCES projects(id)
                    );
                    CREATE INDEX IF NOT EXISTS ix_rule_search_executions_project_created ON rule_search_executions(project_id, started_at);
                    CREATE INDEX IF NOT EXISTS ix_rule_search_executions_review_execution ON rule_search_executions(review_execution_id);";
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                // Create review_rule_usage table
                {
                    await using SqliteCommand cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS review_rule_usage (
                        id                      INTEGER PRIMARY KEY AUTOINCREMENT,
                        review_execution_id     INTEGER NOT NULL,
                        project_id              INTEGER NOT NULL,
                        rule_id                 TEXT NOT NULL,
                        similarity_score        REAL NULL,
                        used_in_prompt          INTEGER NOT NULL,
                        produced_candidate      INTEGER NOT NULL,
                        produced_final_finding  INTEGER NOT NULL,
                        created_at              TEXT NOT NULL,
                        FOREIGN KEY(review_execution_id) REFERENCES review_executions(id),
                        FOREIGN KEY(project_id) REFERENCES projects(id)
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS ux_review_rule_usage_review_project_rule
                        ON review_rule_usage(review_execution_id, project_id, rule_id);
                    CREATE INDEX IF NOT EXISTS ix_review_rule_usage_review
                        ON review_rule_usage(review_execution_id);
                    CREATE INDEX IF NOT EXISTS ix_review_rule_usage_project_rule
                        ON review_rule_usage(project_id, rule_id);";
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task InsertAsync(LearnedRule rule, ILogger? logger = null, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteTransaction tx = conn.BeginTransaction();
                try
                {
                    await using SqliteCommand cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"INSERT INTO learned_rules
                        (id, project_id, merge_request_id, ast_pattern, rule_description, bad_pattern, good_pattern, embedding, confidence_score, last_hit_at, created_at)
                        VALUES (@id, @project_id, @merge_request_id, @ast_pattern, @rule_description, @bad_pattern, @good_pattern, @embedding, @confidence_score, @last_hit_at, @created_at)";
                    cmd.Parameters.AddWithValue("@id", rule.Id);
                    cmd.Parameters.AddWithValue("@project_id", rule.ProjectId);
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

                    await InsertEventAsync(conn, tx,
                        rule.ProjectId, rule.Id, rule.MergeRequestId,
                        RuleLearningEventType.Created,
                        confidenceBefore: null, confidenceAfter: rule.ConfidenceScore,
                        cancellationToken);

                    await tx.CommitAsync(cancellationToken);
                }
                catch (SqliteException ex)
                {
                    await tx.RollbackAsync(cancellationToken);
                    logger?.LogError(ex.Message);
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

        public async Task<List<LearnedRule>> GetAllActiveAsync(long projectId, ILogger? logger = null, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT id, project_id, merge_request_id, ast_pattern, rule_description, bad_pattern, good_pattern,
                    embedding, confidence_score, last_hit_at, created_at
                    FROM learned_rules WHERE project_id = @projectId AND confidence_score > 0";
                cmd.Parameters.AddWithValue("@projectId", projectId);
                await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
                List<LearnedRule> rules = new List<LearnedRule>();
                while (await reader.ReadAsync(cancellationToken))
                {
                    rules.Add(new LearnedRule
                    {
                        Id = (string)reader["id"],
                        ProjectId = (long)reader["project_id"],
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
            catch (Exception ex)
            {
                logger?.LogError(ex.Message);
                return new List<LearnedRule>();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task UpdateLastHitByMergeRequestIdAsync(long projectId, string mergeRequestId, string dataTime, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE learned_rules SET last_hit_at = @ts WHERE project_id = @projectId AND merge_request_id = @merge_request_id";
                cmd.Parameters.AddWithValue("@projectId", projectId);
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

        public async Task<int> PruneStaleAsync(CancellationToken cancellationToken = default)
        {
            int monthsThreshold = Context.Instance.Settings.StaleRuleMonthsThreshold;
            int minConfidence = Context.Instance.Settings.MinConfidenceScoreThreshold;
            string monthsParam = $"-{monthsThreshold} months";

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);

                // Select eligible rules before deletion so we can record Expired events
                List<(string id, long projectId, int confidence)> eligible = new();
                await using (SqliteCommand selectCmd = conn.CreateCommand())
                {
                    selectCmd.CommandText = @"
                        SELECT id, project_id, confidence_score
                        FROM learned_rules
                        WHERE last_hit_at < DATE('now', @months) AND confidence_score < @minScore";
                    selectCmd.Parameters.AddWithValue("@months", monthsParam);
                    selectCmd.Parameters.AddWithValue("@minScore", minConfidence);
                    await using SqliteDataReader reader = await selectCmd.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                        eligible.Add((reader.GetString(0), reader.GetInt64(1), (int)reader.GetInt64(2)));
                }

                if (eligible.Count == 0) return 0;

                await using SqliteTransaction tx = conn.BeginTransaction();
                try
                {
                    // Insert Expired event for each rule before deleting it
                    foreach ((string ruleId, long projectId, int confidence) in eligible)
                    {
                        await InsertEventAsync(conn, tx,
                            projectId, ruleId, mergeRequestId: null,
                            RuleLearningEventType.Expired,
                            confidenceBefore: confidence, confidenceAfter: null,
                            cancellationToken);
                    }

                    // Delete all eligible rules by id
                    await using (SqliteCommand deleteCmd = conn.CreateCommand())
                    {
                        deleteCmd.Transaction = tx;
                        deleteCmd.CommandText = @"
                            DELETE FROM learned_rules
                            WHERE last_hit_at < DATE('now', @months) AND confidence_score < @minScore";
                        deleteCmd.Parameters.AddWithValue("@months", monthsParam);
                        deleteCmd.Parameters.AddWithValue("@minScore", minConfidence);
                        await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await tx.CommitAsync(cancellationToken);
                    return eligible.Count;
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

        public async Task DecrementConfidenceByChunkIdAsync(long projectId, string chunkId, string? triggeringMrKey = null, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);

                await using SqliteTransaction transaction = conn.BeginTransaction();

                try
                {
                    // Resolve the shared merge_request_id for the target chunk
                    string? sharedMrId = null;
                    await using (var resolveCmd = conn.CreateCommand())
                    {
                        resolveCmd.Transaction = transaction;
                        resolveCmd.CommandText = "SELECT merge_request_id FROM learned_rules WHERE id = @id AND project_id = @projectId";
                        resolveCmd.Parameters.AddWithValue("@id", chunkId);
                        resolveCmd.Parameters.AddWithValue("@projectId", projectId);
                        object? r = await resolveCmd.ExecuteScalarAsync(cancellationToken);
                        sharedMrId = r as string;
                    }

                    if (sharedMrId == null)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return;
                    }

                    // Capture before values for all affected rules
                    List<(string id, int before)> beforeValues = new();
                    await using (var selectCmd = conn.CreateCommand())
                    {
                        selectCmd.Transaction = transaction;
                        selectCmd.CommandText = "SELECT id, confidence_score FROM learned_rules WHERE project_id = @projectId AND merge_request_id = @mrId";
                        selectCmd.Parameters.AddWithValue("@projectId", projectId);
                        selectCmd.Parameters.AddWithValue("@mrId", sharedMrId);
                        await using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken);
                        while (await reader.ReadAsync(cancellationToken))
                            beforeValues.Add((reader.GetString(0), (int)reader.GetInt64(1)));
                    }

                    // Decrement confidence
                    await using (var updateCmd = conn.CreateCommand())
                    {
                        updateCmd.Transaction = transaction;
                        updateCmd.CommandText = @"
                        UPDATE learned_rules
                        SET confidence_score = confidence_score - 1
                        WHERE project_id = @projectId AND merge_request_id = @mrId";
                        updateCmd.Parameters.AddWithValue("@projectId", projectId);
                        updateCmd.Parameters.AddWithValue("@mrId", sharedMrId);
                        await updateCmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    // Delete rules that reached zero or below
                    await using (var deleteCmd = conn.CreateCommand())
                    {
                        deleteCmd.Transaction = transaction;
                        deleteCmd.CommandText = @"
                        DELETE FROM learned_rules
                        WHERE project_id = @projectId AND merge_request_id = @mrId AND confidence_score <= 0";
                        deleteCmd.Parameters.AddWithValue("@projectId", projectId);
                        deleteCmd.Parameters.AddWithValue("@mrId", sharedMrId);
                        await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    // Record ConfidenceDecreased events for rules where value actually changed
                    string now = DateTimeOffset.UtcNow.ToString("O");
                    foreach ((string ruleId, int before) in beforeValues)
                    {
                        int after = before - 1;
                        if (after != before)
                        {
                            await InsertEventAsync(conn, transaction,
                                projectId, ruleId, triggeringMrKey,
                                RuleLearningEventType.ConfidenceDecreased,
                                confidenceBefore: before, confidenceAfter: after,
                                cancellationToken);
                        }
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

        public async Task IncrementConfidenceByChunkIdAsync(long projectId, string chunkId, string? triggeringMrKey = null, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using var conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);

                await using SqliteTransaction transaction = conn.BeginTransaction();

                try
                {
                    // Resolve the shared merge_request_id for the target chunk
                    string? sharedMrId = null;
                    await using (var resolveCmd = conn.CreateCommand())
                    {
                        resolveCmd.Transaction = transaction;
                        resolveCmd.CommandText = "SELECT merge_request_id FROM learned_rules WHERE id = @id AND project_id = @projectId";
                        resolveCmd.Parameters.AddWithValue("@id", chunkId);
                        resolveCmd.Parameters.AddWithValue("@projectId", projectId);
                        object? r = await resolveCmd.ExecuteScalarAsync(cancellationToken);
                        sharedMrId = r as string;
                    }

                    if (sharedMrId == null)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return;
                    }

                    // Capture before values for all affected rules
                    List<(string id, int before)> beforeValues = new();
                    await using (var selectCmd = conn.CreateCommand())
                    {
                        selectCmd.Transaction = transaction;
                        selectCmd.CommandText = "SELECT id, confidence_score FROM learned_rules WHERE project_id = @projectId AND merge_request_id = @mrId";
                        selectCmd.Parameters.AddWithValue("@projectId", projectId);
                        selectCmd.Parameters.AddWithValue("@mrId", sharedMrId);
                        await using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken);
                        while (await reader.ReadAsync(cancellationToken))
                            beforeValues.Add((reader.GetString(0), (int)reader.GetInt64(1)));
                    }

                    // Increment confidence (capped at 10)
                    await using (var updateCmd = conn.CreateCommand())
                    {
                        updateCmd.Transaction = transaction;
                        updateCmd.CommandText = @"
                        UPDATE learned_rules
                        SET confidence_score = MIN(confidence_score + 1, 10)
                        WHERE project_id = @projectId AND merge_request_id = @mrId";
                        updateCmd.Parameters.AddWithValue("@projectId", projectId);
                        updateCmd.Parameters.AddWithValue("@mrId", sharedMrId);
                        await updateCmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    // Record ConfidenceIncreased events for rules where value actually changed
                    foreach ((string ruleId, int before) in beforeValues)
                    {
                        int after = Math.Min(before + 1, 10);
                        if (after != before)
                        {
                            await InsertEventAsync(conn, transaction,
                                projectId, ruleId, triggeringMrKey,
                                RuleLearningEventType.ConfidenceIncreased,
                                confidenceBefore: before, confidenceAfter: after,
                                cancellationToken);
                        }
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

        private static async Task InsertEventAsync(
            SqliteConnection conn,
            SqliteTransaction tx,
            long projectId,
            string ruleId,
            string? mergeRequestId,
            RuleLearningEventType eventType,
            double? confidenceBefore,
            double? confidenceAfter,
            CancellationToken cancellationToken)
        {
            await using SqliteCommand cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO rule_learning_events
                    (project_id, rule_id, merge_request_id, event_type, confidence_before, confidence_after, created_at)
                VALUES (@projectId, @ruleId, @mergeRequestId, @eventType, @confidenceBefore, @confidenceAfter, @createdAt)";
            cmd.Parameters.AddWithValue("@projectId", projectId);
            cmd.Parameters.AddWithValue("@ruleId", ruleId);
            cmd.Parameters.AddWithValue("@mergeRequestId", (object?)mergeRequestId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@eventType", eventType.ToString());
            cmd.Parameters.AddWithValue("@confidenceBefore", confidenceBefore.HasValue ? (object)confidenceBefore.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@confidenceAfter", confidenceAfter.HasValue ? (object)confidenceAfter.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("@createdAt", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _semaphore.Dispose();
        }
    }
}
