namespace PeakMediaRemoteMcpServer.Tests.Narration;

public class WavTests
{
    // TOR-03-eKeTzs6: the clip downloads as a 16-bit mono 24 kHz PCM WAV; sampleRate 24000; durationMs =
    // data length / 48, rounded.
    [Fact]
    public async Task TOR_03_eKeTzs6_clip_is_16_bit_mono_24khz_wav()
    {
        await using var client = await NarrationClient.StartAsync();
        var result = await client.SynthesizeAsync([NarrationClient.Clip(1, "One short sentence for the header check.")]);
        var entry = Assert.Single(NarrationClient.Entries(result.Text));

        using var response = await client.DownloadAsync(entry.GetProperty("url").GetString()!);
        var wav = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal("fmt ", Encoding.ASCII.GetString(wav, 12, 4));
        Assert.Equal(1, BitConverter.ToInt16(wav, 20));
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));
        Assert.Equal(24000, BitConverter.ToInt32(wav, 24));
        Assert.Equal(16, BitConverter.ToInt16(wav, 34));
        Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));
        var dataLength = BitConverter.ToInt32(wav, 40);
        Assert.Equal(wav.Length - 44, dataLength);
        Assert.Equal(24000, entry.GetProperty("sampleRate").GetInt32());
        Assert.Equal((int)Math.Round(dataLength / 48.0, MidpointRounding.AwayFromZero), entry.GetProperty("durationMs").GetInt32());
    }
}
