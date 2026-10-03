using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NETRA.Web.Controllers;

/// <summary>
/// Support: the NETRA help page. It documents only what this application actually does,
/// which is static, verified content — so the controller is a single authorised GET with
/// no service and no query.
/// </summary>
/// <remarks>
/// There is deliberately no support-request form. This deployment has no ticket entity, no
/// delivery provider and no configured contact address, so a form here would accept a
/// message and silently discard it. The page says so instead.
/// </remarks>
[Authorize]
public class SupportController : Controller
{
    [HttpGet("/support")]
    public IActionResult Index() => View();
}
