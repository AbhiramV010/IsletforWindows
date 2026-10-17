using System.Text.Json;
using Islet.Hook.Protocol;

namespace Islet.Hook.Agents;

/// <summary>
/// Translates one agent's hook payloads into pipe messages, and the app's decisions back
/// into that agent's hook output. Each agent gets its own, since the schemas differ.
/// </summary>
internal interface IAgentAdapter
{
    /// <summary>The shim's command-line argument, e.g. <c>claude</c>.</summary>
    string Name { get; }

    /// <summary>Null when the payload isn't something this agent sends.</summary>
    HookMessage? Map(JsonElement payload);

    /// <summary>
    /// Stdout for an allow or deny reply. Null means print nothing, so the agent falls back
    /// to its own prompt.
    /// </summary>
    string? FormatDecision(HookReply reply);
}

internal static class AgentAdapters
{
    private static readonly IAgentAdapter[] All = [new ClaudeAdapter()];

    public static IAgentAdapter? Find(string name) =>
        Array.Find(All, adapter => string.Equals(adapter.Name, name, StringComparison.OrdinalIgnoreCase));
}
