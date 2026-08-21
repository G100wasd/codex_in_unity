using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class CodexWindow : EditorWindow
{
    [MenuItem("Codex/Open Codex")]
    public static void Open() => GetWindow<CodexWindow>("Codex");

    private void CreateGUI()
    {
        rootVisualElement.Clear();
        rootVisualElement.style.flexDirection = FlexDirection.Row;
        rootVisualElement.style.flexGrow = 1;
        rootVisualElement.style.backgroundColor = new Color(.12f, .12f, .12f);
        rootVisualElement.Add(CreateSidebar());
        rootVisualElement.Add(CreateVerticalDivider());
        rootVisualElement.Add(CreateMainPanel());
        needsConversationRestore = true;
        RefreshWorkspaceUi();
        BeginWorkspaceRefresh();
    }
}
