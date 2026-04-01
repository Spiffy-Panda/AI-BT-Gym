// ─────────────────────────────────────────────────────────────────────────────
// ArenaConfig.cs — Declarative arena configuration for per-match map variety
// ─────────────────────────────────────────────────────────────────────────────
//
// Each match receives an ArenaConfig describing which features are active.
// An empty ArenaConfig() produces the current default flat arena.

using System.Collections.Generic;
using System.Linq;

namespace AiBtGym.Simulation;

/// <summary>Full description of an arena layout. All feature lists default to empty (flat arena).</summary>
public record ArenaConfig
{
    // Base geometry
    public float Width { get; init; } = 1500f;
    public float Height { get; init; } = 680f;

    // Platforms: solid rectangles fighters can stand on
    public List<PlatformConfig> Platforms { get; init; } = [];

    // Hazard zones: floor regions that deal tick damage
    public List<HazardZoneConfig> HazardZones { get; init; } = [];

    // Wall friction zones: upper wall regions that slow descent
    public List<WallFrictionZoneConfig> WallFrictionZones { get; init; } = [];

    // Corner bumpers: diagonal deflectors
    public List<CornerBumperConfig> CornerBumpers { get; init; } = [];

    // Health pickups: timed spawn points
    public List<PickupSpawnConfig> Pickups { get; init; } = [];

    // Ceiling shape: optional non-flat ceiling
    public CeilingConfig? Ceiling { get; init; }

    // Destructible walls
    public List<DestructibleWallConfig> DestructibleWalls { get; init; } = [];

    // Arena shrink (pressure ring) in late match
    public ArenaShrinkConfig? Shrink { get; init; }

    /// <summary>True if this config has any active features beyond flat geometry.</summary>
    public bool HasModifiers =>
        Platforms.Count > 0 || HazardZones.Count > 0 || WallFrictionZones.Count > 0 ||
        CornerBumpers.Count > 0 || Pickups.Count > 0 || Ceiling != null ||
        DestructibleWalls.Count > 0 || Shrink != null;
}

/// <summary>A solid platform fighters can stand on.</summary>
public record PlatformConfig
{
    /// <summary>Center X position.</summary>
    public float X { get; init; }
    /// <summary>Top Y position (surface the fighter stands on).</summary>
    public float Y { get; init; }
    public float Width { get; init; }
    public float Height { get; init; } = 15f;
    /// <summary>If true, fighters can jump through from below.</summary>
    public bool OneWay { get; init; } = true;
}

/// <summary>A floor region that deals passive damage per second.</summary>
public record HazardZoneConfig
{
    /// <summary>Left edge X.</summary>
    public float X { get; init; }
    /// <summary>Width of the zone.</summary>
    public float Width { get; init; }
    /// <summary>Damage per second while grounded in this zone.</summary>
    public float DamagePerSecond { get; init; } = 1f;
}

/// <summary>A region on a side wall where descent is slowed.</summary>
public record WallFrictionZoneConfig
{
    /// <summary>Which wall: "left" or "right".</summary>
    public string Side { get; init; } = "left";
    /// <summary>Top Y of the friction region.</summary>
    public float TopY { get; init; }
    /// <summary>Bottom Y of the friction region.</summary>
    public float BottomY { get; init; }
    /// <summary>Velocity Y multiplier applied each tick (e.g. 0.5 = halve downward speed).</summary>
    public float FrictionMultiplier { get; init; } = 0.5f;
}

/// <summary>A diagonal deflector in a corner.</summary>
public record CornerBumperConfig
{
    /// <summary>Which corner: "top_left", "top_right", "bottom_left", "bottom_right".</summary>
    public string Corner { get; init; } = "bottom_left";
    /// <summary>Size of the bumper along each axis.</summary>
    public float Size { get; init; } = 60f;
}

/// <summary>A health pickup that spawns on a timer.</summary>
public record PickupSpawnConfig
{
    public float X { get; init; }
    public float Y { get; init; }
    /// <summary>HP restored on pickup.</summary>
    public float HealAmount { get; init; } = 10f;
    /// <summary>Seconds between spawns.</summary>
    public float RespawnSeconds { get; init; } = 10f;
    /// <summary>Max fighter HP (pickup won't heal above this).</summary>
    public float MaxHp { get; init; } = 100f;
}

/// <summary>Non-flat ceiling defined by center dip.</summary>
public record CeilingConfig
{
    /// <summary>Ceiling Y at the left/right edges (default = normal ceiling).</summary>
    public float EdgeY { get; init; } = 10f;
    /// <summary>Ceiling Y at the horizontal center (lower = ceiling dips down).</summary>
    public float CenterY { get; init; } = 60f;
}

