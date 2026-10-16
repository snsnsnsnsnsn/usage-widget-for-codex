using System.Windows;
using CodexUsageWidget.App.Models;
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
        var options = settings.Display;
        var codex = new ProviderViewModel(
            ProviderKind.Codex,
            "Codex",
            "https://chatgpt.com/codex/settings/usage",
            new CodexUsageService(new AppServerUsageProvider(), new LocalSessionUsageProvider()),
            TimeSpan.FromSeconds(Math.Clamp(settings.RefreshSeconds, 15, 300)),
            options);
        var claude = new ProviderViewModel(
            ProviderKind.Claude,
            "Claude",
            "https://claude.ai/settings/usage",
            new ClaudeUsageProvider(),
            TimeSpan.FromSeconds(Math.Clamp(settings.ClaudeRefreshSeconds, 60, 900)),
            options);
        var viewModel = new MainViewModel(options, [codex, claude]);

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
