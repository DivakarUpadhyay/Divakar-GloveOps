using System.Net;
using System.Net.Http.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Divakar.GloveOps.E2E;

/// <summary>Paths outside the happy flow: reject, drafts, permissions, list filters, form errors, callback API, Unicode.</summary>
public sealed class EdgeCaseTests(GloveOpsAppFixture app) : E2ETestBase(app)
{
    [E2EFact]
    public async Task Rejected_request_is_closed_and_cannot_be_edited()
    {
        Prefix = "3";
        await SignInAsync("anna");
        var (requestNo, url) = await CreateSubmittedRequestAsync("10", "8", 400, "E2E: reject path");

        // Reject also needs a comment
        await SignInAsync("kevin");
        await Page.GotoAsync(url);
        await Page.ClickAsync("[data-act=Reject]");
        await Expect(Page.Locator("#act-error")).ToContainTextAsync("comment is required");
        await Page.FillAsync("#comment", "Customer credit limit exceeded");
        await Page.ClickAsync("[data-act=Reject]");
        await ExpectStatusAsync("Rejected");
        await Expect(Page.Locator("#act-panel")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".steps .step.stopped")).ToContainTextAsync("Rejected");
        await ShotAsync("rejected");

        // The applicant can no longer edit or resubmit
        await SignInAsync("anna");
        await Page.GotoAsync(url);
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Edit" })).ToHaveCountAsync(0);
        await Expect(Page.Locator("#btn-submit")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".timeline")).ToContainTextAsync("Customer credit limit exceeded");
        Assert.StartsWith("SR-", requestNo);
    }

    [E2EFact]
    public async Task Draft_is_saved_then_submitted_from_the_details_page()
    {
        Prefix = "4";
        await SignInAsync("anna");
        await FillNewRequestAsync("1", "4", 600, "E2E: draft first");
        await Page.ClickAsync("#btn-save-draft");

        await ExpectStatusAsync("Draft");
        await Expect(Page.Locator(".alert-success")).ToContainTextAsync("saved as draft");
        await Expect(Page.Locator(".steps .step")).ToHaveCountAsync(2);           // small order: 2 steps
        await Expect(Page.Locator(".steps")).ToContainTextAsync("Not started");
        await ShotAsync("draft-saved");

        // AJAX submit button on the details page
        await Page.ClickAsync("#btn-submit");
        await ExpectStatusAsync("In approval");
        await Expect(Page.Locator(".steps .step.current")).ToContainTextAsync("Kevin Chen");
        await Expect(Page.Locator(".timeline li").Last).ToContainTextAsync("Submitted");
        await ShotAsync("draft-submitted");
    }

    [E2EFact]
    public async Task Only_the_applicant_can_edit()
    {
        Prefix = "5";
        await SignInAsync("anna");
        await FillNewRequestAsync("2", "1", 300, "E2E: permission check");
        await Page.ClickAsync("#btn-save-draft");
        await ExpectStatusAsync("Draft");
        var id = Page.Url.Split('/').Last();

        // Brian (another sales rep) sees no Edit button, and the URL is refused too
        await SignInAsync("brian");
        await Page.GotoAsync($"{Base}/SampleRequests/Details/{id}");
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Edit" })).ToHaveCountAsync(0);
        await Page.GotoAsync($"{Base}/SampleRequests/Edit/{id}");
        await Expect(Page).ToHaveURLAsync($"{Base}/SampleRequests/Details/{id}");
        await Expect(Page.Locator(".alert-danger")).ToContainTextAsync("Only the applicant can edit");
        await ShotAsync("edit-refused");
    }

    [E2EFact]
    public async Task List_search_mine_only_and_quick_filter()
    {
        Prefix = "6";
        await SignInAsync("brian");

        // Server search
        await Page.GotoAsync($"{Base}/SampleRequests");
        await Page.FillAsync("input[name=search]", "Rhein");
        await Page.ClickAsync("button:has-text('Apply')");
        var rows = Page.Locator("#requests tbody tr");
        Assert.True(await rows.CountAsync() >= 1);
        foreach (var text in await rows.AllInnerTextsAsync()) Assert.Contains("Rhein Medical", text);
        await ShotAsync("search");

        // Mine only (Brian's seeded requests)
        await Page.GotoAsync($"{Base}/SampleRequests?mine=true");
        Assert.True(await rows.CountAsync() >= 1);
        foreach (var text in await rows.AllInnerTextsAsync()) Assert.Contains("Brian Wu", text);
        await Expect(Page.Locator("#mine")).ToBeCheckedAsync();

        // Client-side quick filter hides non-matching rows without a reload
        await Page.GotoAsync($"{Base}/SampleRequests");
        var total = await rows.CountAsync();
        await Page.FillAsync("[data-filter-table='#requests']", "Sakura");
        var visible = Page.Locator("#requests tbody tr:visible");
        await Expect(visible.First).ToContainTextAsync("Sakura");
        Assert.True(await visible.CountAsync() < total);
        foreach (var text in await visible.AllInnerTextsAsync()) Assert.Contains("Sakura", text);
        await ShotAsync("quick-filter");
    }

    [E2EFact]
    public async Task Delegation_form_rejects_bad_input()
    {
        Prefix = "7";
        await SignInAsync("grace");
        await Page.GotoAsync($"{Base}/Tasks/Delegation");

        // You cannot pick yourself
        var options = await Page.Locator("#DelegateUserId option").AllInnerTextsAsync();
        Assert.DoesNotContain(options, o => o.Contains("Grace Tsai"));

        // No delegate selected + end date before start date
        await Page.FillAsync("#FromDate", DateTime.Today.AddDays(5).ToString("yyyy-MM-dd"));
        await Page.FillAsync("#ToDate", DateTime.Today.AddDays(2).ToString("yyyy-MM-dd"));
        await Page.SelectOptionAsync("#DelegateUserId", "0");
        await Page.ClickAsync("button:has-text('Save delegation')");
        await Expect(Page.Locator(".validation-summary-errors, [data-valmsg-summary]")).ToContainTextAsync("Select a colleague");

        // Fix the delegate only: the server still refuses the dates
        await Page.SelectOptionAsync("#DelegateUserId", new SelectOptionValue { Label = "Kevin Chen (Sales Manager)" });
        await Page.ClickAsync("button:has-text('Save delegation')");
        await Expect(Page.Locator(".alert-danger")).ToContainTextAsync("End date must be on or after the start date");
        await Expect(Page).ToHaveURLAsync($"{Base}/Tasks/Delegation");
        await ShotAsync("delegation-errors");
    }

    [E2EFact]
    public async Task N8n_callback_needs_the_shared_key_and_is_logged()
    {
        Prefix = "8";
        using var http = new HttpClient { BaseAddress = new Uri(Base) };
        var body = new { requestNo = "SR-2026-00001", status = "PlantNotified", message = "E2E callback check" };

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/integrations/n8n-callback", body)).StatusCode);

        var wrong = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/n8n-callback") { Content = JsonContent.Create(body) };
        wrong.Headers.Add("X-GloveOps-Key", "wrong-key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(wrong)).StatusCode);

        var right = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/n8n-callback") { Content = JsonContent.Create(body) };
        right.Headers.Add("X-GloveOps-Key", GloveOpsAppFixture.N8nSharedKey);
        var ok = await http.SendAsync(right);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        await SignInAsync("divakar");
        await Page.GotoAsync($"{Base}/Integrations");
        var row = Page.Locator("tbody tr", new() { HasText = "E2E callback check" });
        await Expect(row).ToContainTextAsync("n8n-callback");
        await Expect(row).ToContainTextAsync("Received");
        await ShotAsync("callback-received");
    }

    [E2EFact]
    public async Task Chinese_text_is_saved_and_shown()
    {
        Prefix = "9";
        const string purpose = "無塵室手套試用：新竹新產線，每月 12,000 箱 — Thử nghiệm";
        await SignInAsync("anna");
        await FillNewRequestAsync("9", "7", 200, purpose);
        await Page.ClickAsync("#btn-save-draft");
        await ExpectStatusAsync("Draft");
        await Expect(Page.Locator("dd").Filter(new() { HasText = "無塵室" })).ToHaveTextAsync(purpose);

        // and it survives a round trip through the edit form
        await Page.GetByRole(AriaRole.Link, new() { Name = "Edit" }).ClickAsync();
        await Expect(Page.Locator("#Purpose")).ToHaveValueAsync(purpose);
        await ShotAsync("chinese-text");
    }
}

