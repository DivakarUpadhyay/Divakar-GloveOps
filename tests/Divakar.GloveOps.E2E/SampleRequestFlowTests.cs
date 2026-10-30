using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Divakar.GloveOps.E2E;

/// <summary>
/// The full demo story in a real browser: create → validation → send back →
/// resubmit → three approvals (large order goes to the plant manager) → audit trail → integration log.
/// Each step saves a screenshot to docs/screenshots.
/// </summary>
public sealed class SampleRequestFlowTests(GloveOpsAppFixture app) : E2ETestBase(app)
{
    [E2EFact]
    public async Task Large_sample_request_goes_through_send_back_resubmit_and_three_approvals()
    {
        Prefix = "1";

        // 0. The login page is styled before sign-in (static files must be anonymous)
        await Context.ClearCookiesAsync();
        await Page.GotoAsync($"{Base}/Account/Login");
        var navBackground = await Page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.go-navbar')).backgroundImage");
        Assert.Contains("gradient", navBackground);
        await ShotAsync("login");
        // 1. Anna (Sales) signs in and sees the dashboard
        await SignInAsync("anna");
        await Expect(Page.Locator("h1")).ToContainTextAsync("Good day, Anna Lin");
        await ShotAsync("dashboard");

        // 2. New request: client-side validation stops qty 0
        await Page.GetByRole(AriaRole.Link, new() { Name = "+ New sample request" }).First.ClickAsync();
        await Page.SelectOptionAsync("#CustomerId", "9");        // Hsinchu Semicon Services
        await Page.SelectOptionAsync("#ProductId", "7");         // CR-NBR-100, made in Tainan
        await Page.SelectOptionAsync("#Size", "M");
        await Page.FillAsync("#Qty", "0");
        await Page.FillAsync("#RequiredDate", DateTime.Today.AddDays(20).ToString("yyyy-MM-dd"));
        await Page.FillAsync("#Purpose", "E2E: new fab line ramp-up");
        await Page.ClickAsync("#btn-save-submit");
        await Expect(Page.Locator("[data-valmsg-for=Qty]")).ToContainTextAsync("between 1 and 100000");
        await ShotAsync("validation-error");

        // 3. 12,000 boxes: the jQuery hint adds the Plant Manager step
        await Page.FillAsync("#Qty", "12000");
        await Page.PressAsync("#Qty", "Tab");    // re-validate now, so the error message doesn't vanish mid-click
        await Expect(Page.Locator("[data-valmsg-for=Qty]")).ToHaveTextAsync("");
        await Expect(Page.Locator("#route-hint")).ToContainTextAsync("Sales Manager → QA → Plant Manager");
        await ShotAsync("route-hint-large-order");
        await Page.ClickAsync("#btn-save-submit");

        await Expect(Page).ToHaveURLAsync(new Regex(@"/SampleRequests/Details/\d+$"));
        var requestNo = (await Page.InnerTextAsync("#request-no")).Trim();
        var detailsUrl = Page.Url;
        await ExpectStatusAsync("In approval");
        await ShotAsync("submitted");

        // 4. Kevin (Sales Manager): send back without a comment is refused, then with a comment
        await SignInAsync("kevin");
        await Page.GotoAsync($"{Base}/Tasks");
        await Page.GetByRole(AriaRole.Link, new() { Name = requestNo }).ClickAsync();
        await Page.ClickAsync("[data-act=SendBack]");
        await Expect(Page.Locator("#act-error")).ToContainTextAsync("comment is required");
        await ShotAsync("send-back-needs-comment");
        await Page.FillAsync("#comment", "Please confirm the monthly volume with the customer");
        await Page.ClickAsync("[data-act=SendBack]");
        await ExpectStatusAsync("Sent back");

        // 5. Anna edits and resubmits
        await SignInAsync("anna");
        await Page.GotoAsync(detailsUrl);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Edit" }).ClickAsync();
        await Page.FillAsync("#Qty", "12500");
        await Page.FillAsync("#Purpose", "E2E: confirmed 12,500 boxes per month");
        await Page.ClickAsync("#btn-save-submit");
        await ExpectStatusAsync("In approval");
        await Expect(Page.Locator(".timeline li").Last).ToContainTextAsync("Resubmitted");
        await ShotAsync("resubmitted");

        // 6. Three approvals; the third goes to the Tainan plant manager (product's plant)
        await ApproveAsync("kevin", detailsUrl, "Customer confirmed");
        await ApproveAsync("grace", detailsUrl, "ISO Class 5 spec OK");
        await Expect(Page.Locator(".steps .step.current")).ToContainTextAsync("Ming Lee");
        await ShotAsync("waiting-for-plant-manager");
        await ApproveAsync("ming", detailsUrl, "Line 2 capacity reserved");

        await ExpectStatusAsync("Approved");
        await Expect(Page.Locator(".steps .step.done")).ToHaveCountAsync(3);
        await Expect(Page.Locator(".timeline")).ToContainTextAsync("Ming Lee (Plant Mgr, Tainan)");
        await ShotAsync("approved-audit-trail");

        // 7. The integration log has the n8n event for this request (Skipped: no webhook configured)
        await Page.GotoAsync($"{Base}/Integrations");
        var row = Page.Locator("tbody tr", new() { HasText = requestNo });
        await Expect(row).ToContainTextAsync("n8n-webhook");
        await Expect(row).ToContainTextAsync("Skipped");
        await ShotAsync("integration-log");

        // 8. The list shows it as approved
        await Page.GotoAsync($"{Base}/SampleRequests?status=Approved");
        await Expect(Page.Locator("#requests")).ToContainTextAsync(requestNo);
        await ShotAsync("approved-list");
    }

    [E2EFact]
    public async Task Delegate_can_approve_while_the_approver_is_on_leave()
    {
        Prefix = "2";
        // Kevin goes on leave and delegates to Mei (the other Sales Manager)
        await SignInAsync("kevin");
        await Page.GotoAsync($"{Base}/Tasks/Delegation");
        await Page.SelectOptionAsync("#DelegateUserId", new SelectOptionValue { Label = "Mei Huang (Sales Manager)" });
        await Page.ClickAsync("button:has-text('Save delegation')");
        await Expect(Page.Locator(".alert-success")).ToContainTextAsync("Delegation saved");
        await ShotAsync("delegation-saved");

        // Mei now sees Kevin's waiting tasks, marked "for Kevin"
        await SignInAsync("mei");
        await Page.GotoAsync($"{Base}/Tasks");
        var delegated = Page.Locator("tbody tr", new() { HasText = "for Kevin Chen" });
        await Expect(delegated.First).ToBeVisibleAsync();
        await ShotAsync("delegated-tasks");

        await delegated.First.Locator("a").First.ClickAsync();
        await Page.FillAsync("#comment", "Approved on behalf of Kevin (on leave)");
        await Page.ClickAsync("[data-act=Approve]");
        await Expect(Page.Locator(".timeline")).ToContainTextAsync("Mei Huang");
        await ShotAsync("approved-by-delegate");
    }
}
