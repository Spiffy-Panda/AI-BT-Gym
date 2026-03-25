// ─────────────────────────────────────────────────────────────────────────────
// SubTrees.cs — Shared behavior tree patterns used across multiple fighters
// ─────────────────────────────────────────────────────────────────────────────
//
// Each method returns a BtNode tagged with SubTree = "Name" so the viewer
// can highlight shared patterns. Trees are inlined at build time — no
// runtime indirection or BtRunner changes needed.

using System;
using System.Collections.Generic;
using System.Linq;
using AiBtGym.BehaviorTree;
using static AiBtGym.BehaviorTree.BtNode;
using static AiBtGym.BehaviorTree.Var;
using static AiBtGym.BehaviorTree.When;

namespace AiBtGym.Godot;

public static class SubTrees
{
    // ── Registry for API serving ──

    public static readonly Dictionary<string, Func<List<BtNode>>> All = new()
    {
        ["DodgeFists"] = () => [DodgeFists()],
        ["CounterPunch"] = () => [CounterPunch()],
        ["StrikeWithAvailable"] = () => [StrikeWithAvailable()],
        ["AnchorLocks"] = () => [AnchorLocks()],
        ["StayAirborne"] = () => [StayAirborne()],
        ["DiveStrike"] = () => [DiveStrike()],
    };

    // ── Shared Patterns ──

    /// <summary>
    /// Dodge incoming fists by jumping. Wraps left+right dodge in a Selector.
    /// Used by: Red, Green, Cyan, Yellow, Magenta (all gens).
    /// </summary>
    public static BtNode DodgeFists(int range = 200) =>
        Sel("Dodge incoming fists",
            Seq("Dodge left fist", Cond(OppLeftExtending), Cond(InRange(range)), Cond(Grounded), Act("jump")),
            Seq("Dodge right fist", Cond(OppRightExtending), Cond(InRange(range)), Cond(Grounded), Act("jump"))
        ) with { SubTree = "DodgeFists" };

    /// <summary>
    /// Punish opponent's whiffed fist by striking with the first available fist.
    /// Used by: All 6 fighters (Gen001+).
    /// </summary>
    public static BtNode CounterPunch(int range = 220) =>
        Seq("Counter-punch whiff",
            Cond(InRange(range)),
            Sel("Either fist retracting", Cond(OppLeftRetracting), Cond(OppRightRetracting)),
            StrikeWithAvailable()
        ) with { SubTree = "CounterPunch" };

    /// <summary>
    /// Strike with whichever fist is ready (left preferred).
    /// Used by: All 6 fighters in counter-punch, mid-range, and dive contexts.
    /// </summary>
    public static BtNode StrikeWithAvailable() =>
        Sel("Strike with available fist",
            Seq("Left strike", Cond(LeftReady), Act("launch_left_at_opponent")),
            Seq("Right strike", Cond(RightReady), Act("launch_right_at_opponent"))
        ) with { SubTree = "StrikeWithAvailable" };

    /// <summary>
    /// Lock extending fists as approach anchors when far from opponent.
    /// Wraps left+right lock in a Selector.
    /// Used by: Green, Blue, Yellow, Magenta, Red(v2+), Cyan(v2+).
    /// </summary>
    public static BtNode AnchorLocks(int outOfRange = 200, int chainOver = 100) =>
        Sel("Lock fists as anchors",
            Seq("Lock left as anchor", Cond(OutOfRange(outOfRange)), Cond(LeftExtending), Cond(LeftChainOver(chainOver)), Act("lock_left")),
            Seq("Lock right as anchor", Cond(OutOfRange(outOfRange)), Cond(RightExtending), Cond(RightChainOver(chainOver)), Act("lock_right"))
        ) with { SubTree = "AnchorLocks" };

    /// <summary>
    /// Jump whenever grounded to maintain aerial advantage.
    /// Used by: All 6 fighters (standard fallback).
    /// </summary>
    public static BtNode StayAirborne() =>
        Seq("Stay airborne", Cond(Grounded), Act("jump")) with { SubTree = "StayAirborne" };

    /// <summary>
    /// Strike during fast unanchored descent toward opponent.
    /// Used by: Yellow(v2+), Green(v3).
    /// </summary>
    public static BtNode DiveStrike(int velThreshold = 80, int range = 220) =>
        Seq("Raw dive strike",
            Cond(Airborne), Cond(VelY.Gt(velThreshold)), Cond(InRange(range)),
            StrikeWithAvailable()
        ) with { SubTree = "DiveStrike" };
}
