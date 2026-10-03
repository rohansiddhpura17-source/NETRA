namespace NETRA.Infrastructure.AI;

/// <summary>
/// Provider configuration, bound from the "Ai" configuration section
/// (appsettings.json or environment variables such as Ai__Provider).
/// No secrets belong here: the local server is an unauthenticated LAN/dev
/// endpoint (e.g. Ollama); hosted providers with keys are out of scope.
/// </summary>
public sealed class AiOptions
{
    /// <summary>"Mock" (default, always works) or "Local" (local model server).</summary>
    public string Provider { get; set; } = "Mock";

    /// <summary>Base address of a local open-source model server, e.g. Ollama.</summary>
    public string Endpoint { get; set; } = "http://localhost:11434";

    public string Model { get; set; } = "llama3.2";

    public int TimeoutSeconds { get; set; } = 15;
}
