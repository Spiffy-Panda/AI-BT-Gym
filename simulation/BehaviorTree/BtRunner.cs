// ─────────────────────────────────────────────────────────────────────────────
// BtRunner.cs — Behavior tree tick engine with condition expression evaluator
// ─────────────────────────────────────────────────────────────────────────────
//
// STATELESS except for cooldown tracking. Every Tick() evaluates the full
// tree from scratch — no Running status.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AiBtGym.BehaviorTree;

public class BehaviorTreeRunner
{
    private readonly List<BtNode> _roots;

    // Cooldown state: maps node reference → tick when cooldown expires
    private readonly Dictionary<BtNode, int> _cooldownExpiry = new();

    public BehaviorTreeRunner(List<BtNode> roots)
    {
        _roots = roots;
    }

    /// <summary>Tick all root nodes against the given context.</summary>
    public void Apply(IBtContext context)
    {
        foreach (var root in _roots)
            Tick(root, context);
    }

    private BtStatus Tick(BtNode node, IBtContext context)
    {
        return node.Type switch
        {
            BtNodeType.Sequence => TickSequence(node, context),
            BtNodeType.Selector => TickSelector(node, context),
            BtNodeType.Parallel => TickParallel(node, context),
            BtNodeType.Inverter => TickInverter(node, context),
            BtNodeType.Repeater => TickRepeater(node, context),
            BtNodeType.Cooldown => TickCooldown(node, context),
            BtNodeType.ConditionGate => TickConditionGate(node, context),
            BtNodeType.Condition => EvalCondition(node.Value ?? "", context) ? BtStatus.Success : BtStatus.Failure,
            BtNodeType.Action => context.ExecuteAction(node.Value ?? ""),
            _ => BtStatus.Failure,
        };
    }

    // ── Composites ──

    private BtStatus TickSequence(BtNode node, IBtContext context)
    {
        if (node.Children == null) return BtStatus.Success;
        foreach (var child in node.Children)
        {
            if (Tick(child, context) == BtStatus.Failure)
                return BtStatus.Failure;
        }
        return BtStatus.Success;
    }

    private BtStatus TickSelector(BtNode node, IBtContext context)
    {
        if (node.Children == null) return BtStatus.Failure;
        foreach (var child in node.Children)
        {
            if (Tick(child, context) == BtStatus.Success)
                return BtStatus.Success;
        }
        return BtStatus.Failure;
    }

    private BtStatus TickParallel(BtNode node, IBtContext context)
    {
        if (node.Children == null) return BtStatus.Success;
        var policy = node.Policy ?? ParallelPolicy.RequireAll;
        int successes = 0, failures = 0;

        foreach (var child in node.Children)
        {
            if (Tick(child, context) == BtStatus.Success)
                successes++;
            else
                failures++;
        }

        return policy switch
        {
            ParallelPolicy.RequireAll => failures > 0 ? BtStatus.Failure : BtStatus.Success,
            ParallelPolicy.RequireOne => successes > 0 ? BtStatus.Success : BtStatus.Failure,
            _ => BtStatus.Failure
        };
    }

    // ── Decorators ──

    private BtStatus TickInverter(BtNode node, IBtContext context)
    {
        var child = node.Children?.FirstOrDefault();
        if (child == null) return BtStatus.Failure;
        return Tick(child, context) == BtStatus.Success ? BtStatus.Failure : BtStatus.Success;
    }

    private BtStatus TickRepeater(BtNode node, IBtContext context)
    {
        var child = node.Children?.FirstOrDefault();
        if (child == null) return BtStatus.Failure;
        int count = int.TryParse(node.Value, out var n) ? n : 1;

        for (int i = 0; i < count; i++)
        {
            if (Tick(child, context) == BtStatus.Failure)
                return BtStatus.Failure;
        }
        return BtStatus.Success;
    }

    private BtStatus TickCooldown(BtNode node, IBtContext context)
    {
        var child = node.Children?.FirstOrDefault();
        if (child == null) return BtStatus.Failure;
        int cooldownTicks = int.TryParse(node.Value, out var n) ? n : 60;

        if (_cooldownExpiry.TryGetValue(node, out int expiry) && context.CurrentTick < expiry)
            return BtStatus.Failure; // still on cooldown

        var result = Tick(child, context);
        if (result == BtStatus.Success)
            _cooldownExpiry[node] = context.CurrentTick + cooldownTicks;

        return result;
    }

    private BtStatus TickConditionGate(BtNode node, IBtContext context)
    {
        if (!EvalCondition(node.Value ?? "", context))
            return BtStatus.Failure;

        var child = node.Children?.FirstOrDefault();
        return child != null ? Tick(child, context) : BtStatus.Success;
    }

    // ── Condition expression evaluator ──

    public static bool EvalCondition(string cond, IBtContext context)
    {
        if (string.IsNullOrWhiteSpace(cond)) return false;
        var c = cond.Trim().ToLowerInvariant();

        if (c is "always" or "true") return true;
        if (c is "never" or "false") return false;

        foreach (var op in new[] { "<=", ">=", "!=", "==", "<", ">" })
        {
            var idx = c.IndexOf(op, StringComparison.Ordinal);
            if (idx < 0) continue;

            var lhs = c[..idx].Trim();
            var rhs = c[(idx + op.Length)..].Trim();
            float lv = EvalExpr(lhs, context);
            float rv = EvalExpr(rhs, context);

            return op switch
            {
                "<" => lv < rv,
                "<=" => lv <= rv,
                ">" => lv > rv,
                ">=" => lv >= rv,
                "==" => MathF.Abs(lv - rv) < 0.001f,
                "!=" => MathF.Abs(lv - rv) >= 0.001f,
                _ => false,
            };
        }

        return false;
    }

    private static float EvalExpr(string expr, IBtContext context)
    {
        expr = expr.Trim();

        foreach (var op in new[] { '+', '-' })
        {
            var idx = expr.LastIndexOf(op);
            if (idx > 0)
            {
                float lv = EvalAtom(expr[..idx].Trim(), context);
                float rv = EvalAtom(expr[(idx + 1)..].Trim(), context);
                return op == '+' ? lv + rv : lv - rv;
            }
        }

        foreach (var op in new[] { '*', '/' })
        {
            var idx = expr.LastIndexOf(op);
            if (idx > 0)
            {
                float lv = EvalAtom(expr[..idx].Trim(), context);
                float rv = EvalAtom(expr[(idx + 1)..].Trim(), context);
                return op == '*' ? lv * rv : (rv != 0 ? lv / rv : 0);
            }
        }

        return EvalAtom(expr, context);
    }

    private static float EvalAtom(string atom, IBtContext context)
    {
        atom = atom.Trim();
        if (float.TryParse(atom, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float num))
            return num;
        return context.ResolveVariable(atom);
    }
}
