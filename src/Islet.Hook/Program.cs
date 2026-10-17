using Islet.Hook;
using Islet.Hook.Protocol;

// Usage:
//   Islet.Hook.exe <agent>                  run as a hook: payload on stdin, decision on stdout
//   Islet.Hook.exe --print-config claude    print the settings.json hooks block for this exe
try
{
    if (args is ["--print-config", var agent])
    {
        string? config = HookConfig.Build(agent, Environment.ProcessPath ?? "Islet.Hook.exe");
        Console.Out.Write(config ?? $"No config template for '{agent}'.{Environment.NewLine}");
        return 0;
    }

    string pipeName = PipeProtocol.ResolvePipeName(Environment.GetEnvironmentVariable("ISLET_PIPE_NAME"));
    var runner = new HookRunner(pipeName, HookRunner.DefaultConnectTimeout);

    using Stream stdin = Console.OpenStandardInput();
    using Stream stdout = Console.OpenStandardOutput();
    return await runner.RunAsync(args, stdin, stdout, CancellationToken.None);
}
catch (Exception exception)
{
    HookLog.Write($"Unhandled: {exception.GetType().Name}: {exception.Message}");
    return 0;
}
