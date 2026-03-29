// ─────────────────────────────────────────────────────────────────────────────
// TournamentRegistry.cs — Registry of tournament configurations
// ─────────────────────────────────────────────────────────────────────────────
//
// Each tournament has an ID, display name, and a function that returns
// the contestant set for a given generation number.

using System;
using System.Collections.Generic;
using System.Linq;
using AiBtGym.BehaviorTree;
using AiBtGym.Simulation.BeaconBrawl;

namespace AiBtGym.Godot;

public record ContestantSet(string[] Names, List<BtNode>[] Trees, string[] HexColors);

public enum TournamentGameType { Fight, BeaconBrawl }

public record TournamentConfig(
    string Id,
    string DisplayName,
    Func<int, ContestantSet> GetContestants,
    TournamentGameType GameType = TournamentGameType.Fight
)
{
    /// <summary>For BeaconBrawl tournaments, get team entries instead of individual fighters.</summary>
    public Func<int, List<BeaconTeamEntry>>? GetBeaconTeams { get; init; }
};

public static class TournamentRegistry
{
    private static readonly Dictionary<string, TournamentConfig> _configs = new();
    private static readonly List<string> _order = new(); // insertion order

    static TournamentRegistry()
    {
        // Default tournament: the original evolutionary pipeline
        Register(new TournamentConfig(
            "default", "Season 1",
            gen => gen switch
            {
                >= 3 => new(Gen003Trees.Names, Gen003Trees.All, Gen003Trees.HexColors),
                2    => new(Gen002Trees.Names, Gen002Trees.All, Gen002Trees.HexColors),
                1    => new(Gen001Trees.Names, Gen001Trees.All, Gen001Trees.HexColors),
                _    => new(SeedTrees.Names, SeedTrees.All, SeedTrees.HexColors)
            }
        ));

        // Season 2: starts fresh from latest evolved fighters
        Register(new TournamentConfig(
            "season2", "Season 2",
            gen => new(Gen003Trees.Names, Gen003Trees.All, Gen003Trees.HexColors)
        ));

        // Season 3: 3 veterans (Red, Green, Yellow) + 4 new challengers
        Register(new TournamentConfig(
            "season3", "Season 3",
            gen => new(Season3Trees.Names, Season3Trees.All, Season3Trees.HexColors)
        ));

        // Beacon Brawl: 2v2 territory control game
        Register(new TournamentConfig(
            "beacon_brawl", "Beacon Brawl",
            gen => new(BeaconSeedTeams.Names, [], BeaconSeedTeams.HexColors), // unused for beacon brawl
            GameType: TournamentGameType.BeaconBrawl
        )
        {
            GetBeaconTeams = gen => gen switch
            {
                >= 4 => BB_G4.GetAllEntries(),
                3    => BB_G3.GetAllEntries(),
                2    => BB_G2.GetAllEntries(),
                1    => BB_G1.GetAllEntries(),
                _    => BeaconSeedTeams.GetAllEntries()
            }
        });
    }

    public static void Register(TournamentConfig config)
    {
        _configs[config.Id] = config;
        if (!_order.Contains(config.Id))
            _order.Add(config.Id);
    }

    public static TournamentConfig? Get(string id) =>
        _configs.TryGetValue(id, out var c) ? c : null;

    public static IReadOnlyList<TournamentConfig> GetAll() =>
        _order.Select(id => _configs[id]).ToList();
}
