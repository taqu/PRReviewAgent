using Microsoft.Data.Sqlite;

namespace PRReviewAgent.Services.AutoReview
{
    public sealed class AutoReviewUserSettingRepository : IAutoReviewUserSettingRepository, IDisposable
    {
        private readonly string _connectionString;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public AutoReviewUserSettingRepository(string dbPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
            _connectionString = $"Data Source={dbPath}";
        }

        public async Task InitializeAsync()
        {
            await _semaphore.WaitAsync();
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync();
                await using SqliteCommand cmd = conn.CreateCommand();
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
                await cmd.ExecuteNonQueryAsync();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<bool?> GetAsync(long projectId, string userId, CancellationToken ct)
        {
            await _semaphore.WaitAsync(ct);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT enabled FROM auto_review_user_settings
                    WHERE project_id = @projectId AND user_id = @userId";
                cmd.Parameters.AddWithValue("@projectId", projectId);
                cmd.Parameters.AddWithValue("@userId", userId);
                object? result = await cmd.ExecuteScalarAsync(ct);
                if (result is null or DBNull) return null;
                return Convert.ToInt64(result) != 0;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task UpsertAsync(long projectId, string userId, bool enabled, CancellationToken ct)
        {
            await _semaphore.WaitAsync(ct);
            try
            {
                string now = DateTime.UtcNow.ToString("O");
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO auto_review_user_settings (project_id, user_id, enabled, created_at, updated_at)
                    VALUES (@projectId, @userId, @enabled, @now, @now)
                    ON CONFLICT(project_id, user_id) DO UPDATE SET
                        enabled = excluded.enabled,
                        updated_at = excluded.updated_at;";
                cmd.Parameters.AddWithValue("@projectId", projectId);
                cmd.Parameters.AddWithValue("@userId", userId);
                cmd.Parameters.AddWithValue("@enabled", enabled ? 1 : 0);
                cmd.Parameters.AddWithValue("@now", now);
                await cmd.ExecuteNonQueryAsync(ct);
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
