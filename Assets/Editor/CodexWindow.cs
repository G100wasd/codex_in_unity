using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class CodexWindow : EditorWindow
{
    private static CodexWindow activeWindow;
    [MenuItem("Codex/Open Codex")]
    public static void Open() => GetWindow<CodexWindow>("Codex");

    private void CreateGUI()
    {
        activeWindow = this;
        rootVisualElement.Clear();
        rootVisualElement.style.flexDirection = FlexDirection.Row;
        rootVisualElement.style.flexGrow = 1;
        rootVisualElement.style.backgroundColor = new Color(.12f, .12f, .12f);

        if (!CodexApprovalPreferences.HasCompletedLoginSetup)
        {
            CreateLoginScreen();
            return;
        }

        rootVisualElement.Add(CreateSidebar());
        rootVisualElement.Add(CreateVerticalDivider());
        rootVisualElement.Add(CreateMainPanel());
        RefreshMcpPanelContent();
        needsConversationRestore = true;
        RefreshWorkspaceUi();
        CodexUnityMcpBridge.EnsureStarted();
        BeginWorkspaceRefresh();
    }
}
