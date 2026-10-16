using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using CodexUsageWidget.App.Models;
using CodexUsageWidget.App.Services;
using CodexUsageWidget.App.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace CodexUsageWidget.App;

public partial class MainWindow : Window, IDisposable
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private readonly MainViewModel _viewModel;
    private readonly SettingsService _settingsService;
    private readonly WidgetSettings _settings;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly Forms.ToolStripMenuItem _showItem;
    private readonly List<Action> _menuCheckSyncs = [];
    private readonly System.Windows.Threading.DispatcherTimer _subscriptionRenewalTimer;
    private bool _allowClose;
    private bool _loaded;
    private bool _fitPending;

    public MainWindow(MainViewModel viewModel, SettingsService settingsService, WidgetSettings settings)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        _settingsService = settingsService;
        _settings = settings;
        _viewModel.Options.PropertyChanged += Options_PropertyChanged;
        foreach (var provider in _viewModel.Providers)
        {
            provider.PropertyChanged += Provider_PropertyChanged;
            provider.FiveHour.PropertyChanged += Metric_PropertyChanged;
            provider.Weekly.PropertyChanged += Metric_PropertyChanged;
            provider.ModelLimit.PropertyChanged += Metric_PropertyChanged;
        }
        _subscriptionRenewalTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _subscriptionRenewalTimer.Tick += (_, _) => RefreshSubscriptionRenewal();
        RefreshSubscriptionRenewal();

        _showItem = new Forms.ToolStripMenuItem("非表示", null, (_, _) => ToggleVisibility());
        _menu = BuildMenu();

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Information,
            Text = "Usage Widget (Codex / Claude)",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ToggleVisibility();
        Closing += Window_Closing;
    }

    /// <summary>ウィジェットの右クリックと通知領域アイコンで同じメニューを使う。</summary>
    private Forms.ContextMenuStrip BuildMenu()
    {
        var options = _viewModel.Options;

        var display = new Forms.ToolStripMenuItem("表示する項目");
        display.DropDownItems.AddRange([
            Toggle("Codex", () => options.ShowCodex, v => options.ShowCodex = v, () => options.ShowClaude),
            Toggle("Claude", () => options.ShowClaude, v => options.ShowClaude = v, () => options.ShowCodex),
            new Forms.ToolStripSeparator(),
            Toggle("5時間枠", () => options.ShowFiveHour, v => options.ShowFiveHour = v, () => options.ShowWeekly),
            Toggle("週間枠", () => options.ShowWeekly, v => options.ShowWeekly = v, () => options.ShowFiveHour),
            Toggle("モデル別の週間枠（Fableなど）", () => options.ShowModelLimit, v => options.ShowModelLimit = v),
            new Forms.ToolStripSeparator(),
            Toggle("リセット時刻", () => options.ShowReset, v => options.ShowReset = v),
            Toggle("ペース", () => options.ShowPace, v => options.ShowPace = v),
            Toggle("サブスク更新日", () => options.ShowRenewal, v => options.ShowRenewal = v)
        ]);
        // 続けて切り替えられるよう、項目のクリックでは閉じない。
        display.DropDown.Closing += (_, e) =>
        {
            if (e.CloseReason == Forms.ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true;
        };

        var renewal = new Forms.ToolStripMenuItem("サブスク更新日を設定");
        var usagePages = new Forms.ToolStripMenuItem("使用状況ページを開く");
        foreach (var provider in _viewModel.Providers)
        {
            renewal.DropDownItems.Add(new Forms.ToolStripMenuItem($"{provider.Name}…", null, (_, _) => EditSubscriptionRenewal(provider)));
            usagePages.DropDownItems.Add(new Forms.ToolStripMenuItem(provider.Name, null, (_, _) => OpenUsagePage(provider.UsagePageUrl)));
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.AddRange([
            new Forms.ToolStripMenuItem("今すぐ更新", null, async (_, _) => await _viewModel.RefreshAsync()),
            display,
            new Forms.ToolStripSeparator(),
            Toggle("位置を固定", () => _settings.Locked, v => { _settings.Locked = v; PersistSettings(); }),
            Toggle("クリックを透過", () => _settings.ClickThrough, v => { _settings.ClickThrough = v; PersistSettings(); ApplyClickThrough(); }),
            Toggle("Windows起動時に開始", StartupService.IsEnabled, SetStartup),
            renewal,
            new Forms.ToolStripSeparator(),
            usagePages,
            _showItem,
            new Forms.ToolStripMenuItem("終了", null, (_, _) => ExitApplication())
        ]);
        menu.Opening += (_, _) => SyncMenuChecks();
        return menu;
    }

    /// <param name="canTurnOff">falseを返す間はオフにできない（最後の1つを消さないため）。</param>
    private Forms.ToolStripMenuItem Toggle(string text, Func<bool> get, Action<bool> set, Func<bool>? canTurnOff = null)
    {
        var item = new Forms.ToolStripMenuItem(text);
        item.Click += (_, _) =>
        {
            var next = !get();
            if (!next && canTurnOff?.Invoke() == false) return;
            set(next);
            item.Checked = get();
        };
        _menuCheckSyncs.Add(() => item.Checked = get());
        return item;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        if (_settings.X is double x && _settings.Y is double y && IsVisibleLocation(x, y))
        {
            Left = x;
            Top = y;
        }
        else
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - ActualWidth - 16;
            Top = workArea.Top + 16;
        }

        ClampToWorkingArea();
        PersistSettings();
        ApplyClickThrough();
        _subscriptionRenewalTimer.Start();
        await _viewModel.StartAsync();
        FitToContent();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_settings.Locked && e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => SaveLocation();

    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _menu.Show(Forms.Cursor.Position);
        e.Handled = true;
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (_loaded && WindowState == WindowState.Normal)
        {
            _settings.X = Left;
            _settings.Y = Top;
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_loaded) return;
        // 列の増減で幅が変わったとき、画面の右半分に置かれていれば右端を基準に保つ。
        if (e.WidthChanged && e.PreviousSize.Width > 0)
        {
            var screen = Forms.Screen.FromPoint(new Drawing.Point((int)Left, (int)Top));
            var center = Left + e.PreviousSize.Width / 2;
            if (center > screen.WorkingArea.Left + screen.WorkingArea.Width / 2d)
            {
                Left -= e.NewSize.Width - e.PreviousSize.Width;
            }
        }
        ClampToWorkingArea();
    }

    private void Options_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        PersistSettings();
        ScheduleFitToContent();
    }

    private void Provider_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProviderViewModel.HasData) or
            nameof(ProviderViewModel.StatusMessage) or
            nameof(ProviderViewModel.IsEnabled) or
            nameof(ProviderViewModel.RenewalText) or
            nameof(ProviderViewModel.ModelLimitName))
        {
            ScheduleFitToContent();
        }
    }

    private void Metric_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LimitViewModel.PercentText) or
            nameof(LimitViewModel.PaceShortText) or
            nameof(LimitViewModel.ResetShortText))
        {
            ScheduleFitToContent();
        }
    }

    private void ScheduleFitToContent()
    {
        if (!_loaded || _fitPending) return;
        _fitPending = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            _fitPending = false;
            FitToContent();
        }));
    }

    private void FitToContent()
    {
        UpdateLayout();
        ClampToWorkingArea();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        SaveLocation();
    }

    private void ToggleVisibility()
    {
        Dispatcher.Invoke(() =>
        {
            if (IsVisible) Hide();
            else { Show(); Activate(); Topmost = true; }
        });
    }

    private void SetStartup(bool enabled)
    {
        _settings.StartWithWindows = StartupService.SetEnabled(enabled) ? enabled : StartupService.IsEnabled();
        PersistSettings();
    }

    private void EditSubscriptionRenewal(ProviderViewModel provider)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => EditSubscriptionRenewal(provider));
            return;
        }

        var dialog = new SubscriptionRenewalDialog(provider.Name, _settings.GetRenewal(provider.Kind).At);
        if (IsVisible) dialog.Owner = this;
        if (dialog.ShowDialog() != true) return;

        _settings.SetRenewal(provider.Kind, dialog.RenewalAt, dialog.RenewalAt?.Day);
        RefreshSubscriptionRenewal();
        PersistSettings();
    }

    private void RefreshSubscriptionRenewal()
    {
        var changed = false;
        foreach (var provider in _viewModel.Providers)
        {
            var (at, day) = _settings.GetRenewal(provider.Kind);
            if (at is not DateTime renewalAt)
            {
                if (day is not null)
                {
                    _settings.SetRenewal(provider.Kind, null, null);
                    changed = true;
                }
                provider.SetRenewal(null);
                continue;
            }

            var schedule = SubscriptionRenewalSchedule.AdvanceToFuture(renewalAt, day, DateTime.Now);
            if (renewalAt != schedule.NextRenewalAt || day != schedule.AnchorDay)
            {
                _settings.SetRenewal(provider.Kind, schedule.NextRenewalAt, schedule.AnchorDay);
                changed = true;
            }
            provider.SetRenewal(schedule.NextRenewalAt);
        }
        if (changed) PersistSettings();
    }

    private void SyncMenuChecks()
    {
        Dispatcher.Invoke(() =>
        {
            foreach (var sync in _menuCheckSyncs) sync();
            _showItem.Text = IsVisible ? "非表示" : "表示";
        });
    }

    private void ApplyClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var style = GetWindowLong(handle, GwlExStyle);
        style = _settings.ClickThrough ? style | WsExTransparent : style & ~WsExTransparent;
        SetWindowLong(handle, GwlExStyle, style);
    }

    private static void OpenUsagePage(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    private void SaveLocation()
    {
        if (WindowState == WindowState.Normal)
        {
            _settings.X = Left;
            _settings.Y = Top;
        }
        PersistSettings();
    }

    private void PersistSettings() => _settingsService.Save(_settings);

    private static bool IsVisibleLocation(double x, double y)
    {
        return Forms.Screen.AllScreens.Any(screen =>
            screen.WorkingArea.IntersectsWith(new Drawing.Rectangle((int)x, (int)y, 120, 80)));
    }

    private void ClampToWorkingArea()
    {
        var screen = Forms.Screen.FromPoint(new Drawing.Point((int)Left, (int)Top));
        if (screen.Primary)
        {
            var primaryArea = SystemParameters.WorkArea;
            Left = Math.Clamp(Left, primaryArea.Left + 8, Math.Max(primaryArea.Left + 8, primaryArea.Right - ActualWidth - 8));
            Top = Math.Clamp(Top, primaryArea.Top + 8, Math.Max(primaryArea.Top + 8, primaryArea.Bottom - ActualHeight - 8));
            return;
        }

        var area = screen.WorkingArea;
        var maxLeft = Math.Max(area.Left + 8, area.Right - ActualWidth - 8);
        var maxTop = Math.Max(area.Top + 8, area.Bottom - ActualHeight - 8);
        Left = Math.Clamp(Left, area.Left + 8, maxLeft);
        Top = Math.Clamp(Top, area.Top + 8, maxTop);
    }

    private void ExitApplication()
    {
        Dispatcher.Invoke(() =>
        {
            _allowClose = true;
            Close();
            System.Windows.Application.Current.Shutdown();
        });
    }

    public void Dispose()
    {
        _subscriptionRenewalTimer.Stop();
        _viewModel.Options.PropertyChanged -= Options_PropertyChanged;
        foreach (var provider in _viewModel.Providers)
        {
            provider.PropertyChanged -= Provider_PropertyChanged;
            provider.FiveHour.PropertyChanged -= Metric_PropertyChanged;
            provider.Weekly.PropertyChanged -= Metric_PropertyChanged;
            provider.ModelLimit.PropertyChanged -= Metric_PropertyChanged;
        }
        _viewModel.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _menu.Dispose();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
