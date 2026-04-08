namespace PowerMate.Views;

public partial class CreditsPage : ContentPage
{
    public CreditsPage()
    {
        InitializeComponent();
        var version = typeof(CreditsPage).Assembly.GetName().Version;
        VersionLabel.Text = $"Version {version?.Major}.{version?.Minor}.{version?.Build}";
    }

    private async void OnGitHubTapped(object? sender, EventArgs e) =>
        await Launcher.OpenAsync(new Uri("https://github.com/Sipowiz/PowerMate"));

    private async void OnLinkedInTapped(object? sender, EventArgs e) =>
        await Launcher.OpenAsync(new Uri("https://www.linkedin.com/in/ville-sipola/"));
}
