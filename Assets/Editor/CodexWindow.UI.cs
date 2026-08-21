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
    private Label activeThreadLabel, accountLabel;
    private VisualElement accountPanel;
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
        }; accountLabel = new Label(); accountPanel.Add(accountLabel); side.Add(accountPanel);
        side.Add(new Button(ToggleAccountPanel) { text = "◎", tooltip = "查看账户与额度" });
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
        messageInput = new TextField { multiline = true, isDelayed = false };
        messageInput.style.height = 40;
        messageInput.style.minHeight = 40; 
        messageInput.style.maxHeight = 104; 
        messageInput.style.whiteSpace = WhiteSpace.Normal; 
        messageInput.style.flexGrow = 1;
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
        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, height = 40, flexShrink = 0 } };
        row.Add(messageInput); sendButton = new Button(SendMessage) { text = "↑", tooltip = "发送", style = { height = 40, width = 36, marginLeft = 6 } }; 
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
