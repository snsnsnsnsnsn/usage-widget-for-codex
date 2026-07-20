using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;
using CodexUsageWidget.App.Models;
using CodexUsageWidget.App.Services;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace CodexUsageWidget.App.ViewModels;

public sealed class MainViewModel : NotifyBase, IDisposable
{
    private readonly UsageService _usageService;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _refreshTimer;
    private readonly CancellationTokenSource _shutdown = new();
    private bool _refreshing;
    private DateTimeOffset? _observedAt;
    private string _sourceText = "WAIT";
    private string _updatedText = "取得中…";
    private bool _hasData;
    private string _statusMessage = "使用量を取得中…";
    private DateTime? _subscriptionRenewalAt;
    private string _subscriptionRenewalValueText = "未設定";

    public MainViewModel(UsageService usageService, int refreshSeconds)
    {
        _usageService = usageService;
        FiveHour = new LimitViewModel();
        Weekly = new LimitViewModel();
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(refreshSeconds, 15, 300)) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
    }

    public LimitViewModel FiveHour { get; }
    public LimitViewModel Weekly { get; }
    public string SourceText { get => _sourceText; private set => Set(ref _sourceText, value); }
    public string UpdatedText { get => _updatedText; private set => Set(ref _updatedText, value); }
    public bool HasData { get => _hasData; private set => Set(ref _hasData, value); }
    public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }
    public DateTime? SubscriptionRenewalAt { get => _subscriptionRenewalAt; private set => Set(ref _subscriptionRenewalAt, value); }
    public string SubscriptionRenewalValueText { get => _subscriptionRenewalValueText; private set => Set(ref _subscriptionRenewalValueText, value); }

    public void SetSubscriptionRenewal(DateTime? renewalAt)
    {
        SubscriptionRenewalAt = renewalAt;
        SubscriptionRenewalValueText = renewalAt?.ToString("yyyy/MM/dd HH:mm") ?? "未設定";
    }

    public async Task StartAsync()
    {
        _clockTimer.Start();
        _refreshTimer.Start();
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var snapshot = await _usageService.GetLatestAsync(_shutdown.Token);
            if (snapshot is null)
            {
                HasData = false;
                StatusMessage = "使用量を取得できません";
                SourceText = "NO DATA";
                UpdatedText = "Codexを一度実行してください";
                return;
            }

            HasData = true;
            FiveHour.Update(snapshot.FiveHour, snapshot.ObservedAt);
            Weekly.Update(snapshot.Weekly, snapshot.ObservedAt);
            _observedAt = snapshot.ObservedAt;
            if (snapshot.Source == UsageSource.AppServer)
            {
                SourceText = "LIVE";
            }
            else
            {
                SourceText = "LOCAL";
            }
            UpdateClock();
        }
        catch (OperationCanceledException) { }
        finally
        {
            _refreshing = false;
        }
    }

    private void UpdateClock()
    {
        FiveHour.UpdateCountdown();
        Weekly.UpdateCountdown();
        if (_observedAt is null) return;
        var age = DateTimeOffset.UtcNow - _observedAt.Value;
        UpdatedText = age.TotalSeconds < 5 ? "たった今" :
            age.TotalMinutes < 1 ? $"{Math.Max(1, (int)age.TotalSeconds)}秒前" :
            age.TotalHours < 1 ? $"{(int)age.TotalMinutes}分前" :
            age.TotalDays < 1 ? $"{(int)age.TotalHours}時間前" :
            $"{(int)age.TotalDays}日前";
    }

    public void Dispose()
    {
        _clockTimer.Stop();
        _refreshTimer.Stop();
        _shutdown.Cancel();
        _shutdown.Dispose();
        _usageService.Dispose();
    }
}

