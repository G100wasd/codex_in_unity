using System;
using System.Collections.Generic;

[Serializable] public sealed class CodexThreadSummary { public string Id; public string Name; public string Preview; }
[Serializable] public sealed class CodexFileChange { public string Path; public int Added; public int Removed; }
[Serializable] public sealed class CodexChatMessage { public string Sender; public string Text; public List<CodexFileChange> FileChanges = new List<CodexFileChange>(); public bool IsFileChange => FileChanges != null && FileChanges.Count > 0; }
[Serializable] public sealed class CodexApprovalRequest { public string Title; public string ThreadId; public string ItemId; public string Reason; public string GrantRoot; [NonSerialized] public Action<string> Respond; }
[Serializable] public sealed class CodexModelOption { public string Id; public string DisplayName; public string DefaultEffort; public List<string> SupportedEfforts = new List<string>(); }
[Serializable] public sealed class CodexAccountInfo { public bool IsLoggedIn; public string Email; public string PlanType; }
public sealed class CodexWorkspaceSnapshot { public CodexAccountInfo Account = new CodexAccountInfo(); public List<CodexThreadSummary> Threads = new List<CodexThreadSummary>(); public List<CodexModelOption> Models = new List<CodexModelOption>(); public string Error; }

/// In-memory data only; credentials and tokens are never stored here.
public sealed class CodexWorkspaceStore
{
    public static CodexWorkspaceStore Instance { get; } = new CodexWorkspaceStore();
    public CodexWorkspaceSnapshot Snapshot { get; private set; } = new CodexWorkspaceSnapshot();
    public event Action Changed;
    public void Set(CodexWorkspaceSnapshot snapshot) { Snapshot = snapshot; Changed?.Invoke(); }
}
