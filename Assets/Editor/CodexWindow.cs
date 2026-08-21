using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Entry point for the Codex Unity editor integration.
/// </summary>
public sealed class CodexWindow : EditorWindow
{
    private const float SidebarWidth = 216f;

    private readonly string[] chatThreads =
    {
        "Unity 插件方案",
        "场景交互设计",
        "修复角色移动",
        "新建对话"
    };

    private ScrollView conversation;
    private TextField messageInput;
    private Label activeThreadLabel;
    private VisualElement accountPanel;

    [MenuItem("Codex/Open Codex")]
    public static void Open()
    {
        GetWindow<CodexWindow>("Codex");
    }

    private void CreateGUI()
    {
        rootVisualElement.Clear();
        rootVisualElement.style.flexDirection = FlexDirection.Row;
        rootVisualElement.style.flexGrow = 1;
        rootVisualElement.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);

        rootVisualElement.Add(CreateSidebar());
        rootVisualElement.Add(CreateVerticalDivider());
        rootVisualElement.Add(CreateMainPanel());
    }

    private VisualElement CreateSidebar()
    {
        var sidebar = new VisualElement();
        sidebar.style.width = SidebarWidth;
        sidebar.style.flexShrink = 0;
        sidebar.style.flexDirection = FlexDirection.Column;
        sidebar.style.paddingTop = 12;
        sidebar.style.paddingBottom = 12;
        sidebar.style.paddingLeft = 10;
        sidebar.style.paddingRight = 10;
        sidebar.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f);

        var projectName = new Label(GetProjectName());
        projectName.style.unityFontStyleAndWeight = FontStyle.Bold;
        projectName.style.fontSize = 15;
        projectName.style.marginBottom = 4;
        sidebar.Add(projectName);

        var projectHint = new Label("项目聊天池");
        projectHint.style.opacity = 0.65f;
        projectHint.style.marginBottom = 10;
        sidebar.Add(projectHint);

        var threadList = new ScrollView();
        threadList.style.flexGrow = 1;
        foreach (var threadName in chatThreads)
        {
            var threadButton = new Button(() => SelectThread(threadName))
            {
                text = threadName
            };
            threadButton.style.unityTextAlign = TextAnchor.MiddleLeft;
            threadButton.style.marginBottom = 4;
            threadList.Add(threadButton);
        }
        sidebar.Add(threadList);

        accountPanel = new VisualElement();
        accountPanel.style.display = DisplayStyle.None;
        accountPanel.style.paddingTop = 8;
        accountPanel.style.paddingBottom = 8;
        accountPanel.style.paddingLeft = 8;
        accountPanel.style.paddingRight = 8;
        accountPanel.style.marginBottom = 6;
        accountPanel.style.backgroundColor = new Color(0.14f, 0.14f, 0.14f);
        accountPanel.Add(new Label("账户额度"));
        accountPanel.Add(new Label("尚未连接 Codex App Server"));
        sidebar.Add(accountPanel);

        var accountButton = new Button(ToggleAccountPanel)
        {
            text = "◎"
        };
        accountButton.tooltip = "查看账户与额度";
        accountButton.style.width = 32;
        accountButton.style.height = 32;
        sidebar.Add(accountButton);

        return sidebar;
    }

    private static VisualElement CreateVerticalDivider()
    {
        var divider = new VisualElement();
        divider.style.width = 1;
        divider.style.flexShrink = 0;
        divider.style.backgroundColor = new Color(0.3f, 0.3f, 0.3f);
        return divider;
    }

    private VisualElement CreateMainPanel()
    {
        var mainPanel = new VisualElement();
        mainPanel.style.flexDirection = FlexDirection.Column;
        mainPanel.style.flexGrow = 1;
        mainPanel.style.paddingTop = 12;
        mainPanel.style.paddingBottom = 12;
        mainPanel.style.paddingLeft = 14;
        mainPanel.style.paddingRight = 14;
        mainPanel.style.backgroundColor = new Color(0.13f, 0.13f, 0.13f);

        activeThreadLabel = new Label(chatThreads[0]);
        activeThreadLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        activeThreadLabel.style.fontSize = 16;
        activeThreadLabel.style.marginBottom = 10;
        mainPanel.Add(activeThreadLabel);

        conversation = new ScrollView();
        conversation.style.flexGrow = 1;
        conversation.style.marginBottom = 10;
        mainPanel.Add(conversation);

        var composer = new VisualElement();
        composer.style.flexDirection = FlexDirection.Column;
        composer.style.paddingTop = 7;
        composer.style.paddingBottom = 7;
        composer.style.paddingLeft = 8;
        composer.style.paddingRight = 8;
        composer.style.backgroundColor = new Color(0.19f, 0.19f, 0.19f);
        composer.style.borderTopLeftRadius = 12;
        composer.style.borderTopRightRadius = 12;
        composer.style.borderBottomLeftRadius = 12;
        composer.style.borderBottomRightRadius = 12;
        composer.style.borderTopWidth = 1;
        composer.style.borderBottomWidth = 1;
        composer.style.borderLeftWidth = 1;
        composer.style.borderRightWidth = 1;
        composer.style.borderTopColor = new Color(0.32f, 0.32f, 0.32f);
        composer.style.borderBottomColor = new Color(0.32f, 0.32f, 0.32f);
        composer.style.borderLeftColor = new Color(0.32f, 0.32f, 0.32f);
        composer.style.borderRightColor = new Color(0.32f, 0.32f, 0.32f);

        var inputRow = new VisualElement();
        inputRow.style.flexDirection = FlexDirection.Row;
        inputRow.style.alignItems = Align.Center;

        var attachButton = new Button { text = "+" };
        attachButton.tooltip = "后续用于添加场景、对象或图片上下文";
        attachButton.style.width = 28;
        attachButton.style.height = 28;
        attachButton.style.marginRight = 6;
        attachButton.style.borderTopLeftRadius = 14;
        attachButton.style.borderTopRightRadius = 14;
        attachButton.style.borderBottomLeftRadius = 14;
        attachButton.style.borderBottomRightRadius = 14;
        inputRow.Add(attachButton);

        messageInput = new TextField
        {
            multiline = true,
            isDelayed = false
        };
        messageInput.style.flexGrow = 1;
        messageInput.style.minHeight = 34;
        messageInput.style.maxHeight = 104;
        messageInput.style.marginRight = 8;
        messageInput.style.whiteSpace = WhiteSpace.Normal;
        messageInput.tooltip = "输入要发送给 Codex 的消息";
        inputRow.Add(messageInput);

        var sendButton = new Button(SendMessage) { text = "↑" };
        sendButton.tooltip = "发送";
        sendButton.style.width = 32;
        sendButton.style.height = 32;
        sendButton.style.unityFontStyleAndWeight = FontStyle.Bold;
        sendButton.style.borderTopLeftRadius = 16;
        sendButton.style.borderTopRightRadius = 16;
        sendButton.style.borderBottomLeftRadius = 16;
        sendButton.style.borderBottomRightRadius = 16;
        inputRow.Add(sendButton);

        composer.Add(inputRow);

        var optionsRow = new VisualElement();
        optionsRow.style.flexDirection = FlexDirection.Row;
        optionsRow.style.marginTop = 4;
        optionsRow.style.paddingLeft = 34;

        var modelMenu = new ToolbarMenu { text = "GPT-5.6 Terra" };
        modelMenu.menu.AppendAction("GPT-5.6 Terra", action => modelMenu.text = action.name);
        modelMenu.menu.AppendAction("GPT-5.6 Sol", action => modelMenu.text = action.name);
        modelMenu.menu.AppendAction("GPT-5.6 Luna", action => modelMenu.text = action.name);
        optionsRow.Add(modelMenu);

        var effortMenu = new ToolbarMenu { text = "思考：中" };
        effortMenu.style.marginLeft = 8;
        effortMenu.menu.AppendAction("思考：低", action => effortMenu.text = action.name);
        effortMenu.menu.AppendAction("思考：中", action => effortMenu.text = action.name);
        effortMenu.menu.AppendAction("思考：高", action => effortMenu.text = action.name);
        optionsRow.Add(effortMenu);
        composer.Add(optionsRow);

        mainPanel.Add(composer);

        return mainPanel;
    }

    private static VisualElement CreateMessage(string sender, string text)
    {
        var message = new VisualElement();
        message.style.marginBottom = 8;
        message.style.paddingTop = 8;
        message.style.paddingBottom = 8;
        message.style.paddingLeft = 10;
        message.style.paddingRight = 10;
        message.style.backgroundColor = new Color(0.15f, 0.15f, 0.15f);
        message.Add(new Label(sender) { style = { unityFontStyleAndWeight = FontStyle.Bold } });
        message.Add(new Label(text) { style = { whiteSpace = WhiteSpace.Normal } });
        return message;
    }

    private void SelectThread(string threadName)
    {
        activeThreadLabel.text = threadName;
        conversation.Clear();
    }

    private void SendMessage()
    {
        var text = messageInput.value?.Trim();
        if (string.IsNullOrEmpty(text))
            return;

        conversation.Add(CreateMessage("你", text));
        messageInput.value = string.Empty;
        conversation.ScrollTo(conversation.contentContainer.ElementAt(conversation.contentContainer.childCount - 1));
    }

    private void ToggleAccountPanel()
    {
        accountPanel.style.display = accountPanel.style.display == DisplayStyle.None
            ? DisplayStyle.Flex
            : DisplayStyle.None;
    }

    private static string GetProjectName()
    {
        return System.IO.Path.GetFileName(System.IO.Directory.GetParent(Application.dataPath).FullName);
    }
}
