using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Divakar.GloveOps.E2E;

/// <summary>
/// The full demo story in a real browser: create → validation → send back →
/// resubmit → three approvals (large order goes to the plant manager) → audit trail → integration log.
/// Each step saves a screenshot to docs/screenshots.
/// </summary>
public sealed class SampleRequestFlowTests(GloveOpsAppFixture app) : IClassFixture<GloveOpsAppFixture>, IAsyncLifetime
{
    private const string Base = GloveOpsAppFixture.BaseUrl;
    private IBrowserContext _context = null!;
    private IPage _page = null!;
    private int _shot;
    private string _prefix = "";

    public async Task InitializeAsync()
    {
        if (app.Browser is null) return;   // skipped run
        _context = await app.Browser.NewContextAsync(new() { ViewportSize = ViewportSize.NoViewport });
        _page = await _context.NewPageAsync();
        _page.SetDefaultTimeout(15_000);
    }

    public async Task DisposeAsync()
    {
        if (_context is not null) await _context.CloseAsync();
    }

    [E2EFact]
    public async Task Large_sample_request_goes_through_send_back_resubmit_and_three_approvals()
    {
        _prefix = "1";

        // 0. The login page is styled before sign-in (static files must be anonymous)
        await _context.ClearCookiesAsync();
        await _page.GotoAsync($"{Base}/Account/Login");
        var navBackground = await _page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.go-navbar')).backgroundImage");
        Assert.Contains("gradient", navBackground);
        await ShotAsync("login");
        // 1. Anna (Sales) signs in and sees the dashboard
        await SignInAsync("anna");
        await Expect(_page.Locator("h1")).ToContainTextAsync("Good day, Anna Lin");
        await ShotAsync("dashboard");

        // 2. New request: client-side validation stops qty 0
        await _page.GetByRole(AriaRole.Link, new() { Name = "+ New sample request" }).First.ClickAsync();
        await _page.SelectOptionAsync("#CustomerId", "9");        // Hsinchu Semicon Services
        await _page.SelectOptionAsync("#ProductId", "7");         // CR-NBR-100, made in Tainan
        await _page.SelectOptionAsync("#Size", "M");
        await _page.FillAsync("#Qty", "0");
        await _page.FillAsync("#RequiredDate", DateTime.Today.AddDays(20).ToString("yyyy-MM-dd"));
        await _page.FillAsync("#Purpose", "E2E: new fab line ramp-up");
        await _page.ClickAsync("#btn-save-submit");
        await Expect(_page.Locator("[data-valmsg-for=Qty]")).ToContainTextAsync("between 1 and 100000");
        await ShotAsync("validation-error");

        // 3. 12,000 boxes: the jQuery hint adds the Plant Manager step
        await _page.FillAsync("#Qty", "12000");
        await _page.PressAsync("#Qty", "Tab");    // re-validate now, so the error message doesn't vanish mid-click
        await Expect(_page.Locator("[data-valmsg-for=Qty]")).ToHaveTextAsync("");
        await Expect(_page.Locator("#route-hint")).ToContainTextAsync("Sales Manager → QA → Plant Manager");
        await ShotAsync("route-hint-large-order");
        await _page.ClickAsync("#btn-save-submit");

        await Expect(_page).ToHaveURLAsync(new Regex(@"/SampleRequests/Details/\d+$"));
        var requestNo = (await _page.InnerTextAsync("#request-no")).Trim();
        var detailsUrl = _page.Url;
        await ExpectStatusAsync("In approval");
        await ShotAsync("submitted");

        // 4. Kevin (Sales Manager): send back without a comment is refused, then with a comment
        await SignInAsync("kevin");
        await _page.GotoAsync($"{Base}/Tasks");
        await _page.GetByRole(AriaRole.Link, new() { Name = requestNo }).ClickAsync();
        await _page.ClickAsync("[data-act=SendBack]");
        await Expect(_page.Locator("#act-error")).ToContainTextAsync("comment is required");
        await ShotAsync("send-back-needs-comment");
        await _page.FillAsync("#comment", "Please confirm the monthly volume with the customer");
        await _page.ClickAsync("[data-act=SendBack]");
        await ExpectStatusAsync("Sent back");

        // 5. Anna edits and resubmits
        await SignInAsync("anna");
        await _page.GotoAsync(detailsUrl);
        await _page.GetByRole(AriaRole.Link, new() { Name = "Edit" }).ClickAsync();
        await _page.FillAsync("#Qty", "12500");
        await _page.FillAsync("#Purpose", "E2E: confirmed 12,500 boxes per month");
        await _page.ClickAsync("#btn-save-submit");
        await ExpectStatusAsync("In approval");
        await Expect(_page.Locator(".timeline li").Last).ToContainTextAsync("Resubmitted");
        await ShotAsync("resubmitted");

        // 6. Three approvals; the third goes to the Tainan plant manager (product's plant)
        await ApproveAsync("kevin", detailsUrl, "Customer confirmed");
        await ApproveAsync("grace", detailsUrl, "ISO Class 5 spec OK");
        await Expect(_page.Locator(".steps .step.current")).ToContainTextAsync("Ming Lee");
        await ShotAsync("waiting-for-plant-manager");
        await ApproveAsync("ming", detailsUrl, "Line 2 capacity reserved");

        await ExpectStatusAsync("Approved");
        await Expect(_page.Locator(".steps .step.done")).ToHaveCountAsync(3);
        await Expect(_page.Locator(".timeline")).ToContainTextAsync("Ming Lee (Plant Mgr, Tainan)");
        await ShotAsync("approved-audit-trail");

        // 7. The integration log has the n8n event for this request (Skipped: no webhook configured)
        await _page.GotoAsync($"{Base}/Integrations");
        var row = _page.Locator("tbody tr", new() { HasText = requestNo });
        await Expect(row).ToContainTextAsync("n8n-webhook");
        await Expect(row).ToContainTextAsync("Skipped");
        await ShotAsync("integration-log");

        // 8. The list shows it as approved
        await _page.GotoAsync($"{Base}/SampleRequests?status=Approved");
        await Expect(_page.Locator("#requests")).ToContainTextAsync(requestNo);
        await ShotAsync("approved-list");
    }

