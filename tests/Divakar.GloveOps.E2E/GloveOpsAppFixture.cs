using System.Diagnostics;
using Microsoft.Playwright;

namespace Divakar.GloveOps.E2E;

/// <summary>
/// Opt-in end-to-end tests: they reset the demo database, start the app and drive
/// a real Chrome window. Run with run-e2e.bat (sets GLOVEOPS_E2E=1).
/// </summary>
public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("GLOVEOPS_E2E") != "1")
            Skip = "End-to-end tests are opt-in: run run-e2e.bat (GLOVEOPS_E2E=1).";
    }
}

/// <summary>Resets the database, builds and starts GloveOps on port 5081, and launches Chrome. Cleans everything up afterwards.</summary>
public sealed class GloveOpsAppFixture : IAsyncLifetime
{
    public const string BaseUrl = "http://localhost:5081";
    /// <summary>n8n shared key the app is started with, so the callback API can be tested.</summary>
    public const string N8nSharedKey = "e2e-shared-key";
    public static readonly string RepoRoot = FindRepoRoot();
    public static readonly string ScreenshotDir = Path.Combine(RepoRoot, "docs", "screenshots");

    private Process? _app;
    private IPlaywright? _playwright;
    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("GLOVEOPS_E2E") != "1") return;

        // Every run starts from the same demo data.
        await RunAsync("sqlcmd", @"-S DIVAKARLAPTOP\DEVELOPERMSSQL -E -b -i database\00_run_all.sql", RepoRoot);
        await RunAsync("dotnet", @"build src\Divakar.GloveOps.Web -c Debug -v q -nologo", RepoRoot);

        var webDir = Path.Combine(RepoRoot, "src", "Divakar.GloveOps.Web");
        var start = new ProcessStartInfo("dotnet", @"bin\Debug\net10.0\Divakar.GloveOps.Web.dll")
        {
            WorkingDirectory = webDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.Environment["ASPNETCORE_URLS"] = BaseUrl;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["N8n__WebhookUrl"] = "";   // keep the demo independent of a local n8n
        start.Environment["N8n__SharedKey"] = N8nSharedKey;
        _app = Process.Start(start)!;
        _app.OutputDataReceived += (_, _) => { };
        _app.ErrorDataReceived += (_, _) => { };
        _app.BeginOutputReadLine();
        _app.BeginErrorReadLine();
        await WaitForAppAsync();

        Directory.CreateDirectory(ScreenshotDir);
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new()
        {
            Channel = "chrome",                 // the installed Google Chrome; no browser download
            Headless = Environment.GetEnvironmentVariable("GLOVEOPS_E2E_HEADLESS") == "1",
            SlowMo = 250,                       // slow enough to watch
            Args = ["--start-maximized"]
        });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.CloseAsync();
        _playwright?.Dispose();
        if (_app is { HasExited: false })
        {
            _app.Kill(entireProcessTree: true);
            await _app.WaitForExitAsync();
        }
        _app?.Dispose();
    }

    private static async Task WaitForAppAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        for (var i = 0; i < 60; i++)
        {
            try
            {
                if ((await http.GetAsync($"{BaseUrl}/Account/Login")).IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(1000);
        }
        throw new TimeoutException($"GloveOps did not start on {BaseUrl}.");
    }

    private static async Task RunAsync(string file, string args, string cwd)
    {
        using var p = Process.Start(new ProcessStartInfo(file, args)
        {
            WorkingDirectory = cwd, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        })!;
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"{file} {args} failed ({p.ExitCode}):\n{await output}\n{await error}");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Divakar.GloveOps.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root (Divakar.GloveOps.slnx) not found.");
    }
}
