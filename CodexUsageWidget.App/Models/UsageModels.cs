using CodexUsageWidget.App.ViewModels;

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
        // 期間の始まり直後は少しの使用でも予測が極端に振れるため出さない。
        if (elapsedFraction < MinimumElapsedFractionForPace) return null;
        return Math.Max(0d, UsedPercent / elapsedFraction);
    }

    private const double MinimumElapsedFractionForPace = 0.05;

    /// <summary>5時間・週間（と期間不明）以外の枠に付ける長さの表示。例: 30日枠</summary>
    public string? NonStandardLabel => WindowMinutes switch
    {
        0 or 300 or 10080 => null,
        var minutes when minutes % 1440 == 0 => $"{minutes / 1440}日枠",
        var minutes when minutes % 60 == 0 => $"{minutes / 60}時間枠",
        var minutes => $"{minutes}分枠"
    };
}

public sealed record UsageSnapshot(
    RateLimitWindow? Primary,
    RateLimitWindow? Secondary,
    DateTimeOffset ObservedAt,
    UsageSource Source,
    string? Detail = null)
{
    // 1日以内の枠は「5h」の行、それより長い枠（週間・30日など）は「週」の行に置く。
    // 期間が不明な枠だけ、primary → 5h、secondary → 週の順番で割り当てる。
    public RateLimitWindow? FiveHour =>
        Find(window => window.WindowMinutes is > 0 and <= ShortWindowMaxMinutes) ??
        (Primary?.WindowMinutes == 0 ? Primary : null);

    public RateLimitWindow? Weekly =>
        Find(window => window.WindowMinutes > ShortWindowMaxMinutes) ??
        (Secondary?.WindowMinutes == 0 ? Secondary : null);

    /// <summary>特定モデルだけに掛かる週間枠（ClaudeのFableなど）。</summary>
    public ModelLimit? ModelLimit { get; init; }

    private const int ShortWindowMaxMinutes = 1440;

    private RateLimitWindow? Find(Func<RateLimitWindow, bool> predicate)
    {
        if (Primary is not null && predicate(Primary)) return Primary;
        if (Secondary is not null && predicate(Secondary)) return Secondary;
        return null;
    }
}

public sealed record ModelLimit(string ModelName, RateLimitWindow Window);

public sealed record UsageFetchResult(UsageSnapshot? Snapshot, string? Message)
{
    public static UsageFetchResult Ok(UsageSnapshot snapshot) => new(snapshot, null);
    public static UsageFetchResult Fail(string message) => new(null, message);
}

public enum UsageSource
{
    AppServer,
    LocalSession,
    ClaudeApi
}

public enum ProviderKind
{
    Codex,
    Claude
}

public sealed class WidgetSettings
{
    public double? X { get; set; }
    public double? Y { get; set; }
    public bool Locked { get; set; }
    public bool ClickThrough { get; set; }
    public bool StartWithWindows { get; set; }
    public int RefreshSeconds { get; set; } = 30;
    public int ClaudeRefreshSeconds { get; set; } = 180;
    public DisplayOptions Display { get; set; } = new();
    public DateTime? SubscriptionRenewalAt { get; set; }
    public int? SubscriptionRenewalDay { get; set; }
    public DateTime? ClaudeSubscriptionRenewalAt { get; set; }
    public int? ClaudeSubscriptionRenewalDay { get; set; }

    public (DateTime? At, int? Day) GetRenewal(ProviderKind kind) => kind == ProviderKind.Claude
        ? (ClaudeSubscriptionRenewalAt, ClaudeSubscriptionRenewalDay)
        : (SubscriptionRenewalAt, SubscriptionRenewalDay);

    public void SetRenewal(ProviderKind kind, DateTime? at, int? day)
    {
        if (kind == ProviderKind.Claude)
        {
            ClaudeSubscriptionRenewalAt = at;
            ClaudeSubscriptionRenewalDay = day;
        }
        else
        {
            SubscriptionRenewalAt = at;
            SubscriptionRenewalDay = day;
        }
    }
}
