## Refactoring Instruction for Coding Agent## Objective
Refactor the PruneStaleAsync method in PRReviewAgent/Services/AutoImprove/RulePruningWorkers.cs to remove hardcoded values. The dynamic values for the time threshold (months) and the confidence score threshold must be retrieved from Context.Instance.Settings.
## Requirements

   1. Retrieve Settings Dynamically:
   * Replace the hardcoded '-3 months' with a value derived from Context.Instance.Settings. (e.g., Context.Instance.Settings.StaleRuleMonthsThreshold)
      * Replace the hardcoded 7 with a value derived from Context.Instance.Settings. (e.g., Context.Instance.Settings.MinConfidenceScoreThreshold)
      * Note: Please use the exact property names available in your Context.Instance.Settings class, or create them if they do not exist yet.
   2. Use Parameterized Query:
   * Modify the SQLite command to use parameters (@months and @min_score) instead of string interpolation or hardcoding, preventing potential query issues and maintaining database best practices.
      * For the date calculation in SQLite, format the parameter to construct the relative date string dynamically, such as DATE('now', @months_string) or compute the boundary date in C# and pass it as a parameter string.
   
------------------------------
## Target Code to Modify

public async Task PruneStaleAsync(CancellationToken cancellationToken = default)
{
    await _semaphore.WaitAsync(cancellationToken);
    try
    {
        await using SqliteConnection conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM learned_rules WHERE last_hit_at < DATE('now', '-3 months') AND confidence_score < 7";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
    finally
    {
        _semaphore.Release();
    }
}

## Expected Output (Example Structure)

public async Task PruneStaleAsync(CancellationToken cancellationToken = default)
{
    await _semaphore.WaitAsync(cancellationToken);
    try
    {
        // 1. Retrieve configurations from Context.Instance.Settings
        int monthsThreshold = Context.Instance.Settings.StaleRuleMonthsThreshold; // Adjust property name if needed
        int minConfidence = Context.Instance.Settings.MinConfidenceScoreThreshold; // Adjust property name if needed

        // 2. Prepare dynamic parameter for SQLite DATE function (e.g., "-3 months")
        string monthsParam = $"-{monthsThreshold} months";

        await using SqliteConnection conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using SqliteCommand cmd = conn.CreateCommand();
        
        // 3. Use parameterized SQL query
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

