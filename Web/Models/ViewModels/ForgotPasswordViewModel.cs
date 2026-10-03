using System.ComponentModel.DataAnnotations;

namespace NETRA.Web.Models.ViewModels;

public class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Enter the email address registered to your account.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [Display(Name = "Email address")]
    public string Email { get; set; } = string.Empty;
}