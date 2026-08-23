using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

/// Resumes one user-initiated Codex task after a script-triggered Domain Reload.
/// It deliberately asks the model to verify state, rather than replaying unsafe Unity writes.
[InitializeOnLoad]
internal static class CodexUnityTaskRecovery
{
    [Serializable] private sealed class PendingTask { public string ThreadId; public string Cwd; public string Model; public string Effort; public string Status; public int ResumeCount; public string UpdatedUtc; }
    private static readonly string TaskPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "CodexUnityBridge", "pending-task.json");
    private static PendingTask pending;
    private static bool resumeQueued;

    static CodexUnityTaskRecovery()
    {
        Load();
        CompilationPipeline.compilationStarted += _ => MarkInterrupted();
        CompilationPipeline.compilationFinished += _ => QueueResume();
        if (pending != null && pending.Status == "interrupted") QueueResume();
    }

    internal static void Begin(string threadId, string cwd, string model, string effort)
    {
        pending = new PendingTask { ThreadId = threadId, Cwd = cwd, Model = model, Effort = effort, Status = "running", ResumeCount = 0, UpdatedUtc = DateTime.UtcNow.ToString("O") };
        Save();
    }
    internal static bool BlocksUserInteraction => pending != null && (pending.Status == "interrupted" || pending.Status == "resuming");
    internal static string BlockingMessage => pending == null ? string.Empty : pending.Status == "interrupted" ? "Unity 编译完成后正在恢复任务…" : pending.Status == "resuming" ? "Codex 正在恢复上一次任务…" : string.Empty;
    internal static void CancelIfThreadMissing(CodexWorkspaceSnapshot snapshot)
    {
        if (pending == null || snapshot == null || snapshot.Threads.Exists(item => item.Id == pending.ThreadId)) return;
        Debug.LogWarning("[Codex Unity] Cleared stale task-recovery lock because its thread is no longer in the active project chat list.");
        Clear();
    }

    internal static void CompleteNormally() { if (pending == null || pending.Status == "interrupted") return; Clear(); }

    private static void MarkInterrupted()
    {
        if (pending == null || pending.Status != "running") return;
        pending.Status = "interrupted"; pending.UpdatedUtc = DateTime.UtcNow.ToString("O"); Save();
        Debug.LogWarning("[Codex Unity] Active Codex task was interrupted by script compilation; it will resume once after the Bridge is ready.");
    }

    private static void QueueResume()
    {
        if (pending == null || pending.Status != "interrupted" || pending.ResumeCount >= 1 || resumeQueued) return;
        resumeQueued = true;
        EditorApplication.delayCall += ResumeWhenReady;
    }

    private static void ResumeWhenReady()
    {
        if (pending == null || pending.Status != "interrupted") { resumeQueued = false; return; }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || !CodexUnityMcpBridge.IsRunning || !CodexWindow.IsReadyForTaskRecovery(pending.ThreadId))
        {
            EditorApplication.delayCall += ResumeWhenReady;
            return;
        }
        resumeQueued = false;
        pending.Status = "resuming"; pending.ResumeCount++; pending.UpdatedUtc = DateTime.UtcNow.ToString("O"); Save();
        _ = ResumeAsync(pending);
    }

    private static async Task ResumeAsync(PendingTask task)
    {
        const string recoveryPrompt = "上一轮 Unity 任务在写入 C# 脚本后被 Unity 编译/Domain Reload 中断。MCP Bridge 现已恢复。请先调用 unity_get_bridge_status、unity_get_interrupted_operations，并读取/验证当前项目和 Console 状态；确认已完成的步骤后，继续完成用户上一条任务的其余步骤。不要盲目重复可能已生效的写操作。";
        try
        {
            Debug.Log("[Codex Unity] Starting automatic task recovery for thread " + task.ThreadId + ".");
            var response = new StringBuilder();
            await CodexAppServerClient.SendMessageAsync(task.Cwd, task.ThreadId, recoveryPrompt, task.Model, task.Effort,
                delta => response.Append(delta),
                request => request.Respond?.Invoke("decline"),
                request => _ = ResolveMcpElicitationAsync(request),
                _ => { });
            Debug.Log("[Codex Unity] Automatic task recovery turn completed for thread " + task.ThreadId + ". Response length: " + response.Length + ".");
            Clear();
            CodexWindow.NotifyRecoveryCompleted(task.ThreadId);
        }
        catch (Exception error)
        {
            if (pending != null) { pending.Status = "recovery_failed"; pending.UpdatedUtc = DateTime.UtcNow.ToString("O"); Save(); }
            Debug.LogError("[Codex Unity] Automatic task recovery failed: " + error.Message);
            CodexWindow.NotifyRecoveryCompleted(task.ThreadId);
        }
    }

    private static async Task ResolveMcpElicitationAsync(CodexMcpElicitationRequest request)
    {
        var decision = await CodexWindow.RequestMcpElicitationAsync(request.ServerName, request.Message, request.RequestedSchema);
        request.Respond?.Invoke(decision);
    }

    private static void Load() { try { if (File.Exists(TaskPath)) pending = JsonUtility.FromJson<PendingTask>(File.ReadAllText(TaskPath)); } catch (Exception error) { Debug.LogWarning("[Codex Unity] Could not load task recovery state: " + error.Message); } }
    private static void Save() { try { Directory.CreateDirectory(Path.GetDirectoryName(TaskPath)); File.WriteAllText(TaskPath, JsonUtility.ToJson(pending, true)); } catch (Exception error) { Debug.LogWarning("[Codex Unity] Could not save task recovery state: " + error.Message); } }
    private static void Clear() { pending = null; try { if (File.Exists(TaskPath)) File.Delete(TaskPath); } catch (Exception error) { Debug.LogWarning("[Codex Unity] Could not clear task recovery state: " + error.Message); } }
}
