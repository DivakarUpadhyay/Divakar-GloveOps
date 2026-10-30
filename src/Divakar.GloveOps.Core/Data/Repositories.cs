using System.Data;
using Dapper;
using Divakar.GloveOps.Core.Models;
using Microsoft.Data.SqlClient;

namespace Divakar.GloveOps.Core.Data;

public interface IDbConnectionFactory
{
    IDbConnection Create();
}

public sealed class SqlConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public IDbConnection Create() => new SqlConnection(connectionString);
}

public interface ISampleRequestRepository
{
    Task<IReadOnlyList<SampleRequestListItem>> ListAsync(string? status, string? search, int? applicantId);
    Task<SampleRequestDetail?> GetDetailAsync(int requestId);
    Task<(int RequestId, string RequestNo)> CreateAsync(SampleRequestInput input, int applicantId);
    Task UpdateAsync(int requestId, SampleRequestInput input, int userId);
    Task SubmitAsync(int requestId, int userId);
    Task<ApprovalResult> ActAsync(int taskId, int userId, ApprovalAction action, string? comment);
    Task<IReadOnlyList<MyTaskItem>> MyTasksAsync(int userId);
    Task<DashboardData> DashboardAsync();
}

public interface ILookupRepository
{
    Task<IReadOnlyList<AppUser>> UsersAsync();
    Task<AppUser?> UserByNameAsync(string userName);
    Task<IReadOnlyList<Customer>> CustomersAsync();
    Task<IReadOnlyList<Product>> ProductsAsync();
    Task SetDelegationAsync(int userId, int delegateUserId, DateTime from, DateTime to);
}

public interface IIntegrationLogRepository
{
    Task AddAsync(int? requestId, string channel, string status, string? detail);
    Task<IReadOnlyList<IntegrationLogItem>> RecentAsync(int top = 100);
}

/// <summary>
/// Dapper over stored procedures. SQL THROW errors (50000+) are business rules,
/// so they become <see cref="BusinessRuleException"/> with the procedure's message.
/// </summary>
public abstract class DapperRepository(IDbConnectionFactory factory)
{
    protected async Task<T> RunAsync<T>(Func<IDbConnection, Task<T>> work)
    {
        using var conn = factory.Create();
        try
        {
            return await work(conn);
        }
        catch (SqlException ex) when (ex.Number >= 50000)
        {
            throw new BusinessRuleException(ex.Message);
        }
    }

    protected Task RunAsync(Func<IDbConnection, Task> work) =>
        RunAsync(async c => { await work(c); return true; });
}

public sealed class SampleRequestRepository(IDbConnectionFactory factory)
    : DapperRepository(factory), ISampleRequestRepository
{
    private const CommandType Sp = CommandType.StoredProcedure;

    public Task<IReadOnlyList<SampleRequestListItem>> ListAsync(string? status, string? search, int? applicantId) =>
        RunAsync(async c => (IReadOnlyList<SampleRequestListItem>)(await c.QueryAsync<SampleRequestListItem>(
            "dbo.usp_SampleRequest_List",
            new { Status = status, Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(), ApplicantId = applicantId },
            commandType: Sp)).AsList());

    public Task<SampleRequestDetail?> GetDetailAsync(int requestId) =>
        RunAsync(async c =>
        {
            using var grid = await c.QueryMultipleAsync("dbo.usp_SampleRequest_GetDetail", new { RequestId = requestId }, commandType: Sp);
            var header = await grid.ReadSingleOrDefaultAsync<SampleRequestHeader>();
            if (header is null) return null;
            var tasks = (await grid.ReadAsync<ApprovalTaskItem>()).AsList();
            var history = (await grid.ReadAsync<ApprovalHistoryItem>()).AsList();
            return new SampleRequestDetail(header, tasks, history);
        });

    public Task<(int RequestId, string RequestNo)> CreateAsync(SampleRequestInput i, int applicantId) =>
        RunAsync(async c =>
        {
            var p = new DynamicParameters(new
            {
                i.CustomerId, i.ProductId, i.Size, i.Qty, RequiredDate = i.RequiredDate.Date, i.Purpose, ApplicantId = applicantId
            });
            p.Add("@RequestId", dbType: DbType.Int32, direction: ParameterDirection.Output);
            p.Add("@RequestNo", dbType: DbType.AnsiString, size: 20, direction: ParameterDirection.Output);
            await c.ExecuteAsync("dbo.usp_SampleRequest_Create", p, commandType: Sp);
            return (p.Get<int>("@RequestId"), p.Get<string>("@RequestNo"));
        });

    public Task UpdateAsync(int requestId, SampleRequestInput i, int userId) =>
        RunAsync(c => c.ExecuteAsync("dbo.usp_SampleRequest_Update",
            new { RequestId = requestId, i.CustomerId, i.ProductId, i.Size, i.Qty, RequiredDate = i.RequiredDate.Date, i.Purpose, UserId = userId },
            commandType: Sp));

    public Task SubmitAsync(int requestId, int userId) =>
        RunAsync(c => c.ExecuteAsync("dbo.usp_SampleRequest_Submit", new { RequestId = requestId, UserId = userId }, commandType: Sp));

    public Task<ApprovalResult> ActAsync(int taskId, int userId, ApprovalAction action, string? comment) =>
        RunAsync(async c =>
        {
            var p = new DynamicParameters(new { TaskId = taskId, UserId = userId, Action = action.ToString(), Comment = comment });
            p.Add("@FinalStatus", dbType: DbType.AnsiString, size: 20, direction: ParameterDirection.Output);
            p.Add("@RequestId", dbType: DbType.Int32, direction: ParameterDirection.Output);
            await c.ExecuteAsync("dbo.usp_ApprovalTask_Act", p, commandType: Sp);
            return new ApprovalResult(p.Get<int>("@RequestId"), p.Get<string>("@FinalStatus"));
        });

    public Task<IReadOnlyList<MyTaskItem>> MyTasksAsync(int userId) =>
        RunAsync(async c => (IReadOnlyList<MyTaskItem>)(await c.QueryAsync<MyTaskItem>(
            "dbo.usp_MyTasks", new { UserId = userId }, commandType: Sp)).AsList());

    public Task<DashboardData> DashboardAsync() =>
        RunAsync(async c =>
        {
            using var grid = await c.QueryMultipleAsync("dbo.usp_Dashboard", commandType: Sp);
            var byStatus = (await grid.ReadAsync<StatusCount>()).AsList();
            var lead = await grid.ReadSingleAsync<decimal?>();
            var byMaterial = (await grid.ReadAsync<MaterialBoxes>()).AsList();
            var countries = (await grid.ReadAsync<CountryRequests>()).AsList();
            return new DashboardData(byStatus, lead, byMaterial, countries);
        });
}

