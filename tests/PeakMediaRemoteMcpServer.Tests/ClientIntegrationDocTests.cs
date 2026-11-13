namespace PeakMediaRemoteMcpServer.Tests;

/// <summary>docs/client-integration.md — the written contract the peak-media skills adopt.</summary>
public class ClientIntegrationDocTests
{
    private static string Doc()
    {
        return File.ReadAllText(Path.Combine(RepoPaths.RepoRoot, "docs", "client-integration.md")).ReplaceLineEndings("\n");
    }

    // TOR-07-LaYp8xj: the doc names every argument of the gateway's synthesize_narration, list_voices and my_usage
    // input schemas (read from the running gateway, so the doc cannot drift), every clip result field, and the cacheKey rule.
    [Fact]
    public async Task TOR_07_LaYp8xj_doc_names_arguments_result_fields_and_cache_key_rule()
    {
        await using var gateway = await NarrationClient.StartAsync();
        var tools = await gateway.ListToolsAsync();
        var doc = Doc();

        string[] toolNames = ["synthesize_narration", "list_voices", "my_usage"];
        Assert.Equal(toolNames.Order(), tools.Select(t => t.GetProperty("name").GetString()).Order());
        foreach (var tool in tools)
        {
            var name = tool.GetProperty("name").GetString()!;
            Assert.Contains($"### `{name}`", doc, StringComparison.Ordinal);
            var arguments = ArgumentNames(tool.GetProperty("inputSchema")).ToList();
            if (name == "synthesize_narration")
            {
                Assert.Equal(["clips", "clips[].n", "clips[].text", "clips[].style", "clips[].voice", "model", "voice", "style"], arguments);
            }
            else
            {
                Assert.Empty(arguments);
                Assert.Contains("No arguments.", Section(doc, $"### `{name}`"), StringComparison.Ordinal);
            }

            foreach (var argument in arguments)
            {
                Assert.Contains($"| `{argument}` |", doc, StringComparison.Ordinal);
            }
        }

        foreach (var field in new[] { "n", "cacheKey", "durationMs", "words", "wpm", "cached", "costUsd", "sampleRate", "url", "path" })
        {
            Assert.Contains($"| `{field}` |", doc, StringComparison.Ordinal);
        }

        Assert.Contains("| `url` | string | **Gateway only**", doc, StringComparison.Ordinal);
        Assert.Contains("| `path` | string | **Stdio server only**", doc, StringComparison.Ordinal);
        Assert.Contains("**SHA-256** of the UTF-8 bytes of **model, voice, style and\ntext joined by NUL bytes**", doc, StringComparison.Ordinal);
        Assert.Contains("followed by **`.wav`**", doc, StringComparison.Ordinal);
    }

    // TOR-07-1XYMIbC: selection order, and an ERROR: result never falls back to a personal key.
    [Fact]
    public void TOR_07_1XYMIbC_doc_states_selection_order_and_no_fallback_after_error()
    {
        var section = Section(Doc(), "## Selection order");

        var server = section.IndexOf("1. **A connected `peak-media` MCP server**", StringComparison.Ordinal);
        var key = section.IndexOf("2. **A personal OpenRouter key**", StringComparison.Ordinal);
        var grid = section.IndexOf("3. **The beat grid**", StringComparison.Ordinal);
        Assert.True(server >= 0 && key > server && grid > key, "selection order must be server, personal key, beat grid");
        Assert.Contains("A tool result whose text starts with **`ERROR:`** is reported to the user", section, StringComparison.Ordinal);
        Assert.Contains("**never followed\nby a fallback to a personal key**", section, StringComparison.Ordinal);
    }

    // TOR-07-8MvsuuM: download each url into ~/.cache/peak-media/tts/<cacheKey> skipping existing files, verify WAV before
    // mixing, 410 = expired link → call again for new links at no cost, stdio path needs no download.
    [Fact]
    public void TOR_07_8MvsuuM_doc_describes_download_into_cache()
    {
        var section = Section(Doc(), "## Download into the cache");

        Assert.Contains("`~/.cache/peak-media/tts/<cacheKey>`", section, StringComparison.Ordinal);
        Assert.Contains("already exists (and is a valid WAV), **skip** it", section, StringComparison.Ordinal);
        Assert.Contains("download its `url`", section, StringComparison.Ordinal);
        Assert.Contains("**Verify** every file is a valid WAV", section, StringComparison.Ordinal);
        Assert.Contains("**before mixing**", section, StringComparison.Ordinal);
        Assert.Contains("**410** means the **link expired**", section, StringComparison.Ordinal);
        Assert.Contains("Call\n`synthesize_narration` again", section, StringComparison.Ordinal);
        Assert.Contains("**new links at no cost**", section, StringComparison.Ordinal);
        Assert.Contains("each `path` is **already inside the local cache**", section, StringComparison.Ordinal);
        Assert.Contains("**needs no download**", section, StringComparison.Ordinal);
    }

    // TOR-07-akAqnvh: claude mcp add commands for both servers under the name peak-media, and the stdio environment variables.
    [Fact]
    public void TOR_07_akAqnvh_doc_gives_claude_mcp_add_commands_and_stdio_variables()
    {
        var doc = Doc();

        Assert.Contains("claude mcp add --transport http peak-media https://<gateway-host>/mcp", doc, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"claude mcp add peak-media -- \S*PeakMediaMcpServer"), doc);
        var stdio = Section(doc, "### Stdio server");
        foreach (var variable in new[] { "PEAK_MEDIA_OPENROUTER_API_KEY", "TTS_OPENROUTER_API_KEY", "PEAK_MEDIA_TTS_CACHE" })
        {
            Assert.Contains($"| `{variable}` |", stdio, StringComparison.Ordinal);
        }
    }

    /// <summary>Top-level argument names, with each clip object's properties as <c>clips[].x</c> right after <c>clips</c>.</summary>
    private static IEnumerable<string> ArgumentNames(JsonElement schema)
    {
        if (!schema.TryGetProperty("properties", out var properties))
        {
            yield break;
        }

        foreach (var property in properties.EnumerateObject())
        {
            yield return property.Name;
            if (property.Value.TryGetProperty("items", out var items) && items.TryGetProperty("properties", out var itemProperties))
            {
                foreach (var item in itemProperties.EnumerateObject())
                {
                    yield return $"{property.Name}[].{item.Name}";
                }
            }
        }
    }

    /// <summary>The doc from <paramref name="heading"/> up to the next heading of the same or a higher level.</summary>
    private static string Section(string doc, string heading)
    {
        var start = doc.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"heading '{heading}' not found");
        var level = heading.TakeWhile(c => c == '#').Count();
        var next = Regex.Match(doc[(start + heading.Length)..], $@"\n#{{1,{level}}} ");
        return next.Success ? doc.Substring(start, heading.Length + next.Index) : doc[start..];
    }
}
