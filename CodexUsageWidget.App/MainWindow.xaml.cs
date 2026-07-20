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
    private readonly Forms.ToolStripMenuItem _trayShowItem;
    private readonly Forms.ToolStripMenuItem _trayLockItem;
    private readonly Forms.ToolStripMenuItem _trayClickThroughItem;
    private readonly Forms.ToolStripMenuItem _trayStartupItem;
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
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.FiveHour.PropertyChanged += Metric_PropertyChanged;
        _viewModel.Weekly.PropertyChanged += Metric_PropertyChanged;
        _subscriptionRenewalTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _subscriptionRenewalTimer.Tick += (_, _) => RefreshSubscriptionRenewal();
        RefreshSubscriptionRenewal();

        _trayShowItem = new Forms.ToolStripMenuItem("表示／非表示", null, (_, _) => ToggleVisibility());
        _trayLockItem = new Forms.ToolStripMenuItem("位置を固定", null, (_, _) => ToggleLock()) { CheckOnClick = false };
        _trayClickThroughItem = new Forms.ToolStripMenuItem("クリックを透過", null, (_, _) => ToggleClickThrough()) { CheckOnClick = false };
        _trayStartupItem = new Forms.ToolStripMenuItem("Windows起動時に開始", null, (_, _) => ToggleStartup()) { CheckOnClick = false };

        var trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.AddRange([
            _trayShowItem,
            new Forms.ToolStripMenuItem("今すぐ更新", null, async (_, _) => await _viewModel.RefreshAsync()),
            new Forms.ToolStripSeparator(),
            _trayLockItem,
            _trayClickThroughItem,
            _trayStartupItem,
            new Forms.ToolStripMenuItem("サブスク更新日を設定…", null, (_, _) => EditSubscriptionRenewal()),
            new Forms.ToolStripSeparator(),
            new Forms.ToolStripMenuItem("Codex Usageを開く", null, (_, _) => OpenUsagePage()),
            new Forms.ToolStripMenuItem("終了", null, (_, _) => ExitApplication())
        ]);
        trayMenu.Opening += (_, _) => SyncMenuChecks();

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Information,
            Text = "Codex Usage Widget",
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ToggleVisibility();
        Closing += Window_Closing;
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
        SyncMenuChecks();
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
        if (_loaded) ClampToWorkingArea();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.HasData) or
            nameof(MainViewModel.StatusMessage) or
            nameof(MainViewModel.SubscriptionRenewalValueText))
        {
            ScheduleFitToContent();
        }
    }

    private void Metric_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LimitViewModel.PercentText) or
            nameof(LimitViewModel.PaceText) or
            nameof(LimitViewModel.ResetDateText))
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

    private void ContextMenu_Opened(object sender, RoutedEventArgs e) => SyncMenuChecks();
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await _viewModel.RefreshAsync();
    private void Lock_Click(object sender, RoutedEventArgs e) => ToggleLock();
    private void ClickThrough_Click(object sender, RoutedEventArgs e) => ToggleClickThrough();
    private void Startup_Click(object sender, RoutedEventArgs e) => ToggleStartup();
    private void SubscriptionRenewal_Click(object sender, RoutedEventArgs e) => EditSubscriptionRenewal();
    private void OpenUsage_Click(object sender, RoutedEventArgs e) => OpenUsagePage();
    private void Exit_Click(object sender, RoutedEventArgs e) => ExitApplication();

    private void ToggleVisibility()
    {
        Dispatcher.Invoke(() =>
        {
            if (IsVisible) Hide();
            else { Show(); Activate(); Topmost = true; }
        });
    }

    private void ToggleLock()
    {
        _settings.Locked = !_settings.Locked;
        PersistSettings();
        SyncMenuChecks();
    }

    private void ToggleClickThrough()
    {
        _settings.ClickThrough = !_settings.ClickThrough;
        PersistSettings();
        ApplyClickThrough();
        SyncMenuChecks();
    }

    private void ToggleStartup()
    {
        _settings.StartWithWindows = !_settings.StartWithWindows;
        if (!StartupService.SetEnabled(_settings.StartWithWindows))
        {
            _settings.StartWithWindows = StartupService.IsEnabled();
        }
        PersistSettings();
        SyncMenuChecks();
    }

    private void EditSubscriptionRenewal()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(EditSubscriptionRenewal);
            return;
        }

        var dialog = new SubscriptionRenewalDialog(_settings.SubscriptionRenewalAt);
        if (IsVisible) dialog.Owner = this;
        if (dialog.ShowDialog() != true) return;

        _settings.SubscriptionRenewalAt = dialog.RenewalAt;
        _settings.SubscriptionRenewalDay = dialog.RenewalAt?.Day;
        RefreshSubscriptionRenewal();
        ScheduleFitToContent();
    }

    private void RefreshSubscriptionRenewal()
    {
        if (_settings.SubscriptionRenewalAt is not DateTime renewalAt)
        {
            var clearDay = _settings.SubscriptionRenewalDay is not null;
            _settings.SubscriptionRenewalDay = null;
            _viewModel.SetSubscriptionRenewal(null);
            if (clearDay) PersistSettings();
            return;
        }

        var schedule = SubscriptionRenewalSchedule.AdvanceToFuture(
            renewalAt,
            _settings.SubscriptionRenewalDay,
            DateTime.Now);
        var changed = _settings.SubscriptionRenewalAt != schedule.NextRenewalAt ||
            _settings.SubscriptionRenewalDay != schedule.AnchorDay;
        _settings.SubscriptionRenewalAt = schedule.NextRenewalAt;
        _settings.SubscriptionRenewalDay = schedule.AnchorDay;
        _viewModel.SetSubscriptionRenewal(schedule.NextRenewalAt);
        if (changed) PersistSettings();
    }

    private void SyncMenuChecks()
    {
        Dispatcher.Invoke(() =>
        {
            LockMenuItem.IsChecked = _settings.Locked;
            ClickThroughMenuItem.IsChecked = _settings.ClickThrough;
            StartupMenuItem.IsChecked = StartupService.IsEnabled();
            _trayLockItem.Checked = _settings.Locked;
            _trayClickThroughItem.Checked = _settings.ClickThrough;
            _trayStartupItem.Checked = StartupService.IsEnabled();
            _trayShowItem.Text = IsVisible ? "非表示" : "表示";
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

    private static void OpenUsagePage()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://chatgpt.com/codex/settings/usage") { UseShellExecute = true });
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
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        _viewModel.FiveHour.PropertyChanged -= Metric_PropertyChanged;
        _viewModel.Weekly.PropertyChanged -= Metric_PropertyChanged;
        _viewModel.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
