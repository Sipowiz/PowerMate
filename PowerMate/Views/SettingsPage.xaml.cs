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

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.StartLevelPolling();
    }

    protected override void OnDisappearing()
    {
        _vm.StopLevelPolling();
        base.OnDisappearing();
    }
}
