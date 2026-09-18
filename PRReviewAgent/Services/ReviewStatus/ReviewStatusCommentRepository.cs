using Microsoft.Data.Sqlite;

namespace PRReviewAgent.Services.ReviewStatus
{
    public sealed class ReviewStatusCommentRepository : IReviewStatusCommentRepository, IDisposable
    {
        private readonly string _connectionString;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public ReviewStatusCommentRepository(string dbPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
            _connectionString = $"Data Source={dbPath}";
        }

        internal ReviewStatusCommentRepository(string dbPath, bool isConnectionString)
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

                // Ensure projects table exists (FK target — also created by RuleRepository when auto_improve is on)
                await using (SqliteCommand cmd = conn.CreateCommand())
                {
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

                await using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS review_status_comments (
                            project_id          INTEGER NOT NULL,
                            merge_request_id    TEXT NOT NULL,
                            comment_id          TEXT NOT NULL,
                            created_at          TEXT NOT NULL,
                            updated_at          TEXT NOT NULL,
                            PRIMARY KEY(project_id, merge_request_id),
                            FOREIGN KEY(project_id) REFERENCES projects(id)
                        );";
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                await using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS auto_review_user_settings (
                            project_id  INTEGER NOT NULL,
                            user_id     TEXT NOT NULL,
                            enabled     INTEGER NOT NULL,
                            created_at  TEXT NOT NULL,
                            updated_at  TEXT NOT NULL,
                            PRIMARY KEY(project_id, user_id),
                            FOREIGN KEY(project_id) REFERENCES projects(id)
                        );";
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<string?> GetCommentIdAsync(long projectId, string mergeRequestId, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT comment_id FROM review_status_comments
                    WHERE project_id = @projectId AND merge_request_id = @mergeRequestId";
                cmd.Parameters.AddWithValue("@projectId", projectId);
                cmd.Parameters.AddWithValue("@mergeRequestId", mergeRequestId);
                object? result = await cmd.ExecuteScalarAsync(cancellationToken);
                return result as string;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task UpsertAsync(long projectId, string mergeRequestId, string commentId, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                string now = DateTime.UtcNow.ToString("O");
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO review_status_comments (project_id, merge_request_id, comment_id, created_at, updated_at)
                    VALUES (@projectId, @mergeRequestId, @commentId, @now, @now)
                    ON CONFLICT(project_id, merge_request_id) DO UPDATE SET
                        comment_id = excluded.comment_id,
                        updated_at = excluded.updated_at;";
                cmd.Parameters.AddWithValue("@projectId", projectId);
                cmd.Parameters.AddWithValue("@mergeRequestId", mergeRequestId);
                cmd.Parameters.AddWithValue("@commentId", commentId);
                cmd.Parameters.AddWithValue("@now", now);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task DeleteAsync(long projectId, string mergeRequestId, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"DELETE FROM review_status_comments
                    WHERE project_id = @projectId AND merge_request_id = @mergeRequestId";
                cmd.Parameters.AddWithValue("@projectId", projectId);
                cmd.Parameters.AddWithValue("@mergeRequestId", mergeRequestId);
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
