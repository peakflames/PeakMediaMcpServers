namespace PeakMediaMcpTools;

/// <summary>
/// The name and semantic version a server reports everywhere: <c>--version</c>, <c>GET /version</c>,
/// the startup log line and the MCP handshake's <c>serverInfo</c>. The version comes from the
/// assembly's informational version, which <c>Directory.Build.props</c> sets.
/// </summary>
public sealed record ServerIdentity(string Name, string Version)
{
    public static ServerIdentity From(Assembly assembly, string name)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            version = version[..plus];
        }

        return new ServerIdentity(name, version);
    }

    /// <summary>The startup log message, e.g. <c>PeakMediaMcpServer v0.1.0 starting</c>.</summary>
    public string StartingMessage => $"{Name} v{Version} starting";
}
