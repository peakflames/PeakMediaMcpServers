namespace PeakMediaRemoteMcpServer;

/// <summary>
/// The HTTP gateway. Startup order: configure logging → write the startup record → refuse to start
/// on unsafe configuration (before Kestrel binds) → build and run. Nothing is ever written to
/// standard output; logs go to standard error and the daily file under <c>logs/</c>.
/// </summary>
public partial class Program
{
    public const string ServerName = "PeakMediaRemoteMcpServer";

    /// <summary>The named <see cref="HttpClient"/> that carries the shared OpenRouter key.</summary>
    public const string OpenRouterHttpClientName = "openrouter";

    public static ServerIdentity Identity { get; } = ServerIdentity.From(typeof(Program).Assembly, ServerName);

    public static int Main(string[] args)
    {
        var builder = CreateBuilder(args);

        Log.Logger = LoggingSetup.CreateLogger(ServerName, builder.Configuration);
        try
        {
            Log.Information(Identity.StartingMessage);

            var validation = GatewayStartupValidator.Validate(builder.Configuration);
            if (validation.IsFailed)
            {
                Log.Fatal(validation.Errors[0].Message);
                return 1;
            }

            var app = BuildApp(builder);
            app.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "{Server} stopped unexpectedly. Check the log file under logs/ and restart.", ServerName);
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    /// <summary>Creates the builder with the content root next to the binary, whatever the working directory.</summary>
    public static WebApplicationBuilder CreateBuilder(string[] args)
    {
        return WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });
    }

    /// <summary>Registers services and maps endpoints. Tests call this directly with a test server.</summary>
    public static WebApplication BuildApp(WebApplicationBuilder builder)
    {
        // The default console logger writes to stdout; the gateway never writes to stdout.
        builder.Logging.ClearProviders();
        builder.Services.AddSerilog();

        builder.Services.Configure<McpAuthOptions>(builder.Configuration.GetSection(McpAuthOptions.SectionName));
        builder.Services.Configure<BrokerOptions>(builder.Configuration.GetSection(BrokerOptions.SectionName));
        builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
        builder.Services.Configure<OpenRouterOptions>(builder.Configuration.GetSection(OpenRouterOptions.SectionName));
        builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));

        builder.Services.Configure<TtsOptions>(builder.Configuration.GetSection(TtsOptions.SectionName));

        AddStorage(builder.Services);
        AddNarration(builder.Services);
        builder.Services.AddBroker();
        builder.Services.AddCallerAuthentication();

        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new ModelContextProtocol.Protocol.Implementation { Name = Identity.Name, Version = Identity.Version };
                // Answer tools/list even before the first tool exists.
                options.ToolCollection ??= [];
            })
            .WithHttpTransport(options => options.Stateless = true)
            .WithToolsFromAssembly(typeof(ToolErrorFormatter).Assembly)
            .WithTools<TtsTools>();

        var app = builder.Build();
        PrepareStorage(app);

        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/version", () => Results.Json(new { name = Identity.Name, version = Identity.Version }))
            .AllowAnonymous();
        app.MapBrokerEndpoints();
        app.MapClipEndpoints();
        app.MapMcp("/mcp").RequireAuthorization();
        // Stateless Streamable HTTP serves POST only. Map the other methods behind the same gate so an
        // unauthenticated caller always gets 401 (never a 405 that reveals routing before sign-in);
        // a signed-in caller gets 405, as the transport specifies for servers without a GET stream.
        app.MapMethods("/mcp", [HttpMethods.Get, HttpMethods.Delete], () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed))
            .RequireAuthorization();

        return app;
    }

    private static void AddStorage(IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("PeakMediaRemoteMcpServer.Storage");
            var environment = sp.GetRequiredService<IHostEnvironment>();
            var storage = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value;
            var path = DatabasePathResolver.Resolve(storage.DatabasePath, environment.ContentRootPath, logger);
            var faultSwitch = StorageFaultSwitch.FromEnvironment(
                Environment.GetEnvironmentVariable(StorageFaultSwitch.VariableName),
                environment.IsProduction(),
                logger);
            return new SqliteConnectionFactory(path, faultSwitch);
        });
        services.AddSingleton<SchemaMigrator>();
    }

    /// <summary>
    /// The narration tools' services: the configured catalogue, the shared clip cache, download links,
    /// and the OpenRouter client carrying the server-held key (set on the client, never logged).
    /// </summary>
    private static void AddNarration(IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.AddSingleton(sp => new TtsCatalog(sp.GetRequiredService<IOptions<TtsOptions>>().Value));
        services.AddSingleton(sp => AudioDirectory.Resolve(
            sp.GetRequiredService<IOptions<StorageOptions>>().Value,
            sp.GetRequiredService<SqliteConnectionFactory>().DatabasePath,
            sp.GetRequiredService<IHostEnvironment>().ContentRootPath));
        services.AddSingleton<DownloadTokens>();
        services.AddSingleton<IClipStore, ClipStore>();
        services.AddSingleton(sp => CostLookupSchedule.FromMilliseconds(
            sp.GetRequiredService<IOptions<OpenRouterOptions>>().Value.CostLookupDelaysMs));
        services.AddSingleton(sp => SpeechRetryPolicy.FromMilliseconds(
            sp.GetRequiredService<IOptions<OpenRouterOptions>>().Value.RetryDelayMs));
        services.AddHttpClient<OpenRouterSpeechClient>(OpenRouterHttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<OpenRouterOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            client.Timeout = options.ClipTimeout;
        });
        services.AddTransient<NarrationService>();
        services.AddSingleton<CacheEvictor>();
        services.AddHostedService<CacheEvictionService>();
    }

    private static void PrepareStorage(WebApplication app)
    {
        var migrated = app.Services.GetRequiredService<SchemaMigrator>().MigrateAsync().GetAwaiter().GetResult();
        if (migrated.IsFailed)
        {
            app.Logger.LogError("Storage could not be prepared: {Problem}. Check Storage:DatabasePath and restart.", migrated.Errors[0].Message);
        }
    }
}
