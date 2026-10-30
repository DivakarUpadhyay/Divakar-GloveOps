using Divakar.GloveOps.Core.Models;
using Divakar.GloveOps.Core.Workflow;

namespace Divakar.GloveOps.Tests;

public class RequestRulesTests
{
    private static readonly DateTime Today = new(2026, 10, 30);

    private static SampleRequestInput Valid(int qty = 500, int daysAhead = 14) =>
        new(CustomerId: 1, ProductId: 1, Size: "M", Qty: qty, RequiredDate: Today.AddDays(daysAhead), Purpose: "Trial");

    [Theory]
    [InlineData(9_999, false)]
    [InlineData(10_000, true)]
    [InlineData(25_000, true)]
    public void IsLargeOrder_uses_the_plant_step_threshold(int qty, bool expected) =>
        Assert.Equal(expected, RequestRules.IsLargeOrder(qty));

    [Theory]
    [InlineData(12_000, 30, "High")]   // large order
    [InlineData(100, 5, "High")]       // urgent
    [InlineData(100, 14, "Normal")]
    [InlineData(100, 40, "Low")]
    public void Priority_depends_on_size_and_urgency(int qty, int daysAhead, string expected) =>
        Assert.Equal(expected, RequestRules.Priority(qty, Today.AddDays(daysAhead), Today));

    [Fact]
    public void Validate_accepts_a_normal_request() =>
        Assert.Empty(RequestRules.Validate(Valid(), Today));

    [Fact]
    public void Validate_reports_every_problem()
    {
        var input = new SampleRequestInput(0, 0, "XXL", 0, Today, new string('x', 501));

        var errors = RequestRules.Validate(input, Today);

        Assert.Equal(6, errors.Count);
        Assert.Contains(errors, e => e.Contains("customer"));
        Assert.Contains(errors, e => e.Contains("size"));
        Assert.Contains(errors, e => e.Contains("3 days"));
    }

    [Fact]
    public void Validate_caps_sample_quantity() =>
        Assert.Single(RequestRules.Validate(Valid(qty: RequestRules.MaxBoxes + 1), Today));

    [Theory]
    [InlineData(RequestStatus.Draft, 1, true)]
    [InlineData(RequestStatus.SentBack, 1, true)]
    [InlineData(RequestStatus.InApproval, 1, false)]
    [InlineData(RequestStatus.Draft, 2, false)]     // not the applicant
    public void CanEdit_only_applicant_and_only_draft_or_sent_back(string status, int userId, bool expected)
    {
        var header = new SampleRequestHeader { ApplicantId = 1, Status = status };
        Assert.Equal(expected, RequestRules.CanEdit(header, userId));
    }
}
