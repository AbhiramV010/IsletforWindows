using Islet.App.Agents;
using Islet.Hook.Protocol;

namespace Islet.App.ViewModels;

/// <summary>One pending permission request, as the card shows it.</summary>
public sealed class PermissionRequestViewModel : ObservableObject
{
    private readonly TaskCompletionSource<HookReply> _reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _isOverdue;

    public PermissionRequestViewModel(HookMessage message, DateTime arrivedAtUtc)
    {
        AgentName = message.Agent switch
        {
            "claude" => "Claude Code",
            "codex" => "Codex",
            _ => message.Agent,
        };
        Repo = PermissionPreview.RepoName(message.Cwd);
        Tool = message.Tool ?? "Tool";
        PermissionPreview preview = PermissionPreview.From(message.Tool, message.Input);
        Preview = preview.Line;
        FullText = preview.Full;
        ArrivedAtUtc = arrivedAtUtc;
    }

    public string AgentName { get; }

    public string Repo { get; }

    public string Tool { get; }

    public string Preview { get; }

    public string FullText { get; }

    public DateTime ArrivedAtUtc { get; }

    /// <summary>When this request reached the front of the queue and became clickable.</summary>
    public DateTime? ShownAtUtc { get; set; }

    /// <summary>Still unanswered after 30 s: the card border pulses.</summary>
    public bool IsOverdue
    {
        get => _isOverdue;
        set => SetField(ref _isOverdue, value);
    }

    public Task<HookReply> Reply => _reply.Task;

    internal bool IsResolved => _reply.Task.IsCompleted;

    internal void Resolve(HookReply reply) => _reply.TrySetResult(reply);
}
