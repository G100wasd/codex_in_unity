using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class CodexWindow
{
#region UI Elements
    private const float SidebarWidth = 216f;
    private ScrollView conversation, threadList;
    private TextField messageInput;
    private Button sendButton;
    private Label activeThreadLabel, accountLabel, quotaLabel, mcpLabel, mcpCategoryLabel;
    private VisualElement accountPanel, mcpPanel, quotaFill, mcpCategories, mcpCategoryPanel;
    private ToolbarMenu modelMenu, effortMenu;

    private VisualElement CreateSidebar()
    {
        var side = new VisualElement(); 
        side.style.width = SidebarWidth; 
        side.style.flexDirection = FlexDirection.Column; 
        side.style.paddingLeft = 10; 
        side.style.paddingRight = 10; 
        side.style.paddingTop = 12;
        side.style.paddingBottom = 12; 
        side.style.backgroundColor = new Color(.16f,.16f,.16f);
        side.Add(new Label(GetProjectName())
        {
            style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 15 }
        });
        var threadHeader = new VisualElement
        {
            style = { flexDirection = FlexDirection.Row }
        };
        threadHeader.Add(new Label("项目聊天池") { style = { flexGrow = 1 } }); 
        threadHeader.Add(new Button(CreateNewThread) { text = "＋", tooltip = "为当前项目创建新聊天" }); 
        side.Add(threadHeader);
        threadList = new ScrollView { style = { flexGrow = 1, marginTop = 8 } }; 
        side.Add(threadList);
        accountPanel = new VisualElement
        {
            style =
            {
                display = DisplayStyle.None, 
                backgroundColor = new Color(.14f,.14f,.14f), 
                paddingLeft = 8, 
                paddingTop = 8,
                paddingBottom = 8
            }
        };
        accountLabel = new Label(); accountPanel.Add(accountLabel);
        quotaLabel = new Label { style = { marginTop = 6, fontSize = 10, opacity = .75f } }; accountPanel.Add(quotaLabel);
        var quotaTrack = new VisualElement { style = { height = 5, marginTop = 3, backgroundColor = new Color(.09f, .09f, .09f) } };
        quotaFill = new VisualElement { style = { height = 5, width = Length.Percent(0), backgroundColor = new Color(.30f, .70f, .95f) } };
        quotaTrack.Add(quotaFill); accountPanel.Add(quotaTrack); side.Add(accountPanel);
        mcpPanel = new VisualElement { style = { display = DisplayStyle.None, backgroundColor = new Color(.10f, .16f, .20f), paddingLeft = 8, paddingRight = 8, paddingTop = 8, paddingBottom = 8 } };
        mcpLabel = new Label { style = { whiteSpace = WhiteSpace.Normal, fontSize = 10 } }; mcpPanel.Add(mcpLabel);
        mcpCategories = new VisualElement { style = { marginTop = 6 } }; mcpPanel.Add(mcpCategories); side.Add(mcpPanel);
        mcpCategoryPanel = new VisualElement { style = { display = DisplayStyle.None, backgroundColor = new Color(.08f, .13f, .17f), paddingLeft = 8, paddingRight = 8, paddingTop = 8, paddingBottom = 8 } };
        mcpCategoryLabel = new Label { style = { whiteSpace = WhiteSpace.Normal, fontSize = 10 } }; mcpCategoryPanel.Add(mcpCategoryLabel); side.Add(mcpCategoryPanel);
        var bottomActions = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 6, height = 22 } };
        bottomActions.Add(new Button(ToggleAccountPanel) { text = "◎", tooltip = "查看账户与额度", style = { flexGrow = 1, marginRight = 3 } });
        bottomActions.Add(new Button(ToggleMcpPanel) { text = "⌁", tooltip = "查看 Unity MCP 端口与工具", style = { flexGrow = 1, marginLeft = 3 } });
        side.Add(bottomActions);
        return side;
    }
    // Creates the visual separator between the sidebar and the main conversation panel.
    private static VisualElement CreateVerticalDivider() => new VisualElement { style = { width = 1, backgroundColor = new Color(.3f,.3f,.3f) } };
    private VisualElement CreateMainPanel()
    {
        var main = new VisualElement
        {
            style =
            {
                flexGrow = 1, 
                flexDirection = FlexDirection.Column, 
                paddingLeft = 14, 
                paddingRight = 14, 
                paddingTop = 12, 
                paddingBottom = 12
            }
        };
        activeThreadLabel = new Label("请选择或新建对话")
        {
            style =
            {
                unityFontStyleAndWeight = FontStyle.Bold, fontSize = 16
            }
        }; main.Add(activeThreadLabel); 
        conversation = new ScrollView { style = { flexGrow = 1 } };
        main.Add(conversation);
        messageInput = new TextField { multiline = true, isDelayed = false, verticalScrollerVisibility = ScrollerVisibility.Auto };
        messageInput.style.height = 42;
        messageInput.style.minHeight = 42;
        messageInput.style.maxHeight = 42;
        messageInput.style.whiteSpace = WhiteSpace.Normal; 
        messageInput.style.flexGrow = 1;
        messageInput.style.flexShrink = 1;
        messageInput.style.flexBasis = 0;
        messageInput.style.minWidth = 0;
        messageInput.RegisterCallback<KeyDownEvent>(evt =>
        {
            if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && !evt.shiftKey)
            {
                evt.PreventDefault();
                evt.StopPropagation();
                SendMessage();
            }
        });
        var composer = new VisualElement { style = { flexDirection = FlexDirection.Column, flexShrink = 0, marginTop = 8 } };
        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, height = 42, flexShrink = 0 } };
        row.Add(messageInput); sendButton = new Button(SendMessage) { text = "↑", tooltip = "发送", style = { height = 42, width = 36, flexShrink = 0, marginLeft = 6 } }; 
        row.Add(sendButton); composer.Add(row);
        var options = new VisualElement { style = { flexDirection = FlexDirection.Row, height = 22, flexShrink = 0, marginTop = 6 } }; 
        modelMenu = new ToolbarMenu { text = "正在加载模型…" };
        effortMenu = new ToolbarMenu { text = "思考：—" };
        options.Add(modelMenu);
        options.Add(effortMenu); composer.Add(options); main.Add(composer); return main;
    }
    private static VisualElement CreateMessage(string sender, string text) { var box = new VisualElement { style = { marginBottom = 8, paddingLeft = 8, paddingTop = 6, paddingBottom = 6 } }; box.Add(new Label(sender)); box.Add(new Label(text) { style = { whiteSpace = WhiteSpace.Normal } }); return box; }
    private static VisualElement CreateStreamingMessage(string sender, out Label content)
    {
        var box = new VisualElement { style = { marginBottom = 8, paddingLeft = 8, paddingTop = 6, paddingBottom = 6 } };
        box.Add(new Label(sender));
        content = new Label("正在生成回复…") { style = { whiteSpace = WhiteSpace.Normal } };
        box.Add(content);
        return box;
    }
    private static VisualElement CreateApprovalCard(CodexApprovalRequest request)
    {
        var card = new VisualElement { style = { marginTop = 8, marginBottom = 8, paddingLeft = 10, paddingRight = 10, paddingTop = 8, paddingBottom = 8, backgroundColor = new Color(.20f, .18f, .10f) } };
        card.Add(new Label(request.Title) { style = { unityFontStyleAndWeight = FontStyle.Bold } });
        card.Add(new Label(request.Reason) { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4 } });
        if (!string.IsNullOrEmpty(request.GrantRoot)) card.Add(new Label("范围：" + request.GrantRoot) { style = { opacity = .75f } });
        var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 6 } };
        actions.Add(new Button(() => ResolveApproval(card, request, "acceptForSession")) { text = "允许全部（本会话）", tooltip = "允许本会话内同一批文件的后续修改" });
        actions.Add(new Button(() => ResolveApproval(card, request, "accept")) { text = "仅本次允许" });
        actions.Add(new Button(() => ResolveApproval(card, request, "cancel")) { text = "取消" });
        card.Add(actions);
        return card;
    }
    private static void ResolveApproval(VisualElement card, CodexApprovalRequest request, string decision)
    {
        card.SetEnabled(false);
        request.Respond?.Invoke(decision);
        card.RemoveFromHierarchy();
    }
    private static VisualElement CreateMcpElicitationCard(CodexMcpElicitationRequest request)
    {
        var card = new VisualElement { style = { marginTop = 8, marginBottom = 8, paddingLeft = 10, paddingRight = 10, paddingTop = 8, paddingBottom = 8, backgroundColor = new Color(.10f, .18f, .23f) } };
        card.Add(new Label("Unity MCP 请求") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
        card.Add(new Label("服务：" + request.ServerName) { style = { opacity = .75f, marginTop = 3 } });
        card.Add(new Label(string.IsNullOrEmpty(request.Message) ? "Codex 请求继续使用 Unity MCP 工具。" : request.Message) { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4 } });
        if (!string.IsNullOrEmpty(request.RequestedSchema)) card.Add(new Label("请求数据：" + request.RequestedSchema) { style = { whiteSpace = WhiteSpace.Normal, opacity = .65f, marginTop = 3 } });
        var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 6 } };
        actions.Add(new Button(() => ResolveMcpElicitation(card, request, "accept")) { text = "允许本次" });
        actions.Add(new Button(() => ResolveMcpElicitation(card, request, "decline")) { text = "拒绝" });
        actions.Add(new Button(() => ResolveMcpElicitation(card, request, "cancel")) { text = "取消" });
        card.Add(actions);
        return card;
    }
    private static void ResolveMcpElicitation(VisualElement card, CodexMcpElicitationRequest request, string decision)
    {
        card.SetEnabled(false);
        request.Respond?.Invoke(decision);
        card.RemoveFromHierarchy();
    }
    private static VisualElement CreateMcpApiApprovalCard(CodexMcpApiApprovalRequest request)
    {
        var card = new VisualElement { style = { marginTop = 8, marginBottom = 8, paddingLeft = 10, paddingRight = 10, paddingTop = 8, paddingBottom = 8, backgroundColor = request.IsLongRunning ? new Color(.22f, .12f, .04f) : new Color(.25f, .14f, .08f) } };
        card.Add(new Label(request.IsLongRunning ? "Unity 长任务审批" : "Unity API 操作审批") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
        card.Add(new Label("工具：" + request.ToolName) { style = { marginTop = 3 } });
        card.Add(new Label(request.Summary) { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4 } });
        card.Add(new Label("参数：" + request.Arguments) { style = { whiteSpace = WhiteSpace.Normal, opacity = .7f, marginTop = 3 } });
        if (request.IsLongRunning) card.Add(new Label("任务获准后会立即返回 jobId；请用状态工具查询进度。构建等 Unity 原生操作开始后通常无法安全中断。") { style = { whiteSpace = WhiteSpace.Normal, opacity = .8f, marginTop = 4 } });
        var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 6 } };
        actions.Add(new Button(() => ResolveMcpApiApproval(card, request, true)) { text = "允许本次" });
        actions.Add(new Button(() => ResolveMcpApiApproval(card, request, false)) { text = "拒绝" });
        card.Add(actions);
        return card;
    }
    private static void ResolveMcpApiApproval(VisualElement card, CodexMcpApiApprovalRequest request, bool allowed)
    {
        card.SetEnabled(false);
        request.Respond?.Invoke(allowed);
        card.RemoveFromHierarchy();
    }
    private static VisualElement CreateFileChangeCard(System.Collections.Generic.List<CodexFileChange> changes)
    {
        var card = new VisualElement
        {
            style =
            {
                marginTop = 8, marginBottom = 8, paddingLeft = 12, paddingRight = 12, paddingTop = 10, paddingBottom = 8,
                backgroundColor = new Color(.12f, .12f, .12f), borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
                borderTopColor = new Color(.24f, .24f, .24f), borderBottomColor = new Color(.24f, .24f, .24f), borderLeftColor = new Color(.24f, .24f, .24f), borderRightColor = new Color(.24f, .24f, .24f)
            }
        };
        var added = 0; var removed = 0; foreach (var change in changes) { added += change.Added; removed += change.Removed; }
        var header = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
        header.Add(CreateFileIcon());
        header.Add(new Label("已编辑 " + changes.Count + " 个文件") { style = { unityFontStyleAndWeight = FontStyle.Bold, flexGrow = 1, marginLeft = 7 } });
        header.Add(new Label("+" + added + "  -" + removed) { style = { color = new Color(.30f, .85f, .55f) } });
        card.Add(header);
        var shown = 0;
        foreach (var change in changes)
        {
            if (shown++ == 3) { card.Add(new Label("还有 " + (changes.Count - 3) + " 个文件") { style = { marginTop = 7, opacity = .7f } }); break; }
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, marginTop = 7, paddingTop = 6, borderTopWidth = 1, borderTopColor = new Color(.21f, .21f, .21f) } };
            row.Add(CreateFileIcon());
            var fileName = new Label(Path.GetFileName(change.Path))
            {
                tooltip = change.Path,
                style = { flexGrow = 1, marginLeft = 7, color = new Color(.63f, .78f, 1f) }
            };
            fileName.RegisterCallback<ClickEvent>(_ => OpenChangedFile(change.Path));
            row.Add(fileName);
            row.Add(new Label("+" + change.Added + "  -" + change.Removed) { style = { color = new Color(.30f, .85f, .55f) } });
            card.Add(row);
        }
        return card;
    }

    private static Image CreateFileIcon()
    {
        return new Image
        {
            image = EditorGUIUtility.IconContent("TextAsset Icon").image,
            scaleMode = ScaleMode.ScaleToFit,
            style = { width = 16, height = 16, flexShrink = 0 }
        };
    }

    private static void OpenChangedFile(string reportedPath)
    {
        if (string.IsNullOrEmpty(reportedPath)) return;

        var projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace('\\', '/');
        var fullPath = Path.GetFullPath(reportedPath).Replace('\\', '/');
        var assetPath = fullPath.StartsWith(projectRoot + "/")
            ? fullPath.Substring(projectRoot.Length + 1)
            : reportedPath.Replace('\\', '/');

        var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
        if (asset == null)
        {
            Debug.LogWarning("[Codex Unity] 无法在项目中打开已编辑文件：" + reportedPath);
            return;
        }

        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
        AssetDatabase.OpenAsset(asset);
    }
#endregion
}
