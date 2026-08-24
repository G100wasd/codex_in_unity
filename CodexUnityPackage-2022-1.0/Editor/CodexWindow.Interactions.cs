using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
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
    private bool isRefreshingApiBalance;
    private string apiBalanceText = "可用额度：点击账户面板查询";
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
            var provider = CodexAgentProviderFactory.Current;
            var fetched = await provider.FetchWorkspaceAsync(GetProjectRoot());
            if (provider.UsesSharedCodexThreads)
            {
                fetched = CodexWorkspaceStore.Instance.MergeKnownThreads(fetched);
                CodexUnityTaskRecovery.CancelIfThreadMissing(fetched);
            }
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
        var apiKeyMode = CodexApprovalPreferences.UsesApiKeyLogin;
        accountLabel.text = apiKeyMode
            ? "模型：" + (string.IsNullOrWhiteSpace(CodexApprovalPreferences.CustomApiModelName) ? "未配置" : CodexApprovalPreferences.CustomApiModelName)
            : !string.IsNullOrEmpty(state.Error) ? state.Error : state.Account.IsLoggedIn ? state.Account.Email + "\n套餐：" + state.Account.PlanType : "未登录 Codex";
        if (quotaLabel != null && quotaFill != null)
        {
            // account/read currently supplies identity and plan but not a reliable remaining-quota value.
            quotaLabel.text = apiKeyMode ? apiBalanceText : "可用额度：暂无法从 Codex App Server 获取";
            quotaFill.style.width = Length.Percent(0);
        }
        var hasSelection = selectedThread != null;
        if (newThreadButton != null) newThreadButton.SetEnabled(!recoveryLocked && !isCreatingThread);
        messageInput.SetEnabled(hasSelection && !recoveryLocked); sendButton.SetEnabled(hasSelection && !recoveryLocked);
        if (modelMenu != null) modelMenu.SetEnabled(!recoveryLocked);
        if (effortMenu != null) effortMenu.SetEnabled(!recoveryLocked);
        if (newThreadButton != null) newThreadButton.SetEnabled(!recoveryLocked);
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
        if (!CodexApprovalPreferences.UsesApiKeyLogin && thread != null && !string.IsNullOrWhiteSpace(thread.Id) && thread.Id.StartsWith("api-", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogWarning("[Codex Unity] Ignored API-key local conversation while Codex login is active: " + thread.Id + ".");
            selectedThreadId = null;
            needsConversationRestore = false;
            BeginWorkspaceRefresh();
            return;
        }
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
            var messages = await CodexAgentProviderFactory.Current.ReadConversationAsync(GetProjectRoot(), thread.Id);
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
            var thread = await CodexAgentProviderFactory.Current.CreateConversationAsync(GetProjectRoot());
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
            await CodexAgentProviderFactory.Current.RenameConversationAsync(GetProjectRoot(), thread.Id, name);
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
        try
        {
            await CodexAgentProviderFactory.Current.DeleteConversationAsync(GetProjectRoot(), thread.Id);
            CodexWorkspaceStore.Instance.RemoveThread(thread.Id);
            if (selectedThreadId == thread.Id) { selectedThreadId = null; conversation.Clear(); activeThreadLabel.text = "请选择或新建对话"; }
            Debug.Log("[Codex Unity] Deleted thread " + thread.Id + "."); BeginWorkspaceRefresh();
        }
        catch (Exception error) { Debug.LogError("[Codex Unity] Delete thread failed: " + error); }
    }
    private async void SendMessage()
    {
        var text = messageInput.value?.Trim();
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(selectedThreadId)) return;
        Debug.Log("[Codex Unity] Sending to thread " + selectedThreadId + ": " + text);
        messageInput.SetEnabled(false); sendButton.SetEnabled(false);
        if (!CodexApprovalPreferences.UsesApiKeyLogin) CodexUnityTaskRecovery.Begin(selectedThreadId, GetProjectRoot(), selectedModelId, selectedEffort);
        try
        {
            conversation.Add(CreateMessage("你", text));
            conversation.Add(CreateStreamingMessage("Codex", out var assistantText));
            ScrollConversationToLatest();
            var hasReply = false;
            var callbacks = new AgentProviderCallbacks
            {
                OnAssistantDelta = delta =>
                {
                    if (!hasReply) { assistantText.text = string.Empty; hasReply = true; }
                    assistantText.text += delta;
                    ScrollConversationToLatest();
                },
                OnFileApprovalRequested = request =>
                {
                    if (CodexApprovalPreferences.AlwaysAllowFileChanges)
                    {
                        request.Respond?.Invoke("acceptForSession");
                        Debug.Log("[Codex Unity] Automatically approved file modification request.");
                        return;
                    }
                    var approvalCard = CreateApprovalCard(request);
                    conversation.Add(approvalCard);
                    ScrollConversationToLatest();
                },
                OnMcpElicitationRequested = request =>
                {
                    if (CodexApprovalPreferences.AlwaysAllowMcpCalls)
                    {
                        request.Respond?.Invoke("accept");
                        Debug.Log("[Codex Unity] Automatically approved MCP elicitation request.");
                        return;
                    }
                    var elicitationCard = CreateMcpElicitationCard(request);
                    conversation.Add(elicitationCard);
                    ScrollConversationToLatest();
                },
                OnFileChanges = changes =>
                {
                    var fileChangeCard = CreateFileChangeCard(changes);
                    conversation.Add(fileChangeCard);
                    ScrollConversationToLatest();
                },
                ExecuteUnityToolAsync = ExecuteApiAgentToolAsync
            };
            await CodexAgentProviderFactory.Current.SendAsync(new AgentProviderRequest
            {
                ProjectRoot = GetProjectRoot(), ConversationId = selectedThreadId, Text = text,
                Model = selectedModelId, Effort = selectedEffort,
                DeveloperInstructions = CodexApprovalPreferences.GlobalPromptEnabled ? CodexApprovalPreferences.GlobalPrompt : null
            }, callbacks);
            messageInput.value = string.Empty;
            if (!CodexApprovalPreferences.UsesApiKeyLogin) CodexUnityTaskRecovery.CompleteNormally();
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
    private static async Task<string> ExecuteApiAgentToolAsync(CodexCustomApiClient.AgentToolCall call)
    {
        if (string.IsNullOrWhiteSpace(call.Name) || !CodexUnityMcpTools.IsToolEnabled(call.Name)) return "Error: Unity tool is unavailable or disabled: " + call.Name;
        if ((EditorApplication.isCompiling || EditorApplication.isUpdating) && call.Name != "unity_get_bridge_status" && call.Name != "unity_get_interrupted_operations" && call.Name != "unity_get_compilation_status") return "Error: Unity is compiling or updating assets; no tool was run.";
        JsonElement arguments;
        try { using (var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments)) arguments = document.RootElement.Clone(); }
        catch (Exception error) { return "Error: tool arguments are invalid JSON: " + error.Message; }
        if (CodexUnityMcpTools.RequiresApiApproval(call.Name))
        {
            var allowed = await RequestMcpApiApprovalAsync(call.Name, CodexUnityMcpTools.GetMutationSummary(call.Name), arguments.GetRawText(), CodexUnityMcpTools.IsLongRunning(call.Name));
            if (!allowed) return "Error: Unity API operation was denied by the user.";
        }
        Debug.Log("[Codex Unity API Agent] Calling Unity tool: " + call.Name + ".");
        var output = await CodexUnityMcpTools.InvokeAsync(call.Name, arguments);
        Debug.Log("[Codex Unity API Agent] Unity tool completed: " + call.Name + " (isError=" + output.IsError + ").");
        return (output.IsError ? "Error: " : "Success: ") + output.Text;
    }
    private void RefreshModelMenus(CodexWorkspaceSnapshot state)
    {
        if (effortMenu == null) return;
        if (CodexApprovalPreferences.UsesApiKeyLogin)
        {
            // API providers do not have a shared model catalogue. The configured
            // model name is therefore shown in the account panel, while this menu
            // deliberately retains only the provider-neutral reasoning choice.
            var apiEfforts = new[] { "none", "low", "medium", "high" };
            if (!apiEfforts.Contains(selectedEffort)) selectedEffort = "medium";
            effortMenu.menu.ClearItems();
            foreach (var effort in apiEfforts)
            {
                var item = effort;
                effortMenu.menu.AppendAction(DisplayEffort(item), _ => SelectEffort(item), _ => selectedEffort == item ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
            effortMenu.text = DisplayEffort(selectedEffort);
            return;
        }

        if (modelMenu == null) return;
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
            if (CodexApprovalPreferences.AlwaysAllowApiOperations)
            {
                completion.TrySetResult(true);
                Debug.Log("[Codex Unity] Automatically approved Unity API operation: " + toolName + ".");
                return 0;
            }
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
            if (CodexApprovalPreferences.AlwaysAllowMcpCalls)
            {
                completion.TrySetResult("accept");
                Debug.Log("[Codex Unity] Automatically approved MCP elicitation request.");
                return 0;
            }
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
        if (show)
        {
            mcpPanel.style.display = DisplayStyle.None; mcpCategoryPanel.style.display = DisplayStyle.None;
            if (CodexApprovalPreferences.UsesApiKeyLogin) RefreshApiBalanceAsync();
        }
    }
    private async void RefreshApiBalanceAsync()
    {
        if (isRefreshingApiBalance) return;
        isRefreshingApiBalance = true;
        apiBalanceText = "可用额度：正在查询…";
        if (quotaLabel != null) quotaLabel.text = apiBalanceText;
        try
        {
            apiBalanceText = await CodexCustomApiClient.TryGetBalanceAsync(CodexApprovalPreferences.CustomApiKey, CodexApprovalPreferences.CustomApiModelUrl);
        }
        catch (Exception error)
        {
            apiBalanceText = "可用额度：查询失败";
            Debug.LogError("[Codex Unity API] Balance query failed: " + error);
        }
        finally
        {
            isRefreshingApiBalance = false;
            if (CodexApprovalPreferences.UsesApiKeyLogin && quotaLabel != null) quotaLabel.text = apiBalanceText;
        }
    }
    private void ToggleMcpPanel()
    {
        var show = mcpPanel.style.display == DisplayStyle.None;
        mcpPanel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        if (!show) { mcpCategoryPanel.style.display = DisplayStyle.None; return; }
        accountPanel.style.display = DisplayStyle.None;
        RefreshMcpPanelContent();
    }
    private void RefreshMcpPanelContent()
    {
        var enabledCategories = CodexUnityMcpTools.ToolCategories.Where(category => category.Tools.Any(CodexUnityMcpTools.IsToolEnabled)).ToArray();
        mcpLabel.text = "Unity MCP\n状态：" + (CodexUnityMcpBridge.IsRunning ? "已连接" : "未连接")
            + "\n端口：" + (CodexUnityMcpBridge.IsRunning ? CodexUnityMcpBridge.Endpoint : "—")
            + "\n可用 API：" + CodexUnityMcpTools.GetEnabledToolNames().Length + " 个\n分类：" + enabledCategories.Length + " 个";
        mcpCategoryPanel.style.display = DisplayStyle.None;
        mcpCategories.Clear();
        foreach (var category in enabledCategories)
        {
            var item = category;
            var count = item.Tools.Count(CodexUnityMcpTools.IsToolEnabled);
            mcpCategories.Add(new Button(() => ShowMcpCategory(item)) { text = item.Name + "（" + count + "）", tooltip = item.Description, style = { marginTop = 3 } });
        }
    }
    private void ShowSettingsPage()
    {
        if (mainPanel == null) return;
        accountPanel.style.display = DisplayStyle.None;
        mcpPanel.style.display = DisplayStyle.None;
        mcpCategoryPanel.style.display = DisplayStyle.None;
        mainPanel.Clear();
        var settings = new VisualElement
        {
            style =
            {
                minWidth = 360, flexGrow = 1,
                backgroundColor = new Color(.13f, .13f, .13f), paddingLeft = 12, paddingRight = 12, paddingTop = 12, paddingBottom = 12,
                borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
                borderTopColor = new Color(.25f, .25f, .25f), borderBottomColor = new Color(.25f, .25f, .25f), borderLeftColor = new Color(.25f, .25f, .25f), borderRightColor = new Color(.25f, .25f, .25f)
            }
        };
        settings.Add(new Label("设置") { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 16, marginBottom = 10 } });
        var approvals = new VisualElement
        {
            style = { backgroundColor = new Color(.16f, .16f, .16f), paddingLeft = 10, paddingRight = 10, paddingTop = 9, paddingBottom = 10 }
        };
        approvals.Add(new Label("审核策略") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 2 } });
        approvals.Add(new Label("开启后，对应类型的请求将不再显示审核卡。") { style = { fontSize = 10, opacity = .7f } });
        AddApprovalSetting(approvals, "始终允许文件修改", "自动批准淡绿色的文件修改审核卡。", CodexApprovalPreferences.AlwaysAllowFileChanges, value => CodexApprovalPreferences.AlwaysAllowFileChanges = value);
        AddApprovalSetting(approvals, "始终允许 MCP 调用", "自动批准蓝色的 MCP 调用审核卡。", CodexApprovalPreferences.AlwaysAllowMcpCalls, value => CodexApprovalPreferences.AlwaysAllowMcpCalls = value);
        AddApprovalSetting(approvals, "始终允许 API 操作", "自动批准棕色的 Unity API 操作审核卡。", CodexApprovalPreferences.AlwaysAllowApiOperations, value => CodexApprovalPreferences.AlwaysAllowApiOperations = value);
        settings.Add(approvals);
        AddConversationAppearanceSettings(settings);
        AddGlobalPromptSettings(settings);
        AddMcpToolSettings(settings);
        AddCustomApiSettings(settings);
        AddLoginSettings(settings);
        var settingsScroll = new ScrollView { style = { flexGrow = 1 } };
        settingsScroll.Add(settings);
        mainPanel.Add(settingsScroll);
        isShowingSettingsPage = true;
    }
    private static void AddApprovalSetting(VisualElement parent, string label, string help, bool currentValue, System.Action<bool> save)
    {
        var toggle = new Toggle(label) { value = currentValue, tooltip = help, style = { marginTop = 8 } };
        toggle.RegisterValueChangedCallback(evt => save(evt.newValue));
        parent.Add(toggle);
        parent.Add(new Label(help) { style = { marginLeft = 22, fontSize = 10, opacity = .7f } });
    }
    private void AddConversationAppearanceSettings(VisualElement parent)
    {
        var card = new VisualElement { style = { backgroundColor = new Color(.16f, .16f, .16f), paddingLeft = 10, paddingRight = 10, paddingTop = 9, paddingBottom = 10, marginTop = 10 } };
        card.Add(new Label("对话区域") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
        card.Add(new Label("输入框默认显示两行；内容增加时会增长到上限，之后可在框内滚动。颜色会用于后续渲染的聊天消息。") { style = { fontSize = 10, opacity = .7f, whiteSpace = WhiteSpace.Normal, marginTop = 2 } });
        var maxLines = new IntegerField("输入框最大行数") { value = CodexApprovalPreferences.ComposerMaxLines, style = { marginTop = 8 } };
        maxLines.RegisterValueChangedCallback(evt =>
        {
            var clamped = Mathf.Clamp(evt.newValue, 2, 12);
            if (clamped != evt.newValue) maxLines.SetValueWithoutNotify(clamped);
            CodexApprovalPreferences.ComposerMaxLines = clamped;
            UpdateComposerHeight();
        });
        var userColor = new ColorField("用户消息背景色") { value = CodexApprovalPreferences.UserMessageColor, style = { marginTop = 5 } };
        userColor.RegisterValueChangedCallback(evt => CodexApprovalPreferences.UserMessageColor = evt.newValue);
        var assistantColor = new ColorField("AI 消息背景色") { value = CodexApprovalPreferences.AssistantMessageColor, style = { marginTop = 5 } };
        assistantColor.RegisterValueChangedCallback(evt => CodexApprovalPreferences.AssistantMessageColor = evt.newValue);
        card.Add(maxLines); card.Add(userColor); card.Add(assistantColor);
        parent.Add(card);
    }

    private void UpdateComposerHeight()
    {
        if (messageInput == null || composerRow == null || sendButton == null) return;
        const float lineHeight = 20f;
        const float chromeHeight = 2f;
        var availableWidth = messageInput.contentRect.width;
        if (availableWidth <= 1f) availableWidth = messageInput.resolvedStyle.width;
        if (availableWidth <= 1f) availableWidth = 360f;
        var charactersPerLine = Mathf.Max(12, Mathf.FloorToInt((availableWidth - 18f) / 7.5f));
        var visualLines = 0;
        var value = messageInput.value ?? string.Empty;
        var paragraphs = value.Replace("\r", string.Empty).Split('\n');
        foreach (var paragraph in paragraphs) visualLines += Mathf.Max(1, Mathf.CeilToInt(paragraph.Length / (float)charactersPerLine));
        visualLines = Mathf.Clamp(visualLines, 2, CodexApprovalPreferences.ComposerMaxLines);
        var height = visualLines * lineHeight + chromeHeight;
        messageInput.style.height = height;
        messageInput.style.minHeight = height;
        messageInput.style.maxHeight = height;
        composerRow.style.height = height;
        sendButton.style.height = height;
    }
    private void AddGlobalPromptSettings(VisualElement parent)
    {
        var promptCard = new VisualElement { style = { backgroundColor = new Color(.16f, .16f, .16f), paddingLeft = 10, paddingRight = 10, paddingTop = 9, paddingBottom = 10, marginTop = 10 } };
        promptCard.Add(new Label("全局提示词") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
        promptCard.Add(new Label("作为开发者指令附加到当前项目的后续 Codex 回合，不会显示为聊天消息。") { style = { fontSize = 10, opacity = .7f, marginTop = 2 } });
        var enabled = new Toggle("启用全局提示词") { value = CodexApprovalPreferences.GlobalPromptEnabled, style = { marginTop = 7 } };
        var prompt = new TextField { multiline = true, isDelayed = false, value = CodexApprovalPreferences.GlobalPrompt };
        prompt.style.height = 118; prompt.style.minHeight = 118; prompt.style.maxHeight = 118; prompt.style.whiteSpace = WhiteSpace.Normal; prompt.style.marginTop = 5;
        promptCard.Add(enabled); promptCard.Add(prompt);
        promptCard.Add(new Button(() =>
        {
            CodexApprovalPreferences.GlobalPromptEnabled = enabled.value;
            CodexApprovalPreferences.GlobalPrompt = prompt.value?.Trim() ?? string.Empty;
            CodexAppServerClient.InvalidateThreadInstructions(selectedThreadId);
            Debug.Log("[Codex Unity] Saved global prompt settings; they apply on the next sent turn.");
        }) { text = "保存全局提示词", style = { marginTop = 8 } });
        parent.Add(promptCard);
    }
    private void AddMcpToolSettings(VisualElement parent)
    {
        var selected = new HashSet<string>(CodexUnityMcpTools.GetEnabledToolNames(), StringComparer.Ordinal);
        var toolsCard = new VisualElement { style = { backgroundColor = new Color(.16f, .16f, .16f), paddingLeft = 10, paddingRight = 10, paddingTop = 9, paddingBottom = 10, marginTop = 10 } };
        toolsCard.Add(new Label("MCP 工具可用性") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
        toolsCard.Add(new Label("默认只启用低风险工具。保存后工具列表立即按选择更新。") { style = { fontSize = 10, opacity = .7f, marginTop = 2 } });
        var toolToggles = new Dictionary<string, Toggle>();
        var categoryToggles = new List<KeyValuePair<string[], Toggle>>();
        var selectAll = new Toggle("选择全部 API") { value = selected.Count == CodexUnityMcpTools.ToolNames.Length, style = { marginTop = 7 } };
        toolsCard.Add(selectAll);
        foreach (var category in CodexUnityMcpTools.ToolCategories)
        {
            var categoryTools = category.Tools;
            var categoryRow = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 3 } };
            var foldout = new Foldout { text = category.Name + "（" + categoryTools.Length + "）", value = false, tooltip = category.Description };
            foldout.style.flexGrow = 1;
            var selectCategory = new Toggle { value = categoryTools.All(selected.Contains), tooltip = "启用或关闭此分类的全部 API", style = { marginRight = 7 } };
            categoryToggles.Add(new KeyValuePair<string[], Toggle>(categoryTools, selectCategory));
            foreach (var tool in categoryTools)
            {
                var name = tool;
                var toggle = new Toggle(name) { value = selected.Contains(name), style = { marginLeft = 18 } };
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue) selected.Add(name); else selected.Remove(name);
                    selectCategory.SetValueWithoutNotify(categoryTools.All(selected.Contains));
                    selectAll.SetValueWithoutNotify(selected.Count == CodexUnityMcpTools.ToolNames.Length);
                });
                toolToggles.Add(name, toggle); foldout.Add(toggle);
                if (CodexUnityMcpTools.IsRiskyTool(name)) foldout.Add(new Label(CodexUnityMcpTools.GetToolRiskDescription(name)) { style = { marginLeft = 40, fontSize = 10, color = new Color(.95f, .72f, .22f) } });
            }
            selectCategory.RegisterValueChangedCallback(evt =>
            {
                foreach (var tool in categoryTools) { if (evt.newValue) selected.Add(tool); else selected.Remove(tool); toolToggles[tool].SetValueWithoutNotify(evt.newValue); }
                selectAll.SetValueWithoutNotify(selected.Count == CodexUnityMcpTools.ToolNames.Length);
            });
            categoryRow.Add(foldout); categoryRow.Add(selectCategory); toolsCard.Add(categoryRow);
        }
        selectAll.RegisterValueChangedCallback(evt =>
        {
            selected.Clear(); if (evt.newValue) foreach (var tool in CodexUnityMcpTools.ToolNames) selected.Add(tool);
            foreach (var pair in toolToggles) pair.Value.SetValueWithoutNotify(evt.newValue);
            foreach (var pair in categoryToggles) pair.Value.SetValueWithoutNotify(evt.newValue);
        });
        var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 8 } };
        actions.Add(new Button(() =>
        {
            selected.Clear(); foreach (var tool in CodexUnityMcpTools.GetDefaultEnabledToolNames()) selected.Add(tool);
            foreach (var pair in toolToggles) pair.Value.SetValueWithoutNotify(selected.Contains(pair.Key));
            foreach (var pair in categoryToggles) pair.Value.SetValueWithoutNotify(pair.Key.All(selected.Contains));
            selectAll.SetValueWithoutNotify(selected.Count == CodexUnityMcpTools.ToolNames.Length);
            CodexUnityMcpTools.SaveEnabledToolNames(selected);
            RefreshMcpPanelContent();
            Debug.Log("[Codex Unity] Reset MCP tool availability to the default low-risk set.");
        }) { text = "重置为默认" });
        actions.Add(new Button(() => { CodexUnityMcpTools.SaveEnabledToolNames(selected); RefreshMcpPanelContent(); Debug.Log("[Codex Unity] Saved " + selected.Count + " enabled MCP tool(s)."); }) { text = "保存 API 可用性", style = { marginLeft = 6 } });
        toolsCard.Add(actions);
        parent.Add(toolsCard);
    }
    private void AddCustomApiSettings(VisualElement parent)
    {
        var apiKeyMode = CodexApprovalPreferences.UsesApiKeyLogin;
        var apiCard = new VisualElement { style = { backgroundColor = new Color(.16f, .16f, .16f), paddingLeft = 10, paddingRight = 10, paddingTop = 9, paddingBottom = 10, marginTop = 10, opacity = apiKeyMode ? 1f : .45f } };
        apiCard.Add(new Label("自定义 API / 模型") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
        apiCard.Add(new Label(apiKeyMode ? "当前使用 API Key 登录。此处配置用于后续自定义模型提供商扩展。" : "当前正在复用 Codex 官方登录，已禁用自定义 API 设置。") { style = { fontSize = 10, opacity = .7f, marginTop = 2 } });
        var key = new TextField("API Key") { value = CodexApprovalPreferences.CustomApiKey, isPasswordField = false, style = { marginTop = 7 } };
        var model = new TextField("模型名称") { value = CodexApprovalPreferences.CustomApiModelName, style = { marginTop = 5 } };
        var url = new TextField("模型链接") { value = CodexApprovalPreferences.CustomApiModelUrl, style = { marginTop = 5 } };
        apiCard.Add(key); apiCard.Add(model); apiCard.Add(url);
        var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 8 } };
        actions.Add(new Button(() =>
        {
            CodexApprovalPreferences.CustomApiKey = key.value;
            CodexApprovalPreferences.CustomApiModelName = model.value?.Trim() ?? string.Empty;
            CodexApprovalPreferences.CustomApiModelUrl = url.value?.Trim() ?? string.Empty;
            Debug.Log("[Codex Unity] Saved custom model metadata for this editor session.");
        }) { text = "保存自定义模型设置", style = { flexGrow = 1 } });
        actions.Add(new Button(() =>
        {
            key.SetValueWithoutNotify(string.Empty);
            model.SetValueWithoutNotify(string.Empty);
            url.SetValueWithoutNotify(string.Empty);
            CodexApprovalPreferences.CustomApiKey = string.Empty;
            CodexApprovalPreferences.CustomApiModelName = string.Empty;
            CodexApprovalPreferences.CustomApiModelUrl = string.Empty;
            Debug.Log("[Codex Unity] Cleared custom API/model configuration.");
        }) { text = "⌫", tooltip = "清空 API Key、模型名称与模型链接", style = { width = 30, marginLeft = 6 } });
        apiCard.Add(actions);
        apiCard.SetEnabled(apiKeyMode);
        parent.Add(apiCard);
    }
    private void AddLoginSettings(VisualElement parent)
    {
        var loginCard = new VisualElement
        {
            style = { backgroundColor = new Color(.16f, .16f, .16f), paddingLeft = 10, paddingRight = 10, paddingTop = 9, paddingBottom = 10, marginTop = 10 }
        };
        loginCard.Add(new Label("登录") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
        loginCard.Add(new Label("退出后将返回插件的登录选择页；不会退出本机 Codex 或清除官方账号登录状态。")
        {
            style = { fontSize = 10, opacity = .7f, marginTop = 2, whiteSpace = WhiteSpace.Normal }
        });
        loginCard.Add(new Button(ExitPluginLogin)
        {
            text = "退出登录",
            tooltip = "退出此项目中的 Codex 插件会话选择",
            style = { marginTop = 8, backgroundColor = new Color(.33f, .16f, .16f) }
        });
        parent.Add(loginCard);
    }
    private void CreateLoginScreen()
    {
        isShowingSettingsPage = false;
        var host = new VisualElement
        {
            style = { flexGrow = 1, justifyContent = Justify.Center, alignItems = Align.Center, paddingLeft = 24, paddingRight = 24 }
        };
        var card = new VisualElement
        {
            style =
            {
                width = 380, maxWidth = 380, backgroundColor = new Color(.16f, .16f, .16f),
                paddingLeft = 22, paddingRight = 22, paddingTop = 20, paddingBottom = 20,
                borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
                borderTopColor = new Color(.29f, .29f, .29f), borderBottomColor = new Color(.29f, .29f, .29f),
                borderLeftColor = new Color(.29f, .29f, .29f), borderRightColor = new Color(.29f, .29f, .29f)
            }
        };
        card.Add(new Label("欢迎使用 Codex for Unity") { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 18 } });
        card.Add(new Label("此项目首次使用插件。选择一种登录方式后即可访问当前项目的 Codex 聊天池与 Unity MCP 工具。")
        {
            style = { whiteSpace = WhiteSpace.Normal, marginTop = 7, marginBottom = 12, opacity = .8f }
        });
        card.Add(new Button(UseLocalCodexLogin)
        {
            text = "使用本机 Codex 登录",
            tooltip = "复用已安装 Codex 的官方登录状态",
            style = { height = 32 }
        });
        var apiKeyLogin = new Button(BeginApiKeyLogin) { text = "通过 API Key 登录", tooltip = "配置 API Key、模型名称和模型链接", style = { height = 32, marginTop = 7 } };
        card.Add(apiKeyLogin);
        card.Add(new Label("本机 Codex 登录会复用官方登录状态；插件不会读取或解析你的凭证文件。")
        {
            style = { fontSize = 10, opacity = .65f, whiteSpace = WhiteSpace.Normal, marginTop = 12 }
        });
        host.Add(card);
        rootVisualElement.Add(host);
    }
    private void UseLocalCodexLogin()
    {
        // Do not render or auto-restore an API-key local conversation while the
        // App Server provider is being loaded.
        selectedThreadId = null;
        needsConversationRestore = false;
        CodexWorkspaceStore.Instance.Set(new CodexWorkspaceSnapshot());
        CodexApprovalPreferences.LoginMode = "local";
        CodexApprovalPreferences.HasCompletedLoginSetup = true;
        Debug.Log("[Codex Unity] Local Codex login selected; checking the official Codex session.");
        CreateGUI();
    }
    private void ExitPluginLogin()
    {
        CodexApprovalPreferences.HasCompletedLoginSetup = false;
        CodexApprovalPreferences.LoginMode = string.Empty;
        selectedThreadId = null;
        needsConversationRestore = false;
        Debug.Log("[Codex Unity] Returned to the plugin login screen. The local Codex account remains signed in.");
        CreateGUI();
    }
    private void BeginApiKeyLogin()
    {
        if (!string.IsNullOrWhiteSpace(CodexApprovalPreferences.CustomApiKey) &&
            !string.IsNullOrWhiteSpace(CodexApprovalPreferences.CustomApiModelName) &&
            !string.IsNullOrWhiteSpace(CodexApprovalPreferences.CustomApiModelUrl))
        {
            CompleteApiKeyLogin();
            return;
        }

        CreateApiKeySetupScreen();
    }
    private void CreateApiKeySetupScreen()
    {
        rootVisualElement.Clear();
        var host = new VisualElement
        {
            style = { flexGrow = 1, justifyContent = Justify.Center, alignItems = Align.Center, paddingLeft = 24, paddingRight = 24 }
        };
        var card = new VisualElement
        {
            style =
            {
                width = 400, maxWidth = 400, backgroundColor = new Color(.16f, .16f, .16f),
                paddingLeft = 22, paddingRight = 22, paddingTop = 20, paddingBottom = 20,
                borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
                borderTopColor = new Color(.29f, .29f, .29f), borderBottomColor = new Color(.29f, .29f, .29f),
                borderLeftColor = new Color(.29f, .29f, .29f), borderRightColor = new Color(.29f, .29f, .29f)
            }
        };
        card.Add(new Label("配置 API Key 登录") { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 18 } });
        card.Add(new Label("请填写自定义模型信息。三个字段均不能为空。") { style = { marginTop = 7, marginBottom = 10, opacity = .75f } });
        var key = new TextField("API Key") { value = CodexApprovalPreferences.CustomApiKey, isPasswordField = false };
        var model = new TextField("模型名称") { value = CodexApprovalPreferences.CustomApiModelName, style = { marginTop = 6 } };
        var url = new TextField("模型链接") { value = CodexApprovalPreferences.CustomApiModelUrl, style = { marginTop = 6 } };
        card.Add(key); card.Add(model); card.Add(url);
        var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 12 } };
        var connectionVerified = false;
        var status = new Label("请先检测连接性。") { style = { fontSize = 10, marginTop = 7, opacity = .75f } };
        var save = new Button(() =>
        {
            CodexApprovalPreferences.CustomApiKey = key.value?.Trim() ?? string.Empty;
            CodexApprovalPreferences.CustomApiModelName = model.value?.Trim() ?? string.Empty;
            CodexApprovalPreferences.CustomApiModelUrl = url.value?.Trim() ?? string.Empty;
            CompleteApiKeyLogin();
        }) { text = "保存并进入插件", style = { flexGrow = 1 } };
        void RefreshSaveState()
        {
            connectionVerified = false;
            save.SetEnabled(false);
            status.text = "参数已变更，请重新检测连接性。";
        }
        key.RegisterValueChangedCallback(_ => RefreshSaveState());
        model.RegisterValueChangedCallback(_ => RefreshSaveState());
        url.RegisterValueChangedCallback(_ => RefreshSaveState());
        save.SetEnabled(false);
        Button test = null;
        test = new Button(async () =>
        {
            test.SetEnabled(false);
            status.text = "正在检测连接性…详细信息将输出到 Console。";
            var checkedKey = key.value;
            var checkedModel = model.value;
            var checkedUrl = url.value;
            var result = await CodexCustomApiClient.ValidateAsync(checkedKey, checkedModel, checkedUrl);
            if (key.value != checkedKey || model.value != checkedModel || url.value != checkedUrl)
            {
                connectionVerified = false;
                save.SetEnabled(false);
                status.text = "参数已在检测期间变更，请重新检测连接性。";
                test.SetEnabled(true);
                return;
            }
            connectionVerified = result == "连接验证成功。";
            save.SetEnabled(connectionVerified);
            status.text = result;
            test.SetEnabled(true);
        }) { text = "检测连接性", style = { marginRight = 6 } };
        actions.Add(test);
        actions.Add(save);
        actions.Add(new Button(CreateGUI) { text = "返回", style = { marginLeft = 6 } });
        card.Add(actions); card.Add(status);
        card.Add(new Label("API Key 仅保存在当前 Unity 编辑器会话中，不会写入项目文件或 EditorPrefs。") { style = { fontSize = 10, opacity = .65f, whiteSpace = WhiteSpace.Normal, marginTop = 12 } });
        host.Add(card);
        rootVisualElement.Add(host);
    }
    private void CompleteApiKeyLogin()
    {
        // The two providers own different conversation pools. Clearing the
        // transient UI snapshot prevents an old Codex thread from being selected
        // before the API-key local pool is refreshed.
        selectedThreadId = null;
        needsConversationRestore = false;
        CodexWorkspaceStore.Instance.Set(new CodexWorkspaceSnapshot());
        CodexApprovalPreferences.LoginMode = "api";
        CodexApprovalPreferences.HasCompletedLoginSetup = true;
        Debug.Log("[Codex Unity] API Key login configuration selected. Custom API/model settings are enabled.");
        CreateGUI();
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
        var enabledTools = category.Tools.Where(CodexUnityMcpTools.IsToolEnabled).ToArray();
        mcpCategoryLabel.text = category.Name + "\n" + category.Description + "\n\nAPI（" + enabledTools.Length + "）\n• " + string.Join("\n• ", enabledTools);
        mcpCategoryPanel.style.display = DisplayStyle.Flex;
    }
    private static string GetProjectName() => Path.GetFileName(GetProjectRoot());
    private static string GetProjectRoot() => Directory.GetParent(Application.dataPath).FullName;
#endregion
}
