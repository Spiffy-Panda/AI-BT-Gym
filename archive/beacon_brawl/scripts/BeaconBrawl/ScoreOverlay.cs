// ─────────────────────────────────────────────────────────────────────────────
// ScoreOverlay.cs — HUD showing scores, beacon status, kills, and match info
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using AiBtGym.Simulation.BeaconBrawl;

namespace AiBtGym.Godot.BeaconBrawl;

public partial class ScoreOverlay : Control
{
    private Label? _labelScore;
    private Label? _labelBeacons;
    private Label? _labelTime;
    private Label? _labelDebug;

    public BeaconMatch? Match { get; set; }
    public Color TeamAColor { get; set; } = new(0.9f, 0.2f, 0.2f);
    public Color TeamBColor { get; set; } = new(0.2f, 0.4f, 0.9f);
    public string TeamAName { get; set; } = "Team A";
    public string TeamBName { get; set; } = "Team B";

    public override void _Ready()
    {
        _labelScore = new Label { Position = new Vector2(600, 10) };
        _labelScore.AddThemeColorOverride("font_color", Colors.White);
        _labelScore.AddThemeFontSizeOverride("font_size", 20);
        AddChild(_labelScore);

        _labelBeacons = new Label { Position = new Vector2(600, 45) };
        _labelBeacons.AddThemeColorOverride("font_color", Colors.LightGray);
        AddChild(_labelBeacons);

        _labelTime = new Label { Position = new Vector2(750, 75) };
        _labelTime.AddThemeColorOverride("font_color", Colors.Gray);
        AddChild(_labelTime);

        _labelDebug = new Label { Position = new Vector2(20, 10) };
        _labelDebug.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f, 0.6f));
        AddChild(_labelDebug);
    }

    public override void _Process(double delta)
    {
        if (Match == null) return;

        // Score + kills
        _labelScore!.Text = $"{TeamAName}: {Match.Scores[0]} (K:{Match.Kills[0]})  |  " +
                            $"{TeamBName}: {Match.Scores[1]} (K:{Match.Kills[1]})  |  Target: {Match.TargetScore}";

        // Beacon status
        string[] beaconNames = ["Left", "Center(2x)", "Right"];
        string beaconText = "";
        for (int i = 0; i < Match.Beacons.Length; i++)
        {
            var b = Match.Beacons[i];
            string owner = b.OwnerTeam switch { 1 => "A", 2 => "B", _ => "-" };
            string contested = b.IsContested ? "!" : "";
            string progress = b.CaptureProgress > 0 ? $"({b.CaptureProgress}/{Beacon.CaptureThreshold})" : "";
            beaconText += $"{beaconNames[i]}:[{owner}]{contested}{progress}  ";
        }
        _labelBeacons!.Text = beaconText;

        // Time
        float seconds = Match.Tick / 60f;
        float maxSeconds = Match.MaxTicks / 60f;
        string timeText = $"Time: {seconds:F1}s / {maxSeconds:F0}s";
        if (Match.IsOvertime) timeText += "  [OVERTIME]";

        if (Match.IsOver)
        {
            string winner = Match.WinnerTeam switch
            {
                0 => $"{TeamAName} WINS!",
                1 => $"{TeamBName} WINS!",
                _ => "DRAW"
            };
            timeText += $"  —  {winner}";
        }
        _labelTime!.Text = timeText;

        // Debug: pawn positions + health
        string debug = "";
        string RoleChar(PawnRole r) => r == PawnRole.Grappler ? "G" : "R";
        for (int i = 0; i < Match.TeamA.Length; i++)
        {
            var p = Match.TeamA[i];
            debug += $"A{i}({RoleChar(p.Role)}): ({p.Position.X:F0},{p.Position.Y:F0}) HP:{p.Health:F0} " +
                     (p.IsDead ? "[DEAD] " : "") +
                     (p.IsStunned ? "[STUN] " : "") +
                     (p.IsVulnerable ? "[VULN] " : "") +
                     (p.IsParryActive ? "[PARRY] " : "") + "\n";
        }
        for (int i = 0; i < Match.TeamB.Length; i++)
        {
            var p = Match.TeamB[i];
            debug += $"B{i}({RoleChar(p.Role)}): ({p.Position.X:F0},{p.Position.Y:F0}) HP:{p.Health:F0} " +
                     (p.IsDead ? "[DEAD] " : "") +
                     (p.IsStunned ? "[STUN] " : "") +
                     (p.IsVulnerable ? "[VULN] " : "") +
                     (p.IsParryActive ? "[PARRY] " : "") + "\n";
        }
        _labelDebug!.Text = debug;
    }
}
