using CodexUsageWidget.App.Models;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace CodexUsageWidget.App.ViewModels;

/// <summary>1つの利用枠（5時間・週間）のセル。</summary>
public sealed class LimitViewModel : NotifyBase
{
    private static readonly Brush Neutral = Frozen(115, 128, 144);
    private static readonly Brush Good = Frozen(71, 214, 151);
    private static readonly Brush Warn = Frozen(250, 184, 76);
    private static readonly Brush Bad = Frozen(244, 91, 105);

    private double _remainingPercent;
    private double _timeRemainingPercent = double.NaN;
    private bool _hasWindow;
    private string _percentText = "--%";
    private string _windowLabel = string.Empty;
    private Brush _paceBrush = Neutral;
    private string _resetText = "データ待機中";
    private DateTimeOffset? _resetsAt;
    private int _windowMinutes;

    public LimitViewModel(DisplayOptions options) => Options = options;

    public DisplayOptions Options { get; }
    /// <summary>残り利用量（0〜100）。ゲージ上段。</summary>
    public double RemainingPercent { get => _remainingPercent; private set => Set(ref _remainingPercent, value); }
    /// <summary>リセットまでの残り時間の割合（0〜100）。ゲージ下段。不明ならNaN。</summary>
    public double TimeRemainingPercent { get => _timeRemainingPercent; private set => Set(ref _timeRemainingPercent, value); }
    /// <summary>この利用枠が存在するか。ないときにバーを空（=使い切り）と誤読させないため、セルごと隠す。</summary>
    public bool HasWindow { get => _hasWindow; private set => Set(ref _hasWindow, value); }
    public string PercentText { get => _percentText; private set => Set(ref _percentText, value); }
    /// <summary>行ラベルと長さが違う枠（例: 30日枠）のときだけ出す。</summary>
    public string WindowLabel { get => _windowLabel; private set => Set(ref _windowLabel, value); }
    /// <summary>ペース（今のペースで使い切るか）の色。ゲージ上段の塗りに使う。</summary>
    public Brush PaceBrush { get => _paceBrush; private set => Set(ref _paceBrush, value); }
    /// <summary>リセットまでの残り時間。例: あと2日3時間</summary>
    public string ResetText { get => _resetText; private set => Set(ref _resetText, value); }

    public void Update(RateLimitWindow? window, DateTimeOffset observedAt)
    {
        HasWindow = window is not null;
        if (window is null)
        {
            RemainingPercent = 0;
            TimeRemainingPercent = double.NaN;
            PercentText = "--%";
            WindowLabel = string.Empty;
            PaceBrush = Neutral;
            ResetText = string.Empty;
            _resetsAt = null;
            _windowMinutes = 0;
            return;
        }

        RemainingPercent = window.RemainingPercent;
        PercentText = $"{Math.Round(RemainingPercent):0}%";
        WindowLabel = window.NonStandardLabel ?? string.Empty;
        _resetsAt = window.ResetsAt;
        _windowMinutes = window.WindowMinutes;
        UpdatePace(window.ProjectedConsumptionPercent(observedAt));
        UpdateCountdown();
    }

    private void UpdatePace(double? projectedConsumptionPercent)
    {
        if (projectedConsumptionPercent is not double projected)
        {
            PaceBrush = Neutral;
            return;
        }

        PaceBrush = projected switch
        {
            > 100 => Bad,
            >= 85 => Warn,
            _ => Good
        };
    }

    public void UpdateCountdown()
    {
        if (_resetsAt is not DateTimeOffset resetsAt)
        {
            ResetText = string.Empty;
            TimeRemainingPercent = double.NaN;
            return;
        }

        var remaining = resetsAt - DateTimeOffset.UtcNow;
        TimeRemainingPercent = _windowMinutes > 0
            ? Math.Round(Math.Clamp(remaining.TotalMinutes / _windowMinutes * 100d, 0d, 100d), 1)
            : double.NaN;

        ResetText = remaining <= TimeSpan.Zero ? "まもなくリセット" :
            remaining.TotalDays >= 1 ? $"あと{(int)remaining.TotalDays}日{remaining.Hours}時間" :
            remaining.TotalHours >= 1 ? $"あと{(int)remaining.TotalHours}時間{remaining.Minutes}分" :
            $"あと{Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))}分";
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
