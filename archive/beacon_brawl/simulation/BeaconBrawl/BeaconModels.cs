// ─────────────────────────────────────────────────────────────────────────────
// BeaconModels.cs — JSON-serializable data models for Beacon Brawl v2 output
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AiBtGym.Simulation.BeaconBrawl;

// ── Battle Log ──

public record BeaconBattleLog
{
    public string MatchId { get; init; } = "";
    public int Generation { get; init; }
    public string Team { get; init; } = "";
    public string? TeamColor { get; init; }
    public string Opponent { get; init; } = "";
    public string? OpponentColor { get; init; }
    public MatchResult Result { get; init; }
    public int DurationTicks { get; init; }
    public float DurationSeconds { get; init; }
    public int[] FinalScores { get; init; } = [0, 0];

    public BeaconControlSummary BeaconControl { get; init; } = new();
    public BeaconCombatSummary CombatSummary { get; init; } = new();
    public Dictionary<string, int> ActionFrequency { get; init; } = new();
    public BeaconPositionalSummary PositionalSummary { get; init; } = new();
    public List<BeaconKeyMoment> KeyMoments { get; init; } = [];
    public BeaconReplayData? Replay { get; init; }
}

public record BeaconControlSummary
{
    public float TimeOwningLeftPct { get; init; }
    public float TimeOwningCenterPct { get; init; }
    public float TimeOwningRightPct { get; init; }
    public float TotalBeaconOwnershipPct { get; init; }
    public int CaptureCount { get; init; }
    public int LostCount { get; init; }
}

public record BeaconCombatSummary
{
    public int KillsScored { get; init; }
    public int Deaths { get; init; }
    public float DamageDealt { get; init; }
    public float DamageReceived { get; init; }
    public int FistHits { get; init; }
    public int PistolHits { get; init; }
    public int RifleHits { get; init; }
    public int HookGrabs { get; init; }
    public int ParrySuccesses { get; init; }
}

public record BeaconPositionalSummary
{
    public float AvgX { get; init; }
    public float AvgY { get; init; }
    public float TimeGroundedPct { get; init; }
    public float TimeOnPlatformPct { get; init; }
    public float TimeInBeaconPct { get; init; }
    public float TimeInBasePct { get; init; }
    public float AvgDistToNearestEnemy { get; init; }
}

public record BeaconKeyMoment
{
    public int Tick { get; init; }
    public string Event { get; init; } = "";
    public string Description { get; init; } = "";
}

// ── Replay Data ──

public record BeaconReplayData
{
    public BeaconReplayArena Arena { get; init; } = new();
    public int TeamSize { get; init; } = 2;
    public int CheckpointInterval { get; init; } = 10;
    public int? MatchSeed { get; init; }
    public List<List<AiBtGym.BehaviorTree.BtNode>>? TeamATrees { get; init; }
    public List<List<AiBtGym.BehaviorTree.BtNode>>? TeamBTrees { get; init; }
    public List<BeaconReplayCheckpoint> Checkpoints { get; init; } = [];
    /// <summary>All pistol shots fired (for ballistic arc reconstruction).</summary>
    public List<ReplayPistolShot> PistolShots { get; init; } = [];
    /// <summary>All rifle shots fired in the match (for dotted ray rendering).</summary>
    public List<ReplayRifleShot> RifleShots { get; init; } = [];
    /// <summary>All pawn hit events in the match (for hit-flash rendering).</summary>
    public List<ReplayHitEvent> HitEvents { get; init; } = [];
}

public record BeaconReplayArena
{
    public float Width { get; init; } = 2000;
    public float Height { get; init; } = 800;
    public float WallThickness { get; init; } = 10;
    public float[] PlatformRect { get; init; } = [875, 380, 250, 20];
    public float[][] BeaconCenters { get; init; } = [];
    public int[] BeaconMultipliers { get; init; } = [];
    public float[][] BaseZoneCenters { get; init; } = [];
    /// <summary>Arena modifiers active during the match (null = flat/no modifiers).</summary>
    public AiBtGym.Simulation.ArenaConfig? Modifiers { get; init; }
}

public record BeaconReplayCheckpoint
{
    [JsonPropertyName("t")]
    public int T { get; init; }
    [JsonPropertyName("s")]
    public int[] S { get; init; } = [0, 0]; // scores
    [JsonPropertyName("k")]
    public int[] K { get; init; } = [0, 0]; // kills
    [JsonPropertyName("b")]
    public List<ReplayBeacon> B { get; init; } = []; // beacons
    [JsonPropertyName("p")]
    public List<ReplayPawn> P { get; init; } = []; // pawns
    [JsonPropertyName("f")]
    public List<ReplayHook> F { get; init; } = []; // hooks (grappler only)
    [JsonPropertyName("pr")]
    public List<ReplayProjectile> Pr { get; init; } = []; // projectiles
}

