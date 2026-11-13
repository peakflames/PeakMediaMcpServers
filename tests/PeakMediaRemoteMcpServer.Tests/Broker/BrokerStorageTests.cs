namespace PeakMediaRemoteMcpServer.Tests.Broker;

public class BrokerStorageTests
{
    private static async Task<(TempDirectory Directory, SqliteConnectionFactory Factory)> FreshDatabaseAsync()
    {
        var directory = new TempDirectory();
        var factory = new SqliteConnectionFactory(directory.File("peakmedia.db"), StorageFaultSwitch.None);
        var migrated = await new SchemaMigrator(factory).MigrateAsync();
        Assert.Equal(1, migrated.Value);
        return (directory, factory);
    }

    [Fact]
    public async Task Migration_1_creates_the_sign_in_tables()
    {
        var (directory, factory) = await FreshDatabaseAsync();
        using var _ = directory;
        await using var connection = (await factory.OpenAsync()).Value;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
        var tables = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        Assert.Superset(
            new HashSet<string> { "app_meta", "users", "oauth_clients", "oauth_transactions", "oauth_codes", "oauth_refresh_tokens" },
            tables.ToHashSet());
    }

    [Fact]
    public async Task Migration_is_idempotent()
    {
        var (directory, factory) = await FreshDatabaseAsync();
        using var _ = directory;

        var again = await new SchemaMigrator(factory).MigrateAsync();

        Assert.Equal(1, again.Value);
    }

    [Fact]
    public async Task Client_round_trips()
    {
        var (directory, factory) = await FreshDatabaseAsync();
        using var _ = directory;
        var store = new ClientStore(factory);
        var client = new RegisteredClient("c1", TokenHashing.Hash("s"), ["http://127.0.0.1/cb", "http://localhost/cb"], "Claude Code", DateTimeOffset.UtcNow);

        await store.InsertAsync(client);
        var read = (await store.TryGetAsync("c1")).Value;

        Assert.NotNull(read);
        Assert.Equal(client.RedirectUris, read.RedirectUris);
        Assert.Equal("Claude Code", read.ClientName);
        Assert.True(TokenHashing.Verify("s", read.ClientSecretHash));
        Assert.Null((await store.TryGetAsync("nope")).Value);
    }

    [Fact]
    public async Task Code_is_single_use_and_hashed()
    {
        var (directory, factory) = await FreshDatabaseAsync();
        using var _ = directory;
        await new UserStore(factory).UpsertSignInAsync("00u-pat", "pat@example.com", false, DateTimeOffset.UtcNow);
        var codes = new CodeStore(factory);
        await codes.InsertAsync("the-code", new AuthorizationCodeRecord("00u-pat", "c1", "http://127.0.0.1/cb", "ch"), DateTimeOffset.UtcNow.AddMinutes(5));

        var first = (await codes.TryConsumeAsync("the-code")).Value;
        var second = (await codes.TryConsumeAsync("the-code")).Value;

        Assert.Equal("00u-pat", first!.Subject);
        Assert.Null(second);
        await using var connection = (await factory.OpenAsync()).Value;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM oauth_codes WHERE code_hash = 'the-code';";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Expired_rows_are_not_returned_and_are_swept()
    {
        var (directory, factory) = await FreshDatabaseAsync();
        using var _ = directory;
        await new UserStore(factory).UpsertSignInAsync("00u-pat", "pat@example.com", false, DateTimeOffset.UtcNow);
        var codes = new CodeStore(factory);
        var refreshTokens = new RefreshTokenStore(factory);
        var transactions = new TransactionStore(factory);
        var past = DateTimeOffset.UtcNow.AddMinutes(-1);
        await codes.InsertAsync("old-code", new AuthorizationCodeRecord("00u-pat", "c1", "http://127.0.0.1/cb", "ch"), past);
        await refreshTokens.InsertAsync("old-refresh", new RefreshTokenRecord("00u-pat", "c1"), past);
        await transactions.InsertAsync(new OAuthTransaction
        {
            TxnId = "t1",
            ClientId = "c1",
            ClientRedirectUri = "http://127.0.0.1/cb",
            ClientState = "s",
            ClientCodeChallenge = "ch",
            UpstreamCodeVerifier = "v",
            Nonce = "n",
            ConsentTokenHash = "h",
            CreatedUtc = past.AddMinutes(-15),
            ExpiresUtc = past,
        });

        Assert.Null((await transactions.TryGetAsync("t1")).Value);
        Assert.Null((await refreshTokens.TryConsumeAsync("old-refresh")).Value);
        var janitor = new BrokerJanitor(transactions, codes, refreshTokens, new Microsoft.Extensions.Logging.Abstractions.NullLogger<BrokerJanitor>());
        Assert.Equal(3, await janitor.SweepOnceAsync());
    }

    [Fact]
    public async Task User_upsert_updates_role_and_last_seen()
    {
        var (directory, factory) = await FreshDatabaseAsync();
        using var _ = directory;
        var users = new UserStore(factory);
        var first = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

        await users.UpsertSignInAsync("00u-pat", "pat@example.com", false, first);
        await users.UpsertSignInAsync("00u-pat", "pat@example.com", true, first.AddDays(1));
        var read = (await users.TryGetAsync("00u-pat")).Value!;

        Assert.True(read.IsAdmin);
        Assert.Equal(first, read.FirstSeenUtc);
        Assert.Equal(first.AddDays(1), read.LastSeenUtc);
    }

    [Fact]
    public async Task Signing_key_persists_across_providers()
    {
        var (directory, factory) = await FreshDatabaseAsync();
        using var _ = directory;

        var first = new SigningKeyProvider(new AppMetaStore(factory)).Key;
        var second = new SigningKeyProvider(new AppMetaStore(factory)).Key;

        Assert.Equal(first.KeyId, second.KeyId);
        Assert.Equal(first.Rsa.ExportParameters(false).Modulus, second.Rsa.ExportParameters(false).Modulus);
    }
}
