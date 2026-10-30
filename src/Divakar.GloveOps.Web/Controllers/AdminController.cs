using Divakar.GloveOps.Core.MasterData;
using Divakar.GloveOps.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Divakar.GloveOps.Web.Controllers;

/// <summary>Master data (customers, products) — EF Core, Admin role only.</summary>
[Authorize(Roles = "Admin")]
public class AdminController(MasterDataService masterData) : Controller
{
    // ---------- customers ----------

    public async Task<IActionResult> Customers(string? search, int page = 1)
    {
        ViewBag.Search = search;
        return View(await masterData.CustomersAsync(search, page));
    }

    public async Task<IActionResult> EditCustomer(int? id)
    {
        if (id is null) return View(new CustomerEntity());
        var customer = await masterData.CustomerAsync(id.Value);
        return customer is null ? NotFound() : View(customer);
    }

    [HttpPost]
    public async Task<IActionResult> EditCustomer(CustomerEntity input)
    {
        try
        {
            await masterData.SaveCustomerAsync(input);
            TempData["Ok"] = input.CustomerId == 0 ? $"Customer \"{input.Name.Trim()}\" added." : "Customer saved.";
            return RedirectToAction(nameof(Customers));
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddModelError("", ex.Message);
            return View(input);
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteCustomer(int id, byte[] rowVer)
    {
        try
        {
            await masterData.DeleteCustomerAsync(id, rowVer);
            TempData["Ok"] = "Customer deleted.";
        }
        catch (BusinessRuleException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Customers));
    }

    // ---------- products ----------

    public async Task<IActionResult> Products(string? search, string? material, int page = 1)
    {
        ViewBag.Search = search;
        ViewBag.Material = material;
        return View(await masterData.ProductsAsync(search, material, page));
    }

    public async Task<IActionResult> EditProduct(int? id)
    {
        ViewBag.Plants = await masterData.PlantsAsync();
        if (id is null) return View(new ProductEntity { Material = "NBR" });
        var product = await masterData.ProductAsync(id.Value);
        return product is null ? NotFound() : View(product);
    }

    [HttpPost]
    public async Task<IActionResult> EditProduct(ProductEntity input)
    {
        try
        {
            await masterData.SaveProductAsync(input);
            TempData["Ok"] = input.ProductId == 0 ? $"Product {input.Sku.Trim().ToUpperInvariant()} added." : "Product saved.";
            return RedirectToAction(nameof(Products));
        }
        catch (BusinessRuleException ex)
        {
            ModelState.AddModelError("", ex.Message);
            ViewBag.Plants = await masterData.PlantsAsync();
            return View(input);
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteProduct(int id, byte[] rowVer)
    {
        try
        {
            await masterData.DeleteProductAsync(id, rowVer);
            TempData["Ok"] = "Product deleted.";
        }
        catch (BusinessRuleException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Products));
    }
}
