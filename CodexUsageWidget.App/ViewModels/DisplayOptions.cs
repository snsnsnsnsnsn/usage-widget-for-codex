using CodexUsageWidget.App.Models;

namespace CodexUsageWidget.App.ViewModels;

/// <summary>右クリックメニューで切り替える表示項目。設定ファイルへそのまま保存する。</summary>
public sealed class DisplayOptions : NotifyBase
{
    private bool _showCodex = true;
    private bool _showClaude = true;
    private bool _showFiveHour = true;
    private bool _showWeekly = true;
    private bool _showCountdown = true;
    private bool _showRenewal = true;
    private bool _showModelLimit = true;

    public bool ShowCodex { get => _showCodex; set => Set(ref _showCodex, value); }
    public bool ShowClaude { get => _showClaude; set => Set(ref _showClaude, value); }
    public bool ShowFiveHour { get => _showFiveHour; set => Set(ref _showFiveHour, value); }
    public bool ShowWeekly { get => _showWeekly; set => Set(ref _showWeekly, value); }
    public bool ShowRenewal { get => _showRenewal; set => Set(ref _showRenewal, value); }
    /// <summary>ClaudeのFableなど、特定モデルだけに掛かる週間枠。</summary>
    public bool ShowModelLimit { get => _showModelLimit; set => Set(ref _showModelLimit, value); }

    /// <summary>リセットまでの残り時間（例: あと2日3時間）。</summary>
    public bool ShowCountdown { get => _showCountdown; set => Set(ref _showCountdown, value); }

    public bool IsShown(ProviderKind kind) => kind == ProviderKind.Claude ? ShowClaude : ShowCodex;
}
