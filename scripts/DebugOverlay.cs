// ─────────────────────────────────────────────────────────────────────────────
// DebugOverlay.cs — HUD showing fighter states, health, and match info
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using AiBtGym.Simulation;

namespace AiBtGym.Godot;

public partial class DebugOverlay : Control
{
    private Label? _labelLeft;
    private Label? _labelRight;
    private Label? _labelCenter;

    public Match? Match { get; set; }

    public override void _Ready()
    {
        _labelLeft = new Label { Position = new Vector2(20, 10) };
        _labelLeft.AddThemeColorOverride("font_color", Colors.Red);
        AddChild(_labelLeft);

        _labelRight = new Label { Position = new Vector2(900, 10) };
        _labelRight.AddThemeColorOverride("font_color", Colors.CornflowerBlue);
        AddChild(_labelRight);

        _labelCenter = new Label { Position = new Vector2(520, 10) };
        _labelCenter.AddThemeColorOverride("font_color", Colors.White);
        AddChild(_labelCenter);
    }

    public override void _Process(double delta)
    {
        if (Match == null) return;

        _labelLeft!.Text = FormatFighter("P1 (Red)", Match.Fighter0);
        _labelRight!.Text = FormatFighter("P2 (Blue)", Match.Fighter1);

        float seconds = Match.Tick / 60f;
        float maxSeconds = Match.MaxTicks / 60f;
        _labelCenter!.Text = $"Time: {seconds:F1}s / {maxSeconds:F0}s\nTick: {Match.Tick}";

        if (Match.IsOver)
        {
            string winner = Match.WinnerIndex switch
            {
                0 => "P1 WINS",
                1 => "P2 WINS",
                _ => "DRAW"
            };
            _labelCenter.Text += $"\n{winner}";
        }
    }

    private static string FormatFighter(string name, Fighter f)
    {
        return $"{name}\n" +
               $"HP: {f.Health:F0}\n" +
               $"Pos: ({f.Position.X:F0}, {f.Position.Y:F0})\n" +
               $"Vel: ({f.Velocity.X:F0}, {f.Velocity.Y:F0})\n" +
               $"Grounded: {f.IsGrounded}\n" +
               $"L: {f.LeftFist.ChainState}" + (f.LeftFist.IsAttachedToWorld ? " [A]" : "") + "\n" +
               $"R: {f.RightFist.ChainState}" + (f.RightFist.IsAttachedToWorld ? " [A]" : "");
    }
}
