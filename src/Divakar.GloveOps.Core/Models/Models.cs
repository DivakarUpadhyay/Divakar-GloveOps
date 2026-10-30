namespace Divakar.GloveOps.Core.Models;

public static class RequestStatus
{
    public const string Draft = "Draft";
    public const string InApproval = "InApproval";
    public const string SentBack = "SentBack";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";

    public static readonly string[] All = [Draft, InApproval, SentBack, Approved, Rejected];
}

public enum ApprovalAction { Approve, Reject, SendBack }

public record AppUser(int UserId, string UserName, string DisplayName, string Email, string Role, int? PlantId);
public record Customer(int CustomerId, string Name, string Country);
public record Product(int ProductId, string Sku, string Name, string Material, string Category, int PlantId);

public class SampleRequestListItem
{
    public int RequestId { get; init; }
    public string RequestNo { get; init; } = "";
    public string CustomerName { get; init; } = "";
    public string Country { get; init; } = "";
    public string Sku { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string Material { get; init; } = "";
    public string Size { get; init; } = "";
    public int Qty { get; init; }
    public DateTime RequiredDate { get; init; }
    public string Status { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public string ApplicantName { get; init; } = "";
    public string? CurrentStepName { get; init; }
}

public class SampleRequestHeader : SampleRequestListItem
{
    public int CustomerId { get; init; }
    public int ProductId { get; init; }
    public string Category { get; init; } = "";
    public string PlantName { get; init; } = "";
    public string? Purpose { get; init; }
    public int? CurrentStep { get; init; }
    public int ApplicantId { get; init; }
    public DateTime? SubmittedAt { get; init; }
    public DateTime? ClosedAt { get; init; }
}

public class ApprovalTaskItem
{
    public int TaskId { get; init; }
    public int StepNo { get; init; }
    public string StepName { get; init; } = "";
    public int ApproverId { get; init; }
    public string ApproverName { get; init; } = "";
    public int? DelegateId { get; init; }
    public string? DelegateName { get; init; }
    public string Status { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime? ActedAt { get; init; }

    public bool CanAct(int userId) => Status == "Pending" && (ApproverId == userId || DelegateId == userId);
}

public class ApprovalHistoryItem
{
    public int HistoryId { get; init; }
    public int? StepNo { get; init; }
    public string UserName { get; init; } = "";
    public string Action { get; init; } = "";
    public string? Comment { get; init; }
    public DateTime ActedAt { get; init; }
}

public record SampleRequestDetail(
    SampleRequestHeader Header,
    IReadOnlyList<ApprovalTaskItem> Tasks,
    IReadOnlyList<ApprovalHistoryItem> History);

public class MyTaskItem
{
    public int TaskId { get; init; }
    public int RequestId { get; init; }
    public string RequestNo { get; init; } = "";
    public string CustomerName { get; init; } = "";
    public string ProductName { get; init; } = "";
    public int Qty { get; init; }
    public DateTime RequiredDate { get; init; }
    public string StepName { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public bool IsDelegated { get; init; }
    public string ApproverName { get; init; } = "";
}

public record StatusCount(string Status, int Total);
public record MaterialBoxes(string Material, int Boxes);
public record CountryRequests(string Country, int Requests);

public record DashboardData(
    IReadOnlyList<StatusCount> ByStatus,
    decimal? AvgLeadHours,
    IReadOnlyList<MaterialBoxes> ByMaterial,
    IReadOnlyList<CountryRequests> TopCountries);

public record ApprovalResult(int RequestId, string FinalStatus);

public class IntegrationLogItem
{
    public int LogId { get; init; }
    public int? RequestId { get; init; }
    public string? RequestNo { get; init; }
    public string Channel { get; init; } = "";
    public string Status { get; init; } = "";
    public string? Detail { get; init; }
    public DateTime LoggedAt { get; init; }
}

/// <summary>Input for create/edit. Validated here so the rules are unit-testable.</summary>
public record SampleRequestInput(int CustomerId, int ProductId, string Size, int Qty, DateTime RequiredDate, string? Purpose);

/// <summary>A rule the database or service refused (shown to the user, not a crash).</summary>
public class BusinessRuleException(string message) : Exception(message);
