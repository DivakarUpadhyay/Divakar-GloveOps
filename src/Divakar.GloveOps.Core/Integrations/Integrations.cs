using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Divakar.GloveOps.Core.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Divakar.GloveOps.Core.Integrations;

public sealed class N8nOptions
{
    /// <summary>n8n Webhook node URL, e.g. http://localhost:5678/webhook/gloveops-approved. Empty = disabled.</summary>
    public string WebhookUrl { get; set; } = "";
    /// <summary>Shared secret sent as X-GloveOps-Key, and expected on n8n's callback.</summary>
    public string SharedKey { get; set; } = "";
}

public sealed class DifyOptions
{
    public string BaseUrl { get; set; } = "https://api.dify.ai/v1";
    /// <summary>Dify app API key (app-...). Keep it in user secrets, never in Git.</summary>
    public string ApiKey { get; set; } = "";
}

public record ApprovedRequestEvent(
    string Event, string RequestNo, string Customer, string Country, string Sku, string Product,
    string Material, string Size, int Qty, DateTime RequiredDate, string Plant, string Priority, string ApprovedBy);

public interface IWorkflowNotifier
{
    Task NotifyApprovedAsync(int requestId, ApprovedRequestEvent evt, CancellationToken ct = default);
}

/// <summary>
/// Sends the "request approved" event to an n8n webhook. A failure is logged,
/// never thrown: the approval is already committed and must not be undone by a notification.
/// </summary>
public sealed class N8nWorkflowNotifier(
    HttpClient http, IOptions<N8nOptions> options, IIntegrationLogRepository log, ILogger<N8nWorkflowNotifier> logger)
    : IWorkflowNotifier
{
    public const string Channel = "n8n-webhook";

    public async Task NotifyApprovedAsync(int requestId, ApprovedRequestEvent evt, CancellationToken ct = default)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.WebhookUrl))
        {
            await log.AddAsync(requestId, Channel, "Skipped", "N8n:WebhookUrl is not configured.");
            return;
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, o.WebhookUrl) { Content = JsonContent.Create(evt) };
            if (!string.IsNullOrEmpty(o.SharedKey)) req.Headers.Add("X-GloveOps-Key", o.SharedKey);

            using var res = await http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);
            await log.AddAsync(requestId, Channel, res.IsSuccessStatusCode ? "Sent" : "Failed",
                $"HTTP {(int)res.StatusCode}: {Truncate(body, 500)}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "n8n webhook failed for request {RequestId}", requestId);
            await log.AddAsync(requestId, Channel, "Failed", ex.Message);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}

public record AiAnswer(string Answer, string? ConversationId);

public interface IAiAssistant
{
    bool IsConfigured { get; }
    Task<AiAnswer> AskAsync(string question, string? conversationId, string user, CancellationToken ct = default);
}

/// <summary>Calls a Dify chat app: POST {BaseUrl}/chat-messages (blocking mode).</summary>
public sealed class DifyAssistant(HttpClient http, IOptions<DifyOptions> options) : IAiAssistant
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.ApiKey);

    public async Task<AiAnswer> AskAsync(string question, string? conversationId, string user, CancellationToken ct = default)
    {
        var o = options.Value;
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{o.BaseUrl.TrimEnd('/')}/chat-messages")
        {
            Content = JsonContent.Create(new
            {
                inputs = new { },
                query = question,
                response_mode = "blocking",
                conversation_id = conversationId ?? "",
                user
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);

        using var res = await http.SendAsync(req, ct);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (!res.IsSuccessStatusCode)
        {
            var msg = json.TryGetProperty("message", out var m) ? m.GetString() : res.ReasonPhrase;
            throw new HttpRequestException($"Dify returned {(int)res.StatusCode}: {msg}");
        }

        return new AiAnswer(
            json.GetProperty("answer").GetString() ?? "",
            json.TryGetProperty("conversation_id", out var cid) ? cid.GetString() : null);
    }
}