    [E2EFact]
    public async Task Delegate_can_approve_while_the_approver_is_on_leave()
    {
        _prefix = "2";
        // Kevin goes on leave and delegates to Mei (the other Sales Manager)
        await SignInAsync("kevin");
        await _page.GotoAsync($"{Base}/Tasks/Delegation");
        await _page.SelectOptionAsync("#DelegateUserId", new SelectOptionValue { Label = "Mei Huang (Sales Manager)" });
        await _page.ClickAsync("button:has-text('Save delegation')");
        await Expect(_page.Locator(".alert-success")).ToContainTextAsync("Delegation saved");
        await ShotAsync("delegation-saved");

        // Mei now sees Kevin's waiting tasks, marked "for Kevin"
        await SignInAsync("mei");
        await _page.GotoAsync($"{Base}/Tasks");
        var delegated = _page.Locator("tbody tr", new() { HasText = "for Kevin Chen" });
        await Expect(delegated.First).ToBeVisibleAsync();
        await ShotAsync("delegated-tasks");

        await delegated.First.Locator("a").First.ClickAsync();
        await _page.FillAsync("#comment", "Approved on behalf of Kevin (on leave)");
        await _page.ClickAsync("[data-act=Approve]");
        await Expect(_page.Locator(".timeline")).ToContainTextAsync("Mei Huang");
        await ShotAsync("approved-by-delegate");
    }

    // ---------- helpers ----------

    private async Task SignInAsync(string userName)
    {
        await _context.ClearCookiesAsync();
        await _page.GotoAsync($"{Base}/Account/Login");
        await _page.ClickAsync($"form:has(input[name=userName][value={userName}]) button");
        await Expect(_page.Locator("nav")).ToContainTextAsync("Switch user");
    }

    private async Task ApproveAsync(string userName, string detailsUrl, string comment)
    {
        await SignInAsync(userName);
        await _page.GotoAsync(detailsUrl);
        await _page.FillAsync("#comment", comment);
        await _page.ClickAsync("[data-act=Approve]");
        await Expect(_page.Locator("#act-panel")).ToHaveCountAsync(0);   // page reloaded, task done
    }

    private Task ExpectStatusAsync(string label) =>
        Expect(_page.Locator("h1 .badge")).ToHaveTextAsync(label);

    private async Task ShotAsync(string name)
    {
        var png = await _page.ScreenshotAsync(new() { FullPage = true });
        var path = Path.Combine(GloveOpsAppFixture.ScreenshotDir, $"{_prefix}-{++_shot:00}-{name}.png");
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
