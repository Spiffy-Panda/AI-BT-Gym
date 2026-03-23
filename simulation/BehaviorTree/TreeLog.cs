// ─────────────────────────────────────────────────────────────────────────────
// TreeLog.cs — JSONL logging and loading for behavior tree history
// ─────────────────────────────────────────────────────────────────────────────
//
// Each record wraps a BT with metadata (name, generation, timestamp, tags).
// Records are appended to a .jsonl file (one JSON object per line) for easy
// data-science consumption. Older trees can be deserialized and run directly.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiBtGym.BehaviorTree;

/// <summary>A single logged behavior tree with metadata.</summary>
public record TreeRecord
{
    public string Name { get; init; } = "";
    public int Generation { get; init; }
    public string Timestamp { get; init; } = "";
    public string? ParentName { get; init; }
    public Dictionary<string, string>? Tags { get; init; }
    public List<BtNode> Roots { get; init; } = [];
}

/// <summary>
/// Append-only JSONL log for behavior trees. One JSON object per line.
/// Supports logging new trees, loading all history, and loading specific
/// trees by name or generation for replay.
/// </summary>
public static class TreeLog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Append a single tree record to the log file.</summary>
    public static void Log(string path, string name, List<BtNode> roots,
        int generation = 0, string? parentName = null, Dictionary<string, string>? tags = null)
    {
        var record = new TreeRecord
        {
            Name = name,
            Generation = generation,
            Timestamp = DateTime.UtcNow.ToString("o"),
            ParentName = parentName,
            Tags = tags,
            Roots = roots
        };
        Append(path, record);
    }

    /// <summary>Append a pre-built record to the log file.</summary>
    public static void Append(string path, TreeRecord record)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var line = JsonSerializer.Serialize(record, Options);
        File.AppendAllText(path, line + "\n");
    }

    /// <summary>Append multiple records in one call.</summary>
    public static void AppendAll(string path, IEnumerable<TreeRecord> records)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var writer = File.AppendText(path);
        foreach (var record in records)
            writer.WriteLine(JsonSerializer.Serialize(record, Options));
    }

    /// <summary>Load all records from a JSONL log file.</summary>
    public static List<TreeRecord> LoadAll(string path)
    {
        if (!File.Exists(path)) return [];

        var records = new List<TreeRecord>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var record = JsonSerializer.Deserialize<TreeRecord>(line, Options);
            if (record != null) records.Add(record);
        }
        return records;
    }

    /// <summary>Load the most recent record with the given name.</summary>
    public static TreeRecord? LoadByName(string path, string name) =>
        LoadAll(path).LastOrDefault(r => r.Name == name);

    /// <summary>Load all records from a specific generation.</summary>
    public static List<TreeRecord> LoadGeneration(string path, int generation) =>
        LoadAll(path).Where(r => r.Generation == generation).ToList();

    /// <summary>Load just the BT roots for a named tree (ready to run).</summary>
    public static List<BtNode>? LoadRoots(string path, string name) =>
        LoadByName(path, name)?.Roots;

    /// <summary>Load the latest generation number in the log.</summary>
    public static int LatestGeneration(string path)
    {
        var records = LoadAll(path);
        return records.Count > 0 ? records.Max(r => r.Generation) : -1;
    }
}
