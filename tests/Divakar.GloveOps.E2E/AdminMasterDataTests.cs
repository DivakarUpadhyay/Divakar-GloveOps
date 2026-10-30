using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Divakar.GloveOps.E2E;

/// <summary>Master data pages (EF Core): role check, create/edit/delete, safe delete, duplicate SKU, concurrency.</summary>
public sealed class AdminMasterDataTests(GloveOpsAppFixture app) : E2ETestBase(app)
{
    [E2EFact]
    public async Task Only_admin_can_open_master_data()
    {
        Prefix = "A";
        await SignInAsync("anna");
        await Expect(Page.Locator("nav").GetByRole(AriaRole.Link, new() { Name = "Admin" })).ToHaveCountAsync(0);
        await Page.GotoAsync($"{Base}/Admin/Customers");
        await Expect(Page.Locator("h1")).ToHaveTextAsync("Access denied");
        await ShotAsync("access-denied");
    }

    [E2EFact]
    public async Task Admin_adds_a_customer_that_sales_can_use_immediately()
    {
        Prefix = "B";
        const string name = "E2E Glove Partners Ltd";
        await SignInAsync("divakar");
        await Page.Locator("nav").GetByRole(AriaRole.Link, new() { Name = "Admin" }).ClickAsync();
        await Expect(Page.Locator("#pager-info")).ToContainTextAsync("page 1 of");    // EF Core paging: COUNT + OFFSET/FETCH
        await ShotAsync("customers");

        // Create (EF Core INSERT)
        await Page.ClickAsync("#btn-new-customer");
        await Page.FillAsync("#Name", name);
        await Page.FillAsync("#Country", "Malaysia");
        await Page.ClickAsync("#btn-save");
        await Expect(Page.Locator(".alert-success")).ToContainTextAsync(name);

        // Search (EF Core LINQ → SQL LIKE)
        await Page.FillAsync("input[name=search]", "Malaysia");
        await Page.ClickAsync("button:has-text('Search')");
        await Expect(Page.Locator("#customers tbody tr")).ToHaveCountAsync(1);
        await Expect(Page.Locator("#customers")).ToContainTextAsync(name);
        await ShotAsync("customer-added");

        // Sales sees it straight away in the request form (read by Dapper)
        await SignInAsync("anna");
        await Page.GotoAsync($"{Base}/SampleRequests/Create");
        await Expect(Page.Locator("#CustomerId")).ToContainTextAsync($"{name} (Malaysia)");

        // Edit then delete (unused customer)
        await SignInAsync("divakar");
        await Page.GotoAsync($"{Base}/Admin/Customers?search=Malaysia");
        await Page.ClickAsync("#customers tbody tr >> text=Edit");
        await Page.FillAsync("#Country", "Singapore");
        await Page.ClickAsync("#btn-save");
        await Expect(Page.Locator(".alert-success")).ToContainTextAsync("Customer saved");

        await Page.GotoAsync($"{Base}/Admin/Customers?search=Singapore");
        await Page.ClickAsync("#customers tbody tr >> text=Edit");
        Page.Dialog += async (_, d) => await d.AcceptAsync();
        await Page.ClickAsync("#btn-delete");
        await Expect(Page.Locator(".alert-success")).ToContainTextAsync("Customer deleted");
    }

    [E2EFact]
    public async Task Customer_in_use_cannot_be_deleted()
    {
        Prefix = "C";
        await SignInAsync("divakar");
        await Page.GotoAsync($"{Base}/Admin/Customers?search=Rhein");
        await Page.ClickAsync("#customers tbody tr >> text=Edit");
        Page.Dialog += async (_, d) => await d.AcceptAsync();
        await Page.ClickAsync("#btn-delete");
        await Expect(Page.Locator(".alert-danger")).ToContainTextAsync("has sample requests and cannot be deleted");
        await ShotAsync("delete-blocked");
    }

    [E2EFact]
    public async Task Duplicate_sku_is_refused()
    {
        Prefix = "D";
        await SignInAsync("divakar");
        await Page.GotoAsync($"{Base}/Admin/EditProduct");
        await Page.FillAsync("#Sku", "nbr-ex-100");   // existing SKU, different case
        await Page.FillAsync("#Name", "Duplicate test");
        await Page.SelectOptionAsync("#Material", "NBR");
        await Page.FillAsync("#Category", "Medical");
        await Page.SelectOptionAsync("#PlantId", "1");
        await Page.ClickAsync("#btn-save");
        await Expect(Page.Locator(".alert-danger")).ToContainTextAsync("SKU NBR-EX-100 already exists");
        await ShotAsync("duplicate-sku");
    }

    [E2EFact]
    public async Task Two_admins_editing_the_same_product_the_second_save_is_refused()
    {
        Prefix = "E";
        await SignInAsync("divakar");
        var url = $"{Base}/Admin/EditProduct/6";   // TPE-FD-100

        // Two tabs open the same version
        var tabB = await Context.NewPageAsync();
        await Page.GotoAsync(url);
        await tabB.GotoAsync(url);

        await tabB.FillAsync("#Name", "TPE Food Glove, low cost (v2)");
        await tabB.ClickAsync("#btn-save");
        await Expect(tabB.Locator(".alert-success")).ToContainTextAsync("Product saved");

        await Page.FillAsync("#Name", "TPE Food Glove - stale edit");
        await Page.ClickAsync("#btn-save");
        await Expect(Page.Locator(".alert-danger")).ToContainTextAsync("Someone else changed");
        await ShotAsync("concurrency-conflict");

        await Page.GotoAsync($"{Base}/Admin/Products?search=TPE");
        await Expect(Page.Locator("#products")).ToContainTextAsync("(v2)");
        await tabB.CloseAsync();
    }
}
