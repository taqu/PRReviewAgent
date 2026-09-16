using Microsoft.Data.Sqlite;

namespace PRReviewAgent.Services.AutoImprove
{
    public sealed class ProjectRepository : IDisposable
    {
        public const string LegacyExternalProjectId = "__legacy__";
        public const string LegacyName = "Legacy Project";

        private readonly string _connectionString;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public ProjectRepository(string dbPath)
        {
            _connectionString = $"Data Source={dbPath}";
        }

        internal ProjectRepository(string dbPath, bool isConnectionString)
        {
            _connectionString = isConnectionString ? dbPath : $"Data Source={dbPath}";
        }

        public async Task<Project> GetOrCreateAsync(
            string externalProjectId,
            string name,
            string? repositoryUrl,
            CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await using SqliteConnection conn = new SqliteConnection(_connectionString);
                await conn.OpenAsync(cancellationToken);

                Project? existing = await FindAsync(conn, externalProjectId, cancellationToken);
                if (existing != null) return existing;

                string now = DateTime.UtcNow.ToString("O");
                await using SqliteCommand insertCmd = conn.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO projects (external_project_id, name, display_name, repository_url, is_active, created_at, updated_at)
                    VALUES (@externalProjectId, @name, NULL, @repositoryUrl, 1, @now, @now);
                    SELECT last_insert_rowid();";
                insertCmd.Parameters.AddWithValue("@externalProjectId", externalProjectId);
                insertCmd.Parameters.AddWithValue("@name", name);
                insertCmd.Parameters.AddWithValue("@repositoryUrl", (object?)repositoryUrl ?? DBNull.Value);
                insertCmd.Parameters.AddWithValue("@now", now);
                long newId = (long)(await insertCmd.ExecuteScalarAsync(cancellationToken))!;

                return new Project
                {
                    Id = newId,
                    ExternalProjectId = externalProjectId,
                    Name = name,
                    RepositoryUrl = repositoryUrl,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                };
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private static async Task<Project?> FindAsync(SqliteConnection conn, string externalProjectId, CancellationToken cancellationToken)
        {
            await using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT id, external_project_id, name, display_name, repository_url, is_active, created_at, updated_at
                FROM projects WHERE external_project_id = @externalProjectId";
            cmd.Parameters.AddWithValue("@externalProjectId", externalProjectId);
            await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
                return ReadProject(reader);
            return null;
        }

        private static Project ReadProject(SqliteDataReader reader) =>
            new Project
            {
                Id = (long)reader["id"],
                ExternalProjectId = (string)reader["external_project_id"],
                Name = (string)reader["name"],
                DisplayName = reader["display_name"] as string,
                RepositoryUrl = reader["repository_url"] as string,
                IsActive = (long)reader["is_active"] == 1,
                CreatedAt = DateTime.Parse((string)reader["created_at"]),
                UpdatedAt = DateTime.Parse((string)reader["updated_at"]),
            };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _semaphore.Dispose();
        }
    }
}
