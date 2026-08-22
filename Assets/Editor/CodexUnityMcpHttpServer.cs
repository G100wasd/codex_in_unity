using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// Minimal Streamable-HTTP MCP transport. It supports the JSON-RPC methods needed by
/// the first Unity bridge version: initialize, notifications/initialized, tools/list, and tools/call.
internal sealed class CodexUnityMcpHttpServer
{
    private const int ToolTimeoutMilliseconds = 20000;
    private HttpListener listener;
    private CancellationTokenSource cancellation;
    public bool IsRunning => listener != null && listener.IsListening;
    public string Endpoint { get; private set; }

    public void Start()
    {
        var port = ReservePort();
        listener = new HttpListener();
        listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
        listener.Start();
        Endpoint = "http://127.0.0.1:" + port + "/mcp";
        cancellation = new CancellationTokenSource();
        _ = AcceptLoopAsync(cancellation.Token);
    }

    public void Stop()
    {
        cancellation?.Cancel();
        try { listener?.Stop(); } catch { }
        try { listener?.Close(); } catch { }
        listener = null;
        cancellation?.Dispose();
        cancellation = null;
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && listener != null && listener.IsListening)
        {
            try { _ = HandleAsync(await listener.GetContextAsync()); }
            catch (HttpListenerException) when (token.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (token.IsCancellationRequested) { }
            catch (Exception error) { UnityEngine.Debug.LogWarning("[Codex Unity MCP] Listener error: " + error.Message); }
        }
    }

    private static async Task HandleAsync(HttpListenerContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var method = string.Empty;
        var requestId = "notification";
        try
        {
            if (context.Request.HttpMethod != "POST" || context.Request.Url.AbsolutePath.TrimEnd('/') != "/mcp")
            {
                UnityEngine.Debug.LogWarning("[Codex Unity MCP] Rejected HTTP " + context.Request.HttpMethod + " " + context.Request.Url.AbsolutePath + ".");
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                context.Response.Close();
                return;
            }

            string body;
            using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding)) body = await reader.ReadToEndAsync();
            using var document = JsonDocument.Parse(body);
            var request = document.RootElement;
            method = request.TryGetProperty("method", out var methodElement) ? methodElement.GetString() : string.Empty;
            var hasId = request.TryGetProperty("id", out var id);
            var idRaw = hasId ? id.GetRawText() : null;
            requestId = hasId ? idRaw : "notification";
            UnityEngine.Debug.Log("[Codex Unity MCP] Request received: " + method + " (id=" + requestId + ", from=" + context.Request.RemoteEndPoint + ").");
            var result = await InvokeAsync(method, request.TryGetProperty("params", out var parameters) ? parameters.Clone() : default);

            if (!hasId)
            {
                context.Response.StatusCode = (int)HttpStatusCode.Accepted;
                context.Response.Close();
                UnityEngine.Debug.Log("[Codex Unity MCP] Notification handled: " + method + " (" + stopwatch.ElapsedMilliseconds + " ms).");
                return;
            }

            await WriteJsonAsync(context.Response, "{\"jsonrpc\":\"2.0\",\"id\":" + idRaw + ",\"result\":" + result + "}");
            UnityEngine.Debug.Log("[Codex Unity MCP] Response sent: " + method + " (id=" + requestId + ", " + stopwatch.ElapsedMilliseconds + " ms).");
        }
        catch (Exception error)
        {
            UnityEngine.Debug.LogError("[Codex Unity MCP] Request failed: " + method + " (id=" + requestId + ", " + stopwatch.ElapsedMilliseconds + " ms): " + error);
            try { await WriteJsonAsync(context.Response, "{\"jsonrpc\":\"2.0\",\"id\":null,\"error\":{\"code\":-32603,\"message\":" + JsonString(error.Message) + "}}"); }
            catch { }
        }
    }

    private static async Task<string> InvokeAsync(string method, JsonElement parameters)
    {
        switch (method)
        {
            case "initialize":
                return "{\"protocolVersion\":\"2025-03-26\",\"capabilities\":{\"tools\":{}},\"serverInfo\":{\"name\":\"unity-editor-bridge\",\"version\":\"0.1.0\"}}";
            case "notifications/initialized": return "{}";
            case "tools/list": return "{\"tools\":" + CodexUnityMcpTools.ToolDefinitionsJson + "}";
            case "tools/call":
                var name = parameters.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : string.Empty;
                var arguments = parameters.TryGetProperty("arguments", out var argumentsElement) ? argumentsElement.Clone() : default;
                UnityEngine.Debug.Log("[Codex Unity MCP] Tool started: " + name + ".");
                if (CodexUnityMcpTools.RequiresApiApproval(name))
                {
                    UnityEngine.Debug.Log("[Codex Unity MCP] Tool is waiting for user approval; execution timeout is paused: " + name + ".");
                    var allowed = await CodexWindow.RequestMcpApiApprovalAsync(name, CodexUnityMcpTools.GetMutationSummary(name), arguments.ValueKind == JsonValueKind.Undefined ? "{}" : arguments.GetRawText(), CodexUnityMcpTools.IsLongRunning(name));
                    if (!allowed) return "{\"content\":[{\"type\":\"text\",\"text\":\"The Unity API operation was denied by the user.\"}],\"isError\":true}";
                    UnityEngine.Debug.Log("[Codex Unity MCP] Tool approved; starting 20-second execution timeout: " + name + ".");
                }
                var outputTask = CodexUnityMcpTools.InvokeAsync(name, arguments);
                if (await Task.WhenAny(outputTask, Task.Delay(ToolTimeoutMilliseconds)) != outputTask)
                {
                    UnityEngine.Debug.LogError("[Codex Unity MCP] Tool timed out after " + ToolTimeoutMilliseconds / 1000 + " seconds: " + name + ".");
                    return "{\"content\":[{\"type\":\"text\",\"text\":\"Unity tool timed out after 20 seconds: " + Escape(name) + ". The Unity Editor may be compiling or reloading.\"}],\"isError\":true}";
                }
                var output = await outputTask;
                UnityEngine.Debug.Log("[Codex Unity MCP] Tool completed: " + name + " (isError=" + output.IsError + ").");
                return "{\"content\":[{\"type\":\"text\",\"text\":" + JsonString(output.Text) + "}],\"isError\":" + (output.IsError ? "true" : "false") + "}";
            default:
                return "{\"content\":[{\"type\":\"text\",\"text\":\"Unsupported MCP method: " + Escape(method) + "\"}],\"isError\":true}";
        }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "application/json";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        response.Close();
    }

    private static int ReservePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static string JsonString(string value) => "\"" + Escape(value) + "\"";
    private static string Escape(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
}
