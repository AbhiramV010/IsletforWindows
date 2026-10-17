using System.Windows.Threading;
using Islet.App.Agents;
using Islet.Hook.Protocol;

namespace Islet.App.ViewModels;

public enum AgentState
{
    Working,
    Waiting,
    Done,
}

/// <summary>One agent session as the one-line status shows it.</summary>
public sealed class AgentSessionViewModel : ObservableObject
{
    private AgentState _state = AgentState.Working;
    private string? _lastTool;

    public AgentSessionViewModel(string agentName, string repo)
    {
        AgentName = agentName;
        Repo = repo;
    }

    public string AgentName { get; }

    /// <summary>"Claude" for Claude Code, otherwise the agent's name.</summary>
    public string ShortName => AgentName == "Claude Code" ? "Claude" : AgentName;

    /// <summary>The line the expanded card shows when the agent finishes.</summary>
    public string DoneHeadline => $"{ShortName} is done";

    public string Repo { get; internal set; }

    public AgentState State
    {
        get => _state;
        internal set
        {
            if (SetField(ref _state, value))
            {
                OnPropertyChanged(nameof(Status));
            }
        }
    }

    public string? LastTool
    {
        get => _lastTool;
        internal set
        {
            if (SetField(ref _lastTool, value))
            {
                OnPropertyChanged(nameof(Status));
            }
        }
    }

    /// <summary>"Working · Bash", "Waiting for you", or "Done".</summary>
    public string Status => State switch
    {
        AgentState.Working => string.IsNullOrEmpty(LastTool) ? "Working" : $"Working · {LastTool}",
        AgentState.Waiting => "Waiting for you",
        _ => "Done",
    };

    internal DateTime UpdatedAtUtc { get; set; }

    /// <summary>When a <c>Stop</c> arrived. Done sessions disappear a few seconds later.</summary>
    internal DateTime? DoneAtUtc { get; set; }
}

/// <summary>
/// Tracks what each agent session is doing from its hook events: <c>PostToolUse</c> means
/// working (and names the tool), a permission or elicitation <c>Notification</c> means waiting,
/// and <c>Stop</c> means done, which clears itself after <see cref="DoneVisibleFor"/>.
/// </summary>
public sealed class AgentSessionsViewModel : ObservableObject
{
    /// <summary>How long the "is done" card stays up, long enough to notice from another window.</summary>
    public static readonly TimeSpan DoneVisibleFor = TimeSpan.FromSeconds(8);

    /// <summary>A session that goes quiet this long (agent killed, terminal closed) is dropped.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    private readonly Dictionary<string, AgentSessionViewModel> _sessions = [];
    private readonly DispatcherTimer _timer;
    private readonly Func<DateTime> _utcNow;
    private AgentSessionViewModel? _current;

    public AgentSessionsViewModel(Func<DateTime>? utcNow = null)
    {
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>The most recently active session, or null when nothing is running.</summary>
    public AgentSessionViewModel? Current
    {
        get => _current;
        private set => SetField(ref _current, value);
    }

    public int Count => _sessions.Count;

    /// <summary>"+2" when other sessions are active behind <see cref="Current"/>.</summary>
    public string? MoreBadge => _sessions.Count > 1 ? $"+{_sessions.Count - 1}" : null;

    public void Apply(HookMessage message)
    {
        AgentState? state = StateFor(message);
        if (state is null)
        {
            return;
        }

        DateTime now = _utcNow();
        string key = $"{message.Agent}:{message.SessionId ?? message.Cwd}";
        if (!_sessions.TryGetValue(key, out AgentSessionViewModel? session))
        {
            session = new AgentSessionViewModel(
                message.Agent == "claude" ? "Claude Code" : message.Agent == "codex" ? "Codex" : message.Agent,
                PermissionPreview.RepoName(message.Cwd));
            _sessions[key] = session;
        }
        else if (!string.IsNullOrEmpty(message.Cwd))
        {
            session.Repo = PermissionPreview.RepoName(message.Cwd);
        }

        session.State = state.Value;
        session.UpdatedAtUtc = now;
        session.DoneAtUtc = state == AgentState.Done ? now : null;
        if (message.Event == "PostToolUse" && !string.IsNullOrEmpty(message.Tool))
        {
            session.LastTool = message.Tool;
        }
        else if (state == AgentState.Done)
        {
            session.LastTool = null;
        }

        Refresh();
    }

    /// <summary>Closes the "is done" card early (a click). Sessions still working are untouched.</summary>
    public void DismissDone()
    {
        foreach ((string key, AgentSessionViewModel session) in _sessions.ToArray())
        {
            if (session.State == AgentState.Done)
            {
                _sessions.Remove(key);
            }
        }

        Refresh();
    }

    /// <summary>Drops finished and stale sessions. Called by the timer, and directly by tests.</summary>
    public void Tick()
    {
        DateTime now = _utcNow();
        foreach ((string key, AgentSessionViewModel session) in _sessions.ToArray())
        {
            bool doneExpired = session.DoneAtUtc is DateTime done && now - done >= DoneVisibleFor;
            bool stale = now - session.UpdatedAtUtc >= StaleAfter;
            if (doneExpired || stale)
            {
                _sessions.Remove(key);
            }
        }

        Refresh();
    }

    private static AgentState? StateFor(HookMessage message) => message.Event switch
    {
        "PostToolUse" => AgentState.Working,
        "Stop" => AgentState.Done,
        "Notification" => message.NotificationType switch
        {
            // Claude is blocked on the user. Idle nudges and auth notices are not progress.
            null or "permission_prompt" => AgentState.Waiting,
            string type when type.StartsWith("elicitation", StringComparison.Ordinal) => AgentState.Waiting,
            _ => null,
        },
        _ => null,
    };

    private void Refresh()
    {
        AgentSessionViewModel? latest = null;
        foreach (AgentSessionViewModel session in _sessions.Values)
        {
            if (latest is null || session.UpdatedAtUtc > latest.UpdatedAtUtc)
            {
                latest = session;
            }
        }

        Current = latest;
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(MoreBadge));

        if (_sessions.Count > 0)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }
}
