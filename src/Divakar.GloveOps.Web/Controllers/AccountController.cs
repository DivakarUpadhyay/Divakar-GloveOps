using System.Security.Claims;
using Divakar.GloveOps.Core.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Divakar.GloveOps.Web.Controllers;

/// <summary>
/// Demo sign-in: pick a user to see the flow from each role.
/// A real deployment would use Windows/AD or Entra ID sign-in instead.
/// </summary>
[AllowAnonymous]
public class AccountController(ILookupRepository lookups) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(await lookups.UsersAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Login(string userName, string? returnUrl)
    {
        var user = await lookups.UserByNameAsync(userName);
        if (user is null) return RedirectToAction(nameof(Login));

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role)
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl) : RedirectToAction("Index", "Home");
    }

    public IActionResult Denied() => View();

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }
}
