using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NETRA.Application.Abstractions;
using NETRA.Application.Users;
using NETRA.Web.Authorization;
using NETRA.Web.Models.ViewModels.Users;

namespace NETRA.Web.Controllers;

/// <summary>
/// User Management (Administration). The controller binds view models, calls
/// <see cref="IUserManagementService"/> and translates the result into a message: it
/// holds no Identity rules, no role policy and no queries. Authorisation is enforced by
/// policy on every action, so hiding the sidebar link is a convenience, not the gate.
/// </summary>
[Authorize(Policy = NetraPolicies.UserManagementManage)]
public class UsersController : Controller
{
    private readonly IUserManagementService _users;
    private readonly IAuditActorAccessor _actor;

    public UsersController(IUserManagementService users, IAuditActorAccessor actor)
    {
        _users = users;
        _actor = actor;
    }

    [HttpGet("/Users")]
    public async Task<IActionResult> Index(
        UserListFilterViewModel filter, CancellationToken cancellationToken)
    {
        var roleOptions = await _users.GetAssignableRolesAsync(cancellationToken);
        var paged = await _users.ListAsync(
            filter.ToCriteria(), _actor.GetUserId(User), cancellationToken);

        return View(UsersIndexViewModel.Create(paged, filter, roleOptions));
    }

    [HttpGet("/Users/Details/{id:guid}")]
    public async Task<IActionResult> Details(
        [FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var detail = await _users.GetDetailAsync(id, _actor.GetUserId(User), cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        return View(UserDetailsViewModel.Create(detail));
    }

    [HttpGet("/Users/Create")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken) =>
        View(new CreateUserViewModel
        {
            RoleOptions = await _users.GetAssignableRolesAsync(cancellationToken)
        });

    [HttpPost("/Users/Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        CreateUserViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return await RerenderCreateAsync(model, cancellationToken);
        }

        var result = await _users.CreateAsync(
            model.ToInput(), _actor.GetUserId(User), cancellationToken);

        if (result.Succeeded && result.Value is { } id)
        {
            TempData["Netra.Success"] = $"Account created for {model.UserName}.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (!TryCarryOverFailures(result))
        {
            return RedirectToAction(nameof(Index));
        }

        return await RerenderCreateAsync(model, cancellationToken);
    }

    [HttpGet("/Users/Edit/{id:guid}")]
    public async Task<IActionResult> Edit([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var detail = await _users.GetDetailAsync(id, _actor.GetUserId(User), cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        return View(EditUserViewModel.FromDetail(
            detail, await _users.GetAssignableRolesAsync(cancellationToken)));
    }

    [HttpPost("/Users/Edit/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        // Form values bind ahead of route values, so the identifier is pinned to the route:
        // a posted "Id" field can neither retarget the edit nor bypass the model's own Id.
        [FromRoute] Guid id,
        EditUserViewModel model,
        CancellationToken cancellationToken)
    {
        // The route identifier wins: a forged form field cannot retarget the edit.
        model.Id = id;

        if (!ModelState.IsValid)
        {
            return await RerenderEditAsync(model, cancellationToken);
        }

        var result = await _users.EditAsync(model.ToInput(), _actor.GetUserId(User), cancellationToken);

        if (result.Succeeded)
        {
            TempData["Netra.Success"] = "Account updated.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (!TryCarryOverFailures(result))
        {
            return RedirectToAction(nameof(Index));
        }

        return await RerenderEditAsync(model, cancellationToken);
    }

    /// <summary>
    /// Account activation is a POST: no link can change an account's state. The identifier is
    /// pinned to the route so a posted "Id" field cannot aim the change at another account.
    /// </summary>
    [HttpPost("/Users/Activate/{id:guid}")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Activate(
        [FromRoute] Guid id, CancellationToken cancellationToken) => SetActiveAsync(id, isActive: true);

    [HttpPost("/Users/Deactivate/{id:guid}")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Deactivate(
        [FromRoute] Guid id, CancellationToken cancellationToken) => SetActiveAsync(id, isActive: false);

    private async Task<IActionResult> SetActiveAsync(Guid id, bool isActive)
    {
        var result = await _users.SetActiveAsync(
            id, isActive, _actor.GetUserId(User), HttpContext.RequestAborted);

        switch (result.Status)
        {
            case OperationStatus.NotFound:
                TempData["Netra.Error"] = FirstError(result);
                return RedirectToAction(nameof(Index));

            case OperationStatus.Forbidden:
            case OperationStatus.ValidationFailed:
                TempData["Netra.Error"] = FirstError(result);
                return RedirectToAction(nameof(Details), new { id });

            default:
                TempData["Netra.Success"] = isActive
                    ? "Account activated. The user can sign in again."
                    : "Account deactivated. Sign-in is now refused for this account.";
                return RedirectToAction(nameof(Details), new { id });
        }
    }

    private async Task<IActionResult> RerenderCreateAsync(
        CreateUserViewModel model, CancellationToken cancellationToken)
    {
        model.RoleOptions = await _users.GetAssignableRolesAsync(cancellationToken);
        return View("Create", model);
    }

    private async Task<IActionResult> RerenderEditAsync(
        EditUserViewModel model, CancellationToken cancellationToken)
    {
        model.RoleOptions = await _users.GetAssignableRolesAsync(cancellationToken);
        return View("Edit", model);
    }

    /// <summary>
    /// Copies service validation, conflict and refusal messages into <c>ModelState</c> so the
    /// form re-renders with the reason. Returns false when the failure is not form-level
    /// (missing account), in which case the caller redirects instead.
    /// </summary>
    private bool TryCarryOverFailures(OperationResult<Guid> result) =>
        TryCarryOverFailures(result.Status, result.Errors);

    private bool TryCarryOverFailures(OperationResult result) =>
        TryCarryOverFailures(result.Status, result.Errors);

    private bool TryCarryOverFailures(
        OperationStatus status, IReadOnlyDictionary<string, string[]> errors)
    {
        if (status == OperationStatus.NotFound)
        {
            return false;
        }

        foreach (var (field, messages) in errors)
        {
            foreach (var message in messages)
            {
                ModelState.AddModelError(field, message);
            }
        }

        return true;
    }

    private static string FirstError(OperationResult result) =>
        result.Errors.Values.SelectMany(e => e).FirstOrDefault()
        ?? "The account could not be updated.";
}
