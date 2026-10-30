using Dapper;
using Divakar.GloveOps.Core.Data;
using Divakar.GloveOps.Core.Models;
using Microsoft.Data.SqlClient;

namespace Divakar.GloveOps.Tests;

/// <summary>Runs only when the DivakarGloveOps database is reachable (skipped in CI).</summary>
public sealed class DatabaseFactAttribute : FactAttribute
{
    public static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("GLOVEOPS_TEST_DB")
        ?? @"Server=DIVAKARLAPTOP\DEVELOPERMSSQL;Database=DivakarGloveOps;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=3";

    private static readonly Lazy<bool> Available = new(() =>
    {
        try
        {
            using var c = new SqlConnection(ConnectionString);
            c.Open();
            return c.ExecuteScalar<int>("SELECT COUNT(*) FROM sys.procedures WHERE name = 'usp_ApprovalTask_Act'") == 1;
        }
        catch { return false; }
    });

    public DatabaseFactAttribute()
    {
        if (!Available.Value) Skip = "DivakarGloveOps database not available (run database/00_run_all.sql).";
    }
}

/// <summary>
/// End-to-end checks of the stored-procedure approval engine.
/// Seed users: 1 anna (Sales), 3 kevin (SalesManager), 5 grace (QA), 6 ming (PM Tainan), 8 linh (PM Vietnam).
/// Product 1 is made in Vietnam (plant 3), product 7 in Tainan (plant 1).
/// </summary>
public sealed class WorkflowDatabaseTests : IDisposable
{
    private const int Anna = 1, Kevin = 3, Grace = 5, Ming = 6, Linh = 8;
    private readonly SampleRequestRepository _repo = new(new SqlConnectionFactory(DatabaseFactAttribute.ConnectionString));
    private readonly List<int> _created = [];

    private async Task<int> NewSubmittedRequestAsync(int productId, int qty)
    {
        var (id, _) = await _repo.CreateAsync(
            new SampleRequestInput(1, productId, "M", qty, DateTime.Today.AddDays(20), "[test]"), Anna);
        _created.Add(id);
        await _repo.SubmitAsync(id, Anna);
        return id;
    }

    private async Task<ApprovalTaskItem> PendingTaskAsync(int requestId) =>
        (await _repo.GetDetailAsync(requestId))!.Tasks.Single(t => t.Status == "Pending");

    [DatabaseFact]
    public async Task Small_request_needs_sales_manager_then_qa()
    {
        var id = await NewSubmittedRequestAsync(productId: 1, qty: 500);

        var t1 = await PendingTaskAsync(id);
        Assert.Equal((1, Kevin), (t1.StepNo, t1.ApproverId));
        Assert.Equal(RequestStatus.InApproval, (await _repo.ActAsync(t1.TaskId, Kevin, ApprovalAction.Approve, null)).FinalStatus);

        var t2 = await PendingTaskAsync(id);
        Assert.Equal((2, Grace), (t2.StepNo, t2.ApproverId));
        Assert.Equal(RequestStatus.Approved, (await _repo.ActAsync(t2.TaskId, Grace, ApprovalAction.Approve, "OK")).FinalStatus);

        var detail = await _repo.GetDetailAsync(id);
        Assert.Equal(["Created", "Submitted", "Approved", "Approved"], detail!.History.Select(h => h.Action));
    }

    [DatabaseFact]
    public async Task Large_request_adds_the_producing_plants_manager()
    {
        var id = await NewSubmittedRequestAsync(productId: 7, qty: 15_000);   // Tainan product

        await _repo.ActAsync((await PendingTaskAsync(id)).TaskId, Kevin, ApprovalAction.Approve, null);
        await _repo.ActAsync((await PendingTaskAsync(id)).TaskId, Grace, ApprovalAction.Approve, null);

        var t3 = await PendingTaskAsync(id);
        Assert.Equal((3, Ming), (t3.StepNo, t3.ApproverId));
        Assert.Equal(RequestStatus.Approved, (await _repo.ActAsync(t3.TaskId, Ming, ApprovalAction.Approve, null)).FinalStatus);
    }

    [DatabaseFact]
    public async Task Task_cannot_be_acted_on_twice_or_by_someone_else()
    {
        var id = await NewSubmittedRequestAsync(productId: 1, qty: 500);
        var task = await PendingTaskAsync(id);

        var notMine = await Assert.ThrowsAsync<BusinessRuleException>(() => _repo.ActAsync(task.TaskId, Linh, ApprovalAction.Approve, null));
        Assert.Contains("not pending for you", notMine.Message);

        await _repo.ActAsync(task.TaskId, Kevin, ApprovalAction.Approve, null);
        await Assert.ThrowsAsync<BusinessRuleException>(() => _repo.ActAsync(task.TaskId, Kevin, ApprovalAction.Approve, null));
    }

    [DatabaseFact]
    public async Task Send_back_needs_a_comment_and_returns_to_applicant_for_resubmit()
    {
        var id = await NewSubmittedRequestAsync(productId: 1, qty: 500);
        var task = await PendingTaskAsync(id);

        await Assert.ThrowsAsync<BusinessRuleException>(() => _repo.ActAsync(task.TaskId, Kevin, ApprovalAction.SendBack, " "));

        var result = await _repo.ActAsync(task.TaskId, Kevin, ApprovalAction.SendBack, "Confirm sizes");
        Assert.Equal(RequestStatus.SentBack, result.FinalStatus);

        await _repo.SubmitAsync(id, Anna);
        var detail = await _repo.GetDetailAsync(id);
        Assert.Equal(RequestStatus.InApproval, detail!.Header.Status);
        Assert.Equal("Resubmitted", detail.History.Last().Action);
        Assert.Equal(1, detail.Tasks.Single(t => t.Status == "Pending").StepNo);   // starts again at step 1
    }

    public void Dispose()
    {
        if (_created.Count == 0) return;
        using var c = new SqlConnection(DatabaseFactAttribute.ConnectionString);
        c.Execute("""
            DELETE dbo.IntegrationLog  WHERE RequestId IN @ids;
            DELETE dbo.ApprovalHistory WHERE RequestId IN @ids;
            DELETE dbo.ApprovalTask    WHERE RequestId IN @ids;
            DELETE dbo.SampleRequest   WHERE RequestId IN @ids;
            """, new { ids = _created });
    }
}
