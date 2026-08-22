using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class CodexUnityMcpTools
{
    internal sealed class ToolCategory { public readonly string Name; public readonly string Description; public readonly string[] Tools; public ToolCategory(string name, string description, params string[] tools) { Name = name; Description = description; Tools = tools; } }
    internal readonly struct ToolOutput { public readonly string Text; public readonly bool IsError; public ToolOutput(string text, bool isError = false) { Text = text; IsError = isError; } }
    private static readonly List<string> RecentLogs = new List<string>();
    internal static readonly string[] ToolNames = { "unity_get_editor_state", "unity_get_open_scenes", "unity_get_scene_view_state", "unity_get_hierarchy", "unity_find_game_objects", "unity_get_game_object_details", "unity_get_selection", "unity_get_recent_logs", "unity_get_project_info", "unity_find_asset", "unity_get_asset_details", "unity_get_asset_dependencies", "unity_get_prefab_details", "unity_open_asset" };
    internal static readonly ToolCategory[] ToolCategories =
    {
        new ToolCategory("编辑器与场景", "编辑器状态、场景与视图上下文", "unity_get_editor_state", "unity_get_open_scenes", "unity_get_scene_view_state", "unity_get_hierarchy"),
        new ToolCategory("对象与 Inspector", "场景对象检索、层级路径与组件详情", "unity_find_game_objects", "unity_get_game_object_details", "unity_get_selection"),
        new ToolCategory("Console", "Bridge 启动后观察到的 Unity 日志", "unity_get_recent_logs"),
        new ToolCategory("项目与资源", "项目、资源、依赖与 Prefab 信息", "unity_get_project_info", "unity_find_asset", "unity_get_asset_details", "unity_get_asset_dependencies", "unity_get_prefab_details", "unity_open_asset")
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
        + "]";

    static CodexUnityMcpTools()
    {
        Application.logMessageReceivedThreaded += CaptureLog;
    }

    internal static Task<ToolOutput> InvokeAsync(string name, JsonElement arguments)
    {
        return CodexUnityEditorDispatcher.RunAsync(() => InvokeOnMainThread(name, arguments));
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
            case "unity_get_recent_logs": return new ToolOutput(GetRecentLogs(GetInt(arguments, "limit", 20)));
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

    private static string GetRecentLogs(int limit)
    {
        lock (RecentLogs)
        {
            return RecentLogs.Count == 0 ? "No Unity Console messages have been observed since the bridge started." : string.Join("\n", RecentLogs.Skip(Mathf.Max(0, RecentLogs.Count - Mathf.Clamp(limit, 1, 50))));
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
    private static string GetString(JsonElement values, string name) => values.ValueKind == JsonValueKind.Object && values.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;
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
}
