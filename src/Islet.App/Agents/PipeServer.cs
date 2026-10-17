using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Islet.Hook.Protocol;

namespace Islet.App.Agents;

/// <summary>Receives what the hook shim sends. Called on thread-pool threads.</summary>
public interface IHookMessageHandler
{
    /// <summary>Fire-and-forget events: Notification, PostToolUse, Stop.</summary>
    void OnEvent(HookMessage message);

    /// <summary>
    /// Resolves when the user decides. <paramref name="clientGone"/> fires if the shim
    /// disconnects first (the agent timed out or was closed), and the reply is then dropped.
    /// </summary>
    Task<HookReply> OnPermissionRequestAsync(HookMessage message, CancellationToken clientGone);
}

/// <summary>
/// Named pipe server for <c>islet-v1</c>. One connection per hook invocation: the shim
/// sends one line, and for a permission request waits for one line back.
/// </summary>
public sealed class PipeServer : IDisposable
{
    /// <summary>The shim writes its line right after connecting, so a slow sender is not a shim.</summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    private readonly string _pipeName;
    private readonly IHookMessageHandler _handler;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _acceptLoop;

    public PipeServer(string pipeName, IHookMessageHandler handler)
    {
        _pipeName = pipeName;
        _handler = handler;
    }

    /// <summary>
    /// Only the current user's SID gets any access, so no other account (and no
    /// lower-integrity sandbox running as someone else) can connect and answer requests.
    /// </summary>
    public static PipeSecurity CreateSecurity()
    {
        SecurityIdentifier user = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("No user SID for the current process.");

        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    public void Start() => _acceptLoop = Task.Run(() => AcceptLoopAsync(_shutdown.Token));

    public void Dispose()
    {
        _shutdown.Cancel();
        try
        {
            _acceptLoop?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // Shutdown races with in-flight connections; nothing to recover.
        }

        _shutdown.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        bool firstInstance = true;
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = CreateInstance(firstInstance);
            }
            catch (Exception exception) when (firstInstance)
            {
                // FirstPipeInstance fails if something else already owns the name.
                FileLog.Write($"Pipe '{_pipeName}' unavailable, agent features off", exception);
                return;
            }

            firstInstance = false;

            try
            {
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync().ConfigureAwait(false);
                return;
            }
            catch (IOException exception)
            {
                FileLog.Write("Pipe accept failed", exception);
                await server.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            _ = HandleConnectionAsync(server, cancellationToken);
        }
    }

    private NamedPipeServerStream CreateInstance(bool firstInstance)
    {
        PipeOptions options = PipeOptions.Asynchronous;
        if (firstInstance)
        {
            options |= PipeOptions.FirstPipeInstance;
        }

        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            options,
            inBufferSize: 0,
            outBufferSize: 0,
            CreateSecurity());
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        await using (server.ConfigureAwait(false))
        {
            try
            {
                string? line;
                using (var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    readTimeout.CancelAfter(ReadTimeout);
                    line = await PipeProtocol.ReadLineAsync(server, readTimeout.Token).ConfigureAwait(false);
                }

                HookMessage? message = PipeProtocol.ParseMessage(line);
                if (message is null)
                {
                    FileLog.Write("Pipe: dropped a malformed message");
                    return;
                }

                if (!message.IsPermissionRequest)
                {
                    _handler.OnEvent(message);
                    return;
                }

                using var clientGone = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _ = WatchForDisconnectAsync(server, clientGone);

                HookReply reply = await _handler.OnPermissionRequestAsync(message, clientGone.Token).ConfigureAwait(false);
                if (clientGone.IsCancellationRequested)
                {
                    return;
                }

                reply.Id = message.Id;
                await PipeProtocol.WriteLineAsync(server, PipeProtocol.Serialize(reply), cancellationToken).ConfigureAwait(false);

                // Let the shim read the reply before the handle closes.
                server.WaitForPipeDrain();
                FileLog.Write($"Pipe: replied {reply.Decision} to {message.Agent} {message.Tool}");
            }
            catch (OperationCanceledException)
            {
                // App shutting down, or the shim was too slow to send its line.
            }
            catch (Exception exception)
            {
                FileLog.Write("Pipe connection failed", exception);
            }
        }
    }

    /// <summary>
    /// The shim sends nothing after its line, so a completed read means it hung up:
    /// the agent timed out, was closed, or the user answered in the terminal.
    /// </summary>
    private static async Task WatchForDisconnectAsync(NamedPipeServerStream server, CancellationTokenSource clientGone)
    {
        try
        {
            byte[] buffer = new byte[1];
            await server.ReadAsync(buffer, clientGone.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            // A broken pipe means the same thing as a clean close.
        }

        try
        {
            clientGone.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The connection already finished.
        }
    }
}
