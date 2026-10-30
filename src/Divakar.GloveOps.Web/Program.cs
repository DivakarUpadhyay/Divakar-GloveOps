using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Divakar.GloveOps.Core.Data;
using Divakar.GloveOps.Core.Integrations;
using Divakar.GloveOps.Core.Workflow;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// ---- data (Dapper over stored procedures) ----
var connectionString = builder.Configuration.GetConnectionString("GloveOps")
    ?? throw new InvalidOperationException("ConnectionStrings:GloveOps is missing.");
builder.Services.AddSingleton<IDbConnectionFactory>(new SqlConnectionFactory(connectionString));
builder.Services.AddScoped<ISampleRequestRepository, SampleRequestRepository>();
builder.Services.AddScoped<ILookupRepository, LookupRepository>();
builder.Services.AddScoped<IIntegrationLogRepository, IntegrationLogRepository>();
builder.Services.AddScoped<ApprovalService>();
builder.Services.AddSingleton(TimeProvider.System);

// ---- integrations (n8n, Dify) ----
builder.Services.Configure<N8nOptions>(builder.Configuration.GetSection("N8n"));
builder.Services.Configure<DifyOptions>(builder.Configuration.GetSection("Dify"));
builder.Services.AddHttpClient<IWorkflowNotifier, N8nWorkflowNotifier>(c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient<IAiAssistant, DifyAssistant>(c => c.Timeout = TimeSpan.FromSeconds(60));

// ---- web ----
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.AccessDeniedPath = "/Account/Login";
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization(o =>
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddControllersWithViews(o =>
        o.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute()))
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// jQuery AJAX sends the anti-forgery token in this header
builder.Services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();   // the local demo runs on plain http://localhost:5080
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets().AllowAnonymous();   // login page needs CSS/JS before sign-in
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}")
   .WithStaticAssets();

// n8n calls this back after it has processed an approval (e.g. "plant notified").
app.MapPost("/api/integrations/n8n-callback", async (
        HttpRequest http, N8nCallback body, IOptions<N8nOptions> options, IIntegrationLogRepository log) =>
    {
        var expected = options.Value.SharedKey;
        var given = http.Headers["X-GloveOps-Key"].ToString();
        if (string.IsNullOrEmpty(expected) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(given)))
            return Results.Unauthorized();

        await log.AddAsync(null, "n8n-callback", "Received", $"{body.RequestNo}: {body.Status} — {body.Message}");
        return Results.Ok(new { ok = true });
    })
   .AllowAnonymous()
   .DisableAntiforgery();

app.Run();

public record N8nCallback(string RequestNo, string Status, string? Message);

public partial class Program;
