using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NETRA.Web.Models.ViewModels;

namespace NETRA.Web.Controllers;

/// <summary>
/// Server-rendered error pages in the NETRA visual style.
/// Handles every status code via one route so status-code re-execution always resolves.
/// </summary>
[AllowAnonymous]
public class ErrorController : Controller
{
    // No HTTP verb constraint: status-code re-execution preserves the original method,
    // so a failed POST (for example a rejected anti-forgery token) must still render.
    [Route("/error/{code:int}")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Status(int code)
    {
        var (title, message) = Describe(code);

        // Preserve the original status so crawlers and monitoring see the real code.
        Response.StatusCode = code;

        var model = new ErrorViewModel
        {
            RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
            StatusCode = code,
            Title = title,
            Message = message
        };

        return View("Error", model);
    }

    private static (string Title, string Message) Describe(int code) => code switch
    {
        400 => ("Request rejected",
            "The request could not be processed. This can happen when a form has been open too long " +
            "or a security token is missing. Reload the page and try again."),

        401 => ("Sign-in required",
            "You must sign in to view this resource."),

        403 => ("Access denied",
            "Your account does not have permission to view this resource. " +
            "If you believe this is a mistake, contact your system administrator."),

        404 => ("Page not found",
            "The resource you requested does not exist or has been moved."),

        405 => ("Method not allowed",
            "That action is not available for this address."),

        500 => ("Something went wrong",
            "An unexpected error occurred while processing your request."),

        _ => ("Request failed",
            "The request could not be completed.")
    };
}