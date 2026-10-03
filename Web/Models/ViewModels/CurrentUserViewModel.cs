namespace NETRA.Web.Models.ViewModels;

public class CurrentUserViewModel
{
    public Guid Id { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? BadgeNumber { get; set; }

    public string? Rank { get; set; }

    public string Initials { get; set; } = string.Empty;

    public string PrimaryRole { get; set; } = string.Empty;

    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
}