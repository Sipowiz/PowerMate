using PowerMate.ViewModels;

namespace PowerMate.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _vm;

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _vm.StartLevelPolling();
        await _vm.CheckForUpdatesAsync();
    }

    protected override void OnDisappearing()
    {
        _vm.StopLevelPolling();
        base.OnDisappearing();
    }

    private async void OnDownloadUpdateClicked(object? sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(_vm.UpdateUrl))
            await Launcher.OpenAsync(new Uri(_vm.UpdateUrl));
    }
}