/// <summary>A vertical wall segment that can be destroyed.</summary>
public record DestructibleWallConfig
{
    /// <summary>Center X position.</summary>
    public float X { get; init; }
    /// <summary>Bottom Y (wall extends upward from here).</summary>
    public float BottomY { get; init; }
    /// <summary>Height of the wall (extends upward from BottomY).</summary>
    public float Height { get; init; } = 200f;
    /// <summary>Thickness of the wall.</summary>
    public float Thickness { get; init; } = 15f;
    /// <summary>Starting HP.</summary>
    public float Hp { get; init; } = 50f;
    /// <summary>Damage dealt to the wall per fist hit.</summary>
    public float DamagePerHit { get; init; } = 7f;
}

/// <summary>Arena shrinks in the last portion of the match.</summary>
public record ArenaShrinkConfig
{
    /// <summary>Fraction of match duration when shrinking begins (e.g. 0.6 = last 40%).</summary>
    public float StartFraction { get; init; } = 0.6f;
    /// <summary>Pixels to shrink on each side per interval.</summary>
    public float ShrinkPerStep { get; init; } = 60f;
    /// <summary>Seconds between each shrink step.</summary>
    public float StepIntervalSeconds { get; init; } = 3f;
    /// <summary>Minimum arena width remaining (stops shrinking).</summary>
    public float MinWidth { get; init; } = 200f;
}

// ── Named presets ──

/// <summary>Pre-built arena configurations for common map types.</summary>
public static class ArenaMaps
{
    /// <summary>Current default: flat 1500x680 rectangle, no features.</summary>
    public static ArenaConfig Flat => new();

    /// <summary>Center platform for king-of-the-hill gameplay.</summary>
    public static ArenaConfig KingOfTheHill => new()
    {
        Platforms = [new PlatformConfig { X = 750f, Y = 400f, Width = 300f }]
    };

    /// <summary>Damage strips flanking center, forcing fighters to cross or fight on them.</summary>
    public static ArenaConfig HazardStrips => new()
    {
        HazardZones =
        [
            new HazardZoneConfig { X = 550f, Width = 150f, DamagePerSecond = 30f },
            new HazardZoneConfig { X = 800f, Width = 150f, DamagePerSecond = 30f }
        ]
    };

    /// <summary>Destructible center wall splitting the arena.</summary>
    public static ArenaConfig CenterWall => new()
    {
        DestructibleWalls = [new DestructibleWallConfig { X = 750f, BottomY = 660f }]
    };

    /// <summary>Health pickup at center ground.</summary>
    public static ArenaConfig HealthPickup => new()
    {
        Pickups = [new PickupSpawnConfig { X = 750f, Y = 650f }]
    };

    /// <summary>Inverted-V ceiling: lower in center, higher at edges.</summary>
    public static ArenaConfig DippedCeiling => new()
    {
        Ceiling = new CeilingConfig { EdgeY = 10f, CenterY = 60f }
    };

    /// <summary>Arena shrinks starting early (25% into the match) to guarantee the feature activates.</summary>
    public static ArenaConfig PressureRing => new()
    {
        Shrink = new ArenaShrinkConfig { StartFraction = 0.25f }
    };

    /// <summary>Kitchen sink: platform + hazards + bumpers + pickup + shrink.</summary>
    public static ArenaConfig CombinedArena => new()
    {
        Platforms = [new PlatformConfig { X = 750f, Y = 400f, Width = 300f }],
        HazardZones =
        [
            new HazardZoneConfig { X = 550f, Width = 150f, DamagePerSecond = 30f },
            new HazardZoneConfig { X = 800f, Width = 150f, DamagePerSecond = 30f }
        ],
        CornerBumpers =
        [
            new CornerBumperConfig { Corner = "bottom_left" },
            new CornerBumperConfig { Corner = "bottom_right" }
        ],
        Pickups = [new PickupSpawnConfig { X = 750f, Y = 650f }],
        Shrink = new ArenaShrinkConfig()
    };

    // ── Beacon Brawl presets (2000x800) ──

    /// <summary>Hazard strips flanking each side beacon.</summary>
    public static ArenaConfig BeaconHazards => new()
    {
        Width = 2000f, Height = 800f,
        HazardZones =
        [
            new HazardZoneConfig { X = 350f, Width = 150f, DamagePerSecond = 30f },
            new HazardZoneConfig { X = 600f, Width = 150f, DamagePerSecond = 30f },
            new HazardZoneConfig { X = 1250f, Width = 150f, DamagePerSecond = 30f },
            new HazardZoneConfig { X = 1500f, Width = 150f, DamagePerSecond = 30f }
        ]
    };

