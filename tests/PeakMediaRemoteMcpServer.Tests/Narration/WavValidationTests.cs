namespace PeakMediaRemoteMcpServer.Tests.Narration;

/// <summary>The WAV check the stdio server runs before serving a clip from its local cache (TOR-06-D8tj7pH).</summary>
public class WavValidationTests
{
    [Fact]
    public void ReadDurationMs_accepts_a_complete_clip()
    {
        var wav = WavAudio.FromPcm(new byte[48_000]);

        var duration = WavAudio.ReadDurationMs(wav);

        Assert.True(duration.IsSuccess);
        Assert.Equal(1000, duration.Value);
    }

    [Fact]
    public void ReadDurationMs_refuses_a_truncated_clip()
    {
        var wav = WavAudio.FromPcm(new byte[48_000]);

        Assert.True(WavAudio.ReadDurationMs(wav.AsSpan(0, wav.Length - 100)).IsFailed);
        Assert.True(WavAudio.ReadDurationMs(wav.AsSpan(0, 20)).IsFailed);
    }

    [Fact]
    public void ReadDurationMs_refuses_another_format()
    {
        var stereo = WavAudio.FromPcm(new byte[4_800]);
        stereo[22] = 2;
        var lowRate = WavAudio.FromPcm(new byte[4_800]);
        BitConverter.GetBytes(16000).CopyTo(lowRate, 24);
        var notWav = Encoding.ASCII.GetBytes(new string('x', 100));

        Assert.True(WavAudio.ReadDurationMs(stereo).IsFailed);
        Assert.True(WavAudio.ReadDurationMs(lowRate).IsFailed);
        Assert.True(WavAudio.ReadDurationMs(notWav).IsFailed);
    }
}