public record ReplayBeacon
{
    [JsonPropertyName("o")]
    public int O { get; init; }
    [JsonPropertyName("cp")]
    public int Cp { get; init; }
    [JsonPropertyName("ct")]
    public bool Ct { get; init; }
}

public record ReplayPawn
{
    [JsonPropertyName("t")]
    public int T { get; init; } // team index
    [JsonPropertyName("r")]
    public int R { get; init; } // role (0=Grappler, 1=Gunner)
    [JsonPropertyName("x")]
    public float X { get; init; }
    [JsonPropertyName("y")]
    public float Y { get; init; }
    [JsonPropertyName("vx")]
    public float Vx { get; init; }
    [JsonPropertyName("vy")]
    public float Vy { get; init; }
    [JsonPropertyName("hp")]
    public float Hp { get; init; }
    [JsonPropertyName("g")]
    public bool G { get; init; } // grounded
    [JsonPropertyName("d")]
    public bool D { get; init; } // dead
    [JsonPropertyName("v")]
    public bool V { get; init; } // vulnerable
    [JsonPropertyName("pa")]
    public bool Pa { get; init; } // parry active
    [JsonPropertyName("wl")]
    public bool Wl { get; init; } // weapon locked (any weapon on extended lockout from parry)
}

public record ReplayHook
{
    [JsonPropertyName("pi")]
    public int Pi { get; init; } // pawn index in AllPawns
    [JsonPropertyName("s")]
    public int S { get; init; } // chain state
    [JsonPropertyName("x")]
    public float X { get; init; }
    [JsonPropertyName("y")]
    public float Y { get; init; }
    [JsonPropertyName("ax")]
    public float Ax { get; init; }
    [JsonPropertyName("ay")]
    public float Ay { get; init; }
    [JsonPropertyName("cl")]
    public float Cl { get; init; }
    [JsonPropertyName("a")]
    public bool A { get; init; } // attached to world
}

public record ReplayProjectile
{
    [JsonPropertyName("x")]
    public float X { get; init; }
    [JsonPropertyName("y")]
    public float Y { get; init; }
    [JsonPropertyName("t")]
    public int T { get; init; } // owner team
}

/// <summary>A pistol shot launch event for ballistic arc reconstruction in the replay viewer.</summary>
public record ReplayPistolShot
{
    [JsonPropertyName("tk")]
    public int Tk { get; init; } // tick fired
    [JsonPropertyName("pi")]
    public int Pi { get; init; } // shooter AllPawns index
    [JsonPropertyName("x")]
    public float X { get; init; } // launch position X
    [JsonPropertyName("y")]
    public float Y { get; init; } // launch position Y
    [JsonPropertyName("vx")]
    public float Vx { get; init; } // launch velocity X
    [JsonPropertyName("vy")]
    public float Vy { get; init; } // launch velocity Y
    [JsonPropertyName("t")]
    public int T { get; init; } // owner team
}

/// <summary>A single rifle shot event for the replay viewer (ray path + hit flag).</summary>
public record ReplayRifleShot
{
    [JsonPropertyName("tk")]
    public int Tk { get; init; } // tick fired
    [JsonPropertyName("pi")]
    public int Pi { get; init; } // shooter AllPawns index
    [JsonPropertyName("sg")]
    public float[][] Sg { get; init; } = []; // ray segments [[x0,y0],[x1,y1],...]
    [JsonPropertyName("h")]
    public bool H { get; init; } // true if hit a pawn
    [JsonPropertyName("hp")]
    public int Hp { get; init; } // hit pawn AllPawns index (-1 if miss)
}

/// <summary>A hit event for the replay viewer (to flash the struck pawn).</summary>
public record ReplayHitEvent
{
    [JsonPropertyName("tk")]
    public int Tk { get; init; } // tick of hit
    [JsonPropertyName("pi")]
    public int Pi { get; init; } // target pawn AllPawns index
}

// ── Team Status ──

public record BeaconTeamStatus
{
    public string TeamId { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Color { get; init; }
    public int Generation { get; init; }

    public RecordData Record { get; init; } = new();
    public float Elo { get; init; } = 1000f;
    public float WinRate { get; init; }

    public BeaconAggregateMetrics AggregateMetrics { get; init; } = new();
    public List<MatchupResult> MatchupResults { get; init; } = [];
}

public record BeaconAggregateMetrics
{
    public float AvgMatchDurationTicks { get; init; }
    public float AvgScoreDiff { get; init; }
    public float AvgBeaconOwnershipPct { get; init; }
    public float AvgKillsPerMatch { get; init; }
    public float AvgDeathsPerMatch { get; init; }
    public int TotalScoreVictories { get; init; }
    public int TotalTimeoutWins { get; init; }
}