/// <summary>Phone-size screen: collapsed menu, no sideways page scrolling, readable details page.</summary>
public sealed class PhoneLayoutTests(GloveOpsAppFixture app) : E2ETestBase(app)
{
    protected override BrowserNewContextOptions ContextOptions => new()
    {
        ViewportSize = new() { Width = 390, Height = 844 },   // iPhone-size
        DeviceScaleFactor = 2,
        IsMobile = true,
        HasTouch = true
    };

    private async Task AssertNoSidewaysScrollAsync() =>
        Assert.True(await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
            $"Page scrolls sideways on {Page.Url}");

    [E2EFact]
    public async Task Works_on_a_phone_size_screen()
    {
        Prefix = "P";
        await Page.GotoAsync($"{Base}/Account/Login");
        await AssertNoSidewaysScrollAsync();
        await ShotAsync("phone-login");

        await Page.TapAsync("form:has(input[name=userName][value=kevin]) button");
        await Expect(Page.Locator("h1")).ToContainTextAsync("Good day");
        await AssertNoSidewaysScrollAsync();

        // Menu is collapsed behind the toggler, and opens on tap
        var myTasks = Page.GetByRole(AriaRole.Link, new() { Name = "My tasks", Exact = true });
        await Expect(myTasks).ToBeHiddenAsync();
        await Page.TapAsync(".navbar-toggler");
        await Expect(myTasks).ToBeVisibleAsync();
        await ShotAsync("phone-menu-open");

        // Wide tables scroll inside their card, not the whole page
        await Page.GotoAsync($"{Base}/SampleRequests");
        await AssertNoSidewaysScrollAsync();
        await ShotAsync("phone-list");

        await Page.GotoAsync($"{Base}/SampleRequests/Details/2");
        await AssertNoSidewaysScrollAsync();
        await ShotAsync("phone-details");
    }
}