public sealed class LookupRepository(IDbConnectionFactory factory) : DapperRepository(factory), ILookupRepository
{
    public Task<IReadOnlyList<AppUser>> UsersAsync() =>
        RunAsync(async c => (IReadOnlyList<AppUser>)(await c.QueryAsync<AppUser>(
            "SELECT UserId, UserName, DisplayName, Email, Role, PlantId FROM dbo.AppUser WHERE IsActive = 1 ORDER BY UserId")).AsList());

    public Task<AppUser?> UserByNameAsync(string userName) =>
        RunAsync(c => c.QuerySingleOrDefaultAsync<AppUser>(
            "SELECT UserId, UserName, DisplayName, Email, Role, PlantId FROM dbo.AppUser WHERE UserName = @userName AND IsActive = 1",
            new { userName }));

    public Task<IReadOnlyList<Customer>> CustomersAsync() =>
        RunAsync(async c => (IReadOnlyList<Customer>)(await c.QueryAsync<Customer>(
            "SELECT CustomerId, Name, Country FROM dbo.Customer ORDER BY Name")).AsList());

    public Task<IReadOnlyList<Product>> ProductsAsync() =>
        RunAsync(async c => (IReadOnlyList<Product>)(await c.QueryAsync<Product>(
            "SELECT ProductId, Sku, Name, Material, Category, PlantId FROM dbo.Product ORDER BY Sku")).AsList());

    public Task SetDelegationAsync(int userId, int delegateUserId, DateTime from, DateTime to) =>
        RunAsync(c => c.ExecuteAsync("dbo.usp_Delegation_Set",
            new { UserId = userId, DelegateUserId = delegateUserId, FromDate = from.Date, ToDate = to.Date },
            commandType: CommandType.StoredProcedure));
}

public sealed class IntegrationLogRepository(IDbConnectionFactory factory) : DapperRepository(factory), IIntegrationLogRepository
{
    public Task AddAsync(int? requestId, string channel, string status, string? detail) =>
        RunAsync(c => c.ExecuteAsync("dbo.usp_IntegrationLog_Add",
            new { RequestId = requestId, Channel = channel, Status = status, Detail = detail },
            commandType: CommandType.StoredProcedure));

    public Task<IReadOnlyList<IntegrationLogItem>> RecentAsync(int top = 100) =>
        RunAsync(async c => (IReadOnlyList<IntegrationLogItem>)(await c.QueryAsync<IntegrationLogItem>(
            """
            SELECT TOP (@top) l.LogId, l.RequestId, r.RequestNo, l.Channel, l.Status, l.Detail, l.LoggedAt
            FROM dbo.IntegrationLog l LEFT JOIN dbo.SampleRequest r ON r.RequestId = l.RequestId
            ORDER BY l.LogId DESC
            """, new { top })).AsList());
}
