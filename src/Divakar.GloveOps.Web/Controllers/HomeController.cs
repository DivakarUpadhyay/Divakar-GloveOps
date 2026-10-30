using System.Diagnostics;
using Divakar.GloveOps.Core.Data;
using Divakar.GloveOps.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Divakar.GloveOps.Web.Controllers;

public class HomeController(ISampleRequestRepository requests) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.MyTaskCount = (await requests.MyTasksAsync(User.UserId())).Count;
        return View(await requests.DashboardAsync());
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
