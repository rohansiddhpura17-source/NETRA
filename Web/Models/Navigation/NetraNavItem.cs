namespace NETRA.Web.Models.Navigation;

/// <summary>
/// A single navigation entry in the NETRA sidebar.
/// Presentation metadata only.
/// </summary>
public sealed class NetraNavItem
{
    public string Label { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public string Controller { get; init; } = "Placeholder";

    public NetraIcon Icon { get; init; } = NetraIcon.Default;

    /// <summary>Roles permitted to see this entry. Empty means all authenticated users.</summary>
    public IReadOnlyList<string> AllowedRoles { get; init; } = Array.Empty<string>();

    public bool IsActive(string? currentController, string? currentAction)
    {
        return string.Equals(Controller, currentController, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Action, currentAction, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsVisibleTo(IReadOnlyCollection<string> roles)
    {
        if (AllowedRoles.Count == 0)
        {
            return true;
        }

        return AllowedRoles.Any(allowed =>
            roles.Contains(allowed, StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>
/// A labelled group of navigation entries.
/// </summary>
public sealed class NetraNavSection
{
    public string Label { get; init; } = string.Empty;

    public IReadOnlyList<NetraNavItem> Items { get; init; } = Array.Empty<NetraNavItem>();
}