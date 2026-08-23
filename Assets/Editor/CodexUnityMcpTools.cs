using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
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
    internal static readonly string[] ToolNames = { "unity_get_bridge_status", "unity_get_interrupted_operations", "unity_get_editor_state", "unity_get_open_scenes", "unity_get_scene_view_state", "unity_capture_scene_view", "unity_get_hierarchy", "unity_find_game_objects", "unity_get_game_object_details", "unity_get_selection", "unity_set_selection", "unity_frame_selection", "unity_get_component_properties", "unity_get_component_values", "unity_create_game_object", "unity_create_primitive", "unity_delete_game_object", "unity_duplicate_game_object", "unity_add_component", "unity_remove_component", "unity_set_transform", "unity_set_game_object_metadata", "unity_set_serialized_property", "unity_get_recent_logs", "unity_get_console_summary", "unity_clear_console", "unity_get_project_info", "unity_get_project_settings", "unity_set_project_identity", "unity_get_tag_layer_settings", "unity_find_asset", "unity_get_asset_details", "unity_get_asset_importer_settings", "unity_set_texture_importer_settings", "unity_get_asset_dependencies", "unity_get_prefab_details", "unity_get_prefab_overrides", "unity_apply_prefab_instance", "unity_revert_prefab_instance", "unity_unpack_prefab_instance", "unity_open_asset", "unity_create_scene", "unity_open_scene", "unity_close_scene", "unity_set_active_scene", "unity_save_active_scene", "unity_save_all_scenes", "unity_create_prefab", "unity_instantiate_prefab", "unity_create_ui_canvas", "unity_create_ui_element", "unity_set_rect_transform", "unity_set_ui_text", "unity_create_folder", "unity_move_asset", "unity_rename_asset", "unity_delete_asset", "unity_duplicate_asset", "unity_create_material", "unity_create_script", "unity_write_scripts_batch", "unity_set_asset_labels", "unity_reimport_asset", "unity_refresh_asset_database", "unity_save_assets", "unity_get_build_settings", "unity_add_scene_to_build_settings", "unity_switch_build_target", "unity_build_player", "unity_get_define_symbols", "unity_set_define_symbols", "unity_get_installed_packages", "unity_find_missing_scripts", "unity_find_unreferenced_assets", "unity_get_compiler_errors", "unity_validate_scene", "unity_get_compilation_status", "unity_run_tests", "unity_get_test_run_status", "unity_undo", "unity_redo", "unity_set_play_mode", "unity_execute_menu_item" };
    internal static readonly ToolCategory[] ToolCategories =
    {
        new ToolCategory("编辑器与场景", "编辑器状态、场景、视图与截图", "unity_get_editor_state", "unity_get_open_scenes", "unity_get_scene_view_state", "unity_capture_scene_view", "unity_get_hierarchy", "unity_get_compilation_status", "unity_validate_scene"),
        new ToolCategory("对象、Inspector 与 UI", "场景对象检索、选择、组件与 UGUI 创建/布局", "unity_find_game_objects", "unity_get_game_object_details", "unity_get_selection", "unity_set_selection", "unity_frame_selection", "unity_get_component_properties", "unity_get_component_values", "unity_create_game_object", "unity_create_primitive", "unity_delete_game_object", "unity_duplicate_game_object", "unity_add_component", "unity_remove_component", "unity_set_transform", "unity_set_game_object_metadata", "unity_set_serialized_property", "unity_create_ui_canvas", "unity_create_ui_element", "unity_set_rect_transform", "unity_set_ui_text"),
        new ToolCategory("Console、测试与诊断", "Bridge 日志、Console 清理、测试、编译与资源诊断", "unity_get_recent_logs", "unity_get_console_summary", "unity_clear_console", "unity_find_missing_scripts", "unity_find_unreferenced_assets", "unity_get_compiler_errors", "unity_run_tests", "unity_get_test_run_status"),
        new ToolCategory("Scene 与 Prefab", "场景与 Prefab 的创建、打开、关闭、保存、覆写与实例化", "unity_create_scene", "unity_open_scene", "unity_close_scene", "unity_set_active_scene", "unity_save_active_scene", "unity_save_all_scenes", "unity_create_prefab", "unity_instantiate_prefab", "unity_get_prefab_details", "unity_get_prefab_overrides", "unity_apply_prefab_instance", "unity_revert_prefab_instance", "unity_unpack_prefab_instance"),
        new ToolCategory("项目与资源", "项目、资源、Importer 与受审批的资源管理", "unity_get_project_info", "unity_find_asset", "unity_get_asset_details", "unity_get_asset_importer_settings", "unity_set_texture_importer_settings", "unity_get_asset_dependencies", "unity_open_asset", "unity_create_folder", "unity_move_asset", "unity_rename_asset", "unity_delete_asset", "unity_duplicate_asset", "unity_create_material", "unity_create_script", "unity_write_scripts_batch", "unity_set_asset_labels", "unity_reimport_asset", "unity_refresh_asset_database", "unity_save_assets"),
        new ToolCategory("构建、包与编辑器控制", "Build Settings、构建 Player、Project Settings、Package 列表与编辑器操作", "unity_get_build_settings", "unity_add_scene_to_build_settings", "unity_switch_build_target", "unity_build_player", "unity_get_define_symbols", "unity_set_define_symbols", "unity_get_installed_packages", "unity_get_project_settings", "unity_set_project_identity", "unity_get_tag_layer_settings", "unity_undo", "unity_redo", "unity_set_play_mode", "unity_execute_menu_item")
    };
    internal const string ToolDefinitionsJson = "["
        + "{\"name\":\"unity_get_editor_state\",\"description\":\"Get the active Unity scene, play mode, and current selection.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}},"
        + "{\"name\":\"unity_get_bridge_status\",\"description\":\"Get Codex Unity MCP bridge readiness, compile state, and interrupted-operation count.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"
        + ",{\"name\":\"unity_get_interrupted_operations\",\"description\":\"List MCP operations interrupted by Unity compilation or Domain Reload. Re-check actual project state before retrying.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}},"
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
        + ",{\"name\":\"unity_write_scripts_batch\",\"description\":\"Write up to 20 C# scripts under Assets/ as one transaction, then perform one AssetDatabase refresh and one compilation request. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"files\":{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"contents\":{\"type\":\"string\"}},\"required\":[\"path\",\"contents\"]}}},\"required\":[\"files\"]}}"
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
        + ",{\"name\":\"unity_capture_scene_view\",\"description\":\"Capture the active Scene View to a PNG asset path. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"width\":{\"type\":\"integer\"},\"height\":{\"type\":\"integer\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_get_component_values\",\"description\":\"Read visible serialized Component property paths, types, and values.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"componentType\":{\"type\":\"string\"}},\"required\":[\"path\",\"componentType\"]}}"
        + ",{\"name\":\"unity_get_prefab_overrides\",\"description\":\"List property overrides on a loaded Prefab instance.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_apply_prefab_instance\",\"description\":\"Apply overrides from a Prefab instance to its source asset. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_revert_prefab_instance\",\"description\":\"Revert a Prefab instance to its source asset. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_unpack_prefab_instance\",\"description\":\"Unpack a Prefab instance. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_get_compilation_status\",\"description\":\"Get Unity script compilation and asset update status.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"
        + ",{\"name\":\"unity_validate_scene\",\"description\":\"Validate loaded scenes for unsaved changes and missing scripts.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"
        + ",{\"name\":\"unity_get_asset_importer_settings\",\"description\":\"Read common importer settings for an asset.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_set_texture_importer_settings\",\"description\":\"Set optional TextureImporter readable, mipmapEnabled, maxTextureSize, or textureType settings. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"readable\":{\"type\":\"boolean\"},\"mipmapEnabled\":{\"type\":\"boolean\"},\"maxTextureSize\":{\"type\":\"integer\"},\"textureType\":{\"type\":\"string\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_clear_console\",\"description\":\"Clear the Unity Console and this bridge's observed log buffer. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"
        + ",{\"name\":\"unity_create_ui_canvas\",\"description\":\"Create an overlay UGUI Canvas with scaler and raycaster in the active scene. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}}"
        + ",{\"name\":\"unity_create_ui_element\",\"description\":\"Create a UGUI Panel, Button, Text, or Image under a Canvas or UI parent. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"type\":{\"type\":\"string\",\"enum\":[\"Panel\",\"Button\",\"Text\",\"Image\"]},\"name\":{\"type\":\"string\"},\"parentPath\":{\"type\":\"string\"},\"text\":{\"type\":\"string\"}},\"required\":[\"type\"]}}"
        + ",{\"name\":\"unity_set_rect_transform\",\"description\":\"Set optional anchoredPosition, sizeDelta, anchorMin, and anchorMax on a UGUI RectTransform. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"anchoredPosition\":{\"type\":\"object\"},\"sizeDelta\":{\"type\":\"object\"},\"anchorMin\":{\"type\":\"object\"},\"anchorMax\":{\"type\":\"object\"}},\"required\":[\"path\"]}}"
        + ",{\"name\":\"unity_set_ui_text\",\"description\":\"Set text on a legacy UGUI Text component. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"text\":{\"type\":\"string\"}},\"required\":[\"path\",\"text\"]}}"
        + ",{\"name\":\"unity_get_project_settings\",\"description\":\"Read common Player Settings for the active build target.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"
        + ",{\"name\":\"unity_set_project_identity\",\"description\":\"Set optional companyName, productName, and version in Player Settings. Requires Unity API approval.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"companyName\":{\"type\":\"string\"},\"productName\":{\"type\":\"string\"},\"version\":{\"type\":\"string\"}}}}"
        + ",{\"name\":\"unity_get_tag_layer_settings\",\"description\":\"Read configured Unity Tags and Layers.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"
        + ",{\"name\":\"unity_switch_build_target\",\"description\":\"Switch Unity's active build target by BuildTarget enum name, for example StandaloneWindows64. Requires Unity API approval and may reimport or recompile.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"target\":{\"type\":\"string\"}},\"required\":[\"target\"]}}"
        + ",{\"name\":\"unity_build_player\",\"description\":\"Build enabled Build Settings scenes to an output path. Requires Unity API approval and can take several minutes.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"outputPath\":{\"type\":\"string\"},\"development\":{\"type\":\"boolean\"}},\"required\":[\"outputPath\"]}}"
        + ",{\"name\":\"unity_find_unreferenced_assets\",\"description\":\"Find candidate project assets with no AssetDatabase incoming references. Treat results as review candidates.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{\"limit\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":200}}}}"
        + ",{\"name\":\"unity_get_compiler_errors\",\"description\":\"Read current Unity compiler warnings and errors from compiled assemblies.\",\"inputSchema\":{\"type\":\"object\",\"properties\":{}}}"
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
            case "unity_get_bridge_status": return new ToolOutput(CodexUnityOperationJournal.DescribeStatus());
            case "unity_get_interrupted_operations": return new ToolOutput(CodexUnityOperationJournal.DescribeInterrupted());
            case "unity_get_editor_state": return new ToolOutput(GetEditorState());
            case "unity_get_open_scenes": return new ToolOutput(GetOpenScenes());
            case "unity_get_scene_view_state": return new ToolOutput(GetSceneViewState());
            case "unity_capture_scene_view": return CaptureSceneView(GetString(arguments, "path"), GetInt(arguments, "width", 1024), GetInt(arguments, "height", 768));
            case "unity_get_hierarchy": return new ToolOutput(GetHierarchy(GetInt(arguments, "maxDepth", 4)));
            case "unity_find_game_objects": return new ToolOutput(FindGameObjects(GetString(arguments, "query"), GetInt(arguments, "maxResults", 30)));
            case "unity_get_game_object_details": return new ToolOutput(GetGameObjectDetails(GetString(arguments, "path")));
            case "unity_get_selection": return new ToolOutput(GetSelection());
            case "unity_set_selection": return SetSelection(GetString(arguments, "path"));
            case "unity_frame_selection": return FrameSelection();
            case "unity_get_component_properties": return new ToolOutput(GetComponentProperties(GetString(arguments, "path"), GetString(arguments, "componentType")));
            case "unity_get_component_values": return new ToolOutput(GetComponentValues(GetString(arguments, "path"), GetString(arguments, "componentType")));
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
            case "unity_create_ui_canvas": return CreateUiCanvas(GetString(arguments, "name"));
            case "unity_create_ui_element": return CreateUiElement(arguments);
            case "unity_set_rect_transform": return SetRectTransform(arguments);
            case "unity_set_ui_text": return SetUiText(GetString(arguments, "path"), GetString(arguments, "text"));
            case "unity_create_folder": return CreateFolder(GetString(arguments, "path"));
            case "unity_move_asset": return MoveAsset(GetString(arguments, "fromPath"), GetString(arguments, "toPath"));
            case "unity_rename_asset": return RenameAsset(GetString(arguments, "path"), GetString(arguments, "newName"));
            case "unity_delete_asset": return DeleteAsset(GetString(arguments, "path"));
            case "unity_duplicate_asset": return DuplicateAsset(GetString(arguments, "fromPath"), GetString(arguments, "toPath"));
            case "unity_create_material": return CreateMaterial(GetString(arguments, "path"), GetString(arguments, "shaderName"));
            case "unity_create_script": return CreateScript(GetString(arguments, "path"), GetString(arguments, "contents"));
            case "unity_write_scripts_batch": return WriteScriptsBatch(arguments);
            case "unity_set_asset_labels": return SetAssetLabels(GetString(arguments, "path"), GetStringArray(arguments, "labels"));
            case "unity_reimport_asset": return ReimportAsset(GetString(arguments, "path"));
            case "unity_refresh_asset_database": return RefreshAssetDatabase();
            case "unity_save_assets": return SaveAssets();
            case "unity_get_build_settings": return new ToolOutput(GetBuildSettings());
            case "unity_add_scene_to_build_settings": return AddSceneToBuildSettings(GetString(arguments, "path"), GetBool(arguments, "enabled", true));
            case "unity_switch_build_target": return SwitchBuildTarget(GetString(arguments, "target"));
            case "unity_build_player": return BuildPlayer(arguments);
            case "unity_get_define_symbols": return new ToolOutput(GetDefineSymbols());
            case "unity_set_define_symbols": return SetDefineSymbols(GetString(arguments, "symbols"));
            case "unity_get_installed_packages": return new ToolOutput(GetInstalledPackages());
            case "unity_find_missing_scripts": return new ToolOutput(FindMissingScripts());
            case "unity_find_unreferenced_assets": return new ToolOutput(FindUnreferencedAssets(GetInt(arguments, "limit", 50)));
            case "unity_get_compiler_errors": return new ToolOutput(GetCompilerErrors());
            case "unity_run_tests": return RunTests(GetString(arguments, "mode"), GetStringArray(arguments, "testNames"));
            case "unity_get_test_run_status": return new ToolOutput(GetTestRunStatus());
            case "unity_set_play_mode": return SetPlayMode(GetBool(arguments, "playing", false));
            case "unity_undo": return PerformUndo();
            case "unity_redo": return PerformRedo();
            case "unity_execute_menu_item": return ExecuteMenuItem(GetString(arguments, "menuItem"));
            case "unity_get_recent_logs": return new ToolOutput(GetRecentLogs(GetInt(arguments, "limit", 20)));
            case "unity_get_console_summary": return new ToolOutput(GetConsoleSummary());
            case "unity_clear_console": return ClearConsole();
            case "unity_get_project_info": return new ToolOutput(GetProjectInfo());
            case "unity_get_project_settings": return new ToolOutput(GetProjectSettings());
            case "unity_set_project_identity": return SetProjectIdentity(arguments);
            case "unity_get_tag_layer_settings": return new ToolOutput(GetTagLayerSettings());
            case "unity_find_asset": return new ToolOutput(FindAssets(GetString(arguments, "query")));
            case "unity_get_asset_details": return new ToolOutput(GetAssetDetails(GetString(arguments, "path")));
            case "unity_get_asset_importer_settings": return new ToolOutput(GetAssetImporterSettings(GetString(arguments, "path")));
            case "unity_set_texture_importer_settings": return SetTextureImporterSettings(arguments);
            case "unity_get_asset_dependencies": return new ToolOutput(GetAssetDependencies(GetString(arguments, "path")));
            case "unity_get_prefab_details": return new ToolOutput(GetPrefabDetails(GetString(arguments, "path")));
            case "unity_get_prefab_overrides": return new ToolOutput(GetPrefabOverrides(GetString(arguments, "path")));
            case "unity_apply_prefab_instance": return ApplyPrefabInstance(GetString(arguments, "path"));
            case "unity_revert_prefab_instance": return RevertPrefabInstance(GetString(arguments, "path"));
            case "unity_unpack_prefab_instance": return UnpackPrefabInstance(GetString(arguments, "path"));
            case "unity_get_compilation_status": return new ToolOutput(GetCompilationStatus());
            case "unity_validate_scene": return new ToolOutput(ValidateScene());
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
    private static ToolOutput CaptureSceneView(string path, int width, int height)
    {
        if (!IsSafeAssetsPath(path) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return new ToolOutput("Screenshot path must be a safe Assets/*.png path.", true);
        if (!EnsureAssetParentFolder(path, out var error)) return new ToolOutput(error, true);
        var view = SceneView.lastActiveSceneView; if (view == null || view.camera == null) return new ToolOutput("No Scene View camera is available.", true);
        width = Mathf.Clamp(width, 64, 2048); height = Mathf.Clamp(height, 64, 2048);
        var target = new RenderTexture(width, height, 24); var previous = view.camera.targetTexture; var previousActive = RenderTexture.active;
        try
        {
            view.camera.targetTexture = target; view.camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            var projectRoot = Directory.GetParent(Application.dataPath).FullName; File.WriteAllBytes(Path.Combine(projectRoot, path), image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); return new ToolOutput("Captured Scene View: " + path);
        }
        finally { view.camera.targetTexture = previous; RenderTexture.active = previousActive; target.Release(); UnityEngine.Object.DestroyImmediate(target); }
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
    private static string GetComponentValues(string path, string typeName)
    {
        var transform = FindTransformByPath(path); var type = FindComponentType(typeName); if (transform == null) return "GameObject not found: " + path; if (type == null) return "Component type not found: " + typeName;
        var component = transform.GetComponent(type); if (component == null) return "Component is not present: " + typeName;
        var serialized = new SerializedObject(component); var iterator = serialized.GetIterator(); var lines = new List<string>(); var enterChildren = true;
        while (iterator.NextVisible(enterChildren) && lines.Count < 100) { lines.Add(iterator.propertyPath + " : " + iterator.propertyType + " = " + SerializedValue(iterator)); enterChildren = false; }
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
    private static ToolOutput CreateUiCanvas(string name)
    {
        var canvasObject = new GameObject(string.IsNullOrWhiteSpace(name) ? "Canvas" : name, typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(canvasObject, "Codex create UI Canvas");
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>().uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        Selection.activeGameObject = canvasObject;
        return new ToolOutput("Created UI Canvas: " + GetTransformPath(canvasObject.transform));
    }
    private static ToolOutput CreateUiElement(JsonElement arguments)
    {
        var type = GetString(arguments, "type");
        var parent = FindTransformByPath(GetString(arguments, "parentPath"));
        if (parent == null) parent = UnityEngine.Object.FindObjectOfType<Canvas>()?.transform;
        if (parent == null) return new ToolOutput("No UI parent was found. Create a Canvas first or provide parentPath.", true);
        var element = new GameObject(string.IsNullOrWhiteSpace(GetString(arguments, "name")) ? type : GetString(arguments, "name"), typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(element, "Codex create UI element"); element.transform.SetParent(parent, false);
        var image = element.AddComponent<UnityEngine.UI.Image>(); var rect = element.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(160, 40);
        if (string.Equals(type, "Panel", StringComparison.OrdinalIgnoreCase)) { rect.sizeDelta = new Vector2(320, 180); image.color = new Color(0.15f, 0.15f, 0.15f, 0.9f); }
        else if (string.Equals(type, "Image", StringComparison.OrdinalIgnoreCase)) image.color = Color.white;
        else if (string.Equals(type, "Text", StringComparison.OrdinalIgnoreCase)) { UnityEngine.Object.DestroyImmediate(image); AddLegacyText(element, GetString(arguments, "text")); }
        else if (string.Equals(type, "Button", StringComparison.OrdinalIgnoreCase))
        {
            image.color = new Color(0.25f, 0.45f, 0.85f, 1f); element.AddComponent<UnityEngine.UI.Button>();
            var label = new GameObject("Text", typeof(RectTransform)); label.transform.SetParent(element.transform, false); var labelRect = label.GetComponent<RectTransform>(); labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one; labelRect.sizeDelta = Vector2.zero; AddLegacyText(label, string.IsNullOrWhiteSpace(GetString(arguments, "text")) ? "Button" : GetString(arguments, "text"));
        }
        else { UnityEngine.Object.DestroyImmediate(element); return new ToolOutput("Unsupported UI type. Use Panel, Button, Text, or Image.", true); }
        Selection.activeGameObject = element; return new ToolOutput("Created UI " + type + ": " + GetTransformPath(element.transform));
    }
    private static void AddLegacyText(GameObject target, string value)
    {
        var text = target.AddComponent<UnityEngine.UI.Text>(); text.text = value ?? string.Empty; text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.color = Color.white; text.alignment = TextAnchor.MiddleCenter; text.resizeTextForBestFit = true;
    }
    private static ToolOutput SetRectTransform(JsonElement arguments)
    {
        var transform = FindTransformByPath(GetString(arguments, "path")) as RectTransform;
        if (transform == null) return new ToolOutput("UGUI RectTransform not found: " + GetString(arguments, "path"), true);
        Undo.RecordObject(transform, "Codex set UI layout");
        if (TryGetVector2(arguments, "anchoredPosition", out var position)) transform.anchoredPosition = position;
        if (TryGetVector2(arguments, "sizeDelta", out var size)) transform.sizeDelta = size;
        if (TryGetVector2(arguments, "anchorMin", out var anchorMin)) transform.anchorMin = anchorMin;
        if (TryGetVector2(arguments, "anchorMax", out var anchorMax)) transform.anchorMax = anchorMax;
        EditorUtility.SetDirty(transform); return new ToolOutput("Updated UI layout: " + GetTransformPath(transform));
    }
    private static ToolOutput SetUiText(string path, string value)
    {
        var transform = FindTransformByPath(path); var text = transform == null ? null : transform.GetComponent<UnityEngine.UI.Text>();
        if (text == null) return new ToolOutput("Legacy UGUI Text not found: " + path, true);
        Undo.RecordObject(text, "Codex set UI text"); text.text = value ?? string.Empty; EditorUtility.SetDirty(text); return new ToolOutput("Updated UI text: " + path);
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
    private static ToolOutput WriteScriptsBatch(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array || files.GetArrayLength() == 0 || files.GetArrayLength() > 20) return new ToolOutput("files must contain 1 to 20 script entries.", true);
        var entries = new List<(string path, string contents, string fullPath)>(); var projectRoot = Directory.GetParent(Application.dataPath).FullName; var assetsRoot = Path.GetFullPath(Application.dataPath);
        foreach (var file in files.EnumerateArray())
        {
            var path = GetString(file, "path"); var contents = GetString(file, "contents"); var fullPath = Path.GetFullPath(Path.Combine(projectRoot, path));
            if (!IsSafeAssetsPath(path) || !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || !fullPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase)) return new ToolOutput("Every batch script must be a safe new Assets/*.cs path.", true);
            if (File.Exists(fullPath) || entries.Exists(item => item.path == path)) return new ToolOutput("Script already exists or is duplicated in batch: " + path, true);
            entries.Add((path, contents, fullPath));
        }
        EditorApplication.LockReloadAssemblies(); AssetDatabase.StartAssetEditing();
        try { foreach (var entry in entries) { Directory.CreateDirectory(Path.GetDirectoryName(entry.fullPath)); File.WriteAllText(entry.fullPath, entry.contents ?? string.Empty, new UTF8Encoding(false)); } }
        finally { AssetDatabase.StopAssetEditing(); EditorApplication.UnlockReloadAssemblies(); }
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate); CompilationPipeline.RequestScriptCompilation();
        return new ToolOutput("Wrote " + entries.Count + " C# scripts in one batch. Unity will perform one refresh/compilation pass: " + string.Join(", ", entries.Select(item => item.path)));
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
    private static ToolOutput SwitchBuildTarget(string targetName)
    {
        if (!Enum.TryParse(targetName, true, out BuildTarget target) || target == BuildTarget.NoTarget) return new ToolOutput("Unknown BuildTarget: " + targetName, true);
        var group = BuildPipeline.GetBuildTargetGroup(target); if (group == BuildTargetGroup.Unknown) return new ToolOutput("Unity cannot determine a BuildTargetGroup for: " + target, true);
        if (EditorUserBuildSettings.activeBuildTarget == target) return new ToolOutput("Build target is already active: " + target);
        return EditorUserBuildSettings.SwitchActiveBuildTarget(group, target) ? new ToolOutput("Switching active build target to " + target + ". Unity may reimport and recompile.") : new ToolOutput("Failed to switch build target to " + target, true);
    }
    private static ToolOutput BuildPlayer(JsonElement arguments)
    {
        var outputPath = GetString(arguments, "outputPath");
        if (string.IsNullOrWhiteSpace(outputPath)) return new ToolOutput("outputPath is required.", true);
        var root = Directory.GetParent(Application.dataPath).FullName;
        var fullOutput = Path.GetFullPath(Path.IsPathRooted(outputPath) ? outputPath : Path.Combine(root, outputPath));
        if (!fullOutput.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return new ToolOutput("Build output must stay inside this Unity project.", true);
        var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
        if (scenes.Length == 0) return new ToolOutput("No enabled scenes are configured in Build Settings.", true);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutput));
        var options = new BuildPlayerOptions { scenes = scenes, locationPathName = fullOutput, target = EditorUserBuildSettings.activeBuildTarget, options = GetBool(arguments, "development", false) ? BuildOptions.Development : BuildOptions.None };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        return report.summary.result == BuildResult.Succeeded ? new ToolOutput("Build succeeded: " + fullOutput + " (" + report.summary.totalSize + " bytes)") : new ToolOutput("Build finished with result " + report.summary.result + ". See Unity Console for details.", true);
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
    private static string FindUnreferencedAssets(int limit)
    {
        limit = Mathf.Clamp(limit, 1, 200);
        var paths = AssetDatabase.FindAssets(string.Empty).Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal) && !AssetDatabase.IsValidFolder(path)).Take(1500).ToArray();
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in paths)
            foreach (var dependency in AssetDatabase.GetDependencies(source, false)) if (!string.Equals(source, dependency, StringComparison.OrdinalIgnoreCase)) referenced.Add(dependency);
        var candidates = paths.Where(path => !referenced.Contains(path) && !path.Contains("/Resources/", StringComparison.OrdinalIgnoreCase))
            .Where(path => { var asset = AssetDatabase.LoadMainAssetAtPath(path); return asset is Material || asset is Texture || asset is AudioClip || asset is GameObject || asset is AnimationClip; })
            .Take(limit).ToArray();
        return candidates.Length == 0 ? "No unreferenced-asset candidates were found." : "Review candidates only (dynamic loading is not detectable):\n" + string.Join("\n", candidates) + (paths.Length >= 1500 ? "\nScan capped at the first 1500 assets." : string.Empty);
    }
    private static string GetCompilerErrors()
    {
        lock (RecentLogs)
        {
            var messages = RecentLogs.Where(message => message.StartsWith("[Error]") || message.StartsWith("[Exception]") || message.StartsWith("[Warning]")).Take(100).ToArray();
            return messages.Length == 0 ? "No compiler warnings or errors have been observed by this bridge since it started." : string.Join("\n", messages);
        }
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
    private static ToolOutput ClearConsole()
    {
        var entries = typeof(EditorWindow).Assembly.GetType("UnityEditor.LogEntries");
        var clear = entries?.GetMethod("Clear", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (clear == null) return new ToolOutput("Unity Console clear API is unavailable in this Unity version.", true);
        clear.Invoke(null, null); lock (RecentLogs) RecentLogs.Clear(); return new ToolOutput("Cleared Unity Console and the bridge log buffer.");
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
    private static string GetProjectSettings()
    {
        var group = EditorUserBuildSettings.selectedBuildTargetGroup;
        return "Company: " + PlayerSettings.companyName + "\nProduct: " + PlayerSettings.productName + "\nVersion: " + PlayerSettings.bundleVersion + "\nTarget group: " + group + "\nApplication identifier: " + PlayerSettings.GetApplicationIdentifier(group) + "\nRun in background: " + PlayerSettings.runInBackground;
    }
    private static ToolOutput SetProjectIdentity(JsonElement arguments)
    {
        var changed = new List<string>();
        if (arguments.TryGetProperty("companyName", out var company) && company.ValueKind == JsonValueKind.String) { PlayerSettings.companyName = company.GetString(); changed.Add("companyName"); }
        if (arguments.TryGetProperty("productName", out var product) && product.ValueKind == JsonValueKind.String) { PlayerSettings.productName = product.GetString(); changed.Add("productName"); }
        if (arguments.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String) { PlayerSettings.bundleVersion = version.GetString(); changed.Add("version"); }
        return changed.Count == 0 ? new ToolOutput("Provide companyName, productName, or version.", true) : new ToolOutput("Updated Player Settings: " + string.Join(", ", changed));
    }
    private static string GetTagLayerSettings()
    {
        var tags = UnityEditorInternal.InternalEditorUtility.tags;
        var layers = UnityEditorInternal.InternalEditorUtility.layers;
        return "Tags:\n" + (tags.Length == 0 ? "none" : string.Join("\n", tags)) + "\nLayers:\n" + (layers.Length == 0 ? "none" : string.Join("\n", layers));
    }

    private static string GetAssetDetails(string path)
    {
        var asset = AssetDatabase.LoadMainAssetAtPath(path);
        if (asset == null) return "Asset not found: " + path;
        var importer = AssetImporter.GetAtPath(path);
        var labels = AssetDatabase.GetLabels(asset);
        return "Path: " + path + "\nName: " + asset.name + "\nType: " + asset.GetType().Name + "\nImporter: " + (importer == null ? "none" : importer.GetType().Name) + "\nLabels: " + (labels.Length == 0 ? "none" : string.Join(", ", labels));
    }
    private static string GetAssetImporterSettings(string path)
    {
        var importer = AssetImporter.GetAtPath(path); if (importer == null) return "Importer not found: " + path;
        if (importer is TextureImporter texture) return "Importer: TextureImporter\nReadable: " + texture.isReadable + "\nMipmaps: " + texture.mipmapEnabled + "\nTexture type: " + texture.textureType + "\nMax texture size: " + texture.maxTextureSize;
        return "Importer: " + importer.GetType().Name + "\nAsset bundle: " + importer.assetBundleName + "\nUser data: " + importer.userData;
    }
    private static ToolOutput SetTextureImporterSettings(JsonElement arguments)
    {
        var path = GetString(arguments, "path"); var importer = AssetImporter.GetAtPath(path) as TextureImporter; if (importer == null) return new ToolOutput("TextureImporter not found: " + path, true);
        if (arguments.TryGetProperty("readable", out var readable) && (readable.ValueKind == JsonValueKind.True || readable.ValueKind == JsonValueKind.False)) importer.isReadable = readable.GetBoolean();
        if (arguments.TryGetProperty("mipmapEnabled", out var mipmaps) && (mipmaps.ValueKind == JsonValueKind.True || mipmaps.ValueKind == JsonValueKind.False)) importer.mipmapEnabled = mipmaps.GetBoolean();
        if (arguments.TryGetProperty("maxTextureSize", out var size) && size.TryGetInt32(out var maxSize)) importer.maxTextureSize = Mathf.Clamp(maxSize, 32, 8192);
        if (arguments.TryGetProperty("textureType", out var type) && type.ValueKind == JsonValueKind.String && Enum.TryParse(type.GetString(), true, out TextureImporterType textureType)) importer.textureType = textureType;
        importer.SaveAndReimport(); return new ToolOutput("Updated TextureImporter: " + path);
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
    private static string GetPrefabOverrides(string path)
    {
        var transform = FindTransformByPath(path); if (transform == null) return "GameObject not found: " + path;
        var root = PrefabUtility.GetOutermostPrefabInstanceRoot(transform.gameObject); if (root == null) return "GameObject is not part of a Prefab instance.";
        var modifications = PrefabUtility.GetPropertyModifications(root) ?? Array.Empty<PropertyModification>();
        return modifications.Length == 0 ? "No Prefab overrides." : string.Join("\n", modifications.Take(100).Select(item => item.target.GetType().Name + "." + item.propertyPath + " = " + item.value));
    }
    private static ToolOutput ApplyPrefabInstance(string path) { var transform = FindTransformByPath(path); var root = transform == null ? null : PrefabUtility.GetOutermostPrefabInstanceRoot(transform.gameObject); if (root == null) return new ToolOutput("Prefab instance not found: " + path, true); PrefabUtility.ApplyPrefabInstance(root, InteractionMode.UserAction); return new ToolOutput("Applied Prefab instance: " + GetTransformPath(root.transform)); }
    private static ToolOutput RevertPrefabInstance(string path) { var transform = FindTransformByPath(path); var root = transform == null ? null : PrefabUtility.GetOutermostPrefabInstanceRoot(transform.gameObject); if (root == null) return new ToolOutput("Prefab instance not found: " + path, true); PrefabUtility.RevertPrefabInstance(root, InteractionMode.UserAction); return new ToolOutput("Reverted Prefab instance: " + path); }
    private static ToolOutput UnpackPrefabInstance(string path) { var transform = FindTransformByPath(path); var root = transform == null ? null : PrefabUtility.GetOutermostPrefabInstanceRoot(transform.gameObject); if (root == null) return new ToolOutput("Prefab instance not found: " + path, true); PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.UserAction); return new ToolOutput("Unpacked Prefab instance: " + path); }
    private static string GetCompilationStatus() => "Is compiling: " + EditorApplication.isCompiling + "\nIs updating assets: " + EditorApplication.isUpdating + "\nIs playing: " + EditorApplication.isPlaying;
    private static string ValidateScene()
    {
        var dirty = new List<string>(); for (var i = 0; i < SceneManager.sceneCount; i++) { var scene = SceneManager.GetSceneAt(i); if (scene.isLoaded && scene.isDirty) dirty.Add(string.IsNullOrEmpty(scene.path) ? scene.name + " (unsaved)" : scene.path); }
        var missing = FindMissingScripts(); return "Dirty scenes: " + (dirty.Count == 0 ? "none" : string.Join(", ", dirty)) + "\n" + missing;
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
        if (string.IsNullOrWhiteSpace(path)) return null;
        var nameMatches = new List<Transform>();
        for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            var scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
            {
                var exact = FindTransformByPath(root.transform, path, nameMatches);
                if (exact != null) return exact;
            }
        }
        // MCP prompts commonly refer to a unique object by name. Accept that
        // concise form only when it resolves unambiguously; otherwise callers
        // must use the full hierarchy path.
        return nameMatches.Count == 1 ? nameMatches[0] : null;
    }
    private static Transform FindTransformByPath(Transform current, string path, List<Transform> nameMatches)
    {
        if (GetTransformPath(current) == path) return current;
        if (current.name == path) nameMatches.Add(current);
        for (var i = 0; i < current.childCount; i++)
        {
            var match = FindTransformByPath(current.GetChild(i), path, nameMatches);
            if (match != null) return match;
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
    internal static bool RequiresApiApproval(string name) => name == "unity_capture_scene_view" || name == "unity_create_game_object" || name == "unity_create_primitive" || name == "unity_delete_game_object" || name == "unity_duplicate_game_object" || name == "unity_add_component" || name == "unity_remove_component" || name == "unity_set_transform" || name == "unity_set_game_object_metadata" || name == "unity_set_serialized_property" || name == "unity_create_scene" || name == "unity_open_scene" || name == "unity_close_scene" || name == "unity_set_active_scene" || name == "unity_save_active_scene" || name == "unity_save_all_scenes" || name == "unity_create_prefab" || name == "unity_instantiate_prefab" || name == "unity_create_ui_canvas" || name == "unity_create_ui_element" || name == "unity_set_rect_transform" || name == "unity_set_ui_text" || name == "unity_apply_prefab_instance" || name == "unity_revert_prefab_instance" || name == "unity_unpack_prefab_instance" || name == "unity_create_folder" || name == "unity_move_asset" || name == "unity_rename_asset" || name == "unity_delete_asset" || name == "unity_duplicate_asset" || name == "unity_create_material" || name == "unity_create_script" || name == "unity_write_scripts_batch" || name == "unity_set_asset_labels" || name == "unity_reimport_asset" || name == "unity_set_texture_importer_settings" || name == "unity_refresh_asset_database" || name == "unity_save_assets" || name == "unity_clear_console" || name == "unity_set_project_identity" || name == "unity_add_scene_to_build_settings" || name == "unity_switch_build_target" || name == "unity_build_player" || name == "unity_set_define_symbols" || name == "unity_run_tests" || name == "unity_undo" || name == "unity_redo" || name == "unity_set_play_mode" || name == "unity_execute_menu_item";
    internal static bool IsLongRunning(string name) => name == "unity_run_tests" || name == "unity_switch_build_target" || name == "unity_build_player" || name == "unity_find_unreferenced_assets";
    internal static string GetMutationSummary(string name)
    {
        switch (name)
        {
            case "unity_create_game_object": return "将在当前场景创建一个 GameObject。";
            case "unity_capture_scene_view": return "将截取当前 Scene View 并把 PNG 写入项目资源。";
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
            case "unity_create_ui_canvas": return "将在当前场景创建一个 UGUI Canvas。";
            case "unity_create_ui_element": return "将在 Canvas 下创建一个 UGUI 界面元素。";
            case "unity_set_rect_transform": return "将修改 UGUI 界面元素的布局。";
            case "unity_set_ui_text": return "将修改 UGUI Text 的显示文字。";
            case "unity_create_folder": return "将在 Assets 下创建资源文件夹。";
            case "unity_move_asset": return "将移动项目中的资源或文件夹。";
            case "unity_rename_asset": return "将重命名项目中的资源或文件夹。";
            case "unity_create_material": return "将在项目中创建 Material 资源。";
            case "unity_create_script": return "将在项目中创建并导入 C# 脚本，这可能触发 Unity 重新编译。";
            case "unity_write_scripts_batch": return "将批量写入多个 C# 脚本，并在全部写入后只触发一次 Unity 刷新和编译。";
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
            case "unity_clear_console": return "将清空 Unity Console 日志。";
            case "unity_set_project_identity": return "将修改 Player Settings 中的公司名、产品名或版本。";
            case "unity_switch_build_target": return "将切换 Unity 当前构建平台，可能触发重新导入和编译。";
            case "unity_build_player": return "将根据 Build Settings 构建 Player 文件，可能耗时数分钟。";
            case "unity_run_tests": return "将启动 Unity Test Runner 测试任务；它会在后台运行，并返回可查询进度的 job id。";
            case "unity_apply_prefab_instance": return "将把 Prefab 实例的覆写应用到源资源。";
            case "unity_revert_prefab_instance": return "将丢弃 Prefab 实例上的全部覆写。";
            case "unity_unpack_prefab_instance": return "将解除 Prefab 实例与源资源的关联。";
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
    private static bool TryGetVector2(JsonElement arguments, string name, out Vector2 result)
    {
        result = default;
        if (!arguments.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object) return false;
        if (!value.TryGetProperty("x", out var x) || !value.TryGetProperty("y", out var y)) return false;
        result = new Vector2(x.GetSingle(), y.GetSingle());
        return true;
    }
    private static string SerializedValue(SerializedProperty property)
    {
        switch (property.propertyType)
        {
            case SerializedPropertyType.Integer: return property.intValue.ToString();
            case SerializedPropertyType.Boolean: return property.boolValue.ToString();
            case SerializedPropertyType.Float: return property.floatValue.ToString("0.###");
            case SerializedPropertyType.String: return property.stringValue;
            case SerializedPropertyType.Enum: return property.enumDisplayNames != null && property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length ? property.enumDisplayNames[property.enumValueIndex] : property.enumValueIndex.ToString();
            case SerializedPropertyType.Vector2: return property.vector2Value.ToString();
            case SerializedPropertyType.Vector3: return property.vector3Value.ToString();
            case SerializedPropertyType.Vector4: return property.vector4Value.ToString();
            case SerializedPropertyType.Color: return property.colorValue.ToString();
            case SerializedPropertyType.ObjectReference: return property.objectReferenceValue == null ? "null" : property.objectReferenceValue.name;
            default: return "…";
        }
    }
}
