using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class CodexWindow
{
#region Window Interaction Logic
    private bool isRefreshing;
    [SerializeField] private string selectedThreadId;
    private string selectedModelId;
    private string selectedEffort;
    private bool isSelectingDefaultThread;
    private bool needsConversationRestore;
    private bool isScrollToLatestScheduled;
    private void OnEnable() => CodexWorkspaceStore.Instance.Changed += RefreshWorkspaceUi;
    private void OnDisable()
    {
        CodexWorkspaceStore.Instance.Changed -= RefreshWorkspaceUi;
        if (activeWindow == this) activeWindow = null;
    }
    private void OnFocus() => BeginWorkspaceRefresh();
    private async void BeginWorkspaceRefresh()
    {
        if (isRefreshing) return; isRefreshing = true;
        try
        {
            var fetched = await CodexAppServerClient.FetchAsync(GetProjectRoot());
            CodexWorkspaceStore.Instance.Set(fetched);
            Debug.Log("[Codex Unity] Chat pool refreshed: " + fetched.Threads.Count + " thread(s).");
            RestoreConversationWhenReady(fetched);
        }
        catch (Exception error) { var state = CodexWorkspaceStore.Instance.Snapshot; state.Error = error.Message; CodexWorkspaceStore.Instance.Set(state); Debug.LogError("[Codex Unity] Chat pool refresh failed: " + error); }
        finally { isRefreshing = false; }
    }
    private void RefreshWorkspaceUi()
    {
        if (threadList == null || accountLabel == null) return;
        var state = CodexWorkspaceStore.Instance.Snapshot; threadList.Clear();
        foreach (var thread in state.Threads) { var item = thread; threadList.Add(new Button(() => SelectThread(item)) { text = item.Name }); }
        var selectedThread = state.Threads.Find(thread => thread.Id == selectedThreadId);
        if (selectedThread != null && activeThreadLabel != null) activeThreadLabel.text = selectedThread.Name;
        RefreshModelMenus(state);
        accountLabel.text = !string.IsNullOrEmpty(state.Error) ? state.Error : state.Account.IsLoggedIn ? state.Account.Email + "\n套餐：" + state.Account.PlanType : "未登录 Codex";
        if (quotaLabel != null && quotaFill != null)
        {
            // account/read currently supplies identity and plan but not a reliable remaining-quota value.
            quotaLabel.text = "可用额度：暂无法从 Codex App Server 获取";
            quotaFill.style.width = Length.Percent(0);
        }
        var hasSelection = selectedThread != null; messageInput.SetEnabled(hasSelection); sendButton.SetEnabled(hasSelection);
        RestoreConversationWhenReady(state);
    }
    private void RestoreConversationWhenReady(CodexWorkspaceSnapshot state)
    {
        if (!needsConversationRestore || isSelectingDefaultThread || state.Threads.Count == 0 || rootVisualElement.panel == null) return;
        isSelectingDefaultThread = true;
        rootVisualElement.schedule.Execute(() =>
        {
            isSelectingDefaultThread = false;
            if (!needsConversationRestore) return;
            var thread = state.Threads.Find(item => item.Id == selectedThreadId) ?? state.Threads[0];
            needsConversationRestore = false;
            SelectThread(thread);
        });
    }
    private async void SelectThread(CodexThreadSummary thread)
    {
        selectedThreadId = thread.Id;
        needsConversationRestore = false;
        activeThreadLabel.text = thread.Name;
        conversation.Clear();
        conversation.Add(CreateMessage("Codex", "正在加载历史消息…"));
        Debug.Log("[Codex Unity] Selected thread: " + thread.Name + " (" + thread.Id + ").");
        RefreshWorkspaceUi();
        try
        {
            var messages = await CodexAppServerClient.ReadThreadAsync(GetProjectRoot(), thread.Id);
            if (selectedThreadId != thread.Id) return;
            conversation.Clear();
            foreach (var message in messages) conversation.Add(message.IsFileChange ? CreateFileChangeCard(message.FileChanges) : CreateMessage(message.Sender, message.Text));
            ScrollConversationToLatest();
            Debug.Log("[Codex Unity] Loaded " + messages.Count + " history message(s) for thread " + thread.Id + ".");
        }
        catch (Exception error)
        {
            if (selectedThreadId != thread.Id) return;
            conversation.Clear(); conversation.Add(CreateMessage("Codex", "聊天历史读取失败：" + error.Message));
            Debug.LogError("[Codex Unity] History read failed for thread " + thread.Id + ": " + error);
        }
    }
    private async void CreateNewThread()
    {
        try
        {
            var thread = await CodexAppServerClient.CreateThreadAsync(GetProjectRoot());
            var state = CodexWorkspaceStore.Instance.Snapshot;
            state.Threads.Insert(0, thread);
            CodexWorkspaceStore.Instance.Set(state);
            Debug.Log("[Codex Unity] Created thread: " + thread.Id + ".");
            SelectThread(thread);
        }
        catch (Exception error) { var state = CodexWorkspaceStore.Instance.Snapshot; state.Error = error.Message; CodexWorkspaceStore.Instance.Set(state); Debug.LogError("[Codex Unity] Create thread failed: " + error); }
    }
    private async void SendMessage()
    {
        var text = messageInput.value?.Trim();
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(selectedThreadId)) return;
        Debug.Log("[Codex Unity] Sending to thread " + selectedThreadId + ": " + text);
        messageInput.SetEnabled(false); sendButton.SetEnabled(false);
        try
        {
            conversation.Add(CreateMessage("你", text));
            conversation.Add(CreateStreamingMessage("Codex", out var assistantText));
            ScrollConversationToLatest();
            var hasReply = false;
            await CodexAppServerClient.SendMessageAsync(
                GetProjectRoot(), selectedThreadId, text, selectedModelId, selectedEffort,
                delta =>
                {
                    if (!hasReply) { assistantText.text = string.Empty; hasReply = true; }
                    assistantText.text += delta;
                    ScrollConversationToLatest();
                },
                request =>
                {
                    var approvalCard = CreateApprovalCard(request);
                    conversation.Add(approvalCard);
                    ScrollConversationToLatest();
                },
                request =>
                {
                    var elicitationCard = CreateMcpElicitationCard(request);
                    conversation.Add(elicitationCard);
                    ScrollConversationToLatest();
                },
                changes =>
                {
                    var fileChangeCard = CreateFileChangeCard(changes);
                    conversation.Add(fileChangeCard);
                    ScrollConversationToLatest();
                });
            messageInput.value = string.Empty;
            Debug.Log("[Codex Unity] Message and assistant reply completed for thread " + selectedThreadId + ".");
        }
        catch (Exception error)
        {
            var state = CodexWorkspaceStore.Instance.Snapshot;
            state.Error = error.Message;
            CodexWorkspaceStore.Instance.Set(state);
            Debug.LogError("[Codex Unity] Send failed for thread " + selectedThreadId + ": " + error);
        }
        finally { RefreshWorkspaceUi(); }
    }
    private void RefreshModelMenus(CodexWorkspaceSnapshot state)
    {
        if (modelMenu == null || effortMenu == null) return;
        var selected = state.Models.Find(model => model.Id == selectedModelId);
        if (selected == null && state.Models.Count > 0)
        {
            selected = state.Models.Find(model => model.Id == selectedModelId) ?? state.Models[0];
            selectedModelId = selected.Id;
        }
        modelMenu.menu.ClearItems();
        foreach (var model in state.Models)
        {
            var item = model;
            modelMenu.menu.AppendAction(item.DisplayName, _ => SelectModel(item), _ => selectedModelId == item.Id ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
        }
        modelMenu.text = selected == null ? "模型不可用" : selected.DisplayName;
        if (selected == null) { effortMenu.menu.ClearItems(); effortMenu.text = "思考：—"; return; }

        if (!selected.SupportedEfforts.Contains(selectedEffort)) selectedEffort = selected.DefaultEffort;
        effortMenu.menu.ClearItems();
        foreach (var effort in selected.SupportedEfforts)
        {
            var item = effort;
            effortMenu.menu.AppendAction(DisplayEffort(item), _ => SelectEffort(item), _ => selectedEffort == item ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
        }
        effortMenu.text = DisplayEffort(selectedEffort);
    }
    private void SelectModel(CodexModelOption model)
    {
        selectedModelId = model.Id;
        selectedEffort = model.DefaultEffort;
        RefreshModelMenus(CodexWorkspaceStore.Instance.Snapshot);
    }
    private void SelectEffort(string effort)
    {
        selectedEffort = effort;
        RefreshModelMenus(CodexWorkspaceStore.Instance.Snapshot);
    }
    private void ScrollConversationToLatest()
    {
        if (conversation == null || isScrollToLatestScheduled) return;
        isScrollToLatestScheduled = true;
        conversation.schedule.Execute(() =>
        {
            isScrollToLatestScheduled = false;
            if (conversation == null || conversation.contentContainer.childCount == 0) return;
            var latest = conversation.contentContainer[conversation.contentContainer.childCount - 1];
            conversation.ScrollTo(latest);
            conversation.verticalScroller.value = conversation.verticalScroller.highValue;
        }).ExecuteLater(50);
    }
    internal static async Task<bool> RequestMcpApiApprovalAsync(string toolName, string summary, string arguments)
    {
        var completion = new TaskCompletionSource<bool>();
        await CodexUnityEditorDispatcher.RunAsync(() =>
        {
            if (activeWindow == null || activeWindow.conversation == null)
            {
                completion.TrySetResult(false);
                return 0;
            }
            var request = new CodexMcpApiApprovalRequest { ToolName = toolName, Summary = summary, Arguments = arguments, Respond = allowed => completion.TrySetResult(allowed) };
            var card = CreateMcpApiApprovalCard(request);
            activeWindow.conversation.Add(card);
            activeWindow.ScrollConversationToLatest();
            return 0;
        });
        return await completion.Task;
    }
    internal static async Task<string> RequestMcpElicitationAsync(string serverName, string message, string requestedSchema)
    {
        var completion = new TaskCompletionSource<string>();
        await CodexUnityEditorDispatcher.RunAsync(() =>
        {
            if (activeWindow == null || activeWindow.conversation == null)
            {
                completion.TrySetResult("cancel");
                return 0;
            }
            var request = new CodexMcpElicitationRequest
            {
                ServerName = serverName,
                Message = message,
                RequestedSchema = requestedSchema,
                Respond = decision => completion.TrySetResult(decision)
            };
            activeWindow.conversation.Add(CreateMcpElicitationCard(request));
            activeWindow.ScrollConversationToLatest();
            return 0;
        });
        return await completion.Task;
    }
    private static string DisplayEffort(string effort)
    {
        switch (effort)
        {
            case "none": return "思考：无";
            case "low": return "思考：低";
            case "medium": return "思考：中";
            case "high": return "思考：高";
            case "xhigh": return "思考：很高";
            case "max": return "思考：最大";
            default: return string.IsNullOrEmpty(effort) ? "思考：—" : "思考：" + effort;
        }
    }
    private void ToggleAccountPanel()
    {
        var show = accountPanel.style.display == DisplayStyle.None;
        accountPanel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        if (show) { mcpPanel.style.display = DisplayStyle.None; mcpCategoryPanel.style.display = DisplayStyle.None; }
    }
    private void ToggleMcpPanel()
    {
        var show = mcpPanel.style.display == DisplayStyle.None;
        mcpPanel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        if (!show) { mcpCategoryPanel.style.display = DisplayStyle.None; return; }
        accountPanel.style.display = DisplayStyle.None;
        mcpLabel.text = "Unity MCP\n状态：" + (CodexUnityMcpBridge.IsRunning ? "已连接" : "未连接")
            + "\n端口：" + (CodexUnityMcpBridge.IsRunning ? CodexUnityMcpBridge.Endpoint : "—")
            + "\n可用 API：" + CodexUnityMcpTools.ToolNames.Length + " 个\n分类：" + CodexUnityMcpTools.ToolCategories.Length + " 个";
        mcpCategoryPanel.style.display = DisplayStyle.None;
        mcpCategories.Clear();
        foreach (var category in CodexUnityMcpTools.ToolCategories)
        {
            var item = category;
            mcpCategories.Add(new Button(() => ShowMcpCategory(item)) { text = item.Name + "（" + item.Tools.Length + "）", tooltip = item.Description, style = { marginTop = 3 } });
        }
    }
    private void ShowMcpCategory(CodexUnityMcpTools.ToolCategory category)
    {
        mcpCategoryLabel.text = category.Name + "\n" + category.Description + "\n\nAPI（" + category.Tools.Length + "）\n• " + string.Join("\n• ", category.Tools);
        mcpCategoryPanel.style.display = DisplayStyle.Flex;
    }
    private static string GetProjectName() => Path.GetFileName(GetProjectRoot());
    private static string GetProjectRoot() => Directory.GetParent(Application.dataPath).FullName;
#endregion
}
