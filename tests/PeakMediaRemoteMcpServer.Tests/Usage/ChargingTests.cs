namespace PeakMediaRemoteMcpServer.Tests.Usage;

public class ChargingTests
{
    private static string TextOf(int n)
    {
        return $"Clip {n} says hello to everyone listening today.";
    }

    // TOR-05-MUV56KT: OpenRouter reports $0.0031, $0.0042 and $0.0027 for three clips; the entries carry
    // those costs, the summary shows "$0.0100 this call", and my_usage shows spent $0.01 and 3 clips.
    [Fact]
    public async Task TOR_05_MUV56KT_charges_the_real_reported_cost()
    {
        await using var client = await NarrationClient.StartAsync();
        var costs = new Dictionary<string, decimal> { [TextOf(1)] = 0.0031m, [TextOf(2)] = 0.0042m, [TextOf(3)] = 0.0027m };
        client.OpenRouter.CostFor = text => costs[text];

        var result = await client.SynthesizeAsync(NarrationClient.Clips(3));

        var entries = NarrationClient.Entries(result.Text);
        Assert.Equal([0.0031m, 0.0042m, 0.0027m], entries.Select(e => e.GetProperty("costUsd").GetDecimal()));
        Assert.Contains("$0.0100 this call", result.Text, StringComparison.Ordinal);
        var usage = NarrationClient.UsageTable((await client.MyUsageAsync()).Text);
        Assert.Equal("$0.01", usage["spent"]);
        Assert.Equal("3", usage["clips synthesized"]);
    }

    // TOR-05-dFazrDm (in-process half; ChargingProcessTests checks the [WRN] record in the gateway's log):
    // every generation-statistics request fails, so a 1,000-character clip is charged the configured
    // price of the model for 1,000 characters.
    [Fact]
    public async Task TOR_05_dFazrDm_unreadable_cost_is_charged_at_the_configured_price()
    {
        await using var client = await NarrationClient.StartAsync();
        client.OpenRouter.GenerationNotReadyFor = int.MaxValue;
        var text = string.Concat(Enumerable.Repeat("Word ", 200));
        Assert.Equal(1000, text.Length);
        var model = client.Host.Services.GetRequiredService<TtsCatalog>().FindModel(UsageSeed.Model)!;
        Assert.True(model.PriceUsdPerThousandCharacters > 0m, "the model has no configured price");

        var result = await client.SynthesizeAsync([NarrationClient.Clip(1, text)], model: UsageSeed.Model);

        var entry = Assert.Single(NarrationClient.Entries(result.Text));
        Assert.False(entry.GetProperty("cached").GetBoolean());
        Assert.Equal(model.PriceUsdPerThousandCharacters, entry.GetProperty("costUsd").GetDecimal());
        Assert.True(client.OpenRouter.GenerationLookups > 1, "the lookup should have been retried");
    }

    // TOR-05-DIrxTPy: clips 1–4 cost $0.0030 each and clip 5 never answers; the call fails at clip 5 and
    // the caller is still charged for clips 1–4: my_usage shows spent $0.01 and 4 clips synthesized.
    [Fact]
    public async Task TOR_05_DIrxTPy_partial_failure_keeps_the_charges()
    {
        await using var client = await NarrationClient.StartAsync();
        client.OpenRouter.CostFor = _ => 0.0030m;
        client.OpenRouter.NoAnswer = request => request.Input == TextOf(5);

        var result = await client.SynthesizeAsync(NarrationClient.Clips(12));

        Assert.StartsWith("ERROR: OpenRouter did not answer for clip 5 of 12", result.Text, StringComparison.Ordinal);
        var usage = NarrationClient.UsageTable((await client.MyUsageAsync()).Text);
        Assert.Equal("$0.01", usage["spent"]);
        Assert.Equal("4", usage["clips synthesized"]);
    }
}
