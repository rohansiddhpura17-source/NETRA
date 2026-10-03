using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NETRA.Application.Abstractions;
using NETRA.Application.AI;
using NETRA.Web.Authorization;
using NETRA.Web.Models.ViewModels.Assistant;

namespace NETRA.Web.Controllers;

/// <summary>
/// NETRA AI Assistant: server-rendered, JavaScript-free chat over authorized
/// NETRA data. The controller only maps the form to the service request and
/// renders the conversation; validation, intent handling, authorized querying
/// and provider fallback live behind <see cref="IAssistantService"/>.
/// </summary>
[Authorize(Policy = NetraPolicies.Assistant)]
public sealed class AssistantController : Controller
{
    private readonly IAssistantService _assistant;
    private readonly IAuditActorAccessor _actor;

    public AssistantController(IAssistantService assistant, IAuditActorAccessor actor)
    {
        _assistant = assistant;
        _actor = actor;
    }

    [HttpGet("/assistant")]
    public async Task<IActionResult> Index(Guid? session, CancellationToken cancellationToken)
    {
        var actorUserId = _actor.GetUserId(User);
        var sessions = await _assistant.GetRecentSessionsAsync(actorUserId, cancellationToken);

        AssistantSessionDto? active;
        if (session is { } sessionId)
        {
            active = await _assistant.GetOwnSessionAsync(sessionId, actorUserId, cancellationToken);
            if (active is null)
            {
                TempData["Netra.Error"] = "That conversation was not found. Start a new one below.";
                active = sessions.Count > 0
                    ? await _assistant.GetOwnSessionAsync(sessions[0].Id, actorUserId, cancellationToken)
                    : null;
            }
        }
        else
        {
            active = sessions.Count > 0
                ? await _assistant.GetOwnSessionAsync(sessions[0].Id, actorUserId, cancellationToken)
                : null;
        }

        return View(AssistantViewModel.Create(sessions, active));
    }

    [HttpPost("/assistant/Ask")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ask(
        // The form binds against AssistantViewModel, so its inputs are named "Ask.*";
        // this action must read them under the same prefix.
        [Bind(Prefix = "Ask")] AskAssistantViewModel input,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Question) ||
            input.Question.Trim().Length < AssistantLimits.MinQuestionLength)
        {
            ModelState.AddModelError("Ask." + nameof(AskAssistantViewModel.Question), "Enter a question to ask the assistant.");
        }

        if (!ModelState.IsValid)
        {
            return await RenderWithErrorAsync(input.SessionId, cancellationToken);
        }

        var result = await _assistant.AskAsync(new AssistantAskRequest
        {
            SessionId = input.SessionId,
            Question = input.Question
        }, _actor.GetUserId(User), cancellationToken);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                foreach (var message in error.Value)
                {
                    ModelState.AddModelError(
                        error.Key.Length == 0 ? string.Empty : $"Ask.{error.Key}", message);
                }
            }

            if (result.Status == OperationStatus.NotFound)
            {
                TempData["Netra.Error"] = result.Errors.Values.SelectMany(v => v).First();
                return RedirectToAction(nameof(Index));
            }

            return await RenderWithErrorAsync(input.SessionId, cancellationToken, preserveQuestion: input.Question);
        }

        return RedirectToAction(nameof(Index), new { session = result.Value!.Id });
    }

    [HttpPost("/assistant/New")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New(CancellationToken cancellationToken)
    {
        var result = await _assistant.StartSessionAsync(_actor.GetUserId(User), cancellationToken);

        if (!result.Succeeded)
        {
            TempData["Netra.Error"] = "The new conversation could not be started.";
            return RedirectToAction(nameof(Index));
        }

        return RedirectToAction(nameof(Index), new { session = result.Value!.Id });
    }

    private async Task<IActionResult> RenderWithErrorAsync(
        Guid? sessionId, CancellationToken cancellationToken, string? preserveQuestion = null)
    {
        var actorUserId = _actor.GetUserId(User);
        var sessions = await _assistant.GetRecentSessionsAsync(actorUserId, cancellationToken);
        var active = sessionId is { } id
            ? await _assistant.GetOwnSessionAsync(id, actorUserId, cancellationToken)
            : null;

        var model = AssistantViewModel.Create(sessions, active);
        model.Ask.Question = preserveQuestion;
        return View(nameof(Index), model);
    }
}
