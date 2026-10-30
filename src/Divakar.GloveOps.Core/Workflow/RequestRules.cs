using Divakar.GloveOps.Core.Models;

namespace Divakar.GloveOps.Core.Workflow;

/// <summary>Business rules kept in C# so they can be unit-tested and reused by the UI.</summary>
public static class RequestRules
{
    /// <summary>Requests of this size need the plant capacity step (matches FlowStep.MinQty).</summary>
    public const int LargeOrderBoxes = 10_000;
    public const int MaxBoxes = 100_000;
    public static readonly string[] Sizes = ["XS", "S", "M", "L", "XL"];

    public static bool IsLargeOrder(int qty) => qty >= LargeOrderBoxes;

    public static string Priority(int qty, DateTime requiredDate, DateTime today)
    {
        var daysLeft = (requiredDate.Date - today.Date).TotalDays;
        if (IsLargeOrder(qty) || daysLeft <= 7) return "High";
        return daysLeft <= 21 ? "Normal" : "Low";
    }

    public static IReadOnlyList<string> Validate(SampleRequestInput input, DateTime today)
    {
        var errors = new List<string>();
        if (input.CustomerId <= 0) errors.Add("Select a customer.");
        if (input.ProductId <= 0) errors.Add("Select a product.");
        if (!Sizes.Contains(input.Size)) errors.Add("Select a valid size (XS–XL).");
        if (input.Qty <= 0) errors.Add("Quantity must be greater than zero.");
        if (input.Qty > MaxBoxes) errors.Add($"Quantity cannot exceed {MaxBoxes:N0} boxes for a sample request.");
        if (input.RequiredDate.Date < today.Date.AddDays(3)) errors.Add("Required date must be at least 3 days from today.");
        if (input.Purpose?.Length > 500) errors.Add("Purpose must be 500 characters or fewer.");
        return errors;
    }

    public static bool CanEdit(SampleRequestHeader r, int userId) =>
        r.ApplicantId == userId && (r.Status == RequestStatus.Draft || r.Status == RequestStatus.SentBack);
}
