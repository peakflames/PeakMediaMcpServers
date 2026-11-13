namespace PeakMediaRemoteMcpServer.Configuration;

/// <summary>The <c>Quota</c> section: the monthly spending limit of every caller without an override.</summary>
public sealed class QuotaOptions
{
    public const string SectionName = "Quota";

    /// <summary>The default monthly quota in US dollars (calendar month, UTC).</summary>
    public decimal DefaultMonthlyUsd { get; set; } = 25m;
}
