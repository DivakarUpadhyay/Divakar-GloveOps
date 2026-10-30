using Divakar.GloveOps.Core.Data;
using Divakar.GloveOps.Core.Integrations;
using Divakar.GloveOps.Core.Models;

namespace Divakar.GloveOps.Core.Workflow;

/// <summary>
/// Orchestrates an approval: the stored procedure does the state change in one
/// transaction, then — only when the whole flow is finished — n8n is notified.
/// </summary>
public sealed class ApprovalService(ISampleRequestRepository requests, IWorkflowNotifier notifier, TimeProvider clock)
{
    public async Task<ApprovalResult> ActAsync(int taskId, int userId, string userName, ApprovalAction action, string? comment)
    {
        var result = await requests.ActAsync(taskId, userId, action, comment);

        if (result.FinalStatus == RequestStatus.Approved)
        {
            var detail = await requests.GetDetailAsync(result.RequestId);
            if (detail is not null)
            {
                var h = detail.Header;
                var evt = new ApprovedRequestEvent(
                    "sample-request.approved", h.RequestNo, h.CustomerName, h.Country, h.Sku, h.ProductName,
                    h.Material, h.Size, h.Qty, h.RequiredDate, h.PlantName,
                    RequestRules.Priority(h.Qty, h.RequiredDate, clock.GetLocalNow().DateTime), userName);
                await notifier.NotifyApprovedAsync(result.RequestId, evt);
            }
        }

        return result;
    }
}
