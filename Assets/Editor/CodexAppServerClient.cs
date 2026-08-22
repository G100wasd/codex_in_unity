using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityDebug = UnityEngine.Debug;

public static class CodexAppServerClient
{
    private static readonly SemaphoreSlim RequestGate = new SemaphoreSlim(1, 1);
    private const string AppServerProcessIdKey = "CodexUnity.AppServerProcessId";
    private const string AppServerProcessStartTicksKey = "CodexUnity.AppServerProcessStartTicks";
    private static Process sharedProcess;
    private static string sharedCwd;
    private static string sharedMcpEndpoint;

    public static async Task SendMessageAsync(string cwd, string threadId, string text, string model, string effort, Action<string> onAssistantDelta, Action<CodexApprovalRequest> onApprovalRequested, Action<CodexMcpElicitationRequest> onMcpElicitationRequested, Action<List<CodexFileChange>> onFileChanges)
    {
        await RequestGate.WaitAsync();
        try
        {
            var process = await GetSharedProcessAsync(cwd);
            await CallAsync(process, 2, "thread/resume", "{\"threadId\":\"" + Escape(threadId) + "\",\"cwd\":\"" + Escape(cwd) + "\"}");
            var settings = string.IsNullOrEmpty(model) ? string.Empty : ",\"model\":\"" + Escape(model) + "\"";
            if (!string.IsNullOrEmpty(effort)) settings += ",\"effort\":\"" + Escape(effort) + "\"";
            var started = await CallAsync(process, 3, "turn/start", "{\"threadId\":\"" + Escape(threadId) + "\",\"cwd\":\"" + Escape(cwd) + "\",\"input\":[{\"type\":\"text\",\"text\":\"" + Escape(text) + "\"}]" + settings + "}");
            var turnId = Text(started.GetProperty("turn"), "id");
            await ReadAssistantReplyAsync(process, threadId, turnId, onAssistantDelta, onApprovalRequested, onMcpElicitationRequested, onFileChanges);
        }
        finally { RequestGate.Release(); }
    }
    public static async Task<CodexThreadSummary> CreateThreadAsync(string cwd)
    {
        await RequestGate.WaitAsync(); try {
        var process = await GetSharedProcessAsync(cwd);
        var result = await CallAsync(process, 2, "thread/start", "{\"cwd\":\"" + Escape(cwd) + "\",\"ephemeral\":false,\"threadSource\":\"appServer\"}");
        var thread = result.TryGetProperty("thread", out var nested) ? nested : result;
        var id = Text(thread, "id");
        await CallAsync(process, 3, "thread/name/set", "{\"threadId\":\"" + Escape(id) + "\",\"name\":\"新聊天\"}");
        return new CodexThreadSummary { Id = id, Name = "新聊天", Preview = Text(thread, "preview") }; } finally { RequestGate.Release(); }
    }
    public static async Task<CodexWorkspaceSnapshot> FetchAsync(string cwd)
    {
        await RequestGate.WaitAsync(); try {
        var process = await GetSharedProcessAsync(cwd);
        var account = await CallAsync(process, 2, "account/read", "{\"refreshToken\":false}");
        var threads = await CallAsync(process, 3, "thread/list", "{\"cwd\":\"" + Escape(cwd) + "\",\"limit\":100}");
        var snapshot = new CodexWorkspaceSnapshot { Account = ParseAccount(account) };
        if (threads.TryGetProperty("data", out var data)) foreach (var item in data.EnumerateArray()) snapshot.Threads.Add(new CodexThreadSummary { Id = Text(item, "id"), Name = Text(item, "name", "未命名对话"), Preview = Text(item, "preview") });
        try
        {
            var models = await CallAsync(process, 4, "model/list", "{\"limit\":100}");
            if (models.TryGetProperty("data", out var modelData)) foreach (var item in modelData.EnumerateArray())
            {
                if (item.TryGetProperty("hidden", out var hidden) && hidden.GetBoolean()) continue;
                var option = new CodexModelOption { Id = Text(item, "model"), DisplayName = Text(item, "displayName"), DefaultEffort = Text(item, "defaultReasoningEffort") };
                if (item.TryGetProperty("supportedReasoningEfforts", out var efforts)) foreach (var value in efforts.EnumerateArray()) option.SupportedEfforts.Add(Text(value, "reasoningEffort"));
                if (!string.IsNullOrEmpty(option.Id)) snapshot.Models.Add(option);
            }
        }
        catch { }
        return snapshot; } finally { RequestGate.Release(); }
    }
    public static async Task<List<CodexChatMessage>> ReadThreadAsync(string cwd, string threadId)
    {
        await RequestGate.WaitAsync(); try {
        var process = await GetSharedProcessAsync(cwd);
        var result = await CallAsync(process, 4, "thread/read", "{\"threadId\":\"" + Escape(threadId) + "\",\"includeTurns\":true}");
        var messages = new List<CodexChatMessage>();
        var thread = result.GetProperty("thread");
        if (!thread.TryGetProperty("turns", out var turns)) return messages;
        foreach (var turn in turns.EnumerateArray())
        {
            if (!turn.TryGetProperty("items", out var items)) continue;
            foreach (var item in items.EnumerateArray())
            {
                var type = Text(item, "type");
                if (type == "agentMessage")
                {
                    var text = Text(item, "text");
                    if (text.Length > 0) messages.Add(new CodexChatMessage { Sender = "Codex", Text = text });
                }
                else if (type == "fileChange" && item.TryGetProperty("changes", out var changes))
                {
                    messages.Add(new CodexChatMessage { FileChanges = ParseFileChanges(changes) });
                }
                else if (type == "userMessage" && item.TryGetProperty("content", out var content))
                    foreach (var input in content.EnumerateArray()) if (Text(input, "type") == "text") { var text = Text(input, "text"); if (text.Length > 0) messages.Add(new CodexChatMessage { Sender = "你", Text = text }); }
            }
        }
        return messages; } finally { RequestGate.Release(); }
    }

