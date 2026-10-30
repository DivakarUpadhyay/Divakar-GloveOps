using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Divakar.GloveOps.E2E;

/// <summary>All E2E classes share one database reset, one app instance and one Chrome, and run one after another.</summary>
[CollectionDefinition(Name)]
public sealed class GloveOpsAppCollection : ICollectionFixture<GloveOpsAppFixture>
{
    public const string Name = "GloveOps app";
}

/// <summary>Fresh browser context per test, plus the helpers every story uses.</summary>
[Collection(GloveOpsAppCollection.Name)]
public abstract class E2ETestBase(GloveOpsAppFixture app) : IAsyncLifetime
{
    protected const string Base = GloveOpsAppFixture.BaseUrl;
    protected GloveOpsAppFixture App { get; } = app;
    protected IBrowserContext Context { get; private set; } = null!;
    protected IPage Page { get; private set; } = null!;

    /// <summary>Screenshot file prefix for the current test, e.g. "3".</summary>
    protected string Prefix { get; set; } = "0";
    private int _shot;

    protected virtual BrowserNewContextOptions ContextOptions => new() { ViewportSize = ViewportSize.NoViewport };

    public async Task InitializeAsync()
    {
        if (App.Browser is null) return;   // skipped run
        Context = await App.Browser.NewContextAsync(ContextOptions);
        Page = await Context.NewPageAsync();
        Page.SetDefaultTimeout(15_000);
    }

    public async Task DisposeAsync()
    {
        if (Context is not null) await Context.CloseAsync();
    }

    protected async Task SignInAsync(string userName)
    {
        await Context.ClearCookiesAsync();
        await Page.GotoAsync($"{Base}/Account/Login");
        await Page.ClickAsync($"form:has(input[name=userName][value={userName}]) button");
        await Expect(Page.Locator("nav")).ToContainTextAsync("Switch user");
    }

    /// <summary>Fills the New sample request form (as the signed-in user) without clicking a button.</summary>
    protected async Task FillNewRequestAsync(string customerId, string productId, int qty, string purpose, string size = "M")
    {
        await Page.GotoAsync($"{Base}/SampleRequests/Create");
        await Page.SelectOptionAsync("#CustomerId", customerId);
        await Page.SelectOptionAsync("#ProductId", productId);
        await Page.SelectOptionAsync("#Size", size);
        await Page.FillAsync("#Qty", qty.ToString());
        await Page.FillAsync("#RequiredDate", DateTime.Today.AddDays(20).ToString("yyyy-MM-dd"));
        await Page.FillAsync("#Purpose", purpose);
        await Page.PressAsync("#Purpose", "Tab");   // settle validation before clicking a button
    }

    /// <summary>Creates and submits a request; returns (request number, details URL).</summary>
    protected async Task<(string RequestNo, string Url)> CreateSubmittedRequestAsync(string customerId, string productId, int qty, string purpose)
    {
        await FillNewRequestAsync(customerId, productId, qty, purpose);
        await Page.ClickAsync("#btn-save-submit");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/SampleRequests/Details/\d+$"));
        return ((await Page.InnerTextAsync("#request-no")).Trim(), Page.Url);
    }

    protected async Task ApproveAsync(string userName, string detailsUrl, string comment)
    {
        await SignInAsync(userName);
        await Page.GotoAsync(detailsUrl);
        await Page.FillAsync("#comment", comment);
        await Page.ClickAsync("[data-act=Approve]");
        await Expect(Page.Locator("#act-panel")).ToHaveCountAsync(0);   // page reloaded, task done
    }

    protected Task ExpectStatusAsync(string label) =>
        Expect(Page.Locator("h1 .badge")).ToHaveTextAsync(label);

    protected async Task ShotAsync(string name)
    {
        var png = await Page.ScreenshotAsync(new() { FullPage = true });
        var path = Path.Combine(GloveOpsAppFixture.ScreenshotDir, $"{Prefix}-{++_shot:00}-{name}.png");
        try
        {
            await File.WriteAllBytesAsync(path, png);
        }
        catch (IOException)
        {
            // the old file is open in a viewer / Explorer preview: keep the run going with a new name
            await File.WriteAllBytesAsync(Path.ChangeExtension(path, $".{DateTime.Now:HHmmss}.png"), png);
        }
    }
}
