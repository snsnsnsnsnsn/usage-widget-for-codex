using System.Windows.Threading;
using CodexUsageWidget.App.Models;
using CodexUsageWidget.App.Services;

namespace CodexUsageWidget.App.ViewModels;

/// <summary>1サービス分（Codex・Claude）の列。取得周期・失敗時の表示を各自で持つ。</summary>
public sealed class ProviderViewModel : NotifyBase, IDisposable
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);
    private readonly IUsageProvider _provider;
    private readonly DispatcherTimer _refreshTimer;
    private readonly CancellationTokenSource _shutdown = new();
    private bool _refreshing;
    private bool _isEnabled;
    private DateTimeOffset? _observedAt;
    private bool _isLocalSource;
    private bool _isStale;
    private bool _hasData;
    private string _statusMessage = "取得中…";
    private IReadOnlyList<TooltipRow> _tooltipRows = [];
    private string _tooltipFooter = string.Empty;
    private string? _modelLimitName;
    private DateTime? _renewalAt;
    private string _renewalText = "—";

    public ProviderViewModel(
        ProviderKind kind,
        string name,
        string usagePageUrl,
        IUsageProvider provider,
        TimeSpan refreshInterval,
        DisplayOptions options)
    {
        Kind = kind;
        Name = name;
        UsagePageUrl = usagePageUrl;
        Options = options;
        _provider = provider;
        FiveHour = new LimitViewModel(options);
        Weekly = new LimitViewModel(options);
        ModelLimit = new LimitViewModel(options);
        _refreshTimer = new DispatcherTimer { Interval = refreshInterval };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
    }

    public ProviderKind Kind { get; }
    public string Name { get; }
    public string UsagePageUrl { get; }
    public DisplayOptions Options { get; }
    public LimitViewModel FiveHour { get; }
    public LimitViewModel Weekly { get; }
    public LimitViewModel ModelLimit { get; }
    /// <summary>モデル別の週間枠の名前（例: Fable）。枠がなければnull。</summary>
    public string? ModelLimitName { get => _modelLimitName; private set { if (Set(ref _modelLimitName, value)) OnPropertyChanged(nameof(HasModelLimit)); } }
    public bool HasModelLimit => ModelLimitName is not null;
    public bool IsEnabled { get => _isEnabled; private set => Set(ref _isEnabled, value); }
    public bool HasData { get => _hasData; private set => Set(ref _hasData, value); }
    public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }
    /// <summary>ホバー時の表。枠ごとに「残り」と「リセットまで」を並べる。</summary>
    public IReadOnlyList<TooltipRow> TooltipRows { get => _tooltipRows; private set { if (!_tooltipRows.SequenceEqual(value)) { _tooltipRows = value; OnPropertyChanged(nameof(TooltipRows)); } } }
    /// <summary>ホバー時の最終行。いつの値か、取得できない理由など。</summary>
    public string TooltipFooter { get => _tooltipFooter; private set => Set(ref _tooltipFooter, value); }
    public bool HasRenewal => _renewalAt is not null;
    public string RenewalText { get => _renewalText; private set => Set(ref _renewalText, value); }

    public async Task SetEnabledAsync(bool enabled)
    {
        if (IsEnabled == enabled) return;
        IsEnabled = enabled;
        if (!enabled)
        {
            _refreshTimer.Stop();
            return;
        }
        _refreshTimer.Start();
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_refreshing || !IsEnabled) return;
        _refreshing = true;
        try
        {
            var result = await _provider.FetchAsync(_shutdown.Token);
            if (result.Snapshot is { } snapshot)
            {
                HasData = true;
                FiveHour.Update(snapshot.FiveHour, snapshot.ObservedAt);
                Weekly.Update(snapshot.Weekly, snapshot.ObservedAt);
                ModelLimit.Update(snapshot.ModelLimit?.Window, snapshot.ObservedAt);
                ModelLimitName = snapshot.ModelLimit?.ModelName;
                _observedAt = snapshot.ObservedAt;
                _isLocalSource = snapshot.Source == UsageSource.LocalSession;
                _isStale = false;
                StatusMessage = string.Empty;
            }
            else if (HasData && _observedAt is { } observedAt && DateTimeOffset.UtcNow - observedAt < StaleAfter)
            {
                // 一時的な失敗では直前の値を残し、ツールチップで理由を示す。
                _isStale = true;
                StatusMessage = result.Message ?? "使用量を取得できません";
            }
            else
            {
                HasData = false;
                _isStale = false;
                StatusMessage = result.Message ?? "使用量を取得できません";
            }
            Tick();
        }
        catch (OperationCanceledException) { }
        finally
        {
            _refreshing = false;
        }
    }

    public void SetRenewal(DateTime? renewalAt)
    {
        _renewalAt = renewalAt;
        RenewalText = renewalAt?.ToString("MM/dd HH:mm") ?? "—";
        OnPropertyChanged(nameof(HasRenewal));
    }

    public void Tick()
    {
        FiveHour.UpdateCountdown();
        Weekly.UpdateCountdown();
        ModelLimit.UpdateCountdown();
        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        if (!HasData)
        {
            TooltipRows = [];
            TooltipFooter = StatusMessage;
            return;
        }

        var rows = new List<TooltipRow>();
        AddRow("5h", FiveHour);
        AddRow("週", Weekly);
        if (ModelLimitName is not null) AddRow(ModelLimitName, ModelLimit);
        TooltipRows = rows;

        var updated = _observedAt is { } observedAt ? $"更新 {FormatAge(DateTimeOffset.UtcNow - observedAt)}" : string.Empty;
        TooltipFooter = _isStale ? $"{updated}（{StatusMessage}）" :
            _isLocalSource ? $"{updated}（PC内の記録）" :
            updated;

        void AddRow(string label, LimitViewModel limit)
        {
            if (!limit.HasWindow) return;
            rows.Add(new TooltipRow(limit.WindowLabel.Length > 0 ? limit.WindowLabel : label, $"残り{limit.PercentText}", limit.ResetText));
        }
    }

    private static string FormatAge(TimeSpan age) =>
        age.TotalMinutes < 1 ? "たった今" :
        age.TotalHours < 1 ? $"{(int)age.TotalMinutes}分前" :
        age.TotalDays < 1 ? $"{(int)age.TotalHours}時間前" :
        $"{(int)age.TotalDays}日前";
    public void Dispose()
    {
        _refreshTimer.Stop();
        _shutdown.Cancel();
        _shutdown.Dispose();
        _provider.Dispose();
    }
}

public sealed record TooltipRow(string Label, string Remaining, string UntilReset);
