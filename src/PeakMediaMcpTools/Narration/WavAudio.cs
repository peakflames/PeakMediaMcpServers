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

    /// <summary>
    /// Checks that <paramref name="wav"/> is a complete clip in the shape <see cref="FromPcm"/> writes — a
    /// 44-byte RIFF/WAVE header for 16-bit PCM, mono, 24 kHz, whose RIFF and data lengths match the bytes
    /// present — and gives its duration in milliseconds. A truncated or foreign file fails.
    /// </summary>
    public static Result<int> ReadDurationMs(ReadOnlySpan<byte> wav)
    {
        if (wav.Length < HeaderLength)
        {
            return Result.Fail<int>("shorter than a WAV header");
        }

        var blockAlign = (short)(Channels * BitsPerSample / 8);
        var dataLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(wav[40..44]);
        var valid = Encoding.ASCII.GetString(wav[0..4]) == "RIFF"
            && System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(wav[4..8]) == wav.Length - 8
            && Encoding.ASCII.GetString(wav[8..12]) == "WAVE"
            && Encoding.ASCII.GetString(wav[12..16]) == "fmt "
            && System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(wav[16..20]) == 16
            && System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(wav[20..22]) == 1
            && System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(wav[22..24]) == Channels
            && System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(wav[24..28]) == SampleRate
            && System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(wav[28..32]) == SampleRate * blockAlign
            && System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(wav[32..34]) == blockAlign
            && System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(wav[34..36]) == BitsPerSample
            && Encoding.ASCII.GetString(wav[36..40]) == "data"
            && dataLength == wav.Length - HeaderLength
            && dataLength % blockAlign == 0;
        return valid
            ? Result.Ok(DurationMs(dataLength))
            : Result.Fail<int>("not a complete 16-bit mono 24 kHz PCM WAV");
    }

    /// <summary>Duration of <paramref name="dataLength"/> PCM bytes: data length ÷ 48, rounded.</summary>
    public static int DurationMs(long dataLength)
    {
        return (int)Math.Round(dataLength / BytesPerMillisecond, MidpointRounding.AwayFromZero);
    }
}
