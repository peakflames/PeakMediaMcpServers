namespace PeakMediaMcpTools.Narration;

/// <summary>
/// Wraps raw PCM from OpenRouter (16-bit little-endian, mono, 24 kHz) in a 44-byte RIFF/WAVE header.
/// At 24,000 samples × 2 bytes per second, one millisecond is 48 bytes.
/// </summary>
public static class WavAudio
{
    public const int SampleRate = 24000;
    public const short Channels = 1;
    public const short BitsPerSample = 16;
    public const int HeaderLength = 44;
    public const double BytesPerMillisecond = SampleRate * Channels * (BitsPerSample / 8) / 1000.0;

    public static byte[] FromPcm(ReadOnlySpan<byte> pcm)
    {
        var blockAlign = (short)(Channels * BitsPerSample / 8);
        var byteRate = SampleRate * blockAlign;
        var wav = new byte[HeaderLength + pcm.Length];
        var span = wav.AsSpan();
        Encoding.ASCII.GetBytes("RIFF", span[0..4]);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span[4..8], 36 + pcm.Length);
        Encoding.ASCII.GetBytes("WAVE", span[8..12]);
        Encoding.ASCII.GetBytes("fmt ", span[12..16]);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span[16..20], 16);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(span[20..22], 1);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(span[22..24], Channels);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span[24..28], SampleRate);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span[28..32], byteRate);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(span[32..34], blockAlign);
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(span[34..36], BitsPerSample);
        Encoding.ASCII.GetBytes("data", span[36..40]);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(span[40..44], pcm.Length);
        pcm.CopyTo(span[HeaderLength..]);
        return wav;
    }

    /// <summary>Duration of <paramref name="dataLength"/> PCM bytes: data length ÷ 48, rounded.</summary>
    public static int DurationMs(long dataLength)
    {
        return (int)Math.Round(dataLength / BytesPerMillisecond, MidpointRounding.AwayFromZero);
    }
}
