namespace PeakMediaMcpServer;

/// <summary>
/// The stdio MCP server. Standard output carries the MCP protocol only (or the <c>--version</c>
/// line); every diagnostic goes to standard error and the daily log file.
/// Exit codes: 0 success, 1 operational failure, 2 invalid invocation.
/// </summary>
public static class Program
{
    public const string ServerName = "PeakMediaMcpServer";

    public static async Task<int> Main(string[] args)
    {
        var identity = ServerIdentity.From(typeof(Program).Assembly, ServerName);

        var invocation = CommandLine.Parse(args);
        if (invocation.IsFailed)
        {
            await Console.Error.WriteLineAsync(invocation.Errors[0].Message);
            return 2;
        }

        if (invocation.Value == InvocationKind.Version)
        {
            await Console.Out.WriteLineAsync($"{identity.Name} v{identity.Version}");
            return 0;
        }

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory,
        });

        Log.Logger = LoggingSetup.CreateLogger(ServerName, builder.Configuration);
        try
        {
            Log.Information(identity.StartingMessage);

            var key = OpenRouterKey.Read();
            if (key.IsFailed)
            {
                Log.Fatal(key.Errors[0].Message);
                return 1;
            }

            // The default console logger writes to stdout, which belongs to the MCP protocol.
            builder.Logging.ClearProviders();
            builder.Services.AddSerilog();
            builder.Services
                .AddMcpServer(options =>
                {
                    options.ServerInfo = new ModelContextProtocol.Protocol.Implementation { Name = identity.Name, Version = identity.Version };
                    // Answer tools/list even before the first tool exists.
                    options.ToolCollection ??= [];
                })
                .WithStdioServerTransport()
                .WithToolsFromAssembly(typeof(ToolErrorFormatter).Assembly);

            using var host = builder.Build();
            await host.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Error: {Server} stopped unexpectedly. Check the log file under logs/ and restart.", ServerName);
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
