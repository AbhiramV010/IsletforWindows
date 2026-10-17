using System.Windows.Threading;
using Islet.App.ViewModels;
using Islet.Hook.Protocol;

namespace Islet.App.Agents;

/// <summary>Bridges pipe messages (thread pool) to the view models (UI thread).</summary>
public sealed class AgentHub : IHookMessageHandler
{
    private readonly Dispatcher _dispatcher;
    private readonly PermissionQueueViewModel _permissions;
    private readonly AgentSessionsViewModel _sessions;

    public AgentHub(Dispatcher dispatcher, PermissionQueueViewModel permissions, AgentSessionsViewModel sessions)
    {
        _dispatcher = dispatcher;
        _permissions = permissions;
        _sessions = sessions;
    }

    public void OnEvent(HookMessage message)
    {
        FileLog.Write($"Agent event: {message.Agent} {message.Event} {message.Tool} {message.NotificationType}".TrimEnd());
        _dispatcher.InvokeAsync(() => _sessions.Apply(message));
    }

    public async Task<HookReply> OnPermissionRequestAsync(HookMessage message, CancellationToken clientGone)
    {
        PermissionRequestViewModel request = await _dispatcher.InvokeAsync(() => _permissions.Enqueue(message));
        FileLog.Write($"Permission request queued: {message.Agent} {message.Tool} in {request.Repo}");

        using (clientGone.Register(() => _dispatcher.InvokeAsync(() => _permissions.Cancel(request))))
        {
            return await request.Reply.ConfigureAwait(false);
        }
    }
}
