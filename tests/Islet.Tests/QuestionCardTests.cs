using System.Text.Json;
using Islet.App.Agents;
using Islet.App.ViewModels;
using Islet.Hook.Protocol;

namespace Islet.Tests;

/// <summary>AskUserQuestion has several options, so the island only points at the session.</summary>
public class QuestionCardTests
{
    private const string QuestionInput =
        """{"questions":[{"question":"Which approach?","header":"Approach","options":[{"label":"A"},{"label":"B"}],"multiSelect":false}]}""";

    private DateTime _now = new(2026, 10, 17, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Question_card_says_so_and_points_at_the_session()
    {
        PermissionRequestViewModel card = NewQueue().Enqueue(Question());

        Assert.True(card.IsQuestion);
        Assert.False(card.IsInteractive);
        Assert.Equal("Claude has a question", card.Headline);
        Assert.Equal("Go to your session", card.Preview);
        Assert.Equal("Which approach?", card.FullText);
    }

    [Fact]
    public void Allow_and_deny_do_nothing_on_a_question()
    {
        PermissionQueueViewModel queue = NewQueue();
        PermissionRequestViewModel card = queue.Enqueue(Question());
        _now += PermissionQueueViewModel.InputGuard;

        Assert.False(queue.Allow());
        Assert.False(queue.Deny());
        Assert.False(card.Reply.IsCompleted);
        Assert.Same(card, queue.Current);
    }

    [Fact]
    public async Task Question_goes_away_when_the_agent_hangs_up_after_it_is_answered()
    {
        PermissionQueueViewModel queue = NewQueue();
        PermissionRequestViewModel card = queue.Enqueue(Question());

        queue.Cancel(card);

        Assert.Equal(Decisions.Passthrough, (await card.Reply).Decision);
        Assert.Null(queue.Current);
    }

    [Fact]
    public void Normal_requests_are_unchanged()
    {
        PermissionRequestViewModel card = NewQueue().Enqueue(Message("Bash", """{"command":"npm test"}"""));

        Assert.False(card.IsQuestion);
        Assert.True(card.IsInteractive);
        Assert.Equal("Bash", card.Headline);
        Assert.Equal("npm test", card.Preview);
    }

    [Fact]
    public void A_question_behind_a_request_does_not_block_it()
    {
        PermissionQueueViewModel queue = NewQueue();
        PermissionRequestViewModel first = queue.Enqueue(Message("Bash", """{"command":"npm test"}"""));
        queue.Enqueue(Question());
        _now += PermissionQueueViewModel.InputGuard;

        Assert.True(queue.Allow());
        Assert.True(first.Reply.IsCompleted);
        Assert.True(queue.Current!.IsQuestion);
    }

    [Theory]
    [InlineData("""{"questions":[{"question":"One?"},{"question":"Two?"}]}""", "One?\r\n\r\nTwo?")]
    [InlineData("""{"questions":[]}""", null)]
    [InlineData("""{"other":1}""", null)]
    [InlineData("""{"questions":"nope"}""", null)]
    [InlineData("""{"questions":[{"header":"x"}]}""", null)]
    public void Question_text_is_pulled_from_the_input(string json, string? expected)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(expected?.Replace("\r\n", Environment.NewLine), PermissionPreview.QuestionText(document.RootElement.Clone()));
    }

    [Fact]
    public void Question_text_without_input_is_null() => Assert.Null(PermissionPreview.QuestionText(null));

    private PermissionQueueViewModel NewQueue() => new(() => _now);

    private static HookMessage Question() => Message("AskUserQuestion", QuestionInput);

    private static HookMessage Message(string tool, string inputJson)
    {
        using JsonDocument input = JsonDocument.Parse(inputJson);
        return new HookMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Agent = "claude",
            Event = PipeProtocol.PermissionRequestEvent,
            Cwd = @"C:\dev\repo",
            Tool = tool,
            Input = input.RootElement.Clone(),
        };
    }
}
