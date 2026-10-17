using System.Text;
using System.Text.Json;
using Islet.Hook;
using Islet.Hook.Protocol;

namespace Islet.Tests;

public class ProtocolTests
{
    [Fact]
    public void Message_round_trips_with_raw_input()
    {
        using JsonDocument input = JsonDocument.Parse("""{"command":"npm test","nested":{"a":[1,2]}}""");
        var message = new HookMessage
        {
            Id = "abc",
            Agent = "claude",
            Event = "PermissionRequest",
            SessionId = "s1",
            Cwd = "C:/dev/repo",
            Tool = "Bash",
            Input = input.RootElement.Clone(),
        };

        string line = PipeProtocol.Serialize(message);
        HookMessage? parsed = PipeProtocol.ParseMessage(line);

        Assert.DoesNotContain('\n', line);
        Assert.StartsWith("""{"v":1,"id":"abc","agent":"claude","event":"PermissionRequest","sessionId":"s1","cwd":"C:/dev/repo","tool":"Bash","input":""", line);
        Assert.NotNull(parsed);
        Assert.Equal("Bash", parsed.Tool);
        Assert.Equal(input.RootElement.GetRawText(), parsed.Input!.Value.GetRawText());
        Assert.True(parsed.IsPermissionRequest);
    }

    [Fact]
    public void Message_omits_null_fields()
    {
        string line = PipeProtocol.Serialize(new HookMessage { Id = "x", Agent = "claude", Event = "Stop" });
        Assert.Equal("""{"v":1,"id":"x","agent":"claude","event":"Stop"}""", line);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"v":2,"id":"x","agent":"claude","event":"Stop"}""")]
    [InlineData("""{"v":1,"agent":"claude","event":"Stop"}""")]
    [InlineData("""{"v":1,"id":"x","agent":"claude"}""")]
    public void Malformed_messages_are_rejected(string? line) => Assert.Null(PipeProtocol.ParseMessage(line));

    [Theory]
    [InlineData("""{"id":"req","decision":"allow"}""", "allow")]
    [InlineData("""{"id":"req","decision":"deny","message":"Not now"}""", "deny")]
    [InlineData("""{"id":"req","decision":"passthrough"}""", "passthrough")]
    public void Valid_replies_parse(string line, string decision)
    {
        HookReply? reply = PipeProtocol.ParseReply(line, "req");
        Assert.NotNull(reply);
        Assert.Equal(decision, reply.Decision);
    }

    [Fact]
    public void Deny_reply_keeps_message() =>
        Assert.Equal("Not now", PipeProtocol.ParseReply("""{"id":"req","decision":"deny","message":"Not now"}""", "req")!.Message);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("""{"id":"other","decision":"allow"}""")]
    [InlineData("""{"decision":"allow"}""")]
    [InlineData("""{"id":"req","decision":"ALLOW"}""")]
    [InlineData("""{"id":"req","decision":"approve"}""")]
    [InlineData("""{"id":"req"}""")]
    [InlineData("""["allow"]""")]
    public void Anything_but_a_valid_reply_to_this_request_is_rejected(string? line) =>
        Assert.Null(PipeProtocol.ParseReply(line, "req"));

    [Theory]
    [InlineData(null, "islet-v1")]
    [InlineData("", "islet-v1")]
    [InlineData("some-other-pipe", "islet-v1")]
    [InlineData("islet-test-123", "islet-test-123")]
    public void Pipe_name_override_only_accepts_test_names(string? overrideName, string expected) =>
        Assert.Equal(expected, PipeProtocol.ResolvePipeName(overrideName));

    [Fact]
    public void Default_pipe_name_is_the_product_constant() =>
        Assert.Equal(ProductInfo.PipeName, PipeProtocol.ResolvePipeName(null));

    [Fact]
    public async Task ReadLine_stops_at_the_first_newline()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"a\":\"é\"}\n{\"b\":2}\n"));
        Assert.Equal("{\"a\":\"é\"}", await PipeProtocol.ReadLineAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ReadLine_without_newline_is_null()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"a\":1}"));
        Assert.Null(await PipeProtocol.ReadLineAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task ReadLine_over_the_limit_is_null()
    {
        using var stream = new MemoryStream(new byte[PipeProtocol.MaxLineBytes + 10]);
        Assert.Null(await PipeProtocol.ReadLineAsync(stream, CancellationToken.None));
    }
}
