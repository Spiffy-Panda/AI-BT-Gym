// ─────────────────────────────────────────────────────────────────────────────
// BtSerializer.cs — JSON round-trip for behavior trees
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiBtGym.BehaviorTree;

public static class BtSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(List<BtNode> roots) =>
        JsonSerializer.Serialize(roots, Options);

    public static List<BtNode> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<BtNode>>(json, Options) ?? [];

    public static List<BtNode> LoadFromFile(string path) =>
        Deserialize(File.ReadAllText(path));

    public static void SaveToFile(List<BtNode> roots, string path) =>
        File.WriteAllText(path, Serialize(roots));
}
