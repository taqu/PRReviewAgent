using Microsoft.Data.Sqlite;

namespace PRReviewAgent.Services.AutoReview
{
    public sealed class UserLanguagePreferenceRepository : IUserLanguagePreferenceRepository, IDisposable
    {
        private readonly string _connectionString;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public UserLanguagePreferenceRepository(string dbPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
            _connectionString = $"Data Source={dbPath}";
        }

        internal UserLanguagePreferenceRepository(string dbPath, bool isConnectionString)
        {
            _connectionString = isConnectionString ? dbPath : $"Data Source={dbPath}";
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
                    CREATE TABLE IF NOT EXISTS user_language_preferences (
                        project_id  INTEGER NOT NULL,
                        user_id     TEXT NOT NULL,
                        language    TEXT NOT NULL,
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

        public async Task<string?> GetAsync(long projectId, string userId, CancellationToken ct)
        {
            await _semaphore.WaitAsync(ct);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT language FROM user_language_preferences
                    WHERE project_id = @p AND user_id = @u";
                cmd.Parameters.AddWithValue("@p", projectId);
                cmd.Parameters.AddWithValue("@u", userId);
                object? result = await cmd.ExecuteScalarAsync(ct);
                if (result is null or DBNull) return null;
                return (string)result;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task SetAsync(long projectId, string userId, string language, CancellationToken ct)
        {
            await _semaphore.WaitAsync(ct);
            try
            {
                string now = DateTime.UtcNow.ToString("O");
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(ct);
                await using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO user_language_preferences (project_id, user_id, language, created_at, updated_at)
                    VALUES (@p, @u, @lang, @now, @now)
                    ON CONFLICT(project_id, user_id) DO UPDATE SET
                        language = excluded.language,
                        updated_at = excluded.updated_at;";
                cmd.Parameters.AddWithValue("@p", projectId);
                cmd.Parameters.AddWithValue("@u", userId);
                cmd.Parameters.AddWithValue("@lang", language);
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
