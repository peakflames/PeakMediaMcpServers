namespace PeakMediaRemoteMcpServer.Configuration;

/// <summary>The <c>McpAuth</c> section: the OAuth 2.1 resource-server gate in front of <c>/mcp</c>.</summary>
public sealed class McpAuthOptions
{
    public const string SectionName = "McpAuth";

    /// <summary>Must be true — the gateway has no anonymous mode and refuses to start otherwise.</summary>
    public bool Enabled { get; set; }

    /// <summary>The protected resource URI (the token audience), e.g. <c>http://localhost:5108/mcp</c>.</summary>
    public string Resource { get; set; } = string.Empty;
}
