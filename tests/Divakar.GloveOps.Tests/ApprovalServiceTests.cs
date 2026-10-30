using System.Net;
using Divakar.GloveOps.Core.Data;
using Divakar.GloveOps.Core.Integrations;
using Divakar.GloveOps.Core.Models;
using Divakar.GloveOps.Core.Workflow;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Divakar.GloveOps.Tests;

public class ApprovalServiceTests
{
    private sealed class FakeRequests(string finalStatus, int qty = 500) : ISampleRequestRepository
    {
        public Task<ApprovalResult> ActAsync(int taskId, int userId, ApprovalAction action, string? comment) =>
            Task.FromResult(new ApprovalResult(7, finalStatus));

        public Task<SampleRequestDetail?> GetDetailAsync(int requestId) =>
            Task.FromResult<SampleRequestDetail?>(new SampleRequestDetail(
                new SampleRequestHeader
                {
                    RequestId = requestId, RequestNo = "SR-2026-00007", CustomerName = "Rhein Medical", Country = "Germany",
                    Sku = "NBR-CH-200", ProductName = "Chemo Nitrile", Material = "NBR", Size = "L", Qty = qty,
                    RequiredDate = DateTime.Today.AddDays(30), PlantName = "Binh Duong Plant", Status = finalStatus
                }, [], []));

        public Task<IReadOnlyList<SampleRequestListItem>> ListAsync(string? s, string? q, int? a) => throw new NotImplementedException();
        public Task<(int RequestId, string RequestNo)> CreateAsync(SampleRequestInput i, int a) => throw new NotImplementedException();
        public Task UpdateAsync(int id, SampleRequestInput i, int u) => throw new NotImplementedException();
        public Task SubmitAsync(int id, int u) => throw new NotImplementedException();
        public Task<IReadOnlyList<MyTaskItem>> MyTasksAsync(int u) => throw new NotImplementedException();
        public Task<DashboardData> DashboardAsync() => throw new NotImplementedException();
    }

    private sealed class RecordingNotifier : IWorkflowNotifier
    {
        public List<ApprovedRequestEvent> Events { get; } = [];
        public Task NotifyApprovedAsync(int requestId, ApprovedRequestEvent evt, CancellationToken ct = default)
        {
            Events.Add(evt);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Final_approval_notifies_n8n_with_request_details()
    {
        var notifier = new RecordingNotifier();
        var service = new ApprovalService(new FakeRequests(RequestStatus.Approved, qty: 12_000), notifier, TimeProvider.System);

        var result = await service.ActAsync(1, 8, "Linh Nguyen", ApprovalAction.Approve, "Capacity OK");

        Assert.Equal(RequestStatus.Approved, result.FinalStatus);
        var evt = Assert.Single(notifier.Events);
        Assert.Equal("SR-2026-00007", evt.RequestNo);
        Assert.Equal("High", evt.Priority);          // 12,000 boxes = large order
        Assert.Equal("Linh Nguyen", evt.ApprovedBy);
    }

    [Theory]
    [InlineData(RequestStatus.InApproval)]   // moved to next step only
    [InlineData(RequestStatus.Rejected)]
    [InlineData(RequestStatus.SentBack)]
    public async Task Non_final_outcomes_do_not_notify(string status)
    {
        var notifier = new RecordingNotifier();
        var service = new ApprovalService(new FakeRequests(status), notifier, TimeProvider.System);

        await service.ActAsync(1, 3, "Kevin", ApprovalAction.Approve, null);

        Assert.Empty(notifier.Events);
    }
}

public class N8nWorkflowNotifierTests
{
    private sealed class StubHandler(HttpStatusCode code) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent("{\"ok\":true}") });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("Connection refused");
    }

    private sealed class MemoryLog : IIntegrationLogRepository
    {
        public List<(string Channel, string Status, string? Detail)> Rows { get; } = [];
        public Task AddAsync(int? requestId, string channel, string status, string? detail)
        {
            Rows.Add((channel, status, detail));
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<IntegrationLogItem>> RecentAsync(int top = 100) => throw new NotImplementedException();
    }

    private static readonly ApprovedRequestEvent Evt = new("sample-request.approved", "SR-2026-00001", "Nordic Care", "Sweden",
        "NBR-EX-100", "Nitrile Exam", "NBR", "M", 200, DateTime.Today, "Binh Duong Plant", "Normal", "Grace");

    private static N8nWorkflowNotifier Create(HttpMessageHandler handler, MemoryLog log, string url = "http://n8n.local/webhook/x") =>
        new(new HttpClient(handler), Options.Create(new N8nOptions { WebhookUrl = url, SharedKey = "secret" }),
            log, NullLogger<N8nWorkflowNotifier>.Instance);

    [Fact]
    public async Task Skips_and_logs_when_webhook_not_configured()
    {
        var log = new MemoryLog();
        var handler = new StubHandler(HttpStatusCode.OK);

        await Create(handler, log, url: "").NotifyApprovedAsync(1, Evt);

        Assert.Null(handler.Last);
        Assert.Equal("Skipped", Assert.Single(log.Rows).Status);
    }

    [Fact]
    public async Task Sends_shared_key_and_logs_success()
    {
        var log = new MemoryLog();
        var handler = new StubHandler(HttpStatusCode.OK);

        await Create(handler, log).NotifyApprovedAsync(1, Evt);

        Assert.Equal("secret", handler.Last!.Headers.GetValues("X-GloveOps-Key").Single());
        Assert.Equal("Sent", Assert.Single(log.Rows).Status);
    }

    [Fact]
    public async Task Http_error_is_logged_not_thrown()
    {
        var log = new MemoryLog();
        await Create(new StubHandler(HttpStatusCode.InternalServerError), log).NotifyApprovedAsync(1, Evt);
        Assert.Equal("Failed", Assert.Single(log.Rows).Status);
    }

    [Fact]
    public async Task Unreachable_n8n_is_logged_not_thrown()
    {
        var log = new MemoryLog();
        await Create(new ThrowingHandler(), log).NotifyApprovedAsync(1, Evt);
        var row = Assert.Single(log.Rows);
        Assert.Equal("Failed", row.Status);
        Assert.Contains("Connection refused", row.Detail);
    }
}
