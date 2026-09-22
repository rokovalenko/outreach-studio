using System.Diagnostics;
using System.Diagnostics.Metrics;
using OutreachStudio.ServiceDefaults;

namespace OutreachStudio.Worker;

/// <summary>
/// The worker's spans and instruments. Both live under the shared name that ServiceDefaults already
/// registers, so a new counter shows up in the dashboard without touching the host setup.
/// </summary>
internal static class Telemetry
{
    public static readonly ActivitySource Source = new(Extensions.TelemetryName);

    private static readonly Meter Meter = new(Extensions.TelemetryName);

    /// <summary>One per delivery that reached a terminal or deferred state, tagged with how it ended.</summary>
    public static readonly Counter<long> Deliveries = Meter.CreateCounter<long>("outreach.deliveries");

    /// <summary>Wall clock of a single provider call.</summary>
    public static readonly Histogram<double> ProviderLatency = Meter.CreateHistogram<double>("outreach.provider.latency", "ms");

    /// <summary>Rows taken off the outbox, so claim rate and send rate can be compared.</summary>
    public static readonly Counter<long> Claimed = Meter.CreateCounter<long>("outreach.outbox.claimed");
}
