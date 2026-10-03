namespace NETRA.Web.Models.Navigation;

/// <summary>
/// Inline SVG icon paths for the NETRA shell. Stroke-based, 24x24 viewBox,
/// rendered server-side so the application ships no JavaScript and no icon font.
/// </summary>
public enum NetraIcon
{
    Default,
    Dashboard,
    Map,
    Analytics,
    Alert,
    Cases,
    Investigation,
    Persons,
    Evidence,
    Network,
    Image,
    Assistant,
    Report,
    Import,
    Users,
    Audit,
    Settings,
    Shield,
    Bell,
    Appearance,
    Sliders,
    Support,
    Search,
    Help,
    Menu,
    Lock,
    Mail,
    User,
    Key,
    Inbox,
    Info,
    Plus
}

public static class NetraIconPaths
{
    private const string WrapperOpen =
        "<svg class=\"netra-icon-svg\" viewBox=\"0 0 24 24\" width=\"16\" height=\"16\" " +
        "fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" " +
        "stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\">";

    private const string WrapperClose = "</svg>";

    public static string Render(NetraIcon icon)
    {
        return WrapperOpen + PathFor(icon) + WrapperClose;
    }

    private static string PathFor(NetraIcon icon) => icon switch
    {
        NetraIcon.Dashboard =>
            "<rect x=\"3\" y=\"3\" width=\"7\" height=\"8\" rx=\"1\"/>" +
            "<rect x=\"14\" y=\"3\" width=\"7\" height=\"5\" rx=\"1\"/>" +
            "<rect x=\"14\" y=\"11\" width=\"7\" height=\"10\" rx=\"1\"/>" +
            "<rect x=\"3\" y=\"14\" width=\"7\" height=\"7\" rx=\"1\"/>",

        NetraIcon.Map =>
            "<path d=\"M9 4 3 7v13l6-3 6 3 6-3V4l-6 3-6-3Z\"/>" +
            "<path d=\"M9 4v13M15 7v13\"/>",

        NetraIcon.Analytics =>
            "<path d=\"M4 20V10M10 20V4M16 20v-7M22 20H2\"/>",

        NetraIcon.Alert =>
            "<path d=\"M10.3 4.3 2.6 17.5A2 2 0 0 0 4.3 20.5h15.4a2 2 0 0 0 1.7-3L13.7 4.3a2 2 0 0 0-3.4 0Z\"/>" +
            "<path d=\"M12 9v4M12 16.5h.01\"/>",

        NetraIcon.Cases =>
            "<rect x=\"3\" y=\"7\" width=\"18\" height=\"13\" rx=\"2\"/>" +
            "<path d=\"M9 7V5a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2v2M3 12h18\"/>",

        NetraIcon.Investigation =>
            "<circle cx=\"11\" cy=\"11\" r=\"7\"/>" +
            "<path d=\"m20 20-3.5-3.5\"/>",

        NetraIcon.Persons =>
            "<path d=\"M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2\"/>" +
            "<circle cx=\"9\" cy=\"7\" r=\"4\"/>" +
            "<path d=\"M22 21v-2a4 4 0 0 0-3-3.87\"/>",

        NetraIcon.Evidence =>
            "<path d=\"M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8Z\"/>" +
            "<path d=\"M14 3v5h5M9 13h6M9 17h4\"/>",

        NetraIcon.Network =>
            "<circle cx=\"12\" cy=\"5\" r=\"2.5\"/>" +
            "<circle cx=\"5\" cy=\"18\" r=\"2.5\"/>" +
            "<circle cx=\"19\" cy=\"18\" r=\"2.5\"/>" +
            "<path d=\"M10.7 6.9 6.3 16M13.3 6.9l4.4 9.1M7.5 18h9\"/>",

        NetraIcon.Image =>
            "<rect x=\"3\" y=\"4\" width=\"18\" height=\"16\" rx=\"2\"/>" +
            "<circle cx=\"8.5\" cy=\"9.5\" r=\"1.5\"/>" +
            "<path d=\"m4 17 4.5-4.5 3 3L15 12l5 5\"/>",

        NetraIcon.Assistant =>
            "<rect x=\"4\" y=\"8\" width=\"16\" height=\"11\" rx=\"3\"/>" +
            "<path d=\"M12 8V4M9 13h.01M15 13h.01M9.5 16.5h5\"/>" +
            "<path d=\"M2 12v3M22 12v3\"/>",

        NetraIcon.Report =>
            "<path d=\"M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8Z\"/>" +
            "<path d=\"M14 3v5h5M8 17v-3M12 17v-6M16 17v-2\"/>",

        NetraIcon.Import =>
            "<path d=\"M12 3v11\"/><path d=\"m8 10 4 4 4-4\"/>" +
            "<path d=\"M4 17v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-2\"/>",

        NetraIcon.Users =>
            "<path d=\"M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2\"/>" +
            "<circle cx=\"9\" cy=\"7\" r=\"4\"/>" +
            "<path d=\"M22 21v-2a4 4 0 0 0-3-3.87M16 3.1a4 4 0 0 1 0 7.8\"/>",

        NetraIcon.Audit =>
            "<path d=\"M4 4h11l5 5v11a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1Z\"/>" +
            "<path d=\"M14 4v5h5M8 13h6M8 17h4\"/>",

        NetraIcon.Settings =>
            "<circle cx=\"12\" cy=\"12\" r=\"3\"/>" +
            "<path d=\"M19.4 15a1.6 1.6 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.6 1.6 0 0 0-1.8-.3 1.6 1.6 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.6 1.6 0 0 0-1-1.5 1.6 1.6 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.6 1.6 0 0 0 .3-1.8 1.6 1.6 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.6 1.6 0 0 0 1.5-1 1.6 1.6 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.6 1.6 0 0 0 1.8.3H9a1.6 1.6 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.6 1.6 0 0 0 1 1.5 1.6 1.6 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.6 1.6 0 0 0-.3 1.8V9a1.6 1.6 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.6 1.6 0 0 0-1.5 1Z\"/>",

        NetraIcon.Shield =>
            "<path d=\"M12 3 4 6.5v5.2c0 4.7 3.4 9.1 8 10.3 4.6-1.2 8-5.6 8-10.3V6.5L12 3Z\"/>" +
            "<path d=\"m9 12 2 2 4-4\"/>",

        NetraIcon.Bell =>
            "<path d=\"M18 9a6 6 0 1 0-12 0c0 5-2 6-2 6h16s-2-1-2-6Z\"/>" +
            "<path d=\"M10.5 20a2 2 0 0 0 3 0\"/>",

        NetraIcon.Appearance =>
            "<circle cx=\"12\" cy=\"12\" r=\"9\"/>" +
            "<path d=\"M12 3a9 9 0 0 0 0 18Z\"/>",

        NetraIcon.Sliders =>
            "<path d=\"M4 6h10M18 6h2M4 12h4M12 12h8M4 18h10M18 18h2\"/>" +
            "<circle cx=\"16\" cy=\"6\" r=\"2\"/><circle cx=\"10\" cy=\"12\" r=\"2\"/>" +
            "<circle cx=\"16\" cy=\"18\" r=\"2\"/>",

        NetraIcon.Support =>
            "<circle cx=\"12\" cy=\"12\" r=\"9\"/>" +
            "<path d=\"M9.2 9.3a3 3 0 0 1 5.6 1c0 2-2.8 2.2-2.8 3.7M12 17.2h.01\"/>",

        NetraIcon.Search =>
            "<circle cx=\"11\" cy=\"11\" r=\"7\"/><path d=\"m20 20-3.5-3.5\"/>",

        NetraIcon.Help =>
            "<circle cx=\"12\" cy=\"12\" r=\"9\"/>" +
            "<path d=\"M9.6 9.4a2.5 2.5 0 0 1 4.9.6c0 1.7-2.5 1.9-2.5 3.4M12 16.8h.01\"/>",

        NetraIcon.Menu =>
            "<path d=\"M4 6h16M4 12h16M4 18h16\"/>",

        NetraIcon.Lock =>
            "<rect x=\"4\" y=\"10\" width=\"16\" height=\"10\" rx=\"2\"/>" +
            "<path d=\"M8 10V7a4 4 0 0 1 8 0v3\"/>",

        NetraIcon.Mail =>
            "<rect x=\"3\" y=\"5\" width=\"18\" height=\"14\" rx=\"2\"/>" +
            "<path d=\"m3.5 6.5 8.5 6 8.5-6\"/>",

        NetraIcon.User =>
            "<circle cx=\"12\" cy=\"8\" r=\"4\"/>" +
            "<path d=\"M4 21a8 8 0 0 1 16 0\"/>",

        NetraIcon.Key =>
            "<circle cx=\"8\" cy=\"14\" r=\"4\"/>" +
            "<path d=\"m11 11 8-8M17 5l2 2M15 7l2 2\"/>",

        NetraIcon.Inbox =>
            "<path d=\"M4 13h4l1.5 3h5L16 13h4\"/>" +
            "<path d=\"M5.5 5h13l2.5 8v5a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-5Z\"/>",

        NetraIcon.Info =>
            "<circle cx=\"12\" cy=\"12\" r=\"9\"/>" +
            "<path d=\"M12 11v5M12 8h.01\"/>",

        NetraIcon.Plus =>
            "<path d=\"M12 5v14M5 12h14\"/>",

        _ =>
            "<circle cx=\"12\" cy=\"12\" r=\"8\"/>"
    };
}