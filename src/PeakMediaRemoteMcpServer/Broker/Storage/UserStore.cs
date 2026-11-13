namespace PeakMediaRemoteMcpServer.Broker.Storage;

/// <summary>A person who has signed in. <see cref="Subject"/> (the OIDC <c>sub</c>) is the owner key of every caller-owned record.</summary>
public sealed record UserRecord(string Subject, string Email, bool IsAdmin, DateTimeOffset FirstSeenUtc, DateTimeOffset LastSeenUtc);

/// <summary>The <c>users</c> table: written at each sign-in, read and touched at each refresh.</summary>
public sealed class UserStore(SqliteConnectionFactory connections)
{
    /// <summary>Records a sign-in: creates the row or refreshes email, role and last-seen time.</summary>
    public async Task<Result> UpsertSignInAsync(string subject, string email, bool isAdmin, DateTimeOffset now, CancellationToken cancellationToken = default)
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
            INSERT INTO users (subject, email, is_admin, first_seen_utc, last_seen_utc)
            VALUES ($subject, $email, $is_admin, $now, $now)
            ON CONFLICT(subject) DO UPDATE SET email = $email, is_admin = $is_admin, last_seen_utc = $now;
            """;
        command.Parameters.AddWithValue("$subject", subject);
        command.Parameters.AddWithValue("$email", email);
        command.Parameters.AddWithValue("$is_admin", isAdmin ? 1 : 0);
        command.Parameters.AddWithValue("$now", UnixTime.Of(now));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return Result.Ok();
    }

    public async Task<Result<UserRecord?>> TryGetAsync(string subject, CancellationToken cancellationToken = default)
    {
        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return opened.ToResult<UserRecord?>();
        }

        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT subject, email, is_admin, first_seen_utc, last_seen_utc FROM users WHERE subject = $subject;";
        command.Parameters.AddWithValue("$subject", subject);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Result.Ok<UserRecord?>(null);
        }

        return Result.Ok<UserRecord?>(new UserRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2) != 0,
            DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(3)),
            DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(4))));
    }

    public async Task<Result> TouchAsync(string subject, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var opened = await connections.OpenAsync(cancellationToken);
        if (opened.IsFailed)
        {
            return opened.ToResult();
        }

        await using var connection = opened.Value;
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE users SET last_seen_utc = $now WHERE subject = $subject;";
        command.Parameters.AddWithValue("$subject", subject);
        command.Parameters.AddWithValue("$now", UnixTime.Of(now));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return Result.Ok();
    }
}
