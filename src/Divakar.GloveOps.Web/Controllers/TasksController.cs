using Divakar.GloveOps.Core.Data;
using Divakar.GloveOps.Core.Models;
using Divakar.GloveOps.Core.Workflow;
using Divakar.GloveOps.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Divakar.GloveOps.Web.Controllers;

public class TasksController(ISampleRequestRepository requests, ILookupRepository lookups, ApprovalService approvals)
    : Controller
{
    /// <summary>My Tasks inbox: tasks assigned to me or delegated to me.</summary>
    public async Task<IActionResult> Index() => View(await requests.MyTasksAsync(User.UserId()));

    /// <summary>AJAX from the details page: Approve / Reject / SendBack.</summary>
    [HttpPost]
    public async Task<IActionResult> Act([FromBody] ActRequest body)
    {
        try
        {
            var result = await approvals.ActAsync(body.TaskId, User.UserId(), User.DisplayName(), body.Action, body.Comment);
            return Json(new { ok = true, status = result.FinalStatus });
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { ok = false, error = ex.Message });
        }
    }

    public async Task<IActionResult> Delegation() => View(await FillUsersAsync(new DelegationForm()));

    [HttpPost]
    public async Task<IActionResult> Delegation(DelegationForm form)
    {
        if (form.ToDate < form.FromDate) ModelState.AddModelError(nameof(form.ToDate), "End date must be on or after the start date.");
        if (form.DelegateUserId == User.UserId()) ModelState.AddModelError(nameof(form.DelegateUserId), "You cannot delegate to yourself.");
        if (!ModelState.IsValid) return View(await FillUsersAsync(form));

        try
        {
            await lookups.SetDelegationAsync(User.UserId(), form.DelegateUserId, form.FromDate, form.ToDate);
            TempData["Ok"] = "Delegation saved. Pending and new tasks will also go to your delegate.";
            return RedirectToAction(nameof(Index));
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddModelError("", ex.Message);
            return View(await FillUsersAsync(form));
        }
    }

    private async Task<DelegationForm> FillUsersAsync(DelegationForm form)
    {
        var me = User.UserId();
        form.Users = (await lookups.UsersAsync())
            .Where(u => u.UserId != me)
            .Select(u => new SelectListItem(u.DisplayName, u.UserId.ToString()));
        return form;
    }
}
