namespace PeakMediaMcpTools.OpenRouter;

/// <summary>Why a speech request to OpenRouter failed.</summary>
public enum SpeechFailureKind
{
    /// <summary>No answer within the per-clip timeout.</summary>
    Timeout,

    /// <summary>The connection could not be made or broke off.</summary>
    Unreachable,

    /// <summary>OpenRouter answered with a 5xx status.</summary>
    ServerError,

    /// <summary>402: the account has no credit left.</summary>
    OutOfCredit,

    /// <summary>401: the key was rejected.</summary>
    KeyRejected,

    /// <summary>Any other non-success status.</summary>
    Other,
}

/// <summary>A classified speech failure. Carries no exception text, response body or key.</summary>
public sealed class SpeechError : Error
{
    public SpeechError(SpeechFailureKind kind, int? status, bool retried)
        : base($"OpenRouter speech request failed: {kind}")
    {
        Kind = kind;
        Status = status;
        Retried = retried;
    }

    public SpeechFailureKind Kind { get; }

    /// <summary>The HTTP status OpenRouter answered with, if it answered.</summary>
    public int? Status { get; }

    /// <summary>True when the request was tried a second time before giving up.</summary>
    public bool Retried { get; }

    /// <summary>Timeouts, unreachable connections and 5xx answers are worth one more try.</summary>
    public static bool IsTransient(SpeechFailureKind kind)
    {
        return kind is SpeechFailureKind.Timeout or SpeechFailureKind.Unreachable or SpeechFailureKind.ServerError;
    }

    public static SpeechFailureKind KindFor(int status)
    {
        return status switch
        {
            401 => SpeechFailureKind.KeyRejected,
            402 => SpeechFailureKind.OutOfCredit,
            >= 500 => SpeechFailureKind.ServerError,
            _ => SpeechFailureKind.Other,
        };
    }
}

/// <summary>How long to wait before the one retry of a transient speech failure.</summary>
public sealed record SpeechRetryPolicy(TimeSpan Delay)
{
    public static readonly SpeechRetryPolicy Default = new(TimeSpan.FromSeconds(1));

    public static SpeechRetryPolicy FromMilliseconds(int delayMs)
    {
        return new SpeechRetryPolicy(TimeSpan.FromMilliseconds(Math.Max(0, delayMs)));
    }
}
