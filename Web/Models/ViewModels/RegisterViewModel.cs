using System.ComponentModel.DataAnnotations;

namespace NETRA.Web.Models.ViewModels;

public class RegisterViewModel
{
    [Required(ErrorMessage = "Enter your first name.")]
    [MaxLength(100, ErrorMessage = "First name cannot exceed 100 characters.")]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your last name.")]
    [MaxLength(100, ErrorMessage = "Last name cannot exceed 100 characters.")]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose a username.")]
    [MaxLength(100, ErrorMessage = "Username cannot exceed 100 characters.")]
    [RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "Username may only contain letters, numbers, periods, dashes, and underscores.")]
    [Display(Name = "Username")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [MaxLength(256, ErrorMessage = "Email cannot exceed 256 characters.")]
    [Display(Name = "Email address")]
    public string Email { get; set; } = string.Empty;

    [MaxLength(50, ErrorMessage = "Badge number cannot exceed 50 characters.")]
    [Display(Name = "Badge number")]
    public string? BadgeNumber { get; set; }

    [Required(ErrorMessage = "Enter a password.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters long.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirm your password.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The password and confirmation password do not match.")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
