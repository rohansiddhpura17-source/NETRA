using System.ComponentModel.DataAnnotations;

namespace NETRA.Web.Models.ViewModels;

public class LoginViewModel
{
    [Required(ErrorMessage = "Enter your username or email address.")]
    [Display(Name = "Username or email")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your password.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }
}