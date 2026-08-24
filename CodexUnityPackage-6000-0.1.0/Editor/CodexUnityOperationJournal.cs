using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

/// Persists enough operation state to explain a Domain Reload and allow the agent to re-check reality.
[InitializeOnLoad]
internal static class CodexUnityOperationJournal
{
    [Serializable] private sealed class JournalFile { public List<Entry> Entries = new List<Entry>(); }
    [Serializable] internal sealed class Entry { public string Id; public string Tool; public string Arguments; public string Status; public string Detail; public string StartedUtc; public string UpdatedUtc; }
    private static readonly string JournalPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "CodexUnityBridge", "operations.json");
    private static readonly List<Entry> Entries = new List<Entry>();
    private static int nextId;

    static CodexUnityOperationJournal()
    {
        Load();
        CompilationPipeline.compilationStarted += _ => InterruptRunning("Unity script compilation started.");
        CompilationPipeline.compilationFinished += _ => Debug.Log("[Codex Unity MCP] Compilation finished; interrupted operations can now be inspected or safely retried.");
    }

    internal static string Begin(string tool, string arguments)
    {
        var entry = new Entry { Id = "unity-op-" + (++nextId), Tool = tool, Arguments = arguments ?? "{}", Status = "running", StartedUtc = DateTime.UtcNow.ToString("O"), UpdatedUtc = DateTime.UtcNow.ToString("O") };
        Entries.Add(entry); Save();
        Debug.Log("[Codex Unity MCP] Operation started: " + entry.Id + " (" + tool + ").");
        return entry.Id;
    }

    internal static void Complete(string id, bool failed, string detail)
    {
        var entry = Entries.LastOrDefault(item => item.Id == id); if (entry == null) return;
        entry.Status = failed ? "failed" : "completed"; entry.Detail = detail ?? string.Empty; entry.UpdatedUtc = DateTime.UtcNow.ToString("O"); Save();
    }

    internal static void InterruptRunning(string reason)
    {
        var changed = false;
        foreach (var entry in Entries.Where(item => item.Status == "running"))
        {
            entry.Status = "interrupted"; entry.Detail = reason; entry.UpdatedUtc = DateTime.UtcNow.ToString("O"); changed = true;
        }
        if (!changed) return;
        Save(); Debug.LogWarning("[Codex Unity MCP] Marked running operations as interrupted: " + reason);
    }

    internal static string DescribeInterrupted()
    {
        var items = Entries.Where(item => item.Status == "interrupted").TakeLast(20).ToArray();
        return items.Length == 0 ? "No interrupted Unity MCP operations." : string.Join("\n", items.Select(item => item.Id + " | " + item.Tool + " | " + item.UpdatedUtc + "\nReason: " + item.Detail + "\nArguments: " + item.Arguments));
    }

    internal static string DescribeStatus()
    {
        return "Bridge endpoint: " + (CodexUnityMcpBridge.IsRunning ? CodexUnityMcpBridge.Endpoint : "not running")
            + "\nCompiling: " + EditorApplication.isCompiling
            + "\nUpdating assets: " + EditorApplication.isUpdating
            + "\nReady for write tools: " + (!EditorApplication.isCompiling && !EditorApplication.isUpdating)
            + "\nInterrupted operations: " + Entries.Count(item => item.Status == "interrupted");
    }

    private static void Load()
    {
        try
        {
            if (!File.Exists(JournalPath)) return;
            var file = JsonUtility.FromJson<JournalFile>(File.ReadAllText(JournalPath));
            if (file?.Entries != null) Entries.AddRange(file.Entries);
            nextId = Entries.Select(item => ParseId(item.Id)).DefaultIfEmpty(0).Max();
        }
        catch (Exception error) { Debug.LogWarning("[Codex Unity MCP] Could not load operation journal: " + error.Message); }
    }
    private static int ParseId(string id) { var index = (id ?? string.Empty).LastIndexOf('-'); return index >= 0 && int.TryParse(id.Substring(index + 1), out var value) ? value : 0; }
    private static void Save()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)); File.WriteAllText(JournalPath, JsonUtility.ToJson(new JournalFile { Entries = Entries }, true)); }
        catch (Exception error) { Debug.LogWarning("[Codex Unity MCP] Could not save operation journal: " + error.Message); }
    }
}
