using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
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
[Serializable] public sealed class CodexApiChatMessage { public string Role; public string Content; }
[Serializable] public sealed class CodexApiChatThread { public string Id; public string Name; public List<CodexApiChatMessage> Messages = new List<CodexApiChatMessage>(); }

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
    // "local" reuses Codex's official desktop login; "api" selects the future
    // custom-model path. This is a plugin preference, not an account credential.
    internal static string LoginMode { get => EditorPrefs.GetString(Prefix + "LoginMode", string.Empty); set => EditorPrefs.SetString(Prefix + "LoginMode", value ?? string.Empty); }
    internal static bool UsesApiKeyLogin => string.Equals(LoginMode, "api", StringComparison.Ordinal);
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
            // API-key conversations are intentionally local to the plugin. They
            // use an "api-" identifier and are never valid App Server threads.
            if (known.Id.StartsWith("api-", StringComparison.OrdinalIgnoreCase)) continue;
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

/// <summary>Session-local chat pool for API Key mode. It is intentionally not a Codex Thread.</summary>
internal static class CodexApiChatStore
{
    [Serializable] private sealed class State { public List<CodexApiChatThread> Threads = new List<CodexApiChatThread>(); }
    private static string Key => "CodexUnity.ApiChats." + Application.dataPath.Replace(':', '_').Replace('\\', '_').Replace('/', '_');
    private static State state;
    private static State Current
    {
        get
        {
            if (state != null) return state;
            try { state = JsonUtility.FromJson<State>(SessionState.GetString(Key, string.Empty)); } catch { }
            return state ?? (state = new State());
        }
    }
    private static void Save() { SessionState.SetString(Key, JsonUtility.ToJson(Current)); }
    internal static List<CodexThreadSummary> GetSummaries() => Current.Threads.ConvertAll(item => new CodexThreadSummary { Id = item.Id, Name = item.Name, Preview = item.Messages.Count == 0 ? string.Empty : item.Messages[item.Messages.Count - 1].Content });
    internal static CodexApiChatThread Create()
    {
        var thread = new CodexApiChatThread { Id = "api-" + Guid.NewGuid().ToString("N"), Name = "新聊天" };
        Current.Threads.Insert(0, thread); Save(); return thread;
    }
    internal static List<CodexApiChatMessage> Read(string id)
    {
        var thread = Current.Threads.Find(item => item.Id == id);
        return thread == null ? new List<CodexApiChatMessage>() : new List<CodexApiChatMessage>(thread.Messages);
    }
    internal static void Append(string id, string role, string content)
    {
        var thread = Current.Threads.Find(item => item.Id == id); if (thread == null) return;
        thread.Messages.Add(new CodexApiChatMessage { Role = role, Content = content ?? string.Empty }); Save();
    }
    internal static void Rename(string id, string name)
    {
        var thread = Current.Threads.Find(item => item.Id == id); if (thread == null) return;
        thread.Name = string.IsNullOrWhiteSpace(name) ? "新聊天" : name.Trim(); Save();
    }
    internal static void Delete(string id) { Current.Threads.RemoveAll(item => item.Id == id); Save(); }
}

/// <summary>
/// Provider-neutral checks for OpenAI-compatible custom endpoints. Requests
/// contain no prompt, and neither the API key nor request headers are logged.
/// </summary>
internal static class CodexCustomApiClient
{
    // Connectivity probes should fail quickly, but a real model response may need
    // substantially longer (especially when its prompt contains Unity tool schemas).
    private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly HttpClient AgentClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

