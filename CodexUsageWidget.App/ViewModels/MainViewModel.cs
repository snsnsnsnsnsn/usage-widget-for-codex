using System.ComponentModel;
using System.Windows.Threading;
using CodexUsageWidget.App.Models;

namespace CodexUsageWidget.App.ViewModels;

public sealed class MainViewModel : NotifyBase, IDisposable
{
    private readonly DispatcherTimer _clockTimer;

    public MainViewModel(DisplayOptions options, IReadOnlyList<ProviderViewModel> providers)
    {
        Options = options;
        Providers = providers;
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) =>
        {
            foreach (var provider in Providers) provider.Tick();
        };
        Options.PropertyChanged += Options_PropertyChanged;
        foreach (var provider in Providers) provider.PropertyChanged += Provider_PropertyChanged;
    }

    public DisplayOptions Options { get; }
    public IReadOnlyList<ProviderViewModel> Providers { get; }

    /// <summary>表示中のどれかに更新日が設定されているときだけ、更新日の行を出す。</summary>
    public bool ShowRenewalRow => Options.ShowRenewal && Providers.Any(p => p.IsEnabled && p.HasRenewal);

    /// <summary>モデル別の週間枠（Fableなど）を持つ表示中の列があるときだけ、その行を出す。</summary>
    public bool ShowModelRow => Options.ShowModelLimit && ModelRowLabel is not null;

    public string? ModelRowLabel =>
        Providers.FirstOrDefault(p => p.IsEnabled && p.HasData && p.HasModelLimit)?.ModelLimitName;

    public ProviderViewModel Get(ProviderKind kind) => Providers.First(p => p.Kind == kind);

    public async Task StartAsync()
    {
        _clockTimer.Start();
        await ApplyProviderVisibilityAsync();
    }

    public Task RefreshAsync() => Task.WhenAll(Providers.Select(p => p.RefreshAsync()));

    private Task ApplyProviderVisibilityAsync() =>
        Task.WhenAll(Providers.Select(p => p.SetEnabledAsync(Options.IsShown(p.Kind))));

    private async void Options_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DisplayOptions.ShowRenewal)) OnPropertyChanged(nameof(ShowRenewalRow));
        if (e.PropertyName == nameof(DisplayOptions.ShowModelLimit)) OnPropertyChanged(nameof(ShowModelRow));
        if (e.PropertyName is nameof(DisplayOptions.ShowCodex) or nameof(DisplayOptions.ShowClaude))
        {
            await ApplyProviderVisibilityAsync();
        }
    }

    private void Provider_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProviderViewModel.IsEnabled) or nameof(ProviderViewModel.HasRenewal))
        {
            OnPropertyChanged(nameof(ShowRenewalRow));
        }
        if (e.PropertyName is nameof(ProviderViewModel.IsEnabled) or nameof(ProviderViewModel.HasData) or
            nameof(ProviderViewModel.ModelLimitName))
        {
            OnPropertyChanged(nameof(ModelRowLabel));
            OnPropertyChanged(nameof(ShowModelRow));
        }
    }

    public void Dispose()
    {
        _clockTimer.Stop();
        Options.PropertyChanged -= Options_PropertyChanged;
        foreach (var provider in Providers)
        {
            provider.PropertyChanged -= Provider_PropertyChanged;
            provider.Dispose();
        }
    }
}
