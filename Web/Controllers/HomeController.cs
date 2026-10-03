using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NETRA.Web.Models.Ui;

namespace NETRA.Web.Controllers;

[AllowAnonymous]
public class HomeController : Controller
{
    [HttpGet("/")]
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Dashboard");
        }

        return RedirectToAction("Login", "Account");
    }

    /// <summary>
    /// Global search entry point. Search indexing arrives with the search module;
    /// this renders an honest placeholder rather than silently returning nothing.
    /// </summary>
    [HttpGet("/search")]
    public IActionResult Search(string? q)
    {
        ViewData["Title"] = "Search";

        ViewData["SearchTerm"] = q;

        return View();
    }
}