public sealed class LimitViewModel : NotifyBase
{
    private double _remainingPercent;
    private string _percentText = "--%";
    private string _resetText = "データ待機中";
    private string _resetDateText = "--/-- --:--";
    private string _resetDatePart = "--/--";
    private string _resetTimePart = "--:--";
    private string _paceText = "--";
    private Brush _accentBrush = new SolidColorBrush(Color.FromRgb(115, 128, 144));
    private Brush _paceBrush = new SolidColorBrush(Color.FromRgb(115, 128, 144));
    private DateTimeOffset? _resetsAt;

    public double RemainingPercent { get => _remainingPercent; private set => Set(ref _remainingPercent, value); }
    public string PercentText { get => _percentText; private set => Set(ref _percentText, value); }
    public string ResetText { get => _resetText; private set => Set(ref _resetText, value); }
    public string ResetDateText { get => _resetDateText; private set => Set(ref _resetDateText, value); }
    public string ResetDatePart { get => _resetDatePart; private set => Set(ref _resetDatePart, value); }
    public string ResetTimePart { get => _resetTimePart; private set => Set(ref _resetTimePart, value); }
    public string PaceText { get => _paceText; private set => Set(ref _paceText, value); }
    public Brush AccentBrush { get => _accentBrush; private set => Set(ref _accentBrush, value); }
    public Brush PaceBrush { get => _paceBrush; private set => Set(ref _paceBrush, value); }

    public void Update(RateLimitWindow? window, DateTimeOffset observedAt)
    {
        if (window is null)
        {
            RemainingPercent = 0;
            PercentText = "--%";
            ResetText = "利用枠なし";
            ResetDateText = "--/-- --:--";
            ResetDatePart = "--/--";
            ResetTimePart = "--:--";
            PaceText = "--";
            AccentBrush = new SolidColorBrush(Color.FromRgb(115, 128, 144));
            PaceBrush = new SolidColorBrush(Color.FromRgb(115, 128, 144));
            _resetsAt = null;
            return;
        }

        RemainingPercent = window.RemainingPercent;
        PercentText = $"{Math.Round(RemainingPercent):0}%";
        _resetsAt = window.ResetsAt;
        var resetLocalTime = _resetsAt?.ToLocalTime();
        ResetDateText = resetLocalTime?.ToString("MM/dd HH:mm") ?? "--/-- --:--";
        ResetDatePart = resetLocalTime?.ToString("MM/dd") ?? "--/--";
        ResetTimePart = resetLocalTime?.ToString("HH:mm") ?? "--:--";
        AccentBrush = RemainingPercent switch
        {
            > 50 => new SolidColorBrush(Color.FromRgb(71, 214, 151)),
            > 20 => new SolidColorBrush(Color.FromRgb(250, 184, 76)),
            _ => new SolidColorBrush(Color.FromRgb(244, 91, 105))
        };
        UpdatePace(window.ProjectedConsumptionPercent(observedAt));
        UpdateCountdown();
    }

    private void UpdatePace(double? projectedConsumptionPercent)
    {
        if (projectedConsumptionPercent is null)
        {
            PaceText = "--";
            PaceBrush = new SolidColorBrush(Color.FromRgb(115, 128, 144));
            return;
        }

        var rounded = Math.Round(projectedConsumptionPercent.Value);
        PaceText = rounded > 999 ? "999%+" : $"{rounded:0}%";
        PaceBrush = projectedConsumptionPercent.Value switch
        {
            > 100 => new SolidColorBrush(Color.FromRgb(244, 91, 105)),
            >= 85 => new SolidColorBrush(Color.FromRgb(250, 184, 76)),
            _ => new SolidColorBrush(Color.FromRgb(71, 214, 151))
        };
    }

    public void UpdateCountdown()
    {
        if (_resetsAt is null)
        {
            if (PercentText != "--%") ResetText = "リセット時刻なし";
            return;
        }

        var remaining = _resetsAt.Value - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            ResetText = "更新を確認中…";
        }
        else if (remaining.TotalDays >= 1)
        {
            ResetText = $"あと {(int)remaining.TotalDays}日 {remaining.Hours:00}:{remaining.Minutes:00}";
        }
        else
        {
            ResetText = $"あと {(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
        }
    }
}

public abstract class NotifyBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
