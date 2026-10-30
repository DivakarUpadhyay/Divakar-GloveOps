using System.Text.Json;
using System.Text.RegularExpressions;
using static Microsoft.Playwright.Assertions;

namespace Divakar.GloveOps.E2E;

/// <summary>
/// E2E opt-in, and additionally only when a Dify key is set in the web app's user secrets.
/// Each run spends a few Dify message credits.
/// </summary>
public sealed class DifyFactAttribute : FactAttribute
{
    private const string UserSecretsId = "319f3826-d04f-461b-9338-65177ad86be2";   // Divakar.GloveOps.Web.csproj

    public DifyFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("GLOVEOPS_E2E") != "1")
            Skip = "End-to-end tests are opt-in: run run-e2e.bat (GLOVEOPS_E2E=1).";
        else if (!HasDifyKey())
            Skip = "Dify is not configured: dotnet user-secrets set \"Dify:ApiKey\" \"app-...\" --project src/Divakar.GloveOps.Web";
    }

    private static bool HasDifyKey()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "UserSecrets", UserSecretsId, "secrets.json");
        if (!File.Exists(path)) return false;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.TryGetProperty("Dify:ApiKey", out var key) && key.GetString()?.StartsWith("app-") == true;
    }
}

/// <summary>Ask AI page → GloveOps server → Dify "Glove Advisor" (RAG over the product spec sheets).</summary>
public sealed class AskAiTests(GloveOpsAppFixture app) : E2ETestBase(app)
{
    private async Task<string> AskAsync(string question)
    {
        var before = await Page.Locator(".chat .msg.bot").CountAsync();
        await Page.FillAsync("#question", question);
        await Page.ClickAsync("#ask-form button");
        var answer = Page.Locator(".chat .msg.bot").Nth(before);
        await Expect(answer).Not.ToHaveTextAsync("Thinking…", new() { Timeout = 90_000 });
        return await answer.InnerTextAsync();
    }

    [DifyFact]
    public async Task Glove_advisor_answers_from_the_spec_sheets_in_english_and_chinese()
    {
        Prefix = "F";
        await SignInAsync("anna");
        await Page.GetByRole(Microsoft.Playwright.AriaRole.Link, new() { Name = "Ask AI" }).ClickAsync();
        await Expect(Page.Locator(".alert-warning")).ToHaveCountAsync(0);   // configured

        var chemo = await AskAsync("Which glove is suitable for handling chemotherapy drugs?");
        Assert.Contains("NBR-CH-200", chemo);
        Assert.Contains("D6978", chemo);
        await ShotAsync("ask-ai-english");

        // Follow-up in the same conversation (conversation_id is kept by the page)
        var followUp = await AskAsync("What is its cuff length?");
        Assert.Contains("300", followUp);

        var cleanroom = await AskAsync("哪一款手套適合半導體無塵室？");
        Assert.Contains("CR-NBR-100", cleanroom);
        Assert.Matches(new Regex(@"\p{IsCJKUnifiedIdeographs}"), cleanroom);   // answered in Chinese
        await ShotAsync("ask-ai-chinese");

        // Not in the documents → no made-up answer
        var price = await AskAsync("What is the price per box of NBR-EX-100?");
        Assert.Contains("don't have", price.Replace('’', '\''), StringComparison.OrdinalIgnoreCase);
        await ShotAsync("ask-ai-no-hallucination");
    }
}
