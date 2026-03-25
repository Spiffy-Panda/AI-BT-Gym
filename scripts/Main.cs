// ─────────────────────────────────────────────────────────────────────────────
// Main.cs — Entry point: sets up match, loads BTs, drives simulation + visuals
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using AiBtGym.BehaviorTree;
using AiBtGym.Simulation;

namespace AiBtGym.Godot;

public partial class Main : Node2D
{
    private Match? _match;
    private ArenaRenderer? _arenaRenderer;
    private FighterRenderer? _renderer0;
    private FighterRenderer? _renderer1;
    private DebugOverlay? _debugOverlay;

    // Which test trees to load (indices into TestTrees.All)
    [Export] public int Tree0Index { get; set; } = 0;
    [Export] public int Tree1Index { get; set; } = 1;

    private bool _paused;
    private int _currentPair;
    private static readonly string[] TreeNames = TestTrees.Names;

    public override void _Ready()
    {
        // Arena
        var arena = new Arena(1500, 680);

        // Load BTs
        var trees = TestTrees.All;
        var bt0 = trees[Tree0Index % trees.Length];
        var bt1 = trees[Tree1Index % trees.Length];

        _match = new Match(arena, bt0, bt1);

        // Arena renderer
        _arenaRenderer = new ArenaRenderer { Arena = arena };
        AddChild(_arenaRenderer);

        // Fighter renderers
        _renderer0 = new FighterRenderer
        {
            Fighter = _match.Fighter0,
            TeamColor = new Color(0.9f, 0.2f, 0.2f) // red
        };
        AddChild(_renderer0);

        _renderer1 = new FighterRenderer
        {
            Fighter = _match.Fighter1,
            TeamColor = new Color(0.3f, 0.5f, 1f) // blue
        };
        AddChild(_renderer1);

        // Debug overlay
        var canvasLayer = new CanvasLayer();
        AddChild(canvasLayer);
        _debugOverlay = new DebugOverlay { Match = _match };
        canvasLayer.AddChild(_debugOverlay);

        _currentPair = Tree0Index * 100 + Tree1Index;

        GD.Print($"Match: {TreeNames[Tree0Index % TreeNames.Length]} vs {TreeNames[Tree1Index % TreeNames.Length]}");
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
                    RestartMatch();
                    break;
                case Key.N:
                    NextMatchup();
                    break;
            }
        }
    }

    private void RestartMatch()
    {
        var trees = TestTrees.All;
        var arena = new Arena(1500, 680);
        _match = new Match(arena, trees[Tree0Index % trees.Length], trees[Tree1Index % trees.Length]);
        _renderer0!.Fighter = _match.Fighter0;
        _renderer1!.Fighter = _match.Fighter1;
        _arenaRenderer!.Arena = arena;
        _arenaRenderer.QueueRedraw();
        _debugOverlay!.Match = _match;
        GD.Print($"Restarted: {TreeNames[Tree0Index % TreeNames.Length]} vs {TreeNames[Tree1Index % TreeNames.Length]}");
    }

    private void NextMatchup()
    {
        Tree1Index++;
        if (Tree1Index >= TestTrees.All.Length)
        {
            Tree1Index = 0;
            Tree0Index = (Tree0Index + 1) % TestTrees.All.Length;
        }
        if (Tree0Index == Tree1Index) Tree1Index = (Tree1Index + 1) % TestTrees.All.Length;
        RestartMatch();
    }
}
