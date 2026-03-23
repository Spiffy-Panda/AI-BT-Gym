// ─────────────────────────────────────────────────────────────────────────────
// BtCore.cs — Types, enums, interfaces, and the BtNode record
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace AiBtGym.BehaviorTree;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BtNodeType
{
    // Composites
    Sequence,
    Selector,
    Parallel,

    // Decorators
    Inverter,
    Repeater,
    Cooldown,
    ConditionGate,

    // Leaves
    Condition,
    Action
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BtStatus { Success, Failure }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ParallelPolicy { RequireAll, RequireOne }

/// <summary>
/// Interface that the host project implements to bridge the BT to its world state.
/// </summary>
public interface IBtContext
{
    /// <summary>Current simulation tick, used for cooldown tracking.</summary>
    int CurrentTick { get; }

    /// <summary>
    /// Resolve a named variable to a float value.
    /// Return 0 for unknown variable names.
    /// </summary>
    float ResolveVariable(string name);

    /// <summary>
    /// Execute a named action. Return Success or Failure.
    /// </summary>
    BtStatus ExecuteAction(string action);
}

/// <summary>
/// A single node in a behavior tree. Serializable to/from JSON.
///
/// - Composites use Children, optionally Policy (for Parallel).
/// - Decorators use Children[0] as their single child, Value for config.
/// - Leaves use Value (condition expression or action name).
/// </summary>
public record BtNode(
    BtNodeType Type,
    string? Value = null,
    List<BtNode>? Children = null,
    ParallelPolicy? Policy = null)
{
    // ── Builder helpers ──

    // Composites
    public static BtNode Seq(params BtNode[] children) =>
        new(BtNodeType.Sequence, Children: children.ToList());

    public static BtNode Sel(params BtNode[] children) =>
        new(BtNodeType.Selector, Children: children.ToList());

    public static BtNode Par(ParallelPolicy policy, params BtNode[] children) =>
        new(BtNodeType.Parallel, Children: children.ToList(), Policy: policy);

    public static BtNode Par(params BtNode[] children) =>
        new(BtNodeType.Parallel, Children: children.ToList(), Policy: ParallelPolicy.RequireAll);

    // Decorators (wrap a single child)
    public static BtNode Inv(BtNode child) =>
        new(BtNodeType.Inverter, Children: [child]);

    public static BtNode Rep(int count, BtNode child) =>
        new(BtNodeType.Repeater, Value: count.ToString(), Children: [child]);

    public static BtNode Cool(int ticks, BtNode child) =>
        new(BtNodeType.Cooldown, Value: ticks.ToString(), Children: [child]);

    public static BtNode Gate(string condition, BtNode child) =>
        new(BtNodeType.ConditionGate, Value: condition, Children: [child]);

    // Leaves
    public static BtNode Cond(string expr) =>
        new(BtNodeType.Condition, expr);

    public static BtNode Act(string action) =>
        new(BtNodeType.Action, action);
}
