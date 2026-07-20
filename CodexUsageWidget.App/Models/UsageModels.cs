namespace CodexUsageWidget.App.Models;

public sealed record RateLimitWindow(double UsedPercent, int WindowMinutes, DateTimeOffset? ResetsAt)
{
    public double RemainingPercent => Math.Clamp(100d - UsedPercent, 0d, 100d);

    public double? ProjectedConsumptionPercent(DateTimeOffset observedAt)
    {
        if (WindowMinutes <= 0 || ResetsAt is null) return null;

        var window = TimeSpan.FromMinutes(WindowMinutes);
        var startedAt = ResetsAt.Value - window;
        var elapsed = observedAt - startedAt;
        if (elapsed <= TimeSpan.Zero || elapsed >= window) return null;

        var elapsedFraction = elapsed.TotalMinutes / WindowMinutes;
        return Math.Max(0d, UsedPercent / elapsedFraction);
    }
}

public sealed record UsageSnapshot(
    RateLimitWindow? Primary,
    RateLimitWindow? Secondary,
    DateTimeOffset ObservedAt,
    UsageSource Source,
    string? Detail = null)
{
    public RateLimitWindow? FiveHour => FindWindow(300) ?? Primary;
    public RateLimitWindow? Weekly => FindWindow(10080) ?? Secondary;

    private RateLimitWindow? FindWindow(int minutes)
    {
        if (Primary?.WindowMinutes == minutes) return Primary;
        if (Secondary?.WindowMinutes == minutes) return Secondary;
        return null;
    }
}

public enum UsageSource
{
    AppServer,
    LocalSession
}

public sealed class WidgetSettings
{
    public double? X { get; set; }
    public double? Y { get; set; }
    public bool Locked { get; set; }
    public bool ClickThrough { get; set; }
    public bool StartWithWindows { get; set; }
    public int RefreshSeconds { get; set; } = 30;
    public DateTime? SubscriptionRenewalAt { get; set; }
    public int? SubscriptionRenewalDay { get; set; }
}
