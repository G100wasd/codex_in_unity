using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class CodexUnityMcpTools
{
    internal sealed class ToolCategory { public readonly string Name; public readonly string Description; public readonly string[] Tools; public ToolCategory(string name, string description, params string[] tools) { Name = name; Description = description; Tools = tools; } }
    internal readonly struct ToolOutput { public readonly string Text; public readonly bool IsError; public ToolOutput(string text, bool isError = false) { Text = text; IsError = isError; } }
    private static readonly List<string> RecentLogs = new List<string>();
    private static TestRunnerApi testRunnerApi;
    private static string testRunId, testRunStatus = "No test run has been started.", testRunSummary;
    internal static readonly string[] ToolNames = { "unity_get_editor_state", "unity_get_open_scenes", "unity_get_scene_view_state", "unity_get_hierarchy", "unity_find_game_objects", "unity_get_game_object_details", "unity_get_selection", "unity_set_selection", "unity_frame_selection", "unity_get_component_properties", "unity_create_game_object", "unity_create_primitive", "unity_delete_game_object", "unity_duplicate_game_object", "unity_add_component", "unity_remove_component", "unity_set_transform", "unity_set_game_object_metadata", "unity_set_serialized_property", "unity_get_recent_logs", "unity_get_console_summary", "unity_get_project_info", "unity_find_asset", "unity_get_asset_details", "unity_get_asset_dependencies", "unity_get_prefab_details", "unity_open_asset", "unity_create_scene", "unity_open_scene", "unity_close_scene", "unity_set_active_scene", "unity_save_active_scene", "unity_save_all_scenes", "unity_create_prefab", "unity_instantiate_prefab", "unity_create_folder", "unity_move_asset", "unity_rename_asset", "unity_delete_asset", "unity_duplicate_asset", "unity_create_material", "unity_create_script", "unity_set_asset_labels", "unity_reimport_asset", "unity_refresh_asset_database", "unity_save_assets", "unity_get_build_settings", "unity_add_scene_to_build_settings", "unity_get_define_symbols", "unity_set_define_symbols", "unity_get_installed_packages", "unity_find_missing_scripts", "unity_run_tests", "unity_get_test_run_status", "unity_undo", "unity_redo", "unity_set_play_mode", "unity_execute_menu_item" };
    internal static readonly ToolCategory[] ToolCategories =
    {
        new ToolCategory("编辑器与场景", "编辑器状态、场景与视图上下文", "unity_get_editor_state", "unity_get_open_scenes", "unity_get_scene_view_state", "unity_get_hierarchy"),
        new ToolCategory("对象与 Inspector", "场景对象检索、选择、组件与受审批的对象修改", "unity_find_game_objects", "unity_get_game_object_details", "unity_get_selection", "unity_set_selection", "unity_frame_selection", "unity_get_component_properties", "unity_create_game_object", "unity_create_primitive", "unity_delete_game_object", "unity_duplicate_game_object", "unity_add_component", "unity_remove_component", "unity_set_transform", "unity_set_game_object_metadata", "unity_set_serialized_property"),
        new ToolCategory("Console、测试与诊断", "Bridge 日志、Unity Test Runner 与 Missing Script 检查", "unity_get_recent_logs", "unity_get_console_summary", "unity_find_missing_scripts", "unity_run_tests", "unity_get_test_run_status"),
        new ToolCategory("Scene 与 Prefab", "场景与 Prefab 的创建、打开、关闭、保存和实例化（写操作均需审批）", "unity_create_scene", "unity_open_scene", "unity_close_scene", "unity_set_active_scene", "unity_save_active_scene", "unity_save_all_scenes", "unity_create_prefab", "unity_instantiate_prefab", "unity_get_prefab_details"),
        new ToolCategory("项目与资源", "项目、资源、依赖与受审批的资源管理", "unity_get_project_info", "unity_find_asset", "unity_get_asset_details", "unity_get_asset_dependencies", "unity_open_asset", "unity_create_folder", "unity_move_asset", "unity_rename_asset", "unity_delete_asset", "unity_duplicate_asset", "unity_create_material", "unity_create_script", "unity_set_asset_labels", "unity_reimport_asset", "unity_refresh_asset_database", "unity_save_assets"),
        new ToolCategory("构建、包与编辑器控制", "Build Settings、Define Symbols、Package 列表与编辑器操作", "unity_get_build_settings", "unity_add_scene_to_build_settings", "unity_get_define_symbols", "unity_set_define_symbols", "unity_get_installed_packages", "unity_undo", "unity_redo", "unity_set_play_mode", "unity_execute_menu_item")
    };
    internal const string ToolDefinitionsJson = "["
        + "{\"name\":\"unity_get_editor_state\",\"description\":\"Get the active Unity scene, play mode, and current selection.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}},"
        + "{\"name\":\"unity_get_hierarchy\",\"description\":\"Get a compact tree of GameObjects in the active scene.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"maxDepth\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":8}}}},"
        + "{\"name\":\"unity_get_selection\",\"description\":\"Get the selected Unity object and its component types.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}},"
        + "{\"name\":\"unity_get_recent_logs\",\"description\":\"Get recent Unity Console messages observed since this bridge started.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":50}}}},"
        + "{\"name\":\"unity_find_asset\",\"description\":\"Find Unity assets by an AssetDatabase search query.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"}},\"required\":[\"query\"]}},"
        + "{\"name\":\"unity_open_asset\",\"description\":\"Select, ping, and open a project asset by its Assets/ path.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_get_open_scenes\",\"description\":\"List all loaded Unity scenes and identify the active scene.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"
        + ",{\"name\":\"unity_get_scene_view_state\",\"description\":\"Get the current Scene View camera position, rotation, size, and 2D mode.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"
        + ",{\"name\":\"unity_find_game_objects\",\"description\":\"Find loaded scene GameObjects by a case-insensitive name fragment.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"},\"maxResults\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":100}}}}"
        + ",{\"name\":\"unity_get_game_object_details\",\"description\":\"Get transform, child, and component details for a loaded GameObject by its full hierarchy path.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_get_project_info\",\"description\":\"Get Unity version, project product/company names, active build target, and project root path.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"
        + ",{\"name\":\"unity_get_asset_details\",\"description\":\"Get type, importer, labels, and basic metadata for an Assets/ or Packages/ asset path.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_get_asset_dependencies\",\"description\":\"List direct dependencies of an Assets/ or Packages/ asset path.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_get_prefab_details\",\"description\":\"Get root object, component, and direct-child details for a Prefab asset path.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_create_game_object\",\"description\":\"Create a GameObject in the active scene. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"},\"parentPath\":{\"type\":\"string\"}},\"required\":[\"name\"]}}"
        + ",{\"name\":\"unity_delete_game_object\",\"description\":\"Delete a loaded GameObject by hierarchy path. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_duplicate_game_object\",\"description\":\"Duplicate a loaded GameObject by hierarchy path. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"newName\":{\"type\":\"string\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_add_component\",\"description\":\"Add a Component by full type name to a GameObject. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"componentType\":{\"type\":\"string\"}},\"required\":[\"path\",\"componentType\"]}}"
        + ",{\"name\":\"unity_remove_component\",\"description\":\"Remove a Component by full type name from a GameObject. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"componentType\":{\"type\":\"string\"}},\"required\":[\"path\",\"componentType\"]}}"
        + ",{\"name\":\"unity_set_transform\",\"description\":\"Set optional localPosition, localEulerAngles, and localScale Vector3 values on a GameObject. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"localPosition\":{\"type\":\"object\"},\"localEulerAngles\":{\"type\":\"object\"},\"localScale\":{\"type\":\"object\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_set_game_object_metadata\",\"description\":\"Set optional name, tag, layer, or activeSelf on a GameObject. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"name\":{\"type\":\"string\"},\"tag\":{\"type\":\"string\"},\"layer\":{\"type\":\"integer\"},\"active\":{\"type\":\"boolean\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_create_scene\",\"description\":\"Create and save a new empty scene at an Assets/ path. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_open_scene\",\"description\":\"Open a scene from an Assets/ path, replacing the current scene or additively. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"additive\":{\"type\":\"boolean\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_save_active_scene\",\"description\":\"Save the active scene, optionally to a new Assets/ path. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}}}}"
        + ",{\"name\":\"unity_create_prefab\",\"description\":\"Save a loaded GameObject as a Prefab asset. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"sourcePath\":{\"type\":\"string\"},\"assetPath\":{\"type\":\"string\"}},\"required\":[\"sourcePath\",\"assetPath\"]}}"
        + ",{\"name\":\"unity_instantiate_prefab\",\"description\":\"Instantiate a Prefab asset into the active scene. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"assetPath\":{\"type\":\"string\"},\"parentPath\":{\"type\":\"string\"}},\"required\":[\"assetPath\"]}}"
        + ",{\"name\":\"unity_create_folder\",\"description\":\"Create an Assets/ folder. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_move_asset\",\"description\":\"Move an asset or folder to a new Assets/ path. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"fromPath\":{\"type\":\"string\"},\"toPath\":{\"type\":\"string\"}},\"required\":[\"fromPath\",\"toPath\"]}}"
        + ",{\"name\":\"unity_rename_asset\",\"description\":\"Rename an asset or folder without changing its parent folder. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"newName\":{\"type\":\"string\"}},\"required\":[\"path\",\"newName\"]}}"
        + ",{\"name\":\"unity_set_play_mode\",\"description\":\"Enter or exit Unity Play Mode. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"playing\":{\"type\":\"boolean\"}},\"required\":[\"playing\"]}}"
        + ",{\"name\":\"unity_execute_menu_item\",\"description\":\"Execute a Unity editor menu item by its exact path. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"menuItem\":{\"type\":\"string\"}},\"required\":[\"menuItem\"]}}"
        + ",{\"name\":\"unity_create_material\",\"description\":\"Create a Material asset with a named shader. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"shaderName\":{\"type\":\"string\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_create_script\",\"description\":\"Create a UTF-8 C# script under Assets/ and import it. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"contents\":{\"type\":\"string\"}},\"required\":[\"path\",\"contents\"]}}"
        + ",{\"name\":\"unity_set_asset_labels\",\"description\":\"Replace the labels on an existing asset. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"labels\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}},\"required\":[\"path\",\"labels\"]}}"
        + ",{\"name\":\"unity_reimport_asset\",\"description\":\"Force reimport of an existing asset. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_get_build_settings\",\"description\":\"List configured Build Settings scenes and the active build target.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"
        + ",{\"name\":\"unity_add_scene_to_build_settings\",\"description\":\"Add or enable a scene in Unity Build Settings. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"enabled\":{\"type\":\"boolean\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_find_missing_scripts\",\"description\":\"Find loaded scene GameObjects that contain missing MonoBehaviour scripts.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"
        + ",{\"name\":\"unity_set_selection\",\"description\":\"Select a loaded GameObject by its full hierarchy path.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_frame_selection\",\"description\":\"Frame the current selection in the active Scene View.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"
        + ",{\"name\":\"unity_get_component_properties\",\"description\":\"List visible serialized property paths and types for a Component on a loaded GameObject.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"componentType\":{\"type\":\"string\"}},\"required\":[\"path\",\"componentType\"]}}"
        + ",{\"name\":\"unity_create_primitive\",\"description\":\"Create a built-in Unity primitive such as Cube, Sphere, Plane, Capsule, Cylinder, or Quad. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"primitive\":{\"type\":\"string\"},\"name\":{\"type\":\"string\"},\"parentPath\":{\"type\":\"string\"}},\"required\":[\"primitive\"]}}"
        + ",{\"name\":\"unity_set_serialized_property\",\"description\":\"Set a supported serialized Component property (integer, float, boolean, string, enum, Vector3). Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"componentType\":{\"type\":\"string\"},\"propertyPath\":{\"type\":\"string\"},\"value\":{}},\"required\":[\"path\",\"componentType\",\"propertyPath\",\"value\"]}}"
        + ",{\"name\":\"unity_get_console_summary\",\"description\":\"Summarize Unity log, warning, error, assertion, and exception counts observed by this bridge.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"
        + ",{\"name\":\"unity_close_scene\",\"description\":\"Close a loaded scene by its Assets/ path. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"removeScene\":{\"type\":\"boolean\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_set_active_scene\",\"description\":\"Set a loaded scene as the active scene. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_save_all_scenes\",\"description\":\"Save all modified loaded scenes. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"
        + ",{\"name\":\"unity_delete_asset\",\"description\":\"Delete an asset or folder under Assets/. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_duplicate_asset\",\"description\":\"Copy an existing asset to a new Assets/ path. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"fromPath\":{\"type\":\"string\"},\"toPath\":{\"type\":\"string\"}},\"required\":[\"fromPath\",\"toPath\"]}}"
        + ",{\"name\":\"unity_refresh_asset_database\",\"description\":\"Refresh Unity AssetDatabase. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"
        + ",{\"name\":\"unity_save_assets\",\"description\":\"Save dirty Unity assets. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"
        + ",{\"name\":\"unity_get_define_symbols\",\"description\":\"Get scripting define symbols for the current selected build target group.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"
        + ",{\"name\":\"unity_set_define_symbols\",\"description\":\"Replace scripting define symbols for the current selected build target group. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"symbols\":{\"type\":\"string\"}},\"required\":[\"symbols\"]}}"
        + ",{\"name\":\"unity_get_installed_packages\",\"description\":\"List packages declared in Packages/manifest.json.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"
        + ",{\"name\":\"unity_undo\",\"description\":\"Perform one Unity Undo operation. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"
        + ",{\"name\":\"unity_redo\",\"description\":\"Perform one Unity Redo operation. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"
        + ",{\"name\":\"unity_run_tests\",\"description\":\"Start Unity EditMode or PlayMode tests and return a job id. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"mode\":{\"type\":\"string\",\"enum\":[\"EditMode\",\"PlayMode\"]},\"testNames\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}},\"required\":[\"mode\"]}}"
        + ",{\"name\":\"unity_get_test_run_status\",\"description\":\"Get status and latest summary for the Unity test run started by this bridge.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"
        + "]";

    static CodexUnityMcpTools()
    {
        Application.logMessageReceivedThreaded += CaptureLog;
    }

    internal static async Task<ToolOutput> InvokeAsync(string name, JsonElement arguments)
    {
        return await CodexUnityEditorDispatcher.RunAsync(() => InvokeOnMainThread(name, arguments));
    }

    private static ToolOutput InvokeOnMainThread(string name, JsonElement arguments)
    {
        switch (name)
        {
            case "unity_get_editor_state": return new ToolOutput(GetEditorState());
            case "unity_get_open_scenes": return new ToolOutput(GetOpenScenes());
            case "unity_get_scene_view_state": return new ToolOutput(GetSceneViewState());
            case "unity_get_hierarchy": return new ToolOutput(GetHierarchy(GetInt(arguments, "maxDepth", 4)));
            case "unity_find_game_objects": return new ToolOutput(FindGameObjects(GetString(arguments, "query"), GetInt(arguments, "maxResults", 30)));
            case "unity_get_game_object_details": return new ToolOutput(GetGameObjectDetails(GetString(arguments, "path")));
            case "unity_get_selection": return new ToolOutput(GetSelection());
            case "unity_set_selection": return SetSelection(GetString(arguments, "path"));
            case "unity_frame_selection": return FrameSelection();
            case "unity_get_component_properties": return new ToolOutput(GetComponentProperties(GetString(arguments, "path"), GetString(arguments, "componentType")));
            case "unity_create_game_object": return CreateGameObject(arguments);
            case "unity_create_primitive": return CreatePrimitive(GetString(arguments, "primitive"), GetString(arguments, "name"), GetString(arguments, "parentPath"));
            case "unity_delete_game_object": return DeleteGameObject(GetString(arguments, "path"));
            case "unity_duplicate_game_object": return DuplicateGameObject(GetString(arguments, "path"), GetString(arguments, "newName"));
            case "unity_add_component": return ChangeComponent(GetString(arguments, "path"), GetString(arguments, "componentType"), true);
            case "unity_remove_component": return ChangeComponent(GetString(arguments, "path"), GetString(arguments, "componentType"), false);
            case "unity_set_transform": return SetTransform(arguments);
            case "unity_set_game_object_metadata": return SetGameObjectMetadata(arguments);
            case "unity_set_serialized_property": return SetSerializedProperty(arguments);
            case "unity_create_scene": return CreateScene(GetString(arguments, "path"));
            case "unity_open_scene": return OpenScene(GetString(arguments, "path"), GetBool(arguments, "additive", false));
            case "unity_close_scene": return CloseScene(GetString(arguments, "path"), GetBool(arguments, "removeScene", true));
            case "unity_set_active_scene": return SetActiveScene(GetString(arguments, "path"));
            case "unity_save_active_scene": return SaveActiveScene(GetString(arguments, "path"));
            case "unity_save_all_scenes": return SaveAllScenes();
            case "unity_create_prefab": return CreatePrefab(GetString(arguments, "sourcePath"), GetString(arguments, "assetPath"));
            case "unity_instantiate_prefab": return InstantiatePrefab(GetString(arguments, "assetPath"), GetString(arguments, "parentPath"));
            case "unity_create_folder": return CreateFolder(GetString(arguments, "path"));
            case "unity_move_asset": return MoveAsset(GetString(arguments, "fromPath"), GetString(arguments, "toPath"));
            case "unity_rename_asset": return RenameAsset(GetString(arguments, "path"), GetString(arguments, "newName"));
            case "unity_delete_asset": return DeleteAsset(GetString(arguments, "path"));
            case "unity_duplicate_asset": return DuplicateAsset(GetString(arguments, "fromPath"), GetString(arguments, "toPath"));
            case "unity_create_material": return CreateMaterial(GetString(arguments, "path"), GetString(arguments, "shaderName"));
            case "unity_create_script": return CreateScript(GetString(arguments, "path"), GetString(arguments, "contents"));
            case "unity_set_asset_labels": return SetAssetLabels(GetString(arguments, "path"), GetStringArray(arguments, "labels"));
            case "unity_reimport_asset": return ReimportAsset(GetString(arguments, "path"));
            case "unity_refresh_asset_database": return RefreshAssetDatabase();
            case "unity_save_assets": return SaveAssets();
            case "unity_get_build_settings": return new ToolOutput(GetBuildSettings());
            case "unity_add_scene_to_build_settings": return AddSceneToBuildSettings(GetString(arguments, "path"), GetBool(arguments, "enabled", true));
            case "unity_get_define_symbols": return new ToolOutput(GetDefineSymbols());
            case "unity_set_define_symbols": return SetDefineSymbols(GetString(arguments, "symbols"));
            case "unity_get_installed_packages": return new ToolOutput(GetInstalledPackages());
            case "unity_find_missing_scripts": return new ToolOutput(FindMissingScripts());
            case "unity_run_tests": return RunTests(GetString(arguments, "mode"), GetStringArray(arguments, "testNames"));
            case "unity_get_test_run_status": return new ToolOutput(GetTestRunStatus());
            case "unity_set_play_mode": return SetPlayMode(GetBool(arguments, "playing", false));
            case "unity_undo": return PerformUndo();
            case "unity_redo": return PerformRedo();
            case "unity_execute_menu_item": return ExecuteMenuItem(GetString(arguments, "menuItem"));
            case "unity_get_recent_logs": return new ToolOutput(GetRecentLogs(GetInt(arguments, "limit", 20)));
            case "unity_get_console_summary": return new ToolOutput(GetConsoleSummary());
            case "unity_get_project_info": return new ToolOutput(GetProjectInfo());
            case "unity_find_asset": return new ToolOutput(FindAssets(GetString(arguments, "query")));
            case "unity_get_asset_details": return new ToolOutput(GetAssetDetails(GetString(arguments, "path")));
            case "unity_get_asset_dependencies": return new ToolOutput(GetAssetDependencies(GetString(arguments, "path")));
            case "unity_get_prefab_details": return new ToolOutput(GetPrefabDetails(GetString(arguments, "path")));
            case "unity_open_asset": return OpenAsset(GetString(arguments, "path"));
            default: return new ToolOutput("Unknown Unity MCP tool: " + name, true);
        }
    }

    private static string GetEditorState()
    {
        var scene = SceneManager.GetActiveScene();
        return "Scene: " + (string.IsNullOrEmpty(scene.path) ? scene.name + " (unsaved)" : scene.path)
            + "\nPlay Mode: " + (EditorApplication.isPlaying ? "playing" : "editing")
            + "\nSelection: " + (Selection.activeObject == null ? "none" : Selection.activeObject.name);
    }

    private static string GetHierarchy(int maxDepth)
    {
        maxDepth = Mathf.Clamp(maxDepth, 1, 8);
        var scene = SceneManager.GetActiveScene();
        var builder = new StringBuilder("Scene: " + scene.name + "\n");
        foreach (var root in scene.GetRootGameObjects()) AppendTransform(builder, root.transform, 0, maxDepth);
        return builder.ToString();
    }

    private static string GetOpenScenes()
    {
        var active = SceneManager.GetActiveScene();
        var builder = new StringBuilder();
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            builder.Append(scene == active ? "* " : "- ").Append(string.IsNullOrEmpty(scene.path) ? scene.name + " (unsaved)" : scene.path).Append(" [").Append(scene.isLoaded ? "loaded" : "not loaded").Append("]\n");
        }
        return builder.Length == 0 ? "No scenes are loaded." : builder.ToString();
    }

    private static string GetSceneViewState()
    {
        var view = SceneView.lastActiveSceneView;
        return view == null ? "No Scene View is currently available." : "Pivot: " + view.pivot + "\nRotation: " + view.rotation.eulerAngles + "\nSize: " + view.size + "\n2D Mode: " + view.in2DMode;
    }

    private static string FindGameObjects(string query, int maxResults)
    {
        maxResults = Mathf.Clamp(maxResults, 1, 100);
        query = query ?? string.Empty;
        var matches = new List<string>();
        for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            var scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects()) CollectGameObjectMatches(root.transform, query, matches, maxResults);
        }
        return matches.Count == 0 ? "No loaded scene GameObjects matched: " + query : string.Join("\n", matches);
    }

    private static void CollectGameObjectMatches(Transform transform, string query, List<string> matches, int maxResults)
    {
        if (matches.Count >= maxResults) return;
        if (transform.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) matches.Add(GetTransformPath(transform) + " [" + (transform.gameObject.activeInHierarchy ? "active" : "inactive") + "]");
        for (var i = 0; i < transform.childCount && matches.Count < maxResults; i++) CollectGameObjectMatches(transform.GetChild(i), query, matches, maxResults);
    }

    private static string GetGameObjectDetails(string path)
    {
        var transform = FindTransformByPath(path);
        if (transform == null) return "No loaded GameObject matches path: " + path;
        var components = transform.GetComponents<Component>().Where(component => component != null).Select(component => component.GetType().Name);
        var children = Enumerable.Range(0, transform.childCount).Select(index => transform.GetChild(index).name);
        return "Path: " + GetTransformPath(transform) + "\nActive: " + transform.gameObject.activeInHierarchy + "\nPosition: " + transform.position + "\nRotation: " + transform.rotation.eulerAngles + "\nScale: " + transform.localScale + "\nComponents: " + string.Join(", ", components) + "\nDirect children: " + (transform.childCount == 0 ? "none" : string.Join(", ", children));
    }

    private static void AppendTransform(StringBuilder builder, Transform transform, int depth, int maxDepth)
    {
        builder.Append(' ', depth * 2).Append("- ").Append(transform.name).Append(" [").Append(transform.gameObject.activeSelf ? "active" : "inactive").Append("]\n");
        if (depth >= maxDepth) return;
        for (var i = 0; i < transform.childCount; i++) AppendTransform(builder, transform.GetChild(i), depth + 1, maxDepth);
    }

    private static string GetSelection()
    {
        var gameObject = Selection.activeGameObject;
        if (gameObject == null) return "No GameObject is selected.";
        var components = gameObject.GetComponents<Component>().Where(component => component != null).Select(component => component.GetType().Name);
        return "Object: " + gameObject.name + "\nPath: " + GetTransformPath(gameObject.transform) + "\nComponents: " + string.Join(", ", components);
    }
    private static ToolOutput SetSelection(string path) { var transform = FindTransformByPath(path); if (transform == null) return new ToolOutput("GameObject not found: " + path, true); Selection.activeGameObject = transform.gameObject; return new ToolOutput("Selected: " + GetTransformPath(transform)); }
    private static ToolOutput FrameSelection() { var view = SceneView.lastActiveSceneView; if (view == null) return new ToolOutput("No Scene View is currently available.", true); view.FrameSelected(); return new ToolOutput("Framed current selection in Scene View."); }
    private static string GetComponentProperties(string path, string typeName)
    {
        var transform = FindTransformByPath(path); var type = FindComponentType(typeName); if (transform == null) return "GameObject not found: " + path; if (type == null) return "Component type not found: " + typeName;
        var component = transform.GetComponent(type); if (component == null) return "Component is not present: " + typeName;
        var serialized = new SerializedObject(component); var iterator = serialized.GetIterator(); var lines = new List<string>(); var enterChildren = true;
        while (iterator.NextVisible(enterChildren) && lines.Count < 100) { lines.Add(iterator.propertyPath + " : " + iterator.propertyType); enterChildren = false; }
        return "Component: " + type.FullName + "\n" + string.Join("\n", lines);
    }
    private static ToolOutput CreateGameObject(JsonElement arguments)
    {
        var name = GetString(arguments, "name"); if (string.IsNullOrWhiteSpace(name)) return new ToolOutput("A non-empty name is required.", true);
        var gameObject = new GameObject(name); var parent = FindTransformByPath(GetString(arguments, "parentPath")); if (parent != null) gameObject.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(gameObject, "Codex create GameObject"); Selection.activeGameObject = gameObject; return new ToolOutput("Created: " + GetTransformPath(gameObject.transform));
    }
    private static ToolOutput CreatePrimitive(string primitiveName, string name, string parentPath)
    {
        if (!Enum.TryParse(primitiveName, true, out PrimitiveType primitive)) return new ToolOutput("Unknown primitive. Use Sphere, Capsule, Cylinder, Cube, Plane, Quad, or Quad.", true);
        var gameObject = GameObject.CreatePrimitive(primitive); gameObject.name = string.IsNullOrWhiteSpace(name) ? primitive.ToString() : name;
        var parent = FindTransformByPath(parentPath); if (parent != null) gameObject.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(gameObject, "Codex create primitive"); Selection.activeGameObject = gameObject; return new ToolOutput("Created primitive: " + GetTransformPath(gameObject.transform));
    }
    private static ToolOutput DeleteGameObject(string path) { var transform = FindTransformByPath(path); if (transform == null) return new ToolOutput("GameObject not found: " + path, true); Undo.DestroyObjectImmediate(transform.gameObject); return new ToolOutput("Deleted: " + path); }
    private static ToolOutput DuplicateGameObject(string path, string newName) { var transform = FindTransformByPath(path); if (transform == null) return new ToolOutput("GameObject not found: " + path, true); var copy = UnityEngine.Object.Instantiate(transform.gameObject, transform.parent); copy.name = string.IsNullOrEmpty(newName) ? transform.name + " Copy" : newName; Undo.RegisterCreatedObjectUndo(copy, "Codex duplicate GameObject"); Selection.activeGameObject = copy; return new ToolOutput("Created duplicate: " + GetTransformPath(copy.transform)); }
    private static ToolOutput ChangeComponent(string path, string typeName, bool add)
    {
        var transform = FindTransformByPath(path); var type = FindComponentType(typeName); if (transform == null) return new ToolOutput("GameObject not found: " + path, true); if (type == null) return new ToolOutput("Component type not found: " + typeName, true);
        if (add) { Undo.AddComponent(transform.gameObject, type); return new ToolOutput("Added " + type.FullName + " to " + path); }
        var component = transform.GetComponent(type); if (component == null) return new ToolOutput("Component is not present: " + typeName, true); Undo.DestroyObjectImmediate(component); return new ToolOutput("Removed " + type.FullName + " from " + path);
    }
    private static ToolOutput SetTransform(JsonElement arguments) { var transform = FindTransformByPath(GetString(arguments, "path")); if (transform == null) return new ToolOutput("GameObject not found.", true); Undo.RecordObject(transform, "Codex set Transform"); if (TryGetVector3(arguments, "localPosition", out var position)) transform.localPosition = position; if (TryGetVector3(arguments, "localEulerAngles", out var rotation)) transform.localEulerAngles = rotation; if (TryGetVector3(arguments, "localScale", out var scale)) transform.localScale = scale; EditorUtility.SetDirty(transform); return new ToolOutput("Updated Transform: " + GetTransformPath(transform)); }
    private static ToolOutput SetGameObjectMetadata(JsonElement arguments) { var transform = FindTransformByPath(GetString(arguments, "path")); if (transform == null) return new ToolOutput("GameObject not found.", true); var gameObject = transform.gameObject; Undo.RecordObject(gameObject, "Codex set GameObject metadata"); if (arguments.TryGetProperty("name", out var name)) gameObject.name = name.GetString(); if (arguments.TryGetProperty("tag", out var tag)) gameObject.tag = tag.GetString(); if (arguments.TryGetProperty("layer", out var layer)) gameObject.layer = layer.GetInt32(); if (arguments.TryGetProperty("active", out var active)) gameObject.SetActive(active.GetBoolean()); return new ToolOutput("Updated GameObject: " + GetTransformPath(transform)); }
    private static ToolOutput SetSerializedProperty(JsonElement arguments)
    {
        var transform = FindTransformByPath(GetString(arguments, "path")); var type = FindComponentType(GetString(arguments, "componentType")); var propertyPath = GetString(arguments, "propertyPath");
        if (transform == null || type == null || string.IsNullOrWhiteSpace(propertyPath)) return new ToolOutput("GameObject, Component type, and propertyPath are required.", true);
        var component = transform.GetComponent(type); if (component == null) return new ToolOutput("Component is not present: " + type.FullName, true);
        var serialized = new SerializedObject(component); var property = serialized.FindProperty(propertyPath); if (property == null) return new ToolOutput("Serialized property was not found: " + propertyPath, true);
        if (!arguments.TryGetProperty("value", out var value)) return new ToolOutput("A value is required.", true);
        Undo.RecordObject(component, "Codex set serialized property");
        switch (property.propertyType)
        {
            case SerializedPropertyType.Integer: if (!value.TryGetInt32(out var integer)) return new ToolOutput("Property requires an integer value.", true); property.intValue = integer; break;
            case SerializedPropertyType.Float: if (!value.TryGetSingle(out var number)) return new ToolOutput("Property requires a numeric value.", true); property.floatValue = number; break;
            case SerializedPropertyType.Boolean: if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False) return new ToolOutput("Property requires a boolean value.", true); property.boolValue = value.GetBoolean(); break;
            case SerializedPropertyType.String: if (value.ValueKind != JsonValueKind.String) return new ToolOutput("Property requires a string value.", true); property.stringValue = value.GetString(); break;
            case SerializedPropertyType.Enum: if (!value.TryGetInt32(out var enumIndex)) return new ToolOutput("Enum properties require an integer enum index.", true); property.enumValueIndex = enumIndex; break;
            case SerializedPropertyType.Vector3: if (!TryGetVector3(arguments, "value", out var vector)) return new ToolOutput("Vector3 requires {x, y, z}.", true); property.vector3Value = vector; break;
            default: return new ToolOutput("Unsupported serialized property type: " + property.propertyType + ". Supported: Integer, Float, Boolean, String, Enum, Vector3.", true);
        }
        serialized.ApplyModifiedProperties(); EditorUtility.SetDirty(component); return new ToolOutput("Updated " + type.Name + "." + propertyPath + " on " + GetTransformPath(transform));
    }

    private static ToolOutput CreateScene(string path)
    {
        if (!IsSafeAssetsPath(path) || !path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) return new ToolOutput("A new scene requires a safe Assets/*.unity path.", true);
        if (AssetDatabase.LoadMainAssetAtPath(path) != null) return new ToolOutput("A scene already exists at: " + path, true);
        if (!EnsureAssetParentFolder(path, out var folderError)) return new ToolOutput(folderError, true);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        return EditorSceneManager.SaveScene(scene, path) ? new ToolOutput("Created and opened scene: " + path) : new ToolOutput("Failed to save scene: " + path, true);
    }
    private static ToolOutput OpenScene(string path, bool additive)
    {
        if (!IsSafeAssetsPath(path) || !path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) || AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) return new ToolOutput("Scene not found at Assets path: " + path, true);
        var scene = EditorSceneManager.OpenScene(path, additive ? OpenSceneMode.Additive : OpenSceneMode.Single);
        return new ToolOutput("Opened scene: " + scene.path + (additive ? " (additive)" : string.Empty));
    }
    private static ToolOutput CloseScene(string path, bool removeScene)
    {
        var scene = SceneManager.GetSceneByPath(path); if (!scene.IsValid() || !scene.isLoaded) return new ToolOutput("Loaded scene not found: " + path, true);
        return EditorSceneManager.CloseScene(scene, removeScene) ? new ToolOutput("Closed scene: " + path) : new ToolOutput("Failed to close scene: " + path, true);
    }
    private static ToolOutput SetActiveScene(string path)
    {
        var scene = SceneManager.GetSceneByPath(path); if (!scene.IsValid() || !scene.isLoaded) return new ToolOutput("Loaded scene not found: " + path, true);
        return SceneManager.SetActiveScene(scene) ? new ToolOutput("Set active scene: " + path) : new ToolOutput("Failed to set active scene: " + path, true);
    }
    private static ToolOutput SaveActiveScene(string path)
    {
        var scene = SceneManager.GetActiveScene();
        var result = string.IsNullOrWhiteSpace(path) ? EditorSceneManager.SaveScene(scene) : IsSafeAssetsPath(path) && path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) && EditorSceneManager.SaveScene(scene, path);
        return result ? new ToolOutput("Saved scene: " + (string.IsNullOrWhiteSpace(path) ? scene.path : path)) : new ToolOutput("Failed to save active scene. Supply an Assets/*.unity path for an unsaved scene.", true);
    }
    private static ToolOutput SaveAllScenes() => EditorSceneManager.SaveOpenScenes() ? new ToolOutput("Saved all open scenes.") : new ToolOutput("Failed to save one or more open scenes.", true);
    private static ToolOutput CreatePrefab(string sourcePath, string assetPath)
    {
        var source = FindTransformByPath(sourcePath); if (source == null) return new ToolOutput("GameObject not found: " + sourcePath, true);
        if (!IsSafeAssetsPath(assetPath) || !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) return new ToolOutput("Prefab path must be a safe Assets/*.prefab path.", true);
        if (!EnsureAssetParentFolder(assetPath, out var folderError)) return new ToolOutput(folderError, true);
        var prefab = PrefabUtility.SaveAsPrefabAsset(source.gameObject, assetPath, out var success);
        return success && prefab != null ? new ToolOutput("Created Prefab: " + assetPath) : new ToolOutput("Failed to create Prefab: " + assetPath, true);
    }
    private static ToolOutput InstantiatePrefab(string assetPath, string parentPath)
    {
        if (!IsSafeAssetsPath(assetPath)) return new ToolOutput("Prefab path must be an Assets/ path.", true);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath); if (prefab == null) return new ToolOutput("Prefab not found: " + assetPath, true);
        var instance = PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene()) as GameObject; if (instance == null) return new ToolOutput("Failed to instantiate Prefab: " + assetPath, true);
        var parent = FindTransformByPath(parentPath); if (parent != null) instance.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(instance, "Codex instantiate Prefab"); Selection.activeGameObject = instance;
        return new ToolOutput("Instantiated Prefab: " + GetTransformPath(instance.transform));
    }
    private static ToolOutput CreateFolder(string path)
    {
        if (!IsSafeAssetsPath(path) || path == "Assets" || AssetDatabase.IsValidFolder(path)) return new ToolOutput("Folder path is invalid or already exists: " + path, true);
        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/'); var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name) || !AssetDatabase.IsValidFolder(parent)) return new ToolOutput("Parent folder does not exist: " + parent, true);
        var guid = AssetDatabase.CreateFolder(parent, name); return string.IsNullOrEmpty(guid) ? new ToolOutput("Failed to create folder: " + path, true) : new ToolOutput("Created folder: " + path);
    }
    private static ToolOutput MoveAsset(string fromPath, string toPath)
    {
        if (!IsSafeAssetsPath(fromPath) || !IsSafeAssetsPath(toPath) || AssetDatabase.LoadMainAssetAtPath(fromPath) == null && !AssetDatabase.IsValidFolder(fromPath)) return new ToolOutput("Source asset/folder or target path is invalid.", true);
        var error = AssetDatabase.MoveAsset(fromPath, toPath); return string.IsNullOrEmpty(error) ? new ToolOutput("Moved: " + fromPath + " -> " + toPath) : new ToolOutput(error, true);
    }
    private static ToolOutput RenameAsset(string path, string newName)
    {
        if (!IsSafeAssetsPath(path) || string.IsNullOrWhiteSpace(newName) || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return new ToolOutput("Asset path or new name is invalid.", true);
        var error = AssetDatabase.RenameAsset(path, newName); return string.IsNullOrEmpty(error) ? new ToolOutput("Renamed asset: " + path + " -> " + newName) : new ToolOutput(error, true);
    }
    private static ToolOutput DeleteAsset(string path)
    {
        if (!IsSafeAssetsPath(path) || (AssetDatabase.LoadMainAssetAtPath(path) == null && !AssetDatabase.IsValidFolder(path))) return new ToolOutput("Asset or folder not found: " + path, true);
        return AssetDatabase.DeleteAsset(path) ? new ToolOutput("Deleted asset: " + path) : new ToolOutput("Failed to delete asset: " + path, true);
    }
    private static ToolOutput DuplicateAsset(string fromPath, string toPath)
    {
        if (!IsSafeAssetsPath(fromPath) || !IsSafeAssetsPath(toPath) || AssetDatabase.LoadMainAssetAtPath(fromPath) == null || AssetDatabase.LoadMainAssetAtPath(toPath) != null) return new ToolOutput("Source asset or target path is invalid.", true);
        if (!EnsureAssetParentFolder(toPath, out var folderError)) return new ToolOutput(folderError, true);
        return AssetDatabase.CopyAsset(fromPath, toPath) ? new ToolOutput("Copied asset: " + fromPath + " -> " + toPath) : new ToolOutput("Failed to copy asset.", true);
    }
    private static ToolOutput CreateMaterial(string path, string shaderName)
    {
        if (!IsSafeAssetsPath(path) || !path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) || AssetDatabase.LoadMainAssetAtPath(path) != null) return new ToolOutput("Material path is invalid or already exists: " + path, true);
        if (!EnsureAssetParentFolder(path, out var folderError)) return new ToolOutput(folderError, true);
        var shader = Shader.Find(string.IsNullOrWhiteSpace(shaderName) ? "Universal Render Pipeline/Lit" : shaderName) ?? Shader.Find("Standard");
        if (shader == null) return new ToolOutput("Shader was not found: " + shaderName, true);
        AssetDatabase.CreateAsset(new Material(shader), path); AssetDatabase.SaveAssets();
        return new ToolOutput("Created Material: " + path + " (" + shader.name + ")");
    }
    private static ToolOutput CreateScript(string path, string contents)
    {
        if (!IsSafeAssetsPath(path) || !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || AssetDatabase.LoadMainAssetAtPath(path) != null) return new ToolOutput("Script path is invalid or already exists: " + path, true);
        if (!EnsureAssetParentFolder(path, out var folderError)) return new ToolOutput(folderError, true);
        var projectRoot = Directory.GetParent(Application.dataPath).FullName;
        var fullPath = Path.GetFullPath(Path.Combine(projectRoot, path));
        if (!fullPath.StartsWith(Path.GetFullPath(Application.dataPath), StringComparison.OrdinalIgnoreCase)) return new ToolOutput("Script path must remain under Assets/.", true);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)); File.WriteAllText(fullPath, contents ?? string.Empty, new UTF8Encoding(false));
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); return new ToolOutput("Created and imported script: " + path);
    }
    private static ToolOutput SetAssetLabels(string path, string[] labels)
    {
        if (!IsSafeAssetsPath(path)) return new ToolOutput("Asset path must be an Assets/ path.", true);
        var asset = AssetDatabase.LoadMainAssetAtPath(path); if (asset == null) return new ToolOutput("Asset not found: " + path, true);
        AssetDatabase.SetLabels(asset, labels ?? Array.Empty<string>()); AssetDatabase.SaveAssets(); return new ToolOutput("Updated labels for: " + path);
    }
    private static ToolOutput ReimportAsset(string path)
    {
        if (!IsSafeAssetsPath(path) || AssetDatabase.LoadMainAssetAtPath(path) == null) return new ToolOutput("Asset not found: " + path, true);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); return new ToolOutput("Reimported asset: " + path);
    }
    private static ToolOutput RefreshAssetDatabase() { AssetDatabase.Refresh(); return new ToolOutput("Refreshed AssetDatabase."); }
    private static ToolOutput SaveAssets() { AssetDatabase.SaveAssets(); return new ToolOutput("Saved dirty assets."); }
    private static string GetBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes;
        var builder = new StringBuilder("Active build target: " + EditorUserBuildSettings.activeBuildTarget + "\nScenes: " + scenes.Length + "\n");
        for (var i = 0; i < scenes.Length; i++) builder.Append(i).Append(". ").Append(scenes[i].enabled ? "[enabled] " : "[disabled] ").Append(scenes[i].path).Append('\n');
        return builder.ToString();
    }
    private static ToolOutput AddSceneToBuildSettings(string path, bool enabled)
    {
        if (!IsSafeAssetsPath(path) || !path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) || AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) return new ToolOutput("Scene not found at Assets path: " + path, true);
        var scenes = EditorBuildSettings.scenes.ToList(); var index = scenes.FindIndex(scene => scene.path == path);
        if (index >= 0) scenes[index] = new EditorBuildSettingsScene(path, enabled); else scenes.Add(new EditorBuildSettingsScene(path, enabled));
        EditorBuildSettings.scenes = scenes.ToArray(); return new ToolOutput((index >= 0 ? "Updated" : "Added") + " Build Settings scene: " + path + " (enabled=" + enabled + ")");
    }
    private static string GetDefineSymbols()
    {
        var group = EditorUserBuildSettings.selectedBuildTargetGroup;
        return "Build target group: " + group + "\nSymbols: " + PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
    }
    private static ToolOutput SetDefineSymbols(string symbols)
    {
        var group = EditorUserBuildSettings.selectedBuildTargetGroup;
        PlayerSettings.SetScriptingDefineSymbolsForGroup(group, symbols ?? string.Empty);
        return new ToolOutput("Updated define symbols for " + group + ". Unity may recompile scripts.");
    }
    private static string GetInstalledPackages()
    {
        var manifest = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Packages", "manifest.json");
        if (!File.Exists(manifest)) return "Packages/manifest.json was not found.";
        using (var document = JsonDocument.Parse(File.ReadAllText(manifest)))
        {
            if (!document.RootElement.TryGetProperty("dependencies", out var dependencies) || dependencies.ValueKind != JsonValueKind.Object) return "No package dependencies were found.";
            return string.Join("\n", dependencies.EnumerateObject().OrderBy(item => item.Name).Select(item => item.Name + " @ " + item.Value.GetString()));
        }
    }
    private static string FindMissingScripts()
    {
        var matches = new List<string>();
        for (var i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i); if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects()) CollectMissingScripts(root.transform, matches);
        }
        return matches.Count == 0 ? "No missing scripts were found in loaded scenes." : "GameObjects with missing scripts:\n" + string.Join("\n", matches);
    }
    private static ToolOutput RunTests(string mode, string[] testNames)
    {
        if (testRunStatus.StartsWith("Starting", StringComparison.Ordinal) || testRunStatus.StartsWith("Running", StringComparison.Ordinal)) return new ToolOutput("A Unity test run is already active. " + GetTestRunStatus(), true);
        var testMode = string.Equals(mode, "PlayMode", StringComparison.OrdinalIgnoreCase) ? TestMode.PlayMode : TestMode.EditMode;
        testRunnerApi ??= ScriptableObject.CreateInstance<TestRunnerApi>();
        testRunnerApi.RegisterCallbacks(new CodexTestCallbacks());
        testRunStatus = "Starting " + testMode + " tests..."; testRunSummary = null;
        testRunId = testRunnerApi.Execute(new ExecutionSettings(new Filter { testMode = testMode, testNames = testNames != null && testNames.Length > 0 ? testNames : null }));
        return new ToolOutput("Started " + testMode + " test run: " + testRunId);
    }
    private static string GetTestRunStatus() => "Job: " + (string.IsNullOrEmpty(testRunId) ? "none" : testRunId) + "\nStatus: " + testRunStatus + (string.IsNullOrEmpty(testRunSummary) ? string.Empty : "\n" + testRunSummary);
    private sealed class CodexTestCallbacks : ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun) { testRunStatus = "Running " + testsToRun.TestCaseCount + " test case(s)."; }
        public void RunFinished(ITestResultAdaptor result) { testRunStatus = "Completed: " + result.ResultState; testRunSummary = "Passed: " + result.PassCount + " | Failed: " + result.FailCount + " | Skipped: " + result.SkipCount + " | Duration: " + result.Duration.ToString("0.00") + "s" + (string.IsNullOrEmpty(result.Message) ? string.Empty : "\nMessage: " + result.Message); }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }
    }
    private static void CollectMissingScripts(Transform transform, List<string> matches)
    {
        if (transform.GetComponents<Component>().Any(component => component == null)) matches.Add(GetTransformPath(transform));
        for (var i = 0; i < transform.childCount; i++) CollectMissingScripts(transform.GetChild(i), matches);
    }
    private static ToolOutput SetPlayMode(bool playing) { EditorApplication.isPlaying = playing; return new ToolOutput(playing ? "Entering Play Mode." : "Exiting Play Mode."); }
    private static ToolOutput PerformUndo() { Undo.PerformUndo(); return new ToolOutput("Performed Undo."); }
    private static ToolOutput PerformRedo() { Undo.PerformRedo(); return new ToolOutput("Performed Redo."); }
    private static ToolOutput ExecuteMenuItem(string menuItem) { return string.IsNullOrWhiteSpace(menuItem) ? new ToolOutput("A menu item path is required.", true) : EditorApplication.ExecuteMenuItem(menuItem) ? new ToolOutput("Executed menu item: " + menuItem) : new ToolOutput("Menu item was not found or could not run: " + menuItem, true); }

    private static string GetRecentLogs(int limit)
    {
        lock (RecentLogs)
        {
            return RecentLogs.Count == 0 ? "No Unity Console messages have been observed since the bridge started." : string.Join("\n", RecentLogs.Skip(Mathf.Max(0, RecentLogs.Count - Mathf.Clamp(limit, 1, 50))));
        }
    }
    private static string GetConsoleSummary()
    {
        lock (RecentLogs)
        {
            var logs = RecentLogs.Count(item => item.StartsWith("[Log]")); var warnings = RecentLogs.Count(item => item.StartsWith("[Warning]"));
            var errors = RecentLogs.Count(item => item.StartsWith("[Error]")); var assertions = RecentLogs.Count(item => item.StartsWith("[Assert]")); var exceptions = RecentLogs.Count(item => item.StartsWith("[Exception]"));
            return "Observed since bridge start:\nLog: " + logs + "\nWarning: " + warnings + "\nError: " + errors + "\nAssert: " + assertions + "\nException: " + exceptions;
        }
    }

    private static string FindAssets(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return "A non-empty search query is required.";
        var guids = AssetDatabase.FindAssets(query);
        var paths = guids.Take(30).Select(AssetDatabase.GUIDToAssetPath).ToArray();
        return paths.Length == 0 ? "No assets found." : string.Join("\n", paths) + (guids.Length > paths.Length ? "\n… " + (guids.Length - paths.Length) + " more result(s)." : string.Empty);
    }

    private static string GetProjectInfo()
    {
        return "Product: " + PlayerSettings.productName + "\nCompany: " + PlayerSettings.companyName + "\nUnity: " + Application.unityVersion + "\nBuild target: " + EditorUserBuildSettings.activeBuildTarget + "\nProject root: " + Directory.GetParent(Application.dataPath).FullName;
    }

    private static string GetAssetDetails(string path)
    {
        var asset = AssetDatabase.LoadMainAssetAtPath(path);
        if (asset == null) return "Asset not found: " + path;
        var importer = AssetImporter.GetAtPath(path);
        var labels = AssetDatabase.GetLabels(asset);
        return "Path: " + path + "\nName: " + asset.name + "\nType: " + asset.GetType().Name + "\nImporter: " + (importer == null ? "none" : importer.GetType().Name) + "\nLabels: " + (labels.Length == 0 ? "none" : string.Join(", ", labels));
    }

    private static string GetAssetDependencies(string path)
    {
        if (AssetDatabase.LoadMainAssetAtPath(path) == null) return "Asset not found: " + path;
        var dependencies = AssetDatabase.GetDependencies(path, false).Where(item => item != path).Take(50).ToArray();
        return dependencies.Length == 0 ? "No direct dependencies." : string.Join("\n", dependencies);
    }

    private static string GetPrefabDetails(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) return "Prefab not found: " + path;
        var components = prefab.GetComponents<Component>().Where(component => component != null).Select(component => component.GetType().Name);
        var children = Enumerable.Range(0, prefab.transform.childCount).Select(index => prefab.transform.GetChild(index).name);
        return "Path: " + path + "\nPrefab type: " + PrefabUtility.GetPrefabAssetType(prefab) + "\nRoot: " + prefab.name + "\nComponents: " + string.Join(", ", components) + "\nDirect children: " + (prefab.transform.childCount == 0 ? "none" : string.Join(", ", children));
    }

    private static ToolOutput OpenAsset(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!path.StartsWith("Assets/") && !path.StartsWith("Packages/"))) return new ToolOutput("Only a project-relative Assets/ or Packages/ path can be opened.", true);
        var asset = AssetDatabase.LoadMainAssetAtPath(path);
        if (asset == null) return new ToolOutput("Asset not found: " + path, true);
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
        AssetDatabase.OpenAsset(asset);
        return new ToolOutput("Opened asset: " + path);
    }

    private static void CaptureLog(string condition, string stackTrace, LogType type)
    {
        lock (RecentLogs)
        {
            RecentLogs.Add("[" + type + "] " + condition);
            if (RecentLogs.Count > 100) RecentLogs.RemoveRange(0, RecentLogs.Count - 100);
        }
    }

    private static int GetInt(JsonElement values, string name, int fallback) => values.ValueKind == JsonValueKind.Object && values.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : fallback;
    private static bool GetBool(JsonElement values, string name, bool fallback) => values.ValueKind == JsonValueKind.Object && values.TryGetProperty(name, out var value) && (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False) ? value.GetBoolean() : fallback;
    private static string GetString(JsonElement values, string name) => values.ValueKind == JsonValueKind.Object && values.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;
    private static string[] GetStringArray(JsonElement values, string name) => values.ValueKind == JsonValueKind.Object && values.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray() : Array.Empty<string>();
    private static string GetTransformPath(Transform transform) => transform.parent == null ? transform.name : GetTransformPath(transform.parent) + "/" + transform.name;
    private static Transform FindTransformByPath(string path)
    {
        for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            var scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
                if (GetTransformPath(root.transform) == path) return root.transform;
        }
        return null;
    }
    private static bool IsSafeAssetsPath(string path) => !string.IsNullOrWhiteSpace(path) && path.StartsWith("Assets/", StringComparison.Ordinal) && !path.Contains("..") && !path.Contains("\\");
    private static bool EnsureAssetParentFolder(string assetPath, out string error)
    {
        error = null;
        var parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(parent) || parent == "Assets") return true;
        if (!IsSafeAssetsPath(parent + "/placeholder")) { error = "Asset parent folder is invalid: " + parent; return false; }
        var current = "Assets";
        foreach (var part in parent.Substring("Assets/".Length).Split('/'))
        {
            if (string.IsNullOrWhiteSpace(part)) { error = "Asset parent folder is invalid: " + parent; return false; }
            var next = current + "/" + part;
            if (!AssetDatabase.IsValidFolder(next) && string.IsNullOrEmpty(AssetDatabase.CreateFolder(current, part))) { error = "Failed to create asset parent folder: " + next; return false; }
            current = next;
        }
        return true;
    }
    internal static bool RequiresApiApproval(string name) => name == "unity_create_game_object" || name == "unity_create_primitive" || name == "unity_delete_game_object" || name == "unity_duplicate_game_object" || name == "unity_add_component" || name == "unity_remove_component" || name == "unity_set_transform" || name == "unity_set_game_object_metadata" || name == "unity_set_serialized_property" || name == "unity_create_scene" || name == "unity_open_scene" || name == "unity_close_scene" || name == "unity_set_active_scene" || name == "unity_save_active_scene" || name == "unity_save_all_scenes" || name == "unity_create_prefab" || name == "unity_instantiate_prefab" || name == "unity_create_folder" || name == "unity_move_asset" || name == "unity_rename_asset" || name == "unity_delete_asset" || name == "unity_duplicate_asset" || name == "unity_create_material" || name == "unity_create_script" || name == "unity_set_asset_labels" || name == "unity_reimport_asset" || name == "unity_refresh_asset_database" || name == "unity_save_assets" || name == "unity_add_scene_to_build_settings" || name == "unity_set_define_symbols" || name == "unity_run_tests" || name == "unity_undo" || name == "unity_redo" || name == "unity_set_play_mode" || name == "unity_execute_menu_item";
    internal static string GetMutationSummary(string name)
    {
        switch (name)
        {
            case "unity_create_game_object": return "将在当前场景创建一个 GameObject。";
            case "unity_create_primitive": return "将在当前场景创建一个 Unity 内置 Primitive。";
            case "unity_delete_game_object": return "将从当前场景删除一个 GameObject。";
            case "unity_duplicate_game_object": return "将在当前场景复制一个 GameObject。";
            case "unity_add_component": return "将为 GameObject 添加组件。";
            case "unity_remove_component": return "将从 GameObject 移除组件。";
            case "unity_set_transform": return "将修改 GameObject 的本地 Transform。";
            case "unity_set_game_object_metadata": return "将修改 GameObject 的名称、Tag、Layer 或激活状态。";
            case "unity_set_serialized_property": return "将修改组件的序列化 Inspector 属性。";
            case "unity_create_scene": return "将新建并保存一个空场景，并切换到该场景。";
            case "unity_open_scene": return "将打开场景，可能替换当前未保存的场景内容。";
            case "unity_save_active_scene": return "将把当前场景保存到磁盘。";
            case "unity_create_prefab": return "将把场景对象保存为 Prefab 资源。";
            case "unity_instantiate_prefab": return "将向当前场景实例化一个 Prefab。";
            case "unity_create_folder": return "将在 Assets 下创建资源文件夹。";
            case "unity_move_asset": return "将移动项目中的资源或文件夹。";
            case "unity_rename_asset": return "将重命名项目中的资源或文件夹。";
            case "unity_create_material": return "将在项目中创建 Material 资源。";
            case "unity_create_script": return "将在项目中创建并导入 C# 脚本，这可能触发 Unity 重新编译。";
            case "unity_set_asset_labels": return "将替换资源的所有 Labels。";
            case "unity_reimport_asset": return "将强制重新导入资源。";
            case "unity_add_scene_to_build_settings": return "将修改 Build Settings 中的场景列表。";
            case "unity_close_scene": return "将关闭当前已加载的场景。";
            case "unity_set_active_scene": return "将切换当前活动场景。";
            case "unity_save_all_scenes": return "将保存全部已打开的场景。";
            case "unity_delete_asset": return "将删除 Assets 下的资源或文件夹。";
            case "unity_duplicate_asset": return "将在 Assets 下复制一份资源。";
            case "unity_refresh_asset_database": return "将刷新 Unity 的资源数据库。";
            case "unity_save_assets": return "将保存全部脏资源。";
            case "unity_set_define_symbols": return "将修改当前构建目标的 Scripting Define Symbols，可能触发重新编译。";
            case "unity_run_tests": return "将启动 Unity Test Runner 测试任务。";
            case "unity_set_play_mode": return "将进入或退出 Unity Play Mode。";
            default: return "将执行 Unity 编辑器菜单命令。";
        }
    }
    private static Type FindComponentType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(typeName) ?? assembly.GetTypes().FirstOrDefault(item => item.Name == typeName);
            if (type != null && typeof(Component).IsAssignableFrom(type) && !type.IsAbstract) return type;
        }
        return null;
    }
    private static bool TryGetVector3(JsonElement arguments, string name, out Vector3 result)
    {
        result = default;
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object) return false;
        if (!value.TryGetProperty("x", out var x) || !value.TryGetProperty("y", out var y) || !value.TryGetProperty("z", out var z)) return false;
        result = new Vector3(x.GetSingle(), y.GetSingle(), z.GetSingle());
        return true;
    }
}
