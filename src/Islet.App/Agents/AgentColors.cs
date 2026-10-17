using System.Windows.Media;

namespace Islet.App.Agents;

/// <summary>The colour each agent's name is drawn in.</summary>
public static class AgentColors
{
    private static readonly Brush Claude = Make("#E8A27C");
    private static readonly Brush Codex = Make("#3DDC97");
    private static readonly Brush Other = Make("#B3FFFFFF");

    public static Brush For(string agent) => agent switch
    {
        "claude" => Claude,
        "codex" => Codex,
        _ => Other,
    };

    private static Brush Make(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
