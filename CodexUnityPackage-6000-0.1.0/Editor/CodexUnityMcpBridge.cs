using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

/// Owns the localhost MCP endpoint for the current Unity Editor process.
/// The endpoint is deliberately not reachable from the LAN.
[InitializeOnLoad]
public static class CodexUnityMcpBridge
{
    private const string ShouldRunKey = "CodexUnity.McpBridgeShouldRun";
    private const string RestartAfterReloadKey = "CodexUnity.McpBridgeRestartAfterReload";
    private static CodexUnityMcpHttpServer server;
    private static bool restoreQueued;

    public static bool IsRunning => server != null && server.IsRunning;
    public static string Endpoint => server == null ? string.Empty : server.Endpoint;

    static CodexUnityMcpBridge()
    {
        AssemblyReloadEvents.beforeAssemblyReload += InterruptForAssemblyReload;
        EditorApplication.quitting += StopForEditorExit;
        if (SessionState.GetBool(ShouldRunKey, false)) ScheduleRestore();
    }

    public static void EnsureStarted()
    {
        if (IsRunning) return;
        try
        {
            server = new CodexUnityMcpHttpServer();
            server.Start();
            SessionState.SetBool(ShouldRunKey, true);
            SessionState.EraseBool(RestartAfterReloadKey);
            Debug.Log("[Codex Unity MCP] Bridge connected: " + Endpoint + " (127.0.0.1 only).");
        }
        catch (System.Exception error)
        {
            server = null;
            Debug.LogError("[Codex Unity MCP] Bridge failed to start: " + error.Message);
        }
    }

    public static void Stop()
    {
        StopInternal(true, "stopped by plugin");
    }

    private static void InterruptForAssemblyReload()
    {
        if (!IsRunning && !SessionState.GetBool(ShouldRunKey, false)) return;
        SessionState.SetBool(RestartAfterReloadKey, true);
        CodexUnityOperationJournal.InterruptRunning("Unity Domain Reload interrupted the operation; re-check project state before retrying.");
        Debug.Log("[Codex Unity MCP] Bridge interrupted: Unity is recompiling/reloading Editor assemblies. Endpoint was " + Endpoint + ".");
        StopInternal(false, "assembly reload");
    }

    private static void StopForEditorExit()
    {
        StopInternal(true, "Unity Editor is closing");
    }

    private static void StopInternal(bool clearRestartIntent, string reason)
    {
        if (server != null)
        {
            server.Stop();
            server = null;
            Debug.Log("[Codex Unity MCP] Bridge disconnected: " + reason + ".");
        }
        if (clearRestartIntent)
        {
            SessionState.EraseBool(ShouldRunKey);
            SessionState.EraseBool(RestartAfterReloadKey);
        }
    }

    private static void ScheduleRestore()
    {
        if (restoreQueued) return;
        restoreQueued = true;
        EditorApplication.delayCall += RestoreWhenEditorIsReady;
    }

    private static void RestoreWhenEditorIsReady()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += RestoreWhenEditorIsReady;
            return;
        }

        restoreQueued = false;
        if (!SessionState.GetBool(RestartAfterReloadKey, false) && !SessionState.GetBool(ShouldRunKey, false)) return;
        EnsureStarted();
        Debug.Log("[Codex Unity MCP] Bridge recovered after Unity assembly reload: " + Endpoint + ".");
    }
}
