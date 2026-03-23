// ─────────────────────────────────────────────────────────────────────────────
// TournamentModels.cs — JSON-serializable data models for tournament output
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiBtGym.Simulation;

// ── Shared serialization options ──

public static class TournamentJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };
}

// ── Battle Log ──

public record BattleLog
{
    public string MatchId { get; init; } = "";
    public int Generation { get; init; }
    public string Fighter { get; init; } = "";
    public string Opponent { get; init; } = "";
    public int FighterBtVersion { get; init; }
    public int OpponentBtVersion { get; init; }
    public MatchResult Result { get; init; }
    public int DurationTicks { get; init; }
    public float DurationSeconds { get; init; }

    public FinalStateData FinalState { get; init; } = new();
    public DamageSummaryData DamageSummary { get; init; } = new();
    public Dictionary<string, int> ActionFrequency { get; init; } = new();
    public PositionalSummaryData PositionalSummary { get; init; } = new();
    public GrappleStatsData GrappleStats { get; init; } = new();
    public List<PhaseData> PhaseBreakdown { get; init; } = [];
    public List<HitEvent> HitLog { get; init; } = [];
    public List<KeyMoment> KeyMoments { get; init; } = [];
}

public enum MatchResult { Win, Loss, Draw }

public record FinalStateData
{
    public float FighterHp { get; init; }
    public float OpponentHp { get; init; }
    public float[] FighterPos { get; init; } = [0, 0];
    public float[] OpponentPos { get; init; } = [0, 0];
}

public record DamageSummaryData
{
    public float Dealt { get; init; }
    public float Received { get; init; }
    public int HitsLanded { get; init; }
    public int HitsTaken { get; init; }
    public int LaunchesAtOpponent { get; init; }
    public float HitAccuracy { get; init; }
}

public record PositionalSummaryData
{
    public float AvgX { get; init; }
    public float AvgY { get; init; }
    public float TimeGroundedPct { get; init; }
    public float TimeAirbornePct { get; init; }
    public float TimeNearOpponentPct { get; init; }
    public float AvgDistanceToOpponent { get; init; }
}

public record GrappleStatsData
{
    public int AttachCount { get; init; }
    public float AvgAttachedDurationTicks { get; init; }
    public int CeilingAttaches { get; init; }
    public int WallAttaches { get; init; }
}

public record PhaseData
{
    public string Phase { get; init; } = "";
    public int[] TickRange { get; init; } = [0, 0];
    public float DamageDealt { get; init; }
    public float DamageReceived { get; init; }
    public List<string> DominantActions { get; init; } = [];
    public float[] HpAtEnd { get; init; } = [0, 0];
}

public record HitEvent
{
    public int Tick { get; init; }
    public string Attacker { get; init; } = "";
    public string Hand { get; init; } = "";
    public float Damage { get; init; }
    public float[] FighterPos { get; init; } = [0, 0];
    public float[] OpponentPos { get; init; } = [0, 0];
}

public record KeyMoment
{
    public int Tick { get; init; }
    public string Event { get; init; } = "";
    public string Description { get; init; } = "";
}

// ── Fighter Status ──

public record FighterStatus
{
    public string FighterId { get; init; } = "";
    public string Name { get; init; } = "";
    public int Generation { get; init; }
    public int BtVersion { get; init; }

    public RecordData Record { get; init; } = new();
    public float Elo { get; init; } = 1000f;
    public float WinRate { get; init; }

    public AggregateMetrics AggregateMetrics { get; init; } = new();
    public List<MatchupResult> MatchupResults { get; init; } = [];
}

public record RecordData
{
    public int Wins { get; init; }
    public int Losses { get; init; }
    public int Draws { get; init; }
    public int MatchesPlayed { get; init; }
}

public record AggregateMetrics
{
    public float AvgDamageDealt { get; init; }
    public float AvgDamageReceived { get; init; }
    public float AvgMatchDurationTicks { get; init; }
    public float AvgHitAccuracy { get; init; }
    public float AvgTimeGroundedPct { get; init; }
    public int TotalKnockouts { get; init; }
    public int TotalTimeoutsWon { get; init; }
    public int TotalTimeoutsLost { get; init; }
}

public record MatchupResult
{
    public string Opponent { get; init; } = "";
    public MatchResult Result { get; init; }
    public string MatchFile { get; init; } = "";
}

// ── Generation Summary ──

public record GenerationSummary
{
    public int Generation { get; init; }
    public string Timestamp { get; init; } = "";
    public int FighterCount { get; init; }
    public int TotalMatches { get; init; }
    public string TournamentFormat { get; init; } = "round_robin";

    public List<LeaderboardEntry> Leaderboard { get; init; } = [];
    public MetaStats MetaStats { get; init; } = new();
}

public record LeaderboardEntry
{
    public int Rank { get; init; }
    public string FighterId { get; init; } = "";
    public string Name { get; init; } = "";
    public float Elo { get; init; }
    public string Record { get; init; } = "";
}

public record MetaStats
{
    public float AvgMatchDurationTicks { get; init; }
    public float KnockoutRate { get; init; }
    public float DrawRate { get; init; }
}
