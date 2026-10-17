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
        IsQuestion = message.Tool == QuestionTool;

        if (IsQuestion)
        {
            // The island never answers a question: the options live in the terminal.
            Headline = $"{(AgentName == "Claude Code" ? "Claude" : AgentName)} has a question";
            Preview = "Go to your session";
            FullText = PermissionPreview.QuestionText(message.Input) ?? Preview;
        }
        else
        {
            PermissionPreview preview = PermissionPreview.From(message.Tool, message.Input);
            Headline = Tool;
            Preview = preview.Line;
            FullText = preview.Full;
        }

        ArrivedAtUtc = arrivedAtUtc;
    }

    /// <summary>The tool Claude Code uses to ask the user a multiple-choice question.</summary>
    public const string QuestionTool = "AskUserQuestion";

    public string AgentName { get; }

    public string Repo { get; }

    public string Tool { get; }

    /// <summary>The card's bold line: the tool name, or "Claude Code has a question".</summary>
    public string Headline { get; }

    /// <summary>A question can't be allowed or denied from the island.</summary>
    public bool IsQuestion { get; }

    /// <summary>False for questions: no Allow / Deny buttons, and Enter / Esc are left alone.</summary>
    public bool IsInteractive => !IsQuestion;

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
