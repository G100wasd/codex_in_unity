using System;
using UnityEditor;
using UnityEngine;

internal sealed class CodexThreadRenamePopup : PopupWindowContent
{
    private string value;
    private readonly Action<string> submit;
    internal CodexThreadRenamePopup(string initialValue, Action<string> submit) { value = initialValue ?? string.Empty; this.submit = submit; }
    public override Vector2 GetWindowSize() => new Vector2(220, 62);
    public override void OnGUI(Rect rect)
    {
        GUILayout.Label("重命名聊天", EditorStyles.boldLabel);
        GUI.SetNextControlName("CodexRename"); value = EditorGUILayout.TextField(value);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("确定") && !string.IsNullOrWhiteSpace(value)) { submit?.Invoke(value); editorWindow.Close(); }
        if (GUILayout.Button("取消")) editorWindow.Close();
        GUILayout.EndHorizontal();
        if (Event.current.type == EventType.Repaint) EditorGUI.FocusTextInControl("CodexRename");
    }
}
