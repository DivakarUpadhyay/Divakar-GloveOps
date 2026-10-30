using Divakar.GloveOps.Core.Data;
using Divakar.GloveOps.Core.Integrations;
using Divakar.GloveOps.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Divakar.GloveOps.Web.Controllers;

public class IntegrationsController(
    IIntegrationLogRepository log, IAiAssistant ai, IOptions<N8nOptions> n8n, ILogger<IntegrationsController> logger)
    : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.N8nConfigured = !string.IsNullOrWhiteSpace(n8n.Value.WebhookUrl);
        ViewBag.DifyConfigured = ai.IsConfigured;
        return View(await log.RecentAsync());
    }

    public IActionResult AskAi()
    {
        ViewBag.DifyConfigured = ai.IsConfigured;
        return View();
    }

    /// <summary>AJAX: the browser never sees the Dify key, the server calls Dify.</summary>
    [HttpPost]
    public async Task<IActionResult> Ask([FromBody] AskRequest body, CancellationToken ct)
    {
        if (!ai.IsConfigured) return BadRequest(new { error = "Dify is not configured (Dify:ApiKey)." });
        if (string.IsNullOrWhiteSpace(body.Question) || body.Question.Length > 1000)
            return BadRequest(new { error = "Ask a question of up to 1,000 characters." });

        try
        {
            var answer = await ai.AskAsync(body.Question.Trim(), body.ConversationId, User.DisplayName(), ct);
            return Json(new { answer = answer.Answer, conversationId = answer.ConversationId });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Dify call failed");
            return StatusCode(502, new { error = "The AI service did not respond. Try again shortly." });
        }
    }
}