    /// <summary>Health pickups near bases and at center.</summary>
    public static ArenaConfig BeaconPickups => new()
    {
        Width = 2000f, Height = 800f,
        Pickups =
        [
            new PickupSpawnConfig { X = 300f, Y = 770f, HealAmount = 15f, RespawnSeconds = 12f, MaxHp = 100f },
            new PickupSpawnConfig { X = 1000f, Y = 770f, HealAmount = 10f, RespawnSeconds = 15f, MaxHp = 100f },
            new PickupSpawnConfig { X = 1700f, Y = 770f, HealAmount = 15f, RespawnSeconds = 12f, MaxHp = 100f }
        ]
    };

    /// <summary>Destructible wall at center — blocks sightlines until broken.</summary>
    public static ArenaConfig BeaconCenterWall => new()
    {
        Width = 2000f, Height = 800f,
        DestructibleWalls = [new DestructibleWallConfig { X = 1000f, BottomY = 780f, Height = 250f, Hp = 60f }]
    };

    /// <summary>Arena shrinks in late match — forces teams to center beacons.</summary>
    public static ArenaConfig BeaconShrink => new()
    {
        Width = 2000f, Height = 800f,
        Shrink = new ArenaShrinkConfig { StartFraction = 0.5f, ShrinkPerStep = 80f, StepIntervalSeconds = 4f, MinWidth = 600f }
    };

    /// <summary>Side platforms for height advantage near beacons.</summary>
    public static ArenaConfig BeaconPlatforms => new()
    {
        Width = 2000f, Height = 800f,
        Platforms =
        [
            new PlatformConfig { X = 500f, Y = 620f, Width = 200f },
            new PlatformConfig { X = 1500f, Y = 620f, Width = 200f }
        ]
    };

    /// <summary>Kitchen sink for beacon brawl: hazards + pickups + shrink.</summary>
    public static ArenaConfig BeaconCombined => new()
    {
        Width = 2000f, Height = 800f,
        HazardZones =
        [
            new HazardZoneConfig { X = 350f, Width = 150f, DamagePerSecond = 30f },
            new HazardZoneConfig { X = 1500f, Width = 150f, DamagePerSecond = 30f }
        ],
        Platforms =
        [
            new PlatformConfig { X = 500f, Y = 620f, Width = 200f },
            new PlatformConfig { X = 1500f, Y = 620f, Width = 200f }
        ],
        Pickups =
        [
            new PickupSpawnConfig { X = 1000f, Y = 770f, HealAmount = 12f, RespawnSeconds = 15f, MaxHp = 100f }
        ],
        Shrink = new ArenaShrinkConfig { StartFraction = 0.5f, ShrinkPerStep = 80f, StepIntervalSeconds = 4f, MinWidth = 600f }
    };

    // ── Merge utility ──

    /// <summary>Merge multiple ArenaConfigs into one. Combines all feature lists; takes the largest Width/Height; last non-null Ceiling/Shrink wins.</summary>
    public static ArenaConfig Merge(params ArenaConfig[] configs)
    {
        if (configs.Length == 0) return new ArenaConfig();
        if (configs.Length == 1) return configs[0];

        float width = configs.Max(c => c.Width);
        float height = configs.Max(c => c.Height);
        CeilingConfig? ceiling = null;
        ArenaShrinkConfig? shrink = null;

        var platforms = new List<PlatformConfig>();
        var hazards = new List<HazardZoneConfig>();
        var frictionZones = new List<WallFrictionZoneConfig>();
        var bumpers = new List<CornerBumperConfig>();
        var pickups = new List<PickupSpawnConfig>();
        var walls = new List<DestructibleWallConfig>();

        foreach (var c in configs)
        {
            platforms.AddRange(c.Platforms);
            hazards.AddRange(c.HazardZones);
            frictionZones.AddRange(c.WallFrictionZones);
            bumpers.AddRange(c.CornerBumpers);
            pickups.AddRange(c.Pickups);
            walls.AddRange(c.DestructibleWalls);
            if (c.Ceiling != null) ceiling = c.Ceiling;
            if (c.Shrink != null) shrink = c.Shrink;
        }

        return new ArenaConfig
        {
            Width = width, Height = height,
            Platforms = platforms, HazardZones = hazards,
            WallFrictionZones = frictionZones, CornerBumpers = bumpers,
            Pickups = pickups, Ceiling = ceiling,
            DestructibleWalls = walls, Shrink = shrink
        };
    }
}