    internal static async Task<string> ValidateAsync(string apiKey, string modelName, string modelUrl)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(modelName) || string.IsNullOrWhiteSpace(modelUrl))
            return "验证失败：API Key、模型名称和模型链接均不能为空。";
        if (!TryBuildModelsEndpoint(modelUrl, out var endpoint, out var reason)) return "验证失败：" + reason;
        try
        {
            using (var request = CreateRequest(endpoint, apiKey))
            using (var response = await Client.SendAsync(request))
            {
                var body = await response.Content.ReadAsStringAsync();
                var preview = string.IsNullOrWhiteSpace(body) ? "(空响应)" : body.Substring(0, Math.Min(body.Length, 500));
                var details = "GET " + endpoint + " → HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase + "\n响应摘要：" + preview;
                Debug.Log("[Codex Unity API] Connectivity check\n" + details);
                return response.IsSuccessStatusCode ? "连接验证成功。" : "连接验证失败：HTTP " + (int)response.StatusCode + "。详细信息已输出到 Console。";
            }
        }
        catch (Exception error)
        {
            Debug.LogError("[Codex Unity API] Connectivity check failed for " + endpoint + ": " + error);
            return "连接验证失败：" + error.Message;
        }
    }

    internal static async Task<string> TryGetBalanceAsync(string apiKey, string modelUrl)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || !TryBuildApiBase(modelUrl, out var apiBase, out _)) return "可用额度：未提供余额查询";
        var candidates = new[] { new Uri(apiBase.GetLeftPart(UriPartial.Authority) + "/dashboard/billing/credit_grants"), new Uri(apiBase, "billing/credit_grants"), new Uri(apiBase, "balance") };
        foreach (var endpoint in candidates)
        {
            try
            {
                using (var request = CreateRequest(endpoint, apiKey))
                using (var response = await Client.SendAsync(request))
                {
                    if (!response.IsSuccessStatusCode) continue;
                    var body = await response.Content.ReadAsStringAsync();
                    if (TryExtractBalance(body, out var balance))
                    {
                        Debug.Log("[Codex Unity API] Balance endpoint available: " + endpoint);
                        return "可用额度：" + balance;
                    }
                }
            }
            catch (Exception error) { Debug.Log("[Codex Unity API] Balance endpoint unavailable: " + endpoint + " (" + error.Message + ")"); }
        }
        return "可用额度：API 未提供可识别的余额查询";
    }

    /// <summary>Logs the model metadata returned by the configured endpoint without exposing the API key.</summary>
    internal static async Task LogAvailableModelsAsync(string apiKey, string modelUrl)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Debug.LogWarning("<color=#F4C542>[Codex Unity API]</color> 无法读取模型目录：API Key 为空。");
            return;
        }
        if (!TryBuildModelsEndpoint(modelUrl, out var endpoint, out var reason))
        {
            Debug.LogWarning("<color=#F4C542>[Codex Unity API]</color> 无法读取模型目录：" + reason);
            return;
        }
        try
        {
            using (var request = CreateRequest(endpoint, apiKey))
            using (var response = await Client.SendAsync(request))
            {
                var body = await response.Content.ReadAsStringAsync();
                Debug.Log("<color=#5DADE2>[Codex Unity API]</color> <color=#D6EAF8>本次发送前读取模型目录</color> " + endpoint + " <color=#F4D03F>HTTP " + (int)response.StatusCode + "</color>");
                if (!response.IsSuccessStatusCode)
                {
                    Debug.LogWarning("<color=#F4C542>[Codex Unity API]</color> 模型目录请求失败，当前聊天仍会继续走 Codex App Server。\n" + body.Substring(0, Math.Min(body.Length, 500)));
                    return;
                }
                using (var document = JsonDocument.Parse(body))
                {
                    if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                    {
                        Debug.LogWarning("<color=#F4C542>[Codex Unity API]</color> 响应不包含 data[] 模型列表。");
                        return;
                    }
                    Debug.Log("<color=#5DADE2>[Codex Unity API]</color> <color=#58D68D>data[] 共 " + data.GetArrayLength() + " 个模型</color>");
                    foreach (var model in data.EnumerateArray())
                    {
                        var id = GetJsonString(model, "id", "—");
                        var owner = GetJsonString(model, "owned_by", "—");
                        var type = GetJsonString(model, "object", "—");
                        var created = GetJsonString(model, "created", "—");
                        Debug.Log("<color=#58D68D>id: " + id + "</color>  <color=#F5B041>owned_by: " + owner + "</color>  <color=#AF7AC5>object: " + type + "</color>  <color=#AAB7B8>created: " + created + "</color>");
                    }
                }
            }
        }
        catch (Exception error)
        {
            Debug.LogError("<color=#EC7063>[Codex Unity API]</color> 读取模型目录失败：" + error);
        }
    }

    private static HttpRequestMessage CreateRequest(Uri endpoint, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.ParseAdd("application/json");
        return request;
    }
    private static string GetJsonString(JsonElement element, string name, string fallback)
    {
        return element.TryGetProperty(name, out var value) ? value.ToString() : fallback;
    }
    private static bool TryBuildModelsEndpoint(string modelUrl, out Uri endpoint, out string reason)
    {
        endpoint = null;
        if (!TryBuildApiBase(modelUrl, out var apiBase, out reason)) return false;
        endpoint = modelUrl.TrimEnd('/').EndsWith("/models", StringComparison.OrdinalIgnoreCase) ? new Uri(modelUrl) : new Uri(apiBase, "models");
        return true;
    }
    private static bool TryBuildApiBase(string modelUrl, out Uri apiBase, out string reason)
    {
        apiBase = null; reason = null;
        if (!Uri.TryCreate(modelUrl, UriKind.Absolute, out var input) || (input.Scheme != Uri.UriSchemeHttps && input.Scheme != Uri.UriSchemeHttp))
        {
            reason = "模型链接必须是有效的 http:// 或 https:// URL。";
            return false;
        }
        var path = input.AbsolutePath.TrimEnd('/');
        if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) path = path.Substring(0, path.Length - "/chat/completions".Length);
        else if (path.EndsWith("/models", StringComparison.OrdinalIgnoreCase)) path = path.Substring(0, path.Length - "/models".Length);
        if (!path.EndsWith("/", StringComparison.Ordinal)) path += "/";
        apiBase = new Uri(input.GetLeftPart(UriPartial.Authority) + path);
        return true;
    }
    private static bool TryExtractBalance(string json, out string balance)
    {
        balance = null;
        try { using (var document = JsonDocument.Parse(json)) return TryExtractBalance(document.RootElement, out balance); }
        catch { return false; }
    }
    private static bool TryExtractBalance(JsonElement element, out string balance)
    {
        balance = null;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var name = property.Name.ToLowerInvariant();
                if ((name == "total_available" || name == "balance" || name == "remaining" || name == "available" || name == "credit") && (property.Value.ValueKind == JsonValueKind.Number || property.Value.ValueKind == JsonValueKind.String))
                {
                    balance = property.Value.ToString();
                    return !string.IsNullOrWhiteSpace(balance);
                }
                if (TryExtractBalance(property.Value, out balance)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) if (TryExtractBalance(item, out balance)) return true;
        }
        return false;
    }

    internal sealed class AgentMessage
    {
        internal string Role;
        internal string Content;
        internal string ToolCallId;
        internal List<AgentToolCall> ToolCalls;
    }
    internal sealed class AgentToolCall { internal string Id; internal string Name; internal string Arguments; }
    internal sealed class AgentResponse { internal string Content; internal List<AgentToolCall> ToolCalls = new List<AgentToolCall>(); }

    /// <summary>Calls an OpenAI-compatible /chat/completions endpoint with the enabled Unity tools.</summary>
    internal static async Task<AgentResponse> CreateAgentCompletionAsync(string apiKey, string modelName, string modelUrl, List<AgentMessage> messages, string developerInstructions, bool requireToolCall)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(modelName)) throw new InvalidOperationException("API Key 或模型名称为空。");
        if (!TryBuildApiBase(modelUrl, out var apiBase, out var reason)) throw new InvalidOperationException(reason);
        var endpoint = modelUrl.TrimEnd('/').EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) ? new Uri(modelUrl) : new Uri(apiBase, "chat/completions");
        var json = BuildAgentRequestJson(modelName, messages, developerInstructions, requireToolCall);
        using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            Debug.Log("<color=#5DADE2>[Codex Unity API Agent]</color> 正在请求 <color=#F4D03F>" + modelName + "</color>（最长等待 120 秒）：" + endpoint);
            try
            {
                using (var response = await AgentClient.SendAsync(request))
                {
                    var body = await response.Content.ReadAsStringAsync();
                    Debug.Log("<color=#5DADE2>[Codex Unity API Agent]</color> POST " + endpoint + " <color=#F4D03F>HTTP " + (int)response.StatusCode + "</color>");
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException("第三方 API 返回 HTTP " + (int)response.StatusCode + "：" + body.Substring(0, Math.Min(body.Length, 1000)));
                    return ParseAgentResponse(body);
                }
            }
            catch (TaskCanceledException exception)
            {
                throw new TimeoutException("第三方模型在 120 秒内未返回。请检查模型接口是否支持 chat/completions 与 tool_calls，或缩短全局提示词/启用的 Unity 工具数量。", exception);
            }
            catch (System.Net.WebException exception)
            {
                throw new InvalidOperationException("第三方模型请求被网络层取消。请检查模型链接、网络/代理，以及接口是否支持当前请求中的 tools 字段。", exception);
            }
        }
    }

    private static string BuildAgentRequestJson(string modelName, List<AgentMessage> messages, string developerInstructions, bool requireToolCall)
    {
        using (var stream = new MemoryStream())
        {
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("model", modelName);
                writer.WritePropertyName("messages"); writer.WriteStartArray();
                var agentInstruction = "You are a Unity Editor agent. For any request that creates, modifies, deletes, saves, builds, tests, or inspects the Unity project, you MUST call the provided Unity tools. Never substitute a C# code snippet, manual steps, or a claimed result for an actual tool call. Do not claim an editor change succeeded unless a tool result confirms it. Use only available tools and interpret their results before giving the final answer.";
                if (!string.IsNullOrWhiteSpace(developerInstructions)) agentInstruction += "\n\nProject developer instructions:\n" + developerInstructions;
                writer.WriteStartObject(); writer.WriteString("role", "system"); writer.WriteString("content", agentInstruction); writer.WriteEndObject();
                foreach (var message in messages) WriteAgentMessage(writer, message);
                writer.WriteEndArray();
                writer.WritePropertyName("tools"); writer.WriteStartArray();
                using (var toolsDocument = JsonDocument.Parse(CodexUnityMcpTools.GetEnabledToolDefinitionsJson()))
                {
                    foreach (var definition in toolsDocument.RootElement.EnumerateArray())
                    {
                        writer.WriteStartObject(); writer.WriteString("type", "function"); writer.WritePropertyName("function"); writer.WriteStartObject();
                        writer.WriteString("name", GetJsonString(definition, "name", string.Empty));
                        writer.WriteString("description", GetJsonString(definition, "description", string.Empty));
                        writer.WritePropertyName("parameters");
                        if (definition.TryGetProperty("inputSchema", out var schema)) schema.WriteTo(writer); else { writer.WriteStartObject(); writer.WriteEndObject(); }
                        writer.WriteEndObject(); writer.WriteEndObject();
                    }
                }
                writer.WriteEndArray(); writer.WriteString("tool_choice", requireToolCall ? "required" : "auto"); writer.WriteBoolean("stream", false); writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
    private static void WriteAgentMessage(Utf8JsonWriter writer, AgentMessage message)
    {
        writer.WriteStartObject(); writer.WriteString("role", message.Role);
        if (message.Content == null) writer.WriteNull("content"); else writer.WriteString("content", message.Content);
        if (message.Role == "tool") writer.WriteString("tool_call_id", message.ToolCallId);
        if (message.ToolCalls != null && message.ToolCalls.Count > 0)
        {
            writer.WritePropertyName("tool_calls"); writer.WriteStartArray();
            foreach (var call in message.ToolCalls)
            {
                writer.WriteStartObject(); writer.WriteString("id", call.Id); writer.WriteString("type", "function"); writer.WritePropertyName("function"); writer.WriteStartObject(); writer.WriteString("name", call.Name); writer.WriteString("arguments", call.Arguments ?? "{}"); writer.WriteEndObject(); writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }
    private static AgentResponse ParseAgentResponse(string json)
    {
        using (var document = JsonDocument.Parse(json))
        {
            if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) throw new InvalidOperationException("第三方 API 响应不包含 choices[0]。");
            var message = choices[0].GetProperty("message");
            var reply = new AgentResponse { Content = message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String ? content.GetString() : string.Empty };
            if (!message.TryGetProperty("tool_calls", out var calls) || calls.ValueKind != JsonValueKind.Array) return reply;
            foreach (var call in calls.EnumerateArray())
            {
                if (!call.TryGetProperty("function", out var function)) continue;
                reply.ToolCalls.Add(new AgentToolCall { Id = GetJsonString(call, "id", Guid.NewGuid().ToString("N")), Name = GetJsonString(function, "name", string.Empty), Arguments = GetJsonString(function, "arguments", "{}") });
            }
            return reply;
        }
    }
}

/// <summary>
/// Common conversation/agent contract used by the Unity window. Providers own
/// their transport and conversation storage, while the window owns rendering,
/// approvals, and execution of Unity tools.
/// </summary>
internal interface ICodexAgentProvider
{
    bool UsesSharedCodexThreads { get; }
    Task<CodexWorkspaceSnapshot> FetchWorkspaceAsync(string projectRoot);
    Task<CodexThreadSummary> CreateConversationAsync(string projectRoot);
    Task<List<CodexChatMessage>> ReadConversationAsync(string projectRoot, string conversationId);
    Task RenameConversationAsync(string projectRoot, string conversationId, string name);
    Task DeleteConversationAsync(string projectRoot, string conversationId);
    Task SendAsync(AgentProviderRequest request, AgentProviderCallbacks callbacks);
}

internal sealed class AgentProviderRequest
{
    internal string ProjectRoot;
    internal string ConversationId;
    internal string Text;
    internal string Model;
    internal string Effort;
    internal string DeveloperInstructions;
}

internal sealed class AgentProviderCallbacks
{
    internal Action<string> OnAssistantDelta;
    internal Action<CodexApprovalRequest> OnFileApprovalRequested;
    internal Action<CodexMcpElicitationRequest> OnMcpElicitationRequested;
    internal Action<List<CodexFileChange>> OnFileChanges;
    internal Func<CodexCustomApiClient.AgentToolCall, Task<string>> ExecuteUnityToolAsync;
}

internal static class CodexAgentProviderFactory
{
    private static readonly ICodexAgentProvider LocalCodex = new CodexAppServerAgentProvider();
    private static readonly ICodexAgentProvider ApiKey = new OpenAiCompatibleAgentProvider();
    internal static ICodexAgentProvider Current => CodexApprovalPreferences.UsesApiKeyLogin ? ApiKey : LocalCodex;
}

internal sealed class CodexAppServerAgentProvider : ICodexAgentProvider
{
    public bool UsesSharedCodexThreads => true;
    public Task<CodexWorkspaceSnapshot> FetchWorkspaceAsync(string projectRoot) => CodexAppServerClient.FetchAsync(projectRoot);
    public Task<CodexThreadSummary> CreateConversationAsync(string projectRoot) => CodexAppServerClient.CreateThreadAsync(projectRoot);
    public Task<List<CodexChatMessage>> ReadConversationAsync(string projectRoot, string conversationId) => CodexAppServerClient.ReadThreadAsync(projectRoot, conversationId);
    public Task RenameConversationAsync(string projectRoot, string conversationId, string name) => CodexAppServerClient.RenameThreadAsync(projectRoot, conversationId, name);
    public Task DeleteConversationAsync(string projectRoot, string conversationId) => CodexAppServerClient.DeleteThreadAsync(projectRoot, conversationId);
    public Task SendAsync(AgentProviderRequest request, AgentProviderCallbacks callbacks)
    {
        return CodexAppServerClient.SendMessageAsync(request.ProjectRoot, request.ConversationId, request.Text, request.Model, request.Effort, request.DeveloperInstructions, callbacks.OnAssistantDelta, callbacks.OnFileApprovalRequested, callbacks.OnMcpElicitationRequested, callbacks.OnFileChanges);
    }
}

internal sealed class OpenAiCompatibleAgentProvider : ICodexAgentProvider
{
    public bool UsesSharedCodexThreads => false;
    public Task<CodexWorkspaceSnapshot> FetchWorkspaceAsync(string projectRoot) => Task.FromResult(new CodexWorkspaceSnapshot { Threads = CodexApiChatStore.GetSummaries() });
    public Task<CodexThreadSummary> CreateConversationAsync(string projectRoot)
    {
        var thread = CodexApiChatStore.Create();
        return Task.FromResult(new CodexThreadSummary { Id = thread.Id, Name = thread.Name });
    }
    public Task<List<CodexChatMessage>> ReadConversationAsync(string projectRoot, string conversationId)
    {
        var result = new List<CodexChatMessage>();
        foreach (var message in CodexApiChatStore.Read(conversationId)) result.Add(new CodexChatMessage { Sender = message.Role == "user" ? "你" : "assistant", Text = message.Content });
        return Task.FromResult(result);
    }
    public Task RenameConversationAsync(string projectRoot, string conversationId, string name) { CodexApiChatStore.Rename(conversationId, name); return Task.CompletedTask; }
    public Task DeleteConversationAsync(string projectRoot, string conversationId) { CodexApiChatStore.Delete(conversationId); return Task.CompletedTask; }
    public async Task SendAsync(AgentProviderRequest request, AgentProviderCallbacks callbacks)
    {
        await CodexCustomApiClient.LogAvailableModelsAsync(CodexApprovalPreferences.CustomApiKey, CodexApprovalPreferences.CustomApiModelUrl);
        var history = new List<CodexCustomApiClient.AgentMessage>();
        foreach (var message in CodexApiChatStore.Read(request.ConversationId)) history.Add(new CodexCustomApiClient.AgentMessage { Role = message.Role, Content = message.Content });
        history.Add(new CodexCustomApiClient.AgentMessage { Role = "user", Content = request.Text });
        CodexApiChatStore.Append(request.ConversationId, "user", request.Text);
        var finalText = string.Empty;
        const int maxToolRounds = 8;
        for (var round = 0; round < maxToolRounds; round++)
        {
            var requireToolCall = round == 0 && LooksLikeUnityOperation(request.Text);
            Debug.Log("[Codex Unity API Agent] Round " + (round + 1) + ": tool_choice=" + (requireToolCall ? "required" : "auto") + ".");
            CodexCustomApiClient.AgentResponse reply;
            try
            {
                reply = await CodexCustomApiClient.CreateAgentCompletionAsync(CodexApprovalPreferences.CustomApiKey, CodexApprovalPreferences.CustomApiModelName, CodexApprovalPreferences.CustomApiModelUrl, history, request.DeveloperInstructions, requireToolCall);
            }
            catch (InvalidOperationException error) when (requireToolCall && error.Message.IndexOf("Thinking mode does not support this tool_choice", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Debug.LogWarning("[Codex Unity API Agent] 当前模型的 Thinking mode 不支持 tool_choice=required；已自动以 tool_choice=auto 重试。该模型可能仍会只输出文本而不调用 Unity 工具。");
                requireToolCall = false;
                reply = await CodexCustomApiClient.CreateAgentCompletionAsync(CodexApprovalPreferences.CustomApiKey, CodexApprovalPreferences.CustomApiModelName, CodexApprovalPreferences.CustomApiModelUrl, history, request.DeveloperInstructions, false);
            }
            Debug.Log("[Codex Unity API Agent] Round " + (round + 1) + " response: tool_calls=" + reply.ToolCalls.Count + ", text=" + (!string.IsNullOrWhiteSpace(reply.Content)) + ".");
            if (!string.IsNullOrWhiteSpace(reply.Content)) { finalText += (finalText.Length == 0 ? string.Empty : "\n") + reply.Content; callbacks.OnAssistantDelta?.Invoke(reply.Content); }
            history.Add(new CodexCustomApiClient.AgentMessage { Role = "assistant", Content = reply.Content, ToolCalls = reply.ToolCalls });
            if (reply.ToolCalls == null || reply.ToolCalls.Count == 0)
            {
                if (round == 0 && LooksLikeUnityOperation(request.Text))
                {
                    const string note = "\n\n[Unity Agent 提示] 当前模型没有返回 tool_calls，因此未执行任何 Unity 操作。该接口的 Thinking mode 不支持强制工具调用；请切换到支持 Function Calling 的非 Thinking 模型，或确认服务商支持 tools + tool_choice。";
                    finalText += note;
                    callbacks.OnAssistantDelta?.Invoke(note);
                }
                if (string.IsNullOrWhiteSpace(finalText)) finalText = "任务已完成，但 API 未返回文本内容。";
                CodexApiChatStore.Append(request.ConversationId, "assistant", finalText);
                Debug.Log("[Codex Unity API Agent] Reply completed after " + (round + 1) + " round(s).");
                return;
            }
            if (callbacks.ExecuteUnityToolAsync == null) throw new InvalidOperationException("API Agent has no Unity tool executor.");
            foreach (var call in reply.ToolCalls)
            {
                Debug.Log("[Codex Unity API Agent] Requested Unity tool: " + call.Name + ".");
                var output = await callbacks.ExecuteUnityToolAsync(call);
                history.Add(new CodexCustomApiClient.AgentMessage { Role = "tool", ToolCallId = call.Id, Content = output });
            }
        }
        throw new InvalidOperationException("API Agent 在 " + maxToolRounds + " 轮工具调用后仍未完成，已安全停止。");
    }

    private static bool LooksLikeUnityOperation(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var lower = text.ToLowerInvariant();
        var markers = new[] { "创建", "生成", "修改", "删除", "保存", "构建", "编译", "运行", "测试", "检查", "查询", "读取", "打开", "添加", "移动", "create", "modify", "delete", "save", "build", "run", "test", "inspect", "read", "open", "add", "move" };
        foreach (var marker in markers) if (lower.Contains(marker)) return true;
        return false;
    }
}
