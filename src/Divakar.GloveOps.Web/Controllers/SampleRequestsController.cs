using Divakar.GloveOps.Core.Data;
using Divakar.GloveOps.Core.Models;
using Divakar.GloveOps.Core.Workflow;
using Divakar.GloveOps.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Divakar.GloveOps.Web.Controllers;

public class SampleRequestsController(ISampleRequestRepository requests, ILookupRepository lookups, TimeProvider clock)
    : Controller
{
    public async Task<IActionResult> Index(string? status, string? search, bool mine = false)
    {
        var items = await requests.ListAsync(status, search, mine ? User.UserId() : null);
        return View(new RequestListViewModel { Items = items, Status = status, Search = search, MineOnly = mine });
    }

    public async Task<IActionResult> Details(int id)
    {
        var detail = await requests.GetDetailAsync(id);
        return detail is null ? NotFound() : View(detail);
    }

    public async Task<IActionResult> Create() => View("Edit", await FillListsAsync(new SampleRequestForm()));

    [HttpPost]
    public async Task<IActionResult> Create(SampleRequestForm form, bool submit = false)
    {
        if (!Validate(form)) return View("Edit", await FillListsAsync(form));

        try
        {
            var (id, no) = await requests.CreateAsync(form.ToInput(), User.UserId());
            if (submit) await requests.SubmitAsync(id, User.UserId());
            TempData["Ok"] = submit ? $"{no} submitted for approval." : $"{no} saved as draft.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddModelError("", ex.Message);
            return View("Edit", await FillListsAsync(form));
        }
    }

    public async Task<IActionResult> Edit(int id)
    {
        var detail = await requests.GetDetailAsync(id);
        if (detail is null) return NotFound();
        var h = detail.Header;
        if (!RequestRules.CanEdit(h, User.UserId()))
        {
            TempData["Error"] = "Only the applicant can edit a Draft or Sent Back request.";
            return RedirectToAction(nameof(Details), new { id });
        }

        return View(await FillListsAsync(new SampleRequestForm
        {
            RequestId = h.RequestId, RequestNo = h.RequestNo, CustomerId = h.CustomerId, ProductId = h.ProductId,
            Size = h.Size, Qty = h.Qty, RequiredDate = h.RequiredDate, Purpose = h.Purpose
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, SampleRequestForm form, bool submit = false)
    {
        form.RequestId = id;
        if (!Validate(form)) return View(await FillListsAsync(form));

        try
        {
            await requests.UpdateAsync(id, form.ToInput(), User.UserId());
            if (submit) await requests.SubmitAsync(id, User.UserId());
            TempData["Ok"] = submit ? "Submitted for approval." : "Changes saved.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddModelError("", ex.Message);
            return View(await FillListsAsync(form));
        }
    }

    /// <summary>AJAX: submit a draft from the details page.</summary>
    [HttpPost]
    public async Task<IActionResult> Submit(int id)
    {
        try
        {
            await requests.SubmitAsync(id, User.UserId());
            return Json(new { ok = true });
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { ok = false, error = ex.Message });
        }
    }

    private bool Validate(SampleRequestForm form)
    {
        foreach (var error in RequestRules.Validate(form.ToInput(), clock.GetLocalNow().DateTime))
            ModelState.AddModelError("", error);
        return ModelState.IsValid;
    }

    private async Task<SampleRequestForm> FillListsAsync(SampleRequestForm form)
    {
        form.Customers = (await lookups.CustomersAsync())
            .Select(c => new SelectListItem($"{c.Name} ({c.Country})", c.CustomerId.ToString()));
        form.Products = (await lookups.ProductsAsync())
            .Select(p => new SelectListItem($"{p.Sku} - {p.Name}", p.ProductId.ToString()));
        return form;
    }
}
