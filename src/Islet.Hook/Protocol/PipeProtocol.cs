using System.Text;
using System.Text.Json;

namespace Islet.Hook.Protocol;

/// <summary>Wire format: one UTF-8 JSON object per line, in each direction.</summary>
public static class PipeProtocol
{
    public const int Version = 1;
    public const string PermissionRequestEvent = "PermissionRequest";

    /// <summary>Upper bound on one line. A tool input bigger than this is not worth showing.</summary>
    public const int MaxLineBytes = 4 * 1024 * 1024;

    /// <summary>Test-only pipe names must carry this prefix, so an override can't point at a real pipe.</summary>
    public const string TestPipePrefix = "islet-test-";

    /// <summary>
    /// The pipe the shim connects to. <c>ISLET_PIPE_NAME</c> lets tests run a private server,
    /// and is ignored unless it starts with <see cref="TestPipePrefix"/>.
    /// </summary>
    public static string ResolvePipeName(string? overrideName) =>
        overrideName is not null && overrideName.StartsWith(TestPipePrefix, StringComparison.Ordinal)
            ? overrideName
            : ProductInfo.PipeName;

    public static string Serialize(HookMessage message) =>
        JsonSerializer.Serialize(message, ProtocolJsonContext.Default.HookMessage);

    public static string Serialize(HookReply reply) =>
        JsonSerializer.Serialize(reply, ProtocolJsonContext.Default.HookReply);

    /// <summary>Parses a shim-to-app line. Null for anything malformed or from another protocol version.</summary>
    public static HookMessage? ParseMessage(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            HookMessage? message = JsonSerializer.Deserialize(line, ProtocolJsonContext.Default.HookMessage);
            return message is { Version: Version } && message.Id.Length > 0 && message.Event.Length > 0
                ? message
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses the app's reply to the request with <paramref name="expectedId"/>. Anything that
    /// isn't a well-formed reply to that exact request comes back as null, which the shim treats
    /// as passthrough.
    /// </summary>
    public static HookReply? ParseReply(string? line, string expectedId)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            HookReply? reply = JsonSerializer.Deserialize(line, ProtocolJsonContext.Default.HookReply);
            if (reply is null || reply.Id.Length == 0 || reply.Id != expectedId)
            {
                return null;
            }

            return reply.Decision is Decisions.Allow or Decisions.Deny or Decisions.Passthrough ? reply : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static async Task WriteLineAsync(Stream stream, string json, CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json + "\n");
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads up to the first newline. Null on end of stream before a newline, or past
    /// <see cref="MaxLineBytes"/>. Bytes after the newline are dropped: each side sends one line.
    /// </summary>
    public static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[4096];

        while (true)
        {
            int read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            int newline = Array.IndexOf(chunk, (byte)'\n', 0, read);
            buffer.Write(chunk, 0, newline >= 0 ? newline : read);

            if (buffer.Length > MaxLineBytes)
            {
                return null;
            }

            if (newline >= 0)
            {
                return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
            }
        }
    }
}
