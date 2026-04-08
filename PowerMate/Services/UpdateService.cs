namespace PowerMate.Services;

public class UpdateService
{
    private const string GitHubReleasesUrl =
        "https://api.github.com/repos/Sipowiz/PowerMate/releases/latest";

    private static readonly Version CurrentVersion =
        typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0);

    private readonly HttpClient _http;

    public UpdateService()
    {
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "PowerMate-Driver");
    }

    /// <summary>
    /// Checks GitHub for a newer release. Returns (true, version, url) if available.
    /// </summary>
    public async Task<(bool available, string version, string url)> CheckForUpdateAsync()
    {
        try
        {
            var json = await _http.GetStringAsync(GitHubReleasesUrl);
            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var htmlUrl = root.GetProperty("html_url").GetString() ?? "";

            var versionStr = tagName.TrimStart('v');
            if (Version.TryParse(versionStr, out var latest) && latest > CurrentVersion)
                return (true, versionStr, htmlUrl);
        }
        catch
        {
            // Network errors, rate limits, etc. — silently ignore
        }

        return (false, "", "");
    }
}
