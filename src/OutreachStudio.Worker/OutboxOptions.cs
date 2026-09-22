namespace OutreachStudio.Worker;

/// <summary>Everything the drain loop is allowed to tune, bound from the Outbox configuration section.</summary>
public sealed class OutboxOptions
{
    /// <summary>Rows claimed in one statement.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>Deliveries in flight inside one worker.</summary>
    public int Parallelism { get; set; } = 8;

    /// <summary>Rounds of provider calls before a row dead letters.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>A claim older than this belongs to a worker that died, so the row goes back to Queued.</summary>
    public TimeSpan ClaimTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How long to wait after an empty batch.</summary>
    public TimeSpan IdleDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Sends from other campaigns a user may already have in the window before this one is skipped.</summary>
    public int FrequencyCap { get; set; } = 3;

    /// <summary>The window the frequency cap counts over.</summary>
    public int FrequencyWindowDays { get; set; } = 7;
}
