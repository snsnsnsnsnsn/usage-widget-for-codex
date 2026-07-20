using System.Windows;
using CodexUsageWidget.App.Services;
using CodexUsageWidget.App.ViewModels;

namespace CodexUsageWidget.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, @"Local\CodexUsageWidget.Singleton", out var createdNew);
        _ownsSingleInstanceMutex = createdNew;
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        var settingsService = new SettingsService();
        var settings = settingsService.Load();
        var usageService = new UsageService(new AppServerUsageProvider(), new LocalSessionUsageProvider());
        var viewModel = new MainViewModel(usageService, settings.RefreshSeconds);

        _mainWindow = new MainWindow(viewModel, settingsService, settings);
        MainWindow = _mainWindow;
        _mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainWindow?.Dispose();
        if (_ownsSingleInstanceMutex) _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
