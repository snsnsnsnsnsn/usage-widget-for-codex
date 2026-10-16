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
    private string _paceShortText = string.Empty;
    private Brush _paceBrush = Neutral;
    private string _resetShortText = string.Empty;
    private string _resetDateText = "--/-- --:--";
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
    public string PaceShortText { get => _paceShortText; private set => Set(ref _paceShortText, value); }
    /// <summary>ペースの色。ゲージ上段の塗りにも使い、文字とゲージの色を常に一致させる。</summary>
    public Brush PaceBrush { get => _paceBrush; private set => Set(ref _paceBrush, value); }
    /// <summary>24時間以内なら時刻、それより先なら日付だけ。</summary>
    public string ResetShortText { get => _resetShortText; private set => Set(ref _resetShortText, value); }
    public string ResetDateText { get => _resetDateText; private set => Set(ref _resetDateText, value); }
    /// <summary>リセットまでのカウントダウン（ツールチップ用）。</summary>
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
            PaceShortText = string.Empty;
            PaceBrush = Neutral;
            ResetDateText = "--/-- --:--";
            ResetShortText = string.Empty;
            ResetText = "利用枠なし";
            _resetsAt = null;
            _windowMinutes = 0;
            return;
        }

        RemainingPercent = window.RemainingPercent;
        PercentText = $"{Math.Round(RemainingPercent):0}%";
        WindowLabel = window.NonStandardLabel ?? string.Empty;
        _resetsAt = window.ResetsAt;
        _windowMinutes = window.WindowMinutes;
        ResetDateText = _resetsAt?.ToLocalTime().ToString("MM/dd HH:mm") ?? "--/-- --:--";
        UpdatePace(window.ProjectedConsumptionPercent(observedAt));
        UpdateCountdown();
    }

    private void UpdatePace(double? projectedConsumptionPercent)
    {
        if (projectedConsumptionPercent is not double projected)
        {
            PaceShortText = string.Empty;
            PaceBrush = Neutral;
            return;
        }

        var rounded = Math.Round(projected);
        PaceShortText = rounded > 999 ? "ペース999%+" : $"ペース{rounded:0}%";
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
            if (PercentText != "--%") ResetText = "リセット時刻なし";
            TimeRemainingPercent = double.NaN;
            return;
        }

        var remaining = resetsAt - DateTimeOffset.UtcNow;
        TimeRemainingPercent = _windowMinutes > 0
            ? Math.Round(Math.Clamp(remaining.TotalMinutes / _windowMinutes * 100d, 0d, 100d), 1)
            : double.NaN;
        var local = resetsAt.ToLocalTime();
        ResetShortText = remaining.TotalDays < 1 ? local.ToString("HH:mm") : local.ToString("MM/dd");
        ResetText = remaining <= TimeSpan.Zero ? "更新を確認中…" :
            remaining.TotalDays >= 1 ? $"あと {(int)remaining.TotalDays}日 {remaining.Hours:00}:{remaining.Minutes:00}" :
            $"あと {(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
