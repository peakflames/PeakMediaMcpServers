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
            AddNarration(builder.Services, builder.Configuration, key.Value);
            builder.Services
                .AddMcpServer(options =>
                {
                    options.ServerInfo = new ModelContextProtocol.Protocol.Implementation { Name = identity.Name, Version = identity.Version };
                })
                .WithStdioServerTransport()
                .WithTools<TtsTools>();

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

    /// <summary>
    /// The narration services behind <c>synthesize_narration</c> and <c>list_voices</c>: the catalogue from
    /// <c>Tts</c> (the gateway's models plus <c>fake/tone</c>), the local clip cache, no usage log, and the
    /// OpenRouter client carrying the user's own key on its Authorization header (never logged).
    /// </summary>
    private static void AddNarration(IServiceCollection services, IConfiguration configuration, string apiKey)
    {
        var openRouter = configuration.GetSection(SoloOpenRouterOptions.SectionName).Get<SoloOpenRouterOptions>() ?? new SoloOpenRouterOptions();
        var tts = configuration.GetSection(TtsOptions.SectionName).Get<TtsOptions>() ?? new TtsOptions();
        var cacheDirectory = LocalClipStore.ResolveDirectory();
        Log.Debug("Clips are cached in {Directory}", cacheDirectory);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new TtsCatalog(tts));
        services.AddSingleton<IClipStore>(new LocalClipStore(cacheDirectory));
        services.AddSingleton<IUsageLedger, UnmeteredUsageLedger>();
        services.AddSingleton(_ =>
        {
            var http = new HttpClient
            {
                BaseAddress = new Uri(openRouter.BaseUrl.TrimEnd('/') + "/"),
                Timeout = openRouter.ClipTimeout,
            };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            return new OpenRouterSpeechClient(
                http,
                CostLookupSchedule.FromMilliseconds(openRouter.CostLookupDelaysMs),
                SpeechRetryPolicy.FromMilliseconds(openRouter.RetryDelayMs));
        });
        services.AddSingleton<ToneSpeechClient>();
        services.AddSingleton<ISpeechClient, SoloSpeechClient>();
        services.AddSingleton(NarrationAccount.Personal);
        services.AddTransient<NarrationService>();
    }
}
