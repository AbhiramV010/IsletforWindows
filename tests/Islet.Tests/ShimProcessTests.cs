using System.Diagnostics;
using Islet.Hook;
using Islet.Hook.Protocol;

namespace Islet.Tests;

/// <summary>
/// The built Islet.Hook.exe, with saved payloads from tests/fixtures piped into it, the way
/// an agent runs it. Includes the exact command string Claude Code runs through Git Bash.
/// </summary>
public class ShimProcessTests
{
    private const string AllowJson =
        """{"hookSpecificOutput":{"hookEventName":"PermissionRequest","decision":{"behavior":"allow"}}}""";

    private readonly string _pipeName = TestPipes.NewName();

    [Fact]
    public async Task Exe_prints_allow_from_the_app()
    {
        await using var app = new FakeApp(_pipeName, m => PipeProtocol.Serialize(new HookReply { Id = m.Id, Decision = "allow" }));
        (int exitCode, string stdout, string stderr) = await RunExeAsync(["claude"], "claude/permission-request-bash.json");

        Assert.Equal(0, exitCode);
        Assert.Equal(AllowJson, stdout);
        Assert.Equal("", stderr);
    }

    [Theory]
    [InlineData("claude/permission-request-bash.json")]
    [InlineData("claude/permission-request-edit.json")]
    [InlineData("claude/notification.json")]
    [InlineData("claude/post-tool-use.json")]
    [InlineData("claude/stop.json")]
    [InlineData("claude/malformed.json")]
    public async Task Exe_without_the_app_exits_0_silently(string fixture)
    {
        (int exitCode, string stdout, string stderr) = await RunExeAsync(["claude"], fixture);
        Assert.Equal(0, exitCode);
        Assert.Equal("", stdout);
        Assert.Equal("", stderr);
    }

    [Fact]
    public async Task Exe_with_bad_arguments_exits_0_silently()
    {
        (int exitCode, string stdout, _) = await RunExeAsync([], "claude/permission-request-bash.json");
        Assert.Equal(0, exitCode);
        Assert.Equal("", stdout);
    }

    [Fact]
    public async Task Git_bash_runs_the_exact_settings_command()
    {
        string? bash = FindGitBash();
        if (bash is null)
        {
            return; // Claude Code falls back to PowerShell without Git Bash; nothing to test here.
        }

        await using var app = new FakeApp(_pipeName, m => PipeProtocol.Serialize(new HookReply { Id = m.Id, Decision = "allow" }));
        string command = HookConfig.CommandFor(ShimExePath, "claude");

        (int exitCode, string stdout, string stderr) = await RunAsync(bash, ["-c", command], "claude/permission-request-bash.json");

        Assert.Equal(0, exitCode);
        Assert.Equal(AllowJson, stdout);
        Assert.Equal("", stderr);
    }

    [Fact]
    public async Task Print_config_fills_in_the_exe_path()
    {
        (int exitCode, string stdout, _) = await RunExeAsync(["--print-config", "claude"], fixture: null);
        Assert.Equal(0, exitCode);

        using var config = System.Text.Json.JsonDocument.Parse(stdout);
        System.Text.Json.JsonElement hooks = config.RootElement.GetProperty("hooks");
        string expectedCommand = $"\"{ShimExePath.Replace('\\', '/')}\" claude";

        System.Text.Json.JsonElement permission = hooks.GetProperty("PermissionRequest")[0].GetProperty("hooks")[0];
        Assert.Equal(expectedCommand, permission.GetProperty("command").GetString());
        Assert.Equal(600, permission.GetProperty("timeout").GetInt32());

        foreach (string eventName in new[] { "Notification", "PostToolUse", "Stop" })
        {
            System.Text.Json.JsonElement hook = hooks.GetProperty(eventName)[0].GetProperty("hooks")[0];
            Assert.Equal(expectedCommand, hook.GetProperty("command").GetString());
            Assert.False(hook.TryGetProperty("timeout", out _));
        }
    }

    private static string ShimExePath
    {
        get
        {
            string local = Path.Combine(AppContext.BaseDirectory, "Islet.Hook.exe");
            if (File.Exists(local))
            {
                return local;
            }

            // Fall back to the shim's own build output.
            string configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}")
                ? "Release"
                : "Debug";
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            return Path.Combine(root, "src", "Islet.Hook", "bin", configuration, "net8.0", "Islet.Hook.exe");
        }
    }

    private static string? FindGitBash()
    {
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "bin", "bash.exe"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    private Task<(int, string, string)> RunExeAsync(string[] args, string? fixture) => RunAsync(ShimExePath, args, fixture);

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string fileName, string[] args, string? fixture)
    {
        Assert.True(File.Exists(fileName), $"missing {fileName}");

        var start = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        start.Environment["ISLET_PIPE_NAME"] = _pipeName;

        using Process process = Process.Start(start)!;
        if (fixture is not null)
        {
            await process.StandardInput.BaseStream.WriteAsync(Fixtures.Bytes(fixture));
        }

        process.StandardInput.Close();

        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        return (process.ExitCode, await stdout, await stderr);
    }
}