    private static async Task<Process> GetSharedProcessAsync(string cwd)
    {
        var mcpEndpoint = CodexUnityMcpBridge.Endpoint;
        if (sharedProcess != null && !sharedProcess.HasExited && sharedCwd == cwd && sharedMcpEndpoint == mcpEndpoint) return sharedProcess;
        StopOwnedAppServer();
        sharedProcess = await StartAsync(mcpEndpoint); sharedCwd = cwd; sharedMcpEndpoint = mcpEndpoint;
        SessionState.SetInt(AppServerProcessIdKey, sharedProcess.Id);
        SessionState.SetString(AppServerProcessStartTicksKey, sharedProcess.StartTime.ToUniversalTime().Ticks.ToString());
        await CallAsync(sharedProcess, 1, "initialize", "{\"clientInfo\":{\"name\":\"codex-unity\",\"version\":\"0.1.0\"}}");
        UnityDebug.Log(string.IsNullOrEmpty(mcpEndpoint)
            ? "[Codex Unity] App Server started without a Unity MCP endpoint."
            : "[Codex Unity] App Server started with Unity MCP endpoint: " + mcpEndpoint);
        return sharedProcess;
    }

    [InitializeOnLoadMethod]
    private static void RegisterEditorShutdown()
    {
        AssemblyReloadEvents.beforeAssemblyReload -= StopOwnedAppServer;
        AssemblyReloadEvents.beforeAssemblyReload += StopOwnedAppServer;
        EditorApplication.quitting -= StopOwnedAppServer;
        EditorApplication.quitting += StopOwnedAppServer;
    }

    private static void StopOwnedAppServer()
    {
        var processId = sharedProcess != null ? sharedProcess.Id : SessionState.GetInt(AppServerProcessIdKey, 0);
        var expectedStartTicks = SessionState.GetString(AppServerProcessStartTicksKey, string.Empty);
        try
        {
            if (processId > 0)
            {
                using var process = Process.GetProcessById(processId);
                var processIsOwned = sharedProcess != null || process.StartTime.ToUniversalTime().Ticks.ToString() == expectedStartTicks;
                if (processIsOwned && !process.HasExited) process.Kill();
            }
        }
        catch (ArgumentException) { }
        finally
        {
            sharedProcess = null;
            sharedCwd = null;
            sharedMcpEndpoint = null;
            SessionState.EraseInt(AppServerProcessIdKey);
            SessionState.EraseString(AppServerProcessStartTicksKey);
        }
    }

