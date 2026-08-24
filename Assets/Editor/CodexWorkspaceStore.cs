using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[Serializable] public sealed class CodexThreadSummary { public string Id; public string Name; public string Preview; public bool IsArchived; }
[Serializable] public sealed class CodexFileChange { public string Path; public int Added; public int Removed; }
[Serializable] public sealed class CodexChatMessage { public string Sender; public string Text; public List<CodexFileChange> FileChanges = new List<CodexFileChange>(); public bool IsFileChange => FileChanges != null && FileChanges.Count > 0; }
[Serializable] public sealed class CodexApprovalRequest { public string Title; public string ThreadId; public string ItemId; public string Reason; public string GrantRoot; [NonSerialized] public Action<string> Respond; }
[Serializable] public sealed class CodexMcpElicitationRequest { public string ServerName; public string Message; public string RequestedSchema; [NonSerialized] public Action<string> Respond; }
  [Serializable] public sealed class CodexMcpApiApprovalRequest { public string ToolName; public string Summary; public string Arguments; public bool IsLongRunning; [NonSerialized] public Action<bool> Respond; }
[Serializable] public sealed class CodexModelOption { public string Id; public string DisplayName; public string DefaultEffort; public List<string> SupportedEfforts = new List<string>(); }
[Serializable] public sealed class CodexAccountInfo { public bool IsLoggedIn; public string Email; public string PlanType; }
[Serializable] public sealed class CodexWorkspaceSnapshot { public CodexAccountInfo Account = new CodexAccountInfo(); public List<CodexThreadSummary> Threads = new List<CodexThreadSummary>(); public List<CodexModelOption> Models = new List<CodexModelOption>(); public string Error; }

/// Project-local persisted choices for the three explicit "always allow" options.
/// They never contain credentials and default to false.
internal static class CodexApprovalPreferences
{
    private static string Prefix => "CodexUnity.Approvals." + Application.dataPath.Replace(':', '_').Replace('\\', '_').Replace('/', '_') + ".";
    internal static bool AlwaysAllowFileChanges { get => EditorPrefs.GetBool(Prefix + "Files", false); set => EditorPrefs.SetBool(Prefix + "Files", value); }
    internal static bool AlwaysAllowMcpCalls { get => EditorPrefs.GetBool(Prefix + "Mcp", false); set => EditorPrefs.SetBool(Prefix + "Mcp", value); }
    internal static bool AlwaysAllowApiOperations { get => EditorPrefs.GetBool(Prefix + "Api", false); set => EditorPrefs.SetBool(Prefix + "Api", value); }
    internal static bool GlobalPromptEnabled { get => EditorPrefs.GetBool(Prefix + "GlobalPromptEnabled", false); set => EditorPrefs.SetBool(Prefix + "GlobalPromptEnabled", value); }
    internal static string GlobalPrompt { get => EditorPrefs.GetString(Prefix + "GlobalPrompt", string.Empty); set => EditorPrefs.SetString(Prefix + "GlobalPrompt", value ?? string.Empty); }
    internal static string CustomApiModelName { get => EditorPrefs.GetString(Prefix + "CustomApiModelName", string.Empty); set => EditorPrefs.SetString(Prefix + "CustomApiModelName", value ?? string.Empty); }
    internal static string CustomApiModelUrl { get => EditorPrefs.GetString(Prefix + "CustomApiModelUrl", string.Empty); set => EditorPrefs.SetString(Prefix + "CustomApiModelUrl", value ?? string.Empty); }
    internal static string CustomApiKey { get => SessionState.GetString(Prefix + "CustomApiKey", string.Empty); set => SessionState.SetString(Prefix + "CustomApiKey", value ?? string.Empty); }
    // This only records that the project has passed this plugin's welcome screen.
    // It never represents, reads, or modifies the user's Codex account session.
    internal static bool HasCompletedLoginSetup { get => EditorPrefs.GetBool(Prefix + "HasCompletedLoginSetup", false); set => EditorPrefs.SetBool(Prefix + "HasCompletedLoginSetup", value); }
}

/// In-memory data only; credentials and tokens are never stored here.
public sealed class CodexWorkspaceStore
{
    private const string SnapshotKey = "CodexUnity.WorkspaceSnapshot";
    public static CodexWorkspaceStore Instance { get; } = new CodexWorkspaceStore();
    public CodexWorkspaceSnapshot Snapshot { get; private set; }
    public event Action Changed;
    private CodexWorkspaceStore()
    {
        try { Snapshot = JsonUtility.FromJson<CodexWorkspaceSnapshot>(SessionState.GetString(SnapshotKey, string.Empty)); } catch { }
        if (Snapshot == null) Snapshot = new CodexWorkspaceSnapshot();
    }
    public void Set(CodexWorkspaceSnapshot snapshot)
    {
        Snapshot = snapshot ?? new CodexWorkspaceSnapshot();
        try { SessionState.SetString(SnapshotKey, JsonUtility.ToJson(Snapshot)); } catch { }
        Changed?.Invoke();
    }
    public void RenameThread(string threadId, string name)
    {
        var thread = Snapshot.Threads.Find(item => item.Id == threadId);
        if (thread == null) return;
        thread.Name = name;
        Set(Snapshot);
    }
    public CodexWorkspaceSnapshot MergeKnownThreads(CodexWorkspaceSnapshot fetched)
    {
        fetched = fetched ?? new CodexWorkspaceSnapshot();
        foreach (var known in Snapshot.Threads)
        {
            if (string.IsNullOrWhiteSpace(known.Id) || fetched.Threads.Exists(item => item.Id == known.Id)) continue;
            // Some App Server builds do not return appServer-origin threads from
            // thread/list. Keep the project-local summary until an explicit
            // delete removes it, rather than erasing it on every refresh.
            fetched.Threads.Add(new CodexThreadSummary
            {
                Id = known.Id,
                Name = known.Name,
                Preview = known.Preview,
                IsArchived = known.IsArchived
            });
        }
        return fetched;
    }
    public void RemoveThread(string threadId)
    {
        Snapshot.Threads.RemoveAll(item => item.Id == threadId);
        Set(Snapshot);
    }
}
