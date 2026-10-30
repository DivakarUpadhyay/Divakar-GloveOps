using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Divakar.GloveOps.Core.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Divakar.GloveOps.Web.Models;

public static class UserExtensions
{
    public static int UserId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

    public static string DisplayName(this ClaimsPrincipal user) => user.Identity?.Name ?? "";
    public static string RoleName(this ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Role) ?? "";
}

public class SampleRequestForm
{
    public int? RequestId { get; set; }
    public string? RequestNo { get; set; }

    [Display(Name = "Customer"), Range(1, int.MaxValue, ErrorMessage = "Select a customer.")]
    public int CustomerId { get; set; }

    [Display(Name = "Product"), Range(1, int.MaxValue, ErrorMessage = "Select a product.")]
    public int ProductId { get; set; }

    [Required] public string Size { get; set; } = "M";

    [Display(Name = "Quantity (boxes)"), Range(1, 100_000)]
    public int Qty { get; set; } = 100;

    [Display(Name = "Required date"), DataType(DataType.Date)]
    public DateTime RequiredDate { get; set; } = DateTime.Today.AddDays(14);

    [StringLength(500)] public string? Purpose { get; set; }

    public IEnumerable<SelectListItem> Customers { get; set; } = [];
    public IEnumerable<SelectListItem> Products { get; set; } = [];

    public SampleRequestInput ToInput() => new(CustomerId, ProductId, Size, Qty, RequiredDate, Purpose);
}

public class RequestListViewModel
{
    public IReadOnlyList<SampleRequestListItem> Items { get; init; } = [];
    public string? Status { get; init; }
    public string? Search { get; init; }
    public bool MineOnly { get; init; }
}

public record ActRequest(int TaskId, ApprovalAction Action, string? Comment);

public record AskRequest(string Question, string? ConversationId);

public class DelegationForm
{
    [Display(Name = "Delegate to"), Range(1, int.MaxValue, ErrorMessage = "Select a colleague.")]
    public int DelegateUserId { get; set; }

    [DataType(DataType.Date)] public DateTime FromDate { get; set; } = DateTime.Today;
    [DataType(DataType.Date)] public DateTime ToDate { get; set; } = DateTime.Today.AddDays(5);

    public IEnumerable<SelectListItem> Users { get; set; } = [];
}

public class ErrorViewModel
{
    public string? RequestId { get; set; }
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}

public static class StatusUi
{
    public static string Badge(string status) => status switch
    {
        RequestStatus.Draft => "text-bg-secondary",
        RequestStatus.InApproval or "Pending" => "text-bg-primary",
        RequestStatus.SentBack => "text-bg-warning",
        RequestStatus.Approved or "Sent" or "Received" => "text-bg-success",
        RequestStatus.Rejected or "Failed" or "Cancelled" => "text-bg-danger",
        _ => "text-bg-light"
    };

    public static string Label(string status) => status switch
    {
        RequestStatus.InApproval => "In approval",
        RequestStatus.SentBack => "Sent back",
        _ => status
    };
}

/// <summary>Previous/Next pager for list pages; RouteValues keeps the current filters.</summary>
public record PagerViewModel(int Page, int TotalPages, int TotalCount, string Action, Dictionary<string, string> RouteValues)
{
    public Dictionary<string, string> For(int page) => new(RouteValues) { ["page"] = page.ToString() };
}
