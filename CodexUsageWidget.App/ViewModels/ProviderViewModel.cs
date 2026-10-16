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
    private string? _sourceDetail;
    private string _sourceText = "WAIT";
    private string _updatedText = "取得中…";
    private bool _hasData;
    private string _statusMessage = "取得中…";
    private string _detailText = string.Empty;
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
    public string DetailText { get => _detailText; private set => Set(ref _detailText, value); }
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
                _sourceDetail = snapshot.Detail;
                _sourceText = snapshot.Source == UsageSource.LocalSession ? "LOCAL" : "LIVE";
                StatusMessage = string.Empty;
            }
            else if (HasData && _observedAt is { } observedAt && DateTimeOffset.UtcNow - observedAt < StaleAfter)
            {
                // 一時的な失敗では直前の値を残し、ツールチップで理由を示す。
                _sourceText = "STALE";
                StatusMessage = result.Message ?? "使用量を取得できません";
            }
            else
            {
                HasData = false;
                _sourceText = "NO DATA";
                _sourceDetail = null;
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
        if (_observedAt is { } observedAt)
        {
            var age = DateTimeOffset.UtcNow - observedAt;
            _updatedText = age.TotalSeconds < 5 ? "たった今" :
                age.TotalMinutes < 1 ? $"{Math.Max(1, (int)age.TotalSeconds)}秒前" :
                age.TotalHours < 1 ? $"{(int)age.TotalMinutes}分前" :
                age.TotalDays < 1 ? $"{(int)age.TotalHours}時間前" :
                $"{(int)age.TotalDays}日前";
        }
        DetailText = BuildDetailText();
    }

    private string BuildDetailText()
    {
        var lines = new List<string>
        {
            $"{Name}  取得元: {_sourceText}{(_sourceDetail is null ? string.Empty : $"（{_sourceDetail}）")}"
        };
        if (_observedAt is not null) lines.Add($"更新: {_updatedText}");
        if (!string.IsNullOrEmpty(StatusMessage)) lines.Add(StatusMessage);
        if (HasData)
        {
            AddLimitLine("5h", FiveHour);
            AddLimitLine("週", Weekly);
            if (ModelLimitName is not null) AddLimitLine($"{ModelLimitName}（週間）", ModelLimit);
            lines.Add("ゲージ: 上 = 残り利用量、下 = 残り時間。上が下より長ければ余裕");
            lines.Add("ペース: 今のペースを続けた場合の、リセット時点の消費率（100%超で使い切る見込み）");
        }
        return string.Join(Environment.NewLine, lines);

        void AddLimitLine(string label, LimitViewModel limit)
        {
            if (!limit.HasWindow) return;
            var window = limit.WindowLabel.Length > 0 ? $"（{limit.WindowLabel}）" : string.Empty;
            lines.Add($"{label}{window} 残り{limit.PercentText}  リセット: {limit.ResetDateText}（{limit.ResetText}）");
        }
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _shutdown.Cancel();
        _shutdown.Dispose();
        _provider.Dispose();
    }
}
