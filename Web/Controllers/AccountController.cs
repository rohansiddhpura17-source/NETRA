using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using NETRA.Domain.Security;
using NETRA.Web.Models.ViewModels;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace NETRA.Web.Controllers;

[Route("Account")]
public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ILogger<AccountController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
    }

    [HttpGet("Login")]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Dashboard");
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost("Login")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.FindByNameAsync(model.UserName);

        // The password is verified before the account's activation state is consulted, so
        // "this account is not active" is only ever said to someone who knows the password:
        // the page cannot be used to enumerate accounts. CheckPasswordSignInAsync performs
        // the verification and the lockout bookkeeping without creating a session.
        var result = user is null
            ? SignInResult.Failed
            : await _signInManager.CheckPasswordSignInAsync(user, model.Password, lockoutOnFailure: true);

        if (result.Succeeded && user is { IsActive: false })
        {
            _logger.LogWarning("Sign-in refused for a deactivated account: {UserName}", model.UserName);
            ModelState.AddModelError(
                string.Empty,
                "This account is not active. Contact your administrator.");

            return View(model);
        }

        if (result.Succeeded)
        {
            await _signInManager.SignInAsync(user!, model.RememberMe);

            _logger.LogInformation("User signed in: {UserName}", model.UserName);

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return LocalRedirect(model.ReturnUrl);
            }

            return RedirectToAction("Index", "Dashboard");
        }

        if (result.IsLockedOut)
        {
            _logger.LogWarning("Account locked out for user: {UserName}", model.UserName);
            ModelState.AddModelError(
                string.Empty,
                "This account is temporarily locked. Try again later or contact your administrator.");
        }
        else if (result.IsNotAllowed)
        {
            ModelState.AddModelError(
                string.Empty,
                "Sign-in is not allowed for this account.");
        }
        else
        {
            // Deliberately generic: do not disclose whether the account exists.
            ModelState.AddModelError(
                string.Empty,
                "Invalid username or password.");
        }

        return View(model);
    }

    [HttpPost("Logout")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var userName = User.Identity?.Name;
        await _signInManager.SignOutAsync();
        _logger.LogInformation("User signed out: {UserName}", userName);

        return RedirectToAction(nameof(Login));
    }

    [HttpGet("ForgotPassword")]
    [AllowAnonymous]
    public IActionResult ForgotPassword() => View(new ForgotPasswordViewModel());

    [HttpPost("ForgotPassword")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);

        if (user != null && await _userManager.IsEmailConfirmedAsync(user))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            _logger.LogInformation(
                "Password reset requested for user {UserId}. A delivery provider must be " +
                "configured to email the reset link. Reset token length: {TokenLength}",
                user.Id,
                encodedToken.Length);
        }

        // Same response regardless of whether the address exists, so this endpoint
        // cannot be used to enumerate accounts.
        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [HttpGet("ForgotPasswordConfirmation")]
    [AllowAnonymous]
    public IActionResult ForgotPasswordConfirmation() => View();
}