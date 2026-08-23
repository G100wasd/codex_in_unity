using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using EditorPopupWindow = UnityEditor.PopupWindow;

public sealed partial class CodexWindow
{
#region Window Interaction Logic
    private bool isRefreshing;
    [SerializeField] private string selectedThreadId;
    private string selectedModelId;
    private string selectedEffort;
    private bool isSelectingDefaultThread;
    private bool needsConversationRestore;
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
            fetched = CodexWorkspaceStore.Instance.MergeKnownThreads(fetched);
            CodexUnityTaskRecovery.CancelIfThreadMissing(fetched);
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
        var recoveryLocked = CodexUnityTaskRecovery.BlocksUserInteraction;
        if (isCreatingThread)
        {
            var creating = new Button { text = "正在创建聊天…", tooltip = "正在等待 Codex App Server 返回真实聊天 ID" };
            creating.SetEnabled(false);
            threadList.Add(creating);
        }
        foreach (var thread in state.Threads) { var item = thread; var button = new Button(() => SelectThread(item)) { text = item.Name }; button.SetEnabled(!recoveryLocked); button.AddManipulator(new ContextualMenuManipulator(evt => PopulateThreadMenu(evt, item))); threadList.Add(button); }
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
        var hasSelection = selectedThread != null;
        if (newThreadButton != null) newThreadButton.SetEnabled(!recoveryLocked && !isCreatingThread);
        messageInput.SetEnabled(hasSelection && !recoveryLocked); sendButton.SetEnabled(hasSelection && !recoveryLocked);
        modelMenu.SetEnabled(!recoveryLocked); effortMenu.SetEnabled(!recoveryLocked); if (newThreadButton != null) newThreadButton.SetEnabled(!recoveryLocked);
        if (recoveryLocked && activeThreadLabel != null) activeThreadLabel.tooltip = CodexUnityTaskRecovery.BlockingMessage;
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
        RestoreChatPageIfNeeded();
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
        if (isCreatingThread) return;
        isCreatingThread = true;
        RefreshWorkspaceUi();
        try
        {
            var thread = await CodexAppServerClient.CreateThreadAsync(GetProjectRoot());
            var state = CodexWorkspaceStore.Instance.Snapshot;
            isCreatingThread = false;
            state.Threads.Insert(0, thread);
            CodexWorkspaceStore.Instance.Set(state);
            Debug.Log("[Codex Unity] Created thread: " + thread.Id + ".");
            SelectThread(thread);
        }
        catch (Exception error) { isCreatingThread = false; var state = CodexWorkspaceStore.Instance.Snapshot; state.Error = error.Message; CodexWorkspaceStore.Instance.Set(state); Debug.LogError("[Codex Unity] Create thread failed: " + error); }
    }
    private void PopulateThreadMenu(ContextualMenuPopulateEvent evt, CodexThreadSummary thread)
    {
        evt.menu.AppendAction("重命名", _ => ShowRenameThreadPopup(evt.mousePosition, thread));
        evt.menu.AppendAction("删除聊天", _ => DeleteThread(thread));
    }
    private void ShowRenameThreadPopup(Vector2 mousePosition, CodexThreadSummary thread)
    {
        EditorPopupWindow.Show(new Rect(mousePosition, Vector2.zero), new CodexThreadRenamePopup(thread.Name, name => RenameThread(thread, name)));
    }
    private async void RenameThread(CodexThreadSummary thread, string name)
    {
        try
        {
            await CodexAppServerClient.RenameThreadAsync(GetProjectRoot(), thread.Id, name);
            CodexWorkspaceStore.Instance.RenameThread(thread.Id, name.Trim());
            if (selectedThreadId == thread.Id) activeThreadLabel.text = name.Trim();
            Debug.Log("[Codex Unity] Renamed thread " + thread.Id + ".");
            rootVisualElement.schedule.Execute(BeginWorkspaceRefresh).ExecuteLater(100);
        }
        catch (Exception error) { Debug.LogError("[Codex Unity] Rename thread failed: " + error); }
    }
    private async void DeleteThread(CodexThreadSummary thread)
    {
        if (!EditorUtility.DisplayDialog("删除聊天", "确定永久删除“" + thread.Name + "”吗？此操作不可恢复。", "删除", "取消")) return;
        try { await CodexAppServerClient.DeleteThreadAsync(GetProjectRoot(), thread.Id); CodexWorkspaceStore.Instance.RemoveThread(thread.Id); if (selectedThreadId == thread.Id) { selectedThreadId = null; conversation.Clear(); activeThreadLabel.text = "请选择或新建对话"; } Debug.Log("[Codex Unity] Deleted thread " + thread.Id + "."); BeginWorkspaceRefresh(); }
        catch (Exception error) { Debug.LogError("[Codex Unity] Delete thread failed: " + error); }
    }
    private async void SendMessage()
    {
        var text = messageInput.value?.Trim();
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(selectedThreadId)) return;
        Debug.Log("[Codex Unity] Sending to thread " + selectedThreadId + ": " + text);
        messageInput.SetEnabled(false); sendButton.SetEnabled(false);
        CodexUnityTaskRecovery.Begin(selectedThreadId, GetProjectRoot(), selectedModelId, selectedEffort);
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
            CodexUnityTaskRecovery.CompleteNormally();
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
        if (conversation == null) return;
        ScheduleConversationScroll(30);
        ScheduleConversationScroll(160);
        ScheduleConversationScroll(420);
    }
    private void ScheduleConversationScroll(long delayMilliseconds)
    {
        if (conversation == null) return;
        conversation.schedule.Execute(() =>
        {
            if (conversation == null || conversation.contentContainer.childCount == 0) return;
            var latest = conversation.contentContainer[conversation.contentContainer.childCount - 1];
            conversation.ScrollTo(latest);
            conversation.verticalScroller.value = conversation.verticalScroller.highValue;
        }).ExecuteLater(delayMilliseconds);
    }
    internal static async Task<bool> RequestMcpApiApprovalAsync(string toolName, string summary, string arguments, bool isLongRunning = false)
    {
        var completion = new TaskCompletionSource<bool>();
        await CodexUnityEditorDispatcher.RunAsync(() =>
        {
            if (activeWindow == null || activeWindow.conversation == null)
            {
                completion.TrySetResult(false);
                return 0;
            }
            var request = new CodexMcpApiApprovalRequest { ToolName = toolName, Summary = summary, Arguments = arguments, IsLongRunning = isLongRunning, Respond = allowed => completion.TrySetResult(allowed) };
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
    internal static void NotifyRecoveryCompleted(string threadId)
    {
        if (activeWindow == null) return;
        activeWindow.selectedThreadId = threadId;
        activeWindow.needsConversationRestore = true;
        activeWindow.BeginWorkspaceRefresh();
    }
    internal static bool IsReadyForTaskRecovery(string threadId)
    {
        if (activeWindow == null || activeWindow.rootVisualElement.panel == null || activeWindow.conversation == null) return false;
        if (activeWindow.isRefreshing) return false;
        var thread = CodexWorkspaceStore.Instance.Snapshot.Threads.Find(item => item.Id == threadId);
        if (thread == null)
        {
            activeWindow.BeginWorkspaceRefresh();
            return false;
        }
        if (activeWindow.selectedThreadId != threadId)
        {
            activeWindow.needsConversationRestore = false;
            activeWindow.SelectThread(thread);
            return false;
        }
        return true;
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
    private void ShowSettingsPage()
    {
        if (mainPanel == null) return;
        accountPanel.style.display = DisplayStyle.None;
        mcpPanel.style.display = DisplayStyle.None;
        mcpCategoryPanel.style.display = DisplayStyle.None;
        mainPanel.Clear();
        isShowingSettingsPage = true;
    }
    private void RestoreChatPageIfNeeded()
    {
        if (!isShowingSettingsPage || mainPanel == null || mainPanel.parent == null) return;
        var parent = mainPanel.parent;
        var index = parent.IndexOf(mainPanel);
        parent.Remove(mainPanel);
        parent.Insert(index, CreateMainPanel());
        isShowingSettingsPage = false;
        RefreshWorkspaceUi();
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
