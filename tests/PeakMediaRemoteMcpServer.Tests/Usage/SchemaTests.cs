namespace PeakMediaRemoteMcpServer.Tests.Usage;

public class SchemaTests
{
    // TOR-02-Dc8Z0Ge / TOR-01-65SaYiJ (schema half): migration 3 creates usage_log and quota_overrides,
    // each owned by a `subject` column, in the database at Storage:DatabasePath; usage_log has no column
    // that could hold narration text (TOR-05-ryNGNXA).
    [Fact]
    public async Task Migration_3_creates_owned_usage_and_quota_tables_without_text()
    {
        await using var host = await GatewayTestHost.StartAsync();
        var opened = await host.Services.GetRequiredService<SqliteConnectionFactory>().OpenAsync();
        await using var connection = opened.Value;

        Assert.Equal(3L, await ScalarAsync(connection, "SELECT MAX(version) FROM schema_version;"));
        var usageColumns = await ColumnsAsync(connection, "usage_log");
        var quotaColumns = await ColumnsAsync(connection, "quota_overrides");

        Assert.Contains("subject", usageColumns);
        Assert.Contains("subject", quotaColumns);
        Assert.Equal(
            ["id", "subject", "at_utc", "model", "voice", "characters", "cost_nanousd", "cache_key", "cache_hit"],
            usageColumns);
        Assert.DoesNotContain(usageColumns, c => c.Contains("text", StringComparison.OrdinalIgnoreCase) || c.Contains("input", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(["subject", "monthly_nanousd", "set_by_subject", "set_at_utc"], quotaColumns);
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static async Task<List<string>> ColumnsAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM pragma_table_info('{table}');";
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(0));
        }

        return columns;
    }
}
