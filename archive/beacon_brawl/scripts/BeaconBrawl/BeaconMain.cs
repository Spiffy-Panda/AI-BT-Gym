// ─────────────────────────────────────────────────────────────────────────────
// BeaconMain.cs — Entry point for interactive Beacon Brawl v2 viewer
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Godot;
using AiBtGym.BehaviorTree;
using AiBtGym.Simulation.BeaconBrawl;

namespace AiBtGym.Godot.BeaconBrawl;

public partial class BeaconMain : Node2D
{
    private BeaconMatch? _match;
    private BeaconArenaRenderer? _arenaRenderer;
    private PawnRenderer[]? _pawnRenderers;
    private ProjectileRenderer? _projectileRenderer;
    private ScoreOverlay? _scoreOverlay;

    [Export] public int Team0Index { get; set; } = 0;
    [Export] public int Team1Index { get; set; } = 1;

    private bool _paused;

    public override void _Ready()
    {
        StartMatch();
    }

    private void StartMatch()
    {
        // Clear old renderers
        foreach (var child in GetChildren())
            child.QueueFree();

        var allTeams = BeaconSeedTeams.All;
        int t0 = Team0Index % allTeams.Length;
        int t1 = Team1Index % allTeams.Length;
        if (t0 == t1) t1 = (t1 + 1) % allTeams.Length;

        var teamA = allTeams[t0];
        var teamB = allTeams[t1];

        var arena = new BeaconArena();
        _match = new BeaconMatch(arena, teamA.Trees, teamA.Roles, teamB.Trees, teamB.Roles);

        // Parse team colors
        var colorA = Color.FromHtml(BeaconSeedTeams.HexColors[t0]);
        var colorB = Color.FromHtml(BeaconSeedTeams.HexColors[t1]);

        // Arena renderer
        _arenaRenderer = new BeaconArenaRenderer
        {
            Arena = arena,
            Beacons = _match.Beacons,
            TeamAColor = colorA,
            TeamBColor = colorB
        };
        AddChild(_arenaRenderer);

        // Pawn renderers
        _pawnRenderers = new PawnRenderer[_match.AllPawns.Length];
        for (int i = 0; i < _match.AllPawns.Length; i++)
        {
            var pawn = _match.AllPawns[i];
            _pawnRenderers[i] = new PawnRenderer
            {
                Pawn = pawn,
                TeamColor = pawn.TeamIndex == 0 ? colorA : colorB
            };
            AddChild(_pawnRenderers[i]);
        }

        // Projectile renderer (drawn above pawns)
        _projectileRenderer = new ProjectileRenderer
        {
            Match = _match,
            TeamAColor = colorA,
            TeamBColor = colorB
        };
        AddChild(_projectileRenderer);

        // Score overlay
        var canvasLayer = new CanvasLayer();
        AddChild(canvasLayer);
        _scoreOverlay = new ScoreOverlay
        {
            Match = _match,
            TeamAColor = colorA,
            TeamBColor = colorB,
            TeamAName = BeaconSeedTeams.Names[t0],
            TeamBName = BeaconSeedTeams.Names[t1]
        };
        canvasLayer.AddChild(_scoreOverlay);

        GD.Print($"Beacon Brawl v2: {BeaconSeedTeams.Names[t0]} vs {BeaconSeedTeams.Names[t1]}");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_paused || _match == null || _match.IsOver) return;
        _match.Step();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true } key)
        {
            switch (key.Keycode)
            {
                case Key.Space:
                    _paused = !_paused;
                    GD.Print(_paused ? "PAUSED" : "RUNNING");
                    break;
                case Key.R:
                    StartMatch();
                    break;
                case Key.N:
                    NextMatchup();
                    break;
            }
        }
    }

    private void NextMatchup()
    {
        Team1Index++;
        if (Team1Index >= BeaconSeedTeams.All.Length)
        {
            Team1Index = 0;
            Team0Index = (Team0Index + 1) % BeaconSeedTeams.All.Length;
        }
        if (Team0Index == Team1Index)
            Team1Index = (Team1Index + 1) % BeaconSeedTeams.All.Length;
        StartMatch();
    }
}
