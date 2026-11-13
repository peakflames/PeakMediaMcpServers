namespace PeakMediaRemoteMcpServer.Tests;

public class StorageBootstrapTests
{
    [Fact]
    public async Task Fresh_path_creates_database_with_schema_version_table()
    {
        using var directory = new TempDirectory();
        var path = Path.Combine(directory.Path, "nested", "peakmedia.db");
        var resolved = DatabasePathResolver.Resolve(path, directory.Path, new ListLogger());
        var factory = new SqliteConnectionFactory(resolved, StorageFaultSwitch.None);

        var migrated = await new SchemaMigrator(factory).MigrateAsync();

        Assert.True(migrated.IsSuccess);
        Assert.True(File.Exists(path));
        var opened = await factory.OpenAsync();
        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'schema_version';";
        Assert.Equal("schema_version", await command.ExecuteScalarAsync());
    }

    [Fact]
    public void Relative_path_resolves_against_content_root()
    {
        using var directory = new TempDirectory();

        var resolved = DatabasePathResolver.Resolve("data/peakmedia.db", directory.Path, new ListLogger());

        Assert.Equal(Path.Combine(directory.Path, "data", "peakmedia.db"), resolved);
    }

    [Fact]
    public void Unwritable_path_falls_back_to_temp_with_warning()
    {
        var logger = new ListLogger();

        var resolved = DatabasePathResolver.Resolve("/dev/null/cannot/peakmedia.db", "/", logger);

        Assert.Equal(DatabasePathResolver.FallbackPath, resolved);
        Assert.Contains(logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("do not survive a restart", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Fault_switch_fail_is_honored_outside_production()
    {
        var logger = new ListLogger();
        var faultSwitch = StorageFaultSwitch.FromEnvironment("fail", isProduction: false, logger);
        using var directory = new TempDirectory();

        var opened = await new SqliteConnectionFactory(directory.File("peakmedia.db"), faultSwitch).OpenAsync();

        Assert.True(opened.IsFailed);
    }

    [Fact]
    public void Fault_switch_delay_is_parsed()
    {
        var faultSwitch = StorageFaultSwitch.FromEnvironment("delay:250", isProduction: false, new ListLogger());

        Assert.Equal(TimeSpan.FromMilliseconds(250), faultSwitch.Delay);
        Assert.False(faultSwitch.Fail);
    }

    [Fact]
    public void Fault_switch_is_ignored_in_production_with_warning()
    {
        var logger = new ListLogger();

        var faultSwitch = StorageFaultSwitch.FromEnvironment("fail", isProduction: true, logger);

        Assert.False(faultSwitch.IsActive);
        Assert.Contains(logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public async Task Gateway_host_creates_its_database_at_startup()
    {
        using var directory = new TempDirectory();
        var path = directory.File("gw.db");

        await using var host = await GatewayTestHost.StartAsync(new Dictionary<string, string?> { ["Storage:DatabasePath"] = path });

        Assert.True(File.Exists(path));
    }
}
