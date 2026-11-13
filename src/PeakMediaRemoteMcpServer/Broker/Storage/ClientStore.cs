namespace PeakMediaRemoteMcpServer.Broker.Storage;

/// <summary>An MCP client registered through dynamic client registration.</summary>
public sealed record RegisteredClient(string ClientId, string ClientSecretHash, IReadOnlyList<string> RedirectUris, string? ClientName, DateTimeOffset CreatedUtc);

/// <summary>The <c>oauth_clients</c> table.</summary>
public sealed class ClientStore(SqliteConnectionFactory connections)
{
    public async Task<Result> InsertAsync(RegisteredClient client, CancellationToken cancellationToken = default)
    {
        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return opened.ToResult();
        }

        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO oauth_clients (client_id, client_secret_hash, redirect_uris_json, client_name, created_utc)
            VALUES ($client_id, $secret_hash, $redirect_uris, $client_name, $created);
            """;
        command.Parameters.AddWithValue("$client_id", client.ClientId);
        command.Parameters.AddWithValue("$secret_hash", client.ClientSecretHash);
        command.Parameters.AddWithValue("$redirect_uris", JsonSerializer.Serialize(client.RedirectUris));
        command.Parameters.AddWithValue("$client_name", (object?)client.ClientName ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", UnixTime.Of(client.CreatedUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<RegisteredClient?>> TryGetAsync(string clientId, CancellationToken cancellationToken = default)
    {
        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return opened.ToResult<RegisteredClient?>();
        }

        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT client_id, client_secret_hash, redirect_uris_json, client_name, created_utc FROM oauth_clients WHERE client_id = $client_id;";
        command.Parameters.AddWithValue("$client_id", clientId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Result.Ok<RegisteredClient?>(null);
        }

        var redirectUris = JsonSerializer.Deserialize<List<string>>(reader.GetString(2)) ?? [];
        return Result.Ok<RegisteredClient?>(new RegisteredClient(
            reader.GetString(0),
            reader.GetString(1),
            redirectUris,
            reader.IsDBNull(3) ? null : reader.GetString(3),
            DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(4))));
    }
}