    private static async Task<Process> StartAsync(string mcpEndpoint)
    {
        var finder = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command \"(Get-AppxPackage -Name OpenAI.Codex | Select-Object -First 1 -ExpandProperty InstallLocation)\"") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
        using var shell = Process.Start(finder) ?? throw new FileNotFoundException("未找到 Codex 桌面应用。");
        var install = (await shell.StandardOutput.ReadToEndAsync()).Trim(); await Task.Run(() => shell.WaitForExit());
        var exe = Path.Combine(install, "app", "resources", "codex.exe"); if (!File.Exists(exe)) throw new FileNotFoundException("未找到 Codex App Server。");
        // The App Server launches sibling helpers (such as codex-code-mode-host.exe) by relative path.
        // Keep its process directory at the installed resources folder; each request supplies the Unity project cwd explicitly.
        var info = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe), UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false), CreateNoWindow = true };
        if (!string.IsNullOrEmpty(mcpEndpoint))
        {
            // This is a command-line configuration override, not a write to ~/.codex/config.toml.
            info.ArgumentList.Add("--config");
            info.ArgumentList.Add("mcp_servers.unity_editor.url=\"" + EscapeToml(mcpEndpoint) + "\"");
        }
        info.ArgumentList.Add("app-server"); info.ArgumentList.Add("--stdio");
        return Process.Start(info) ?? throw new InvalidOperationException("无法启动 Codex App Server。");
    }

    private static string EscapeToml(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static async Task<JsonElement> CallAsync(Process process, int id, string method, string parameters)
    {
        await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"" + method + "\",\"params\":" + parameters + "}"); await process.StandardInput.FlushAsync();
        var timeout = Task.Delay(60000);
        while (true) { var read = process.StandardOutput.ReadLineAsync(); if (await Task.WhenAny(read, timeout) != read) throw new TimeoutException("Codex App Server 未在 60 秒内响应。"); var line = await read; if (string.IsNullOrEmpty(line)) continue; using var doc = JsonDocument.Parse(line); var root = doc.RootElement; if (!root.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id) continue; if (root.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.GetRawText()); return root.GetProperty("result").Clone(); }
    }

    private static async Task ReadAssistantReplyAsync(Process process, string threadId, string turnId, Action<string> onAssistantDelta, Action<CodexApprovalRequest> onApprovalRequested, Action<CodexMcpElicitationRequest> onMcpElicitationRequested, Action<List<CodexFileChange>> onFileChanges)
    {
        var timeout = Task.Delay(300000);
        while (true)
        {
            var read = process.StandardOutput.ReadLineAsync();
            if (await Task.WhenAny(read, timeout) != read)
                throw new TimeoutException("Codex 在 5 分钟内未完成本次回复。");

            var line = await read;
            if (string.IsNullOrEmpty(line)) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("method", out var method)) continue;
            if (!root.TryGetProperty("params", out var parameters)) continue;
            var methodName = method.GetString();
            if (methodName != "item/agentMessage/delta" && methodName != "turn/completed") UnityDebug.Log("[Codex Unity] App Server event: " + methodName);

            if (methodName == "mcpServer/elicitation/request" && root.TryGetProperty("id", out var elicitationId))
            {
                var elicitationIdRaw = elicitationId.GetRawText();
                var requestedSchema = parameters.TryGetProperty("requestedSchema", out var schema) ? schema.GetRawText() : string.Empty;
                var request = new CodexMcpElicitationRequest
                {
                    ServerName = Text(parameters, "serverName", Text(parameters, "serverId", "Unity MCP")),
                    Message = Text(parameters, "message", "Codex 请求继续使用 Unity MCP 工具。"),
                    RequestedSchema = requestedSchema,
                    Respond = decision => _ = RespondToMcpElicitationRequestAsync(process, elicitationIdRaw, decision)
                };
                UnityDebug.Log("[Codex Unity] Showing Unity MCP approval card. Parameters: " + parameters.GetRawText());
                onMcpElicitationRequested?.Invoke(request);
                continue;
            }

            var notificationThreadId = Text(parameters, "threadId");
            var notificationTurnId = Text(parameters, "turnId");
            if (notificationThreadId != threadId || (notificationTurnId.Length > 0 && notificationTurnId != turnId)) continue;

            var requestMethod = methodName;
            if ((requestMethod == "item/fileChange/requestApproval" || requestMethod == "item/permissions/requestApproval" || requestMethod == "item/commandExecution/requestApproval") && root.TryGetProperty("id", out var requestId))
            {
                var isPermissionRequest = requestMethod == "item/permissions/requestApproval";
                var isCommandRequest = requestMethod == "item/commandExecution/requestApproval";
                var requestIdRaw = requestId.GetRawText();
                var permissionsRaw = isPermissionRequest ? parameters.GetProperty("permissions").GetRawText() : string.Empty;
                var approval = new CodexApprovalRequest
                {
                    Title = isPermissionRequest ? "文件写入权限申请" : isCommandRequest ? "命令执行申请" : "修改申请",
                    ThreadId = threadId,
                    ItemId = Text(parameters, "itemId"),
                    Reason = Text(parameters, "reason", isCommandRequest ? "将执行命令：" + Text(parameters, "command") : "Codex 请求修改项目文件。"),
                    GrantRoot = isPermissionRequest || isCommandRequest ? Text(parameters, "cwd") : Text(parameters, "grantRoot"),
                    Respond = decision => _ = isPermissionRequest
                        ? RespondToPermissionsRequestAsync(process, requestIdRaw, permissionsRaw, decision)
                        : RespondToServerRequestAsync(process, requestIdRaw, decision)
                };
                UnityDebug.Log("[Codex Unity] Showing approval card for " + requestMethod + ".");
                onApprovalRequested?.Invoke(approval);
                continue;
            }

            if (method.GetString() == "item/agentMessage/delta")
            {
                var delta = Text(parameters, "delta");
                if (delta.Length > 0) onAssistantDelta?.Invoke(delta);
            }
            else if (method.GetString() == "item/fileChange/patchUpdated" && parameters.TryGetProperty("changes", out var changes))
            {
                onFileChanges?.Invoke(ParseFileChanges(changes));
            }
            else if (method.GetString() == "turn/completed")
            {
                var turn = parameters.GetProperty("turn");
                if (Text(turn, "status") == "failed")
                    throw new InvalidOperationException("Codex 未能完成本次回复：" + (turn.TryGetProperty("error", out var error) ? error.GetRawText() : "未知错误"));
                return;
            }
        }
    }

    private static async Task RespondToServerRequestAsync(Process process, string requestId, string decision)
    {
        await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":" + requestId + ",\"result\":{\"decision\":\"" + Escape(decision) + "\"}}");
        await process.StandardInput.FlushAsync();
    }

    private static async Task RespondToMcpElicitationRequestAsync(Process process, string requestId, string decision)
    {
        var result = decision == "accept" ? "{\"action\":\"accept\",\"content\":{}}" : "{\"action\":\"" + Escape(decision) + "\"}";
        UnityDebug.Log("[Codex Unity] Responding to Unity MCP elicitation: " + decision + ".");
        await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":" + requestId + ",\"result\":" + result + "}");
        await process.StandardInput.FlushAsync();
    }

    private static async Task RespondToPermissionsRequestAsync(Process process, string requestId, string permissions, string decision)
    {
        var result = decision == "cancel" ? "{\"permissions\":{}}" : "{\"permissions\":" + permissions + ",\"scope\":\"" + (decision == "acceptForSession" ? "session" : "turn") + "\"}";
        await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":" + requestId + ",\"result\":" + result + "}");
        await process.StandardInput.FlushAsync();
    }

    private static List<CodexFileChange> ParseFileChanges(JsonElement changes)
    {
        var result = new List<CodexFileChange>();
        foreach (var change in changes.EnumerateArray())
        {
            var diff = Text(change, "diff");
            var added = 0; var removed = 0;
            foreach (var line in diff.Split('\n'))
            {
                if (line.StartsWith("+++") || line.StartsWith("---")) continue;
                if (line.StartsWith("+")) added++;
                else if (line.StartsWith("-")) removed++;
            }
            result.Add(new CodexFileChange { Path = Text(change, "path"), Added = added, Removed = removed });
        }
        return result;
    }

    private static CodexAccountInfo ParseAccount(JsonElement result) { if (!result.TryGetProperty("account", out var account) || account.ValueKind == JsonValueKind.Null) return new CodexAccountInfo(); return new CodexAccountInfo { IsLoggedIn = true, Email = Text(account, "email"), PlanType = Text(account, "planType") }; }
    private static string Text(JsonElement item, string name, string fallback = "") => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
