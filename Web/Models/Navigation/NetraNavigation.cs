namespace NETRA.Web.Models.Navigation;

/// <summary>
/// The NETRA sidebar catalogue. Mirrors the NETRA Figma navigation structure.
/// Modules that are not implemented yet resolve to the placeholder controller,
/// which renders an honest "not yet implemented" page rather than a dead link.
/// </summary>
public static class NetraNavigation
{
    private const string AdminRole = "System Administrator";
    private const string AnalystRole = "Police / Intelligence Analyst";
    private const string InvestigatorRole = "Investigator";
    private const string SeniorOfficerRole = "Senior Officer";

    private static readonly string[] AdministrationRoles =
        { AdminRole };

    private static readonly string[] SeniorRoles =
        { AdminRole, SeniorOfficerRole, AnalystRole };

    private static readonly string[] ImageInvestigatorRoles =
        { AdminRole, InvestigatorRole, AnalystRole };

    public static IReadOnlyList<NetraNavSection> Sections { get; } =
        new List<NetraNavSection>
        {
            new()
            {
                Label = "Intelligence",
                Items = new List<NetraNavItem>
                {
                    new() { Label = "Dashboard", Controller = "Dashboard", Action = "Index", Icon = NetraIcon.Dashboard },
                    new() { Label = "Crime Map", Controller = "Map", Action = "Index", Icon = NetraIcon.Map },
                    new() { Label = "Crime Analytics", Controller = "Analytics", Action = "Index", Icon = NetraIcon.Analytics, AllowedRoles = SeniorRoles },
                    new() { Label = "Risk & Anomaly Analysis", Controller = "Risk", Action = "Index", Icon = NetraIcon.Alert, AllowedRoles = SeniorRoles },
                }
            },
            new()
            {
                Label = "Operations",
                Items = new List<NetraNavItem>
                {
                    new() { Label = "Crime Cases", Controller = "Cases", Action = "Index", Icon = NetraIcon.Cases },
                    // The workspace is per-case (/Investigations/Details/{id}) and is reached from
                    // case details, so this entry stays a placeholder until a workspace index exists.
                    new() { Label = "Investigations", Controller = "Placeholder", Action = "Investigations", Icon = NetraIcon.Investigation },
                    new() { Label = "Persons & Suspects", Controller = "Placeholder", Action = "Persons", Icon = NetraIcon.Persons },
                    new() { Label = "Evidence", Controller = "Placeholder", Action = "Evidence", Icon = NetraIcon.Evidence },
                }
            },
            new()
            {
                Label = "Analysis",
                Items = new List<NetraNavItem>
                {
                    new() { Label = "Network & Link Analysis", Controller = "Network", Action = "Index", Icon = NetraIcon.Network, AllowedRoles = SeniorRoles },
                    new() { Label = "Image Investigation", Controller = "ImageInvestigation", Action = "Index", Icon = NetraIcon.Image, AllowedRoles = ImageInvestigatorRoles },
                }
            },
            new()
            {
                Label = "Intelligence",
                Items = new List<NetraNavItem>
                {
                    new() { Label = "AI Assistant", Controller = "Assistant", Action = "Index", Icon = NetraIcon.Assistant, AllowedRoles = SeniorRoles },
                    new() { Label = "Alerts", Controller = "Alerts", Action = "Index", Icon = NetraIcon.Bell },
                    new() { Label = "Reports", Controller = "Reports", Action = "Index", Icon = NetraIcon.Report },
                }
            },
            new()
            {
                Label = "Data",
                Items = new List<NetraNavItem>
                {
                    new() { Label = "Bulk Data Import", Controller = "BulkImport", Action = "Index", Icon = NetraIcon.Import, AllowedRoles = SeniorRoles },
                }
            },
            new()
            {
                Label = "Administration",
                Items = new List<NetraNavItem>
                {
                    new() { Label = "User Management", Controller = "Users", Action = "Index", Icon = NetraIcon.Users, AllowedRoles = AdministrationRoles },
                    new() { Label = "Audit Logs", Controller = "AuditLogs", Action = "Index", Icon = NetraIcon.Audit, AllowedRoles = SeniorRoles },
                }
            },
            new()
            {
                Label = "Settings",
                Items = new List<NetraNavItem>
                {
                    new() { Label = "Account", Controller = "Settings", Action = "Account", Icon = NetraIcon.User },
                    new() { Label = "Security", Controller = "Settings", Action = "Security", Icon = NetraIcon.Shield },
                    new() { Label = "Notifications", Controller = "Settings", Action = "Notifications", Icon = NetraIcon.Bell },
                    new() { Label = "Appearance", Controller = "Settings", Action = "Appearance", Icon = NetraIcon.Appearance },
                    new() { Label = "System Preferences", Controller = "Settings", Action = "SystemPreferences", Icon = NetraIcon.Sliders, AllowedRoles = AdministrationRoles },
                }
            },
            new()
            {
                Label = "Support",
                Items = new List<NetraNavItem>
                {
                    new() { Label = "Support", Controller = "Support", Action = "Index", Icon = NetraIcon.Support },
                }
            },
        };

    /// <summary>Navigation entries pinned to the bottom of the sidebar.</summary>
    public static IReadOnlyList<NetraNavItem> FooterItems { get; } =
        new List<NetraNavItem>
        {
            new() { Label = "Settings", Controller = "Settings", Action = "Index", Icon = NetraIcon.Settings },
            new() { Label = "Support", Controller = "Support", Action = "Index", Icon = NetraIcon.Support },
        };

    /// <summary>
    /// Finds the canonical route for a placeholder action so the placeholder page
    /// can report which module it represents.
    /// </summary>
    public static NetraNavItem? Find(string action)
    {
        return Sections
            .SelectMany(section => section.Items)
            .Concat(FooterItems)
            .FirstOrDefault(item => string.Equals(item.Action, action, StringComparison.OrdinalIgnoreCase));
    }

    public static string DisplayNameFor(string action)
    {
        return Find(action)?.Label ?? "Module";
    }

    public static NetraIcon IconFor(string action)
    {
        return Find(action)?.Icon ?? NetraIcon.Default;
    }

    /// <summary>Investigator tooling is limited to these roles.</summary>
    public static bool CanUseInvestigatorTools(IReadOnlyCollection<string> roles)
    {
        return roles.Any(role =>
            role.Equals(InvestigatorRole, StringComparison.OrdinalIgnoreCase)
            || role.Equals(SeniorOfficerRole, StringComparison.OrdinalIgnoreCase)
            || role.Equals(AdminRole, StringComparison.OrdinalIgnoreCase)
            || role.Equals(AnalystRole, StringComparison.OrdinalIgnoreCase));
    }
}
