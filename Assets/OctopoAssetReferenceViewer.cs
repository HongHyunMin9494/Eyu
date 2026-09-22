using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class OctopoAssetReferenceIndex
{
    private static Dictionary<string, List<string>> forwardDirect;
    private static Dictionary<string, List<string>> reverseDirect;
    private static int indexedAssetCount;
    private static DateTime lastBuildLocalTime;
    private static bool indexedPackageInternals;
    private static bool isBuilding;
    private static bool invalidateAfterBuild;
    private static bool invalidateAfterBuildNotify;
    private static bool invalidationNotifyScheduled;

    public static event Action Invalidated;

    public static bool IsReady
    {
        get { return forwardDirect != null && reverseDirect != null; }
    }

    public static bool IsBuilding
    {
        get { return isBuilding; }
    }

    public static int IndexedAssetCount
    {
        get { return indexedAssetCount; }
    }

    public static Dictionary<string, List<string>> Forward
    {
        get { return forwardDirect; }
    }

    public static Dictionary<string, List<string>> Reverse
    {
        get { return reverseDirect; }
    }


    public static void Invalidate(bool notifyViewer = true)
    {
        if (isBuilding)
        {
            invalidateAfterBuild = true;
            invalidateAfterBuildNotify |= notifyViewer;
            return;
        }

        forwardDirect = null;
        reverseDirect = null;
        indexedAssetCount = 0;
        lastBuildLocalTime = default(DateTime);
        indexedPackageInternals = false;

        if (notifyViewer)
            ScheduleInvalidationNotification();
    }

    private static void ScheduleInvalidationNotification()
    {
        if (invalidationNotifyScheduled)
            return;

        invalidationNotifyScheduled = true;
        EditorApplication.delayCall += NotifyInvalidated;
    }

    private static void NotifyInvalidated()
    {
        invalidationNotifyScheduled = false;
        Action handler = Invalidated;
        if (handler != null)
            handler();
    }

    public static string GetStatusSummary()
    {
        if (isBuilding)
            return "Index Cache: Building...";

        if (!IsReady)
            return "Index Cache: Not built yet";

        return string.Format(
            "Index Cache: Ready • Scope: {0} • Assets: {1} • Built: {2}",
            indexedPackageInternals ? "Assets + Packages" : "Assets Only",
            indexedAssetCount,
            lastBuildLocalTime.ToString("HH:mm:ss"));
    }

    public static bool EnsureBuilt()
    {
        if (IsReady)
            return true;

        return Rebuild();
    }

    public static bool Rebuild()
    {
        if (isBuilding)
            return false;

        isBuilding = true;

        try
        {
            Dictionary<string, List<string>> newForward = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<string>> newReverse = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            bool includePackages = OctopoAssetReferenceViewerPreferences.IndexPackages;
            string[] allPaths = AssetDatabase.GetAllAssetPaths();
            List<string> candidates = new List<string>(allPaths.Length);

            for (int i = 0; i < allPaths.Length; i++)
            {
                string path = allPaths[i];
                if (!IsIndexSourcePath(path, includePackages))
                    continue;

                candidates.Add(path);
            }

            int count = candidates.Count;
            for (int i = 0; i < count; i++)
            {
                string path = candidates[i];

                if (EditorUtility.DisplayCancelableProgressBar(
                    "Asset Reference Viewer",
                    string.Format("Indexing {0}/{1}\n{2}", i + 1, count, path),
                    (float)(i + 1) / Mathf.Max(1, count)))
                {
                    return false;
                }

                string[] directDeps = AssetDatabase.GetDependencies(path, false);
                HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                List<string> list = new List<string>();

                for (int d = 0; d < directDeps.Length; d++)
                {
                    string dep = directDeps[d];
                    if (string.Equals(dep, path, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!IsGraphAssetPath(dep))
                        continue;

                    if (!unique.Add(dep))
                        continue;

                    list.Add(dep);

                    List<string> revList;
                    if (!newReverse.TryGetValue(dep, out revList))
                    {
                        revList = new List<string>();
                        newReverse.Add(dep, revList);
                    }

                    revList.Add(path);
                }

                newForward[path] = list;
            }

            forwardDirect = newForward;
            reverseDirect = newReverse;
            indexedAssetCount = count;
            indexedPackageInternals = includePackages;
            lastBuildLocalTime = DateTime.Now;
            return true;
        }
        finally
        {
            isBuilding = false;
            EditorUtility.ClearProgressBar();

            if (invalidateAfterBuild)
            {
                bool notifyViewer = invalidateAfterBuildNotify;
                invalidateAfterBuild = false;
                invalidateAfterBuildNotify = false;
                Invalidate(notifyViewer);
            }
        }
    }

    internal static bool IsIndexSourcePath(string path, bool includePackages)
    {
        if (!IsGraphAssetPath(path))
            return false;

        if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            return true;

        return includePackages &&
               path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsGraphAssetPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        if (!(path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
              path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)))
            return false;

        if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            return false;

        if (AssetDatabase.IsValidFolder(path))
            return false;

        return true;
    }

}

internal static class OctopoAssetReferenceViewerPreferences
{
    private const string AutoReindexKey = "Octopo.AssetReferenceViewer.AutoReindex";
    private const string AutoPingKey = "Octopo.AssetReferenceViewer.AutoPing";
    private const string IndexPackagesKey = "Octopo.AssetReferenceViewer.IndexPackages";

    public static bool AutoReindex
    {
        get { return EditorPrefs.GetBool(AutoReindexKey, true); }
        set { EditorPrefs.SetBool(AutoReindexKey, value); }
    }

    public static bool AutoPing
    {
        get { return EditorPrefs.GetBool(AutoPingKey, true); }
        set { EditorPrefs.SetBool(AutoPingKey, value); }
    }

    public static bool IndexPackages
    {
        get { return EditorPrefs.GetBool(IndexPackagesKey, false); }
        set { EditorPrefs.SetBool(IndexPackagesKey, value); }
    }
}

internal sealed class OctopoAssetReferenceViewerAssetPostprocessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if (!HasRelevantAssetPath(importedAssets) &&
            !HasRelevantAssetPath(deletedAssets) &&
            !HasRelevantAssetPath(movedAssets) &&
            !HasRelevantAssetPath(movedFromAssetPaths))
            return;

        OctopoAssetReferenceIndex.Invalidate(OctopoAssetReferenceViewerPreferences.AutoReindex);
    }

    private static bool HasRelevantAssetPath(string[] paths)
    {
        if (paths == null)
            return false;

        for (int i = 0; i < paths.Length; i++)
        {
            if (IsRelevantProjectPath(paths[i]))
                return true;
        }

        return false;
    }

    private static bool IsRelevantProjectPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        if (!(path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
              path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)))
            return false;

        if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}

public class OctopoAssetReferenceViewer : EditorWindow
{
    private const float MinZoom = 0.12f;
    private const float MaxZoom = 1.8f;
    private const float RenderCullPadding = 180f;

    private UnityEngine.Object targetAsset;
    private string targetAssetGuid;

    private Vector2 graphPan = Vector2.zero;
    private float graphZoom = 1.0f;
    private int maxDepth = 1;

    private readonly List<NodeData> referencers = new List<NodeData>();
    private readonly List<NodeData> dependencies = new List<NodeData>();
    private readonly List<List<NodeData>> referencerLevels = new List<List<NodeData>>();
    private readonly List<List<NodeData>> dependencyLevels = new List<List<NodeData>>();
    private readonly Dictionary<string, NodeData> nodesById = new Dictionary<string, NodeData>(StringComparer.OrdinalIgnoreCase);
    private readonly List<EdgeData> edges = new List<EdgeData>();
    private readonly HashSet<string> edgeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Texture> iconCache = new Dictionary<string, Texture>(StringComparer.OrdinalIgnoreCase);
    private Texture2D selectedOutlineTextureCache;
    private int selectedOutlineCacheWidth = -1;
    private int selectedOutlineCacheHeight = -1;
    private int selectedOutlineCachePad = -1;
    private float selectedOutlineCacheRadius = -1f;
    private float selectedOutlineCacheThickness = -1f;
    private Color selectedOutlineCacheTopColor = new Color(-1f, -1f, -1f, -1f);
    private Color selectedOutlineCacheBottomColor = new Color(-1f, -1f, -1f, -1f);

    private NodeData targetNode;
    private NodeData selectedNode;
    private NodeData hoveredNode;
    private Vector2 detailsScrollPosition;

    private string statusText = "Project window에서 우클릭 > Reference View 또는 Target Asset에 드래그해서 사용하세요.";

    private class NodeData
    {
        public string id;
        public string assetPath;
        public string assetGuid;
        public string displayName;
        public string kind;
        public UnityEngine.Object asset;
        public Rect graphRect;
        public bool isTarget;
        public bool isReferencer;
        public int depth;
        public Vector2 cachedSize;
        public bool cachedSizeValid;
        public List<string> details = new List<string>();
        public bool detailsLoaded;
        public bool usageStateEvaluated;
        public bool usageConfirmed;
        public string usageStateTitle;
        public string usageStateDescription;
    }

    private struct EdgeData
    {
        public string fromId;
        public string toId;
    }

    [MenuItem("Tools/Octopo/Asset Reference Viewer")]
    public static void ShowWindow()
    {
        OctopoAssetReferenceViewer wnd = GetWindow<OctopoAssetReferenceViewer>("Asset Reference Viewer");
        wnd.minSize = new Vector2(980, 560);
    }

    [MenuItem("Assets/Reference View", false, 2000)]
    public static void OpenFromProject()
    {
        OctopoAssetReferenceViewer wnd = GetWindow<OctopoAssetReferenceViewer>("Asset Reference Viewer");
        wnd.minSize = new Vector2(980, 560);
        wnd.SetTarget(Selection.activeObject);
    }

    [MenuItem("Assets/Reference View", true)]
    public static bool ValidateOpenFromProject()
    {
        return Selection.activeObject != null && AssetDatabase.Contains(Selection.activeObject);
    }

    private void OnEnable()
    {
        wantsMouseMove = true;
        OctopoAssetReferenceIndex.Invalidated += HandleReferenceIndexInvalidated;
    }

    private void OnDisable()
    {
        OctopoAssetReferenceIndex.Invalidated -= HandleReferenceIndexInvalidated;
        DestroySelectedOutlineCache();
    }

    private void Update()
    {
        if (OctopoAssetReferenceIndex.IsBuilding)
            Repaint();
    }

    private void HandleReferenceIndexInvalidated()
    {
        iconCache.Clear();
        DestroySelectedOutlineCache();

        if (targetAsset == null)
            RestoreTargetFromGuid();

        if (targetAsset == null)
        {
            statusText = "Project asset change detected. Index cache was cleared.";
            Repaint();
            return;
        }

        string targetPath = AssetDatabase.GetAssetPath(targetAsset);
        if (string.IsNullOrEmpty(targetPath))
        {
            ClearGraphData(true);
            statusText = "Target asset was moved or deleted. Select a target again.";
            Repaint();
            return;
        }

        statusText = "Project asset change detected. Rebuilding graph...";
        RebuildGraph();
    }

    private void ClearGraphData(bool clearTargetAsset)
    {
        if (clearTargetAsset)
        {
            targetAsset = null;
            targetAssetGuid = null;
        }

        selectedNode = null;
        hoveredNode = null;
        targetNode = null;
        referencers.Clear();
        dependencies.Clear();
        referencerLevels.Clear();
        dependencyLevels.Clear();
        nodesById.Clear();
        edges.Clear();
        edgeKeys.Clear();
    }


    private static string GetAssetGuid(UnityEngine.Object asset)
    {
        if (asset == null)
            return null;

        string path = AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrEmpty(path))
            return null;

        string guid = AssetDatabase.AssetPathToGUID(path);
        return string.IsNullOrEmpty(guid) ? null : guid;
    }

    private void RestoreTargetFromGuid()
    {
        if (targetAsset != null || string.IsNullOrEmpty(targetAssetGuid))
            return;

        string path = AssetDatabase.GUIDToAssetPath(targetAssetGuid);
        if (string.IsNullOrEmpty(path))
            return;

        targetAsset = AssetDatabase.LoadMainAssetAtPath(path);
    }

    private void SetTarget(UnityEngine.Object asset)
    {
        string newGuid = GetAssetGuid(asset);
        if (asset == targetAsset && string.Equals(newGuid, targetAssetGuid, StringComparison.Ordinal) && targetNode != null)
            return;

        targetAsset = asset;
        targetAssetGuid = newGuid;
        graphPan = Vector2.zero;
        graphZoom = 1.0f;
        selectedNode = null;
        hoveredNode = null;
        detailsScrollPosition = Vector2.zero;
        RebuildGraph();
    }

    private void OnGUI()
    {
        DrawTopBar();
        Rect graphRect = GUILayoutUtility.GetRect(10, 100000, 10, 100000, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        DrawGraphArea(graphRect);
    }

    private void DrawTopBar()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField("Target Asset", EditorStyles.boldLabel, GUILayout.Width(90));

            UnityEngine.Object newTarget = EditorGUILayout.ObjectField(targetAsset, typeof(UnityEngine.Object), false);
            if (newTarget != targetAsset)
                SetTarget(newTarget);

            if (GUILayout.Button("Use Selection", GUILayout.Width(100)))
                SetTarget(Selection.activeObject);

            if (GUILayout.Button("Clear", GUILayout.Width(70)))
            {
                ClearGraphData(true);
                statusText = "Cleared.";
            }

            GUILayout.Space(8);

            EditorGUILayout.LabelField("Depth", GUILayout.Width(40));
            int newDepth = EditorGUILayout.IntSlider(maxDepth, 1, 5, GUILayout.Width(220));
            if (newDepth != maxDepth)
            {
                maxDepth = newDepth;
                if (targetAsset != null)
                    RebuildGraph();
            }

            using (new EditorGUI.DisabledScope(OctopoAssetReferenceIndex.IsBuilding))
            {
                if (GUILayout.Button("Reindex", GUILayout.Width(80)))
                {
                    statusText = "Rebuilding cached index...";
                    Repaint();

                    if (OctopoAssetReferenceIndex.Rebuild())
                    {
                        statusText = "Index rebuilt.";
                        if (targetAsset != null)
                            RebuildGraph();
                    }
                    else
                    {
                        statusText = "Index rebuild canceled.";
                    }
                }
            }

            if (GUILayout.Button("Reset View", GUILayout.Width(90)))
            {
                graphPan = Vector2.zero;
                graphZoom = 1.0f;
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(OctopoAssetReferenceIndex.GetStatusSummary(), EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();

            bool autoReindex = EditorGUILayout.ToggleLeft(
                "Auto Reindex",
                OctopoAssetReferenceViewerPreferences.AutoReindex,
                GUILayout.Width(105f));
            if (autoReindex != OctopoAssetReferenceViewerPreferences.AutoReindex)
                OctopoAssetReferenceViewerPreferences.AutoReindex = autoReindex;

            bool autoPing = EditorGUILayout.ToggleLeft(
                "Auto Ping",
                OctopoAssetReferenceViewerPreferences.AutoPing,
                GUILayout.Width(90f));
            if (autoPing != OctopoAssetReferenceViewerPreferences.AutoPing)
                OctopoAssetReferenceViewerPreferences.AutoPing = autoPing;

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            bool indexPackages = EditorGUILayout.ToggleLeft(
                "Index Packages",
                OctopoAssetReferenceViewerPreferences.IndexPackages,
                GUILayout.Width(115f));
            if (indexPackages != OctopoAssetReferenceViewerPreferences.IndexPackages)
            {
                OctopoAssetReferenceViewerPreferences.IndexPackages = indexPackages;
                OctopoAssetReferenceIndex.Invalidate(false);

                statusText = indexPackages
                    ? "Package indexing enabled. Rebuilding index..."
                    : "Package indexing disabled. Rebuilding Assets-only index...";

                if (targetAsset != null)
                    RebuildGraph();
                else
                    Repaint();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(
                "Controls: Ctrl + Mouse Wheel = Zoom, Mouse Wheel = Vertical Pan, Shift + Mouse Wheel = Horizontal Pan, Middle Mouse Drag = Pan, Double Click Node = Open Asset",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField(statusText, EditorStyles.miniLabel);
        }
    }

    private void DrawGraphArea(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.16f, 0.16f, 0.16f, 1f));

        Rect localRect = new Rect(0f, 0f, rect.width, rect.height);
        HandleGraphEvents(rect, localRect);

        GUI.BeginGroup(rect);
        DrawGrid(localRect, 20f, 0.10f);
        DrawGrid(localRect, 100f, 0.16f);

        if (targetAsset == null || targetNode == null)
        {
            GUI.Label(new Rect(12f, 10f, localRect.width - 24f, 22f),
                "Graph View • Left = Referencers / Center = Target / Right = Dependencies",
                EditorStyles.boldLabel);
            GUI.Label(new Rect(12f, 34f, localRect.width - 24f, 22f),
                "Project window에서 우클릭 > Reference View 또는 위 Target Asset에 드래그해서 사용하세요.",
                EditorStyles.miniLabel);
            GUI.EndGroup();
            return;
        }

        LayoutNodes();
        DrawConnections(localRect);
        DrawNodes(localRect);

        if (selectedNode != null)
            DrawSelectedNodeOutline(localRect, selectedNode);

        DrawSelectedOverlay(localRect);
        DrawMiniMap(localRect);

        GUI.Label(new Rect(12f, 10f, localRect.width - 24f, 22f),
            "Graph View • Left = Referencers / Center = Target / Right = Dependencies",
            EditorStyles.boldLabel);

        GUI.EndGroup();
    }

    private void HandleGraphEvents(Rect rect, Rect localRect)
    {
        Event e = Event.current;
        Vector2 localMouse = e.mousePosition - rect.position;
        Rect overlayRect = GetSelectedOverlayRect(localRect);
        bool isOverOverlay = selectedNode != null && overlayRect.Contains(localMouse);

        if (rect.Contains(e.mousePosition) && !isOverOverlay)
        {
            if (e.type == EventType.MouseMove)
            {
                NodeData newHovered = GetNodeAtPosition(localMouse, localRect);
                if (newHovered != hoveredNode)
                {
                    hoveredNode = newHovered;
                    Repaint();
                }
            }
        }
        else if (hoveredNode != null && e.type == EventType.MouseMove)
        {
            hoveredNode = null;
            Repaint();
        }

        if (!rect.Contains(e.mousePosition))
            return;

        if (isOverOverlay && (e.isMouse || e.type == EventType.ScrollWheel))
            return;

        if (e.type == EventType.ScrollWheel)
        {
            if (e.control || e.command)
            {
                float oldZoom = graphZoom;
                float newZoom = Mathf.Clamp(oldZoom - e.delta.y * 0.03f, MinZoom, MaxZoom);

                Vector2 mouseFromCenter = e.mousePosition - rect.center;
                graphPan = mouseFromCenter - (mouseFromCenter - graphPan) * (newZoom / oldZoom);
                graphZoom = newZoom;

                e.Use();
                Repaint();
                return;
            }

            if (e.shift)
            {
                graphPan.x -= e.delta.y * 22f;
            }
            else
            {
                graphPan.y -= e.delta.y * 22f;
                graphPan.x -= e.delta.x * 22f;
            }

            e.Use();
            Repaint();
            return;
        }

        if (e.type == EventType.MouseDrag && e.button == 2)
        {
            graphPan += e.delta;
            e.Use();
            Repaint();
            return;
        }

        if (e.type == EventType.MouseDown && e.button == 0)
        {
            NodeData clicked = GetNodeAtPosition(localMouse, localRect);
            if (clicked != null)
            {
                if (selectedNode != clicked)
                    detailsScrollPosition = Vector2.zero;

                selectedNode = clicked;
                EnsureNodeDetails(selectedNode);

                if (clicked.asset != null && OctopoAssetReferenceViewerPreferences.AutoPing)
                {
                    Selection.activeObject = clicked.asset;
                    EditorGUIUtility.PingObject(clicked.asset);
                }

                if (e.clickCount == 2 && clicked.asset != null)
                    AssetDatabase.OpenAsset(clicked.asset);

                e.Use();
                Repaint();
            }
            else
            {
                selectedNode = null;
                hoveredNode = null;
                e.Use();
                Repaint();
            }
        }

        if (e.type == EventType.ContextClick)
        {
            NodeData clicked = GetNodeAtPosition(localMouse, localRect);
            if (clicked != null)
            {
                GenericMenu menu = new GenericMenu();
                menu.AddItem(new GUIContent("Ping"), false, () =>
                {
                    EditorGUIUtility.PingObject(clicked.asset);
                    Selection.activeObject = clicked.asset;
                });
                menu.AddItem(new GUIContent("Open"), false, () =>
                {
                    AssetDatabase.OpenAsset(clicked.asset);
                });
                menu.AddItem(new GUIContent("Set as Target"), false, () =>
                {
                    SetTarget(clicked.asset);
                });
                menu.ShowAsContext();
                e.Use();
            }
        }
    }

    private void RebuildGraph()
    {
        referencers.Clear();
        dependencies.Clear();
        referencerLevels.Clear();
        dependencyLevels.Clear();
        nodesById.Clear();
        edges.Clear();
        edgeKeys.Clear();
        selectedNode = null;
        hoveredNode = null;
        targetNode = null;

        if (targetAsset == null)
            RestoreTargetFromGuid();

        if (targetAsset == null)
        {
            statusText = "Target asset is null.";
            Repaint();
            return;
        }

        targetAssetGuid = GetAssetGuid(targetAsset);
        string targetPath = AssetDatabase.GetAssetPath(targetAsset);
        if (string.IsNullOrEmpty(targetPath))
        {
            statusText = "Target must be a saved project asset.";
            Repaint();
            return;
        }

        try
        {
            statusText = OctopoAssetReferenceIndex.IsReady
                ? "Building graph from cached index..."
                : "Building cached index...";
            Repaint();

            if (!OctopoAssetReferenceIndex.EnsureBuilt())
            {
                statusText = "Index build canceled.";
                Repaint();
                return;
            }

            targetNode = CreateNode("T|" + targetPath, targetPath, targetAsset, false, true, 0);
            nodesById[targetNode.id] = targetNode;

            BuildDependencyGraph(targetPath, OctopoAssetReferenceIndex.Forward);
            BuildReferencerGraph(targetPath, OctopoAssetReferenceIndex.Reverse);

            statusText = string.Format(
                "Done • Depth: {0} • Referencers: {1} • Dependencies: {2}",
                maxDepth,
                referencers.Count,
                dependencies.Count);
        }
        finally
        {
            Repaint();
        }
    }

    private void BuildDependencyGraph(string targetPath, Dictionary<string, List<string>> forwardDirect)
    {
        Dictionary<string, NodeData> sideNodes = new Dictionary<string, NodeData>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> frontier = new List<string> { targetPath };

        for (int depth = 1; depth <= maxDepth; depth++)
        {
            List<NodeData> level = new List<NodeData>();
            HashSet<string> levelSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> nextFrontierSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < frontier.Count; i++)
            {
                string parentPath = frontier[i];
                List<string> children;
                if (!forwardDirect.TryGetValue(parentPath, out children))
                    continue;

                for (int c = 0; c < children.Count; c++)
                {
                    string childPath = children[c];
                    if (string.Equals(childPath, targetPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    NodeData childNode;
                    if (!sideNodes.TryGetValue(childPath, out childNode))
                    {
                        UnityEngine.Object childAsset = AssetDatabase.LoadMainAssetAtPath(childPath);
                        if (childAsset == null)
                            continue;

                        childNode = CreateNode("D|" + childPath, childPath, childAsset, false, false, depth);
                        sideNodes.Add(childPath, childNode);
                        nodesById[childNode.id] = childNode;
                        dependencies.Add(childNode);
                    }

                    if (levelSet.Add(childNode.id))
                        level.Add(childNode);

                    string fromId = string.Equals(parentPath, targetPath, StringComparison.OrdinalIgnoreCase)
                        ? targetNode.id
                        : "D|" + parentPath;

                    AddEdge(fromId, childNode.id);

                    if (visited.Add(childPath))
                        nextFrontierSet.Add(childPath);
                }
            }

            dependencyLevels.Add(level);
            frontier = new List<string>(nextFrontierSet);
            if (frontier.Count == 0)
                break;
        }
    }

    private void BuildReferencerGraph(string targetPath, Dictionary<string, List<string>> reverseDirect)
    {
        Dictionary<string, NodeData> sideNodes = new Dictionary<string, NodeData>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> frontier = new List<string> { targetPath };

        for (int depth = 1; depth <= maxDepth; depth++)
        {
            List<NodeData> level = new List<NodeData>();
            HashSet<string> levelSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> nextFrontierSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < frontier.Count; i++)
            {
                string currentPath = frontier[i];
                List<string> refs;
                if (!reverseDirect.TryGetValue(currentPath, out refs))
                    continue;

                for (int r = 0; r < refs.Count; r++)
                {
                    string referencerPath = refs[r];
                    if (string.Equals(referencerPath, targetPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    NodeData refNode;
                    if (!sideNodes.TryGetValue(referencerPath, out refNode))
                    {
                        UnityEngine.Object refAsset = AssetDatabase.LoadMainAssetAtPath(referencerPath);
                        if (refAsset == null)
                            continue;

                        refNode = CreateNode("R|" + referencerPath, referencerPath, refAsset, true, false, depth);
                        sideNodes.Add(referencerPath, refNode);
                        nodesById[refNode.id] = refNode;
                        referencers.Add(refNode);
                    }

                    if (levelSet.Add(refNode.id))
                        level.Add(refNode);

                    string toId = string.Equals(currentPath, targetPath, StringComparison.OrdinalIgnoreCase)
                        ? targetNode.id
                        : "R|" + currentPath;

                    AddEdge(refNode.id, toId);

                    if (visited.Add(referencerPath))
                        nextFrontierSet.Add(referencerPath);
                }
            }

            referencerLevels.Add(level);
            frontier = new List<string>(nextFrontierSet);
            if (frontier.Count == 0)
                break;
        }
    }

    private NodeData CreateNode(string id, string assetPath, UnityEngine.Object asset, bool isReferencer, bool isTarget, int depth)
    {
        return new NodeData
        {
            id = id,
            assetPath = assetPath,
            assetGuid = AssetDatabase.AssetPathToGUID(assetPath),
            asset = asset,
            displayName = GetDisplayName(asset, assetPath),
            kind = GetKind(assetPath),
            isReferencer = isReferencer,
            isTarget = isTarget,
            depth = depth
        };
    }

    private void AddEdge(string fromId, string toId)
    {
        if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId))
            return;

        string key = fromId + "\n" + toId;
        if (!edgeKeys.Add(key))
            return;

        EdgeData e;
        e.fromId = fromId;
        e.toId = toId;
        edges.Add(e);
    }


    private void RefreshNodeFromAsset(NodeData node)
    {
        if (node == null)
            return;

        if (node.asset == null && !string.IsNullOrEmpty(node.assetGuid))
        {
            string restoredPath = AssetDatabase.GUIDToAssetPath(node.assetGuid);
            if (!string.IsNullOrEmpty(restoredPath))
                node.asset = AssetDatabase.LoadMainAssetAtPath(restoredPath);
        }

        if (node.asset == null)
            return;

        string freshPath = AssetDatabase.GetAssetPath(node.asset);
        if (string.IsNullOrEmpty(freshPath) || string.Equals(freshPath, node.assetPath, StringComparison.OrdinalIgnoreCase))
            return;

        node.assetPath = freshPath;
        node.assetGuid = AssetDatabase.AssetPathToGUID(freshPath);
        node.displayName = GetDisplayName(node.asset, freshPath);
        node.kind = GetKind(freshPath);
        node.cachedSizeValid = false;
        node.detailsLoaded = false;
        node.usageStateEvaluated = false;
        node.usageConfirmed = false;
        node.details.Clear();
    }

    private void EnsureNodeDetails(NodeData node)
    {
        RefreshNodeFromAsset(node);

        if (node == null || node.detailsLoaded || node.asset == null)
            return;

        node.detailsLoaded = true;
        node.details.Clear();

        if (node.isReferencer)
        {
            if (node.kind == "Scene")
            {
                node.details.AddRange(DeepSearchScene(node.assetPath, targetAsset));
            }
            else if (node.kind == "Prefab")
            {
                node.details.AddRange(DeepSearchPrefab(node.assetPath, targetAsset));
            }
            else if (node.kind == "Material" && targetAsset is Texture)
            {
                node.details.AddRange(InspectMaterialTextureSlots(node.assetPath, targetAsset as Texture));
            }
        }
        else if (!node.isTarget)
        {
            node.details.AddRange(FindDependencyUsageDetails(node));
        }

        EnsureNodeUsageState(node);
    }

    private List<string> FindDependencyUsageDetails(NodeData node)
    {
        List<string> details = new List<string>();
        if (node == null || node.asset == null || targetAsset == null)
            return details;

        string targetPath = AssetDatabase.GetAssetPath(targetAsset);
        if (string.IsNullOrEmpty(targetPath))
            return details;

        if (targetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
        {
            details.AddRange(DeepSearchPrefab(targetPath, node.asset));
            return details;
        }

        if (targetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
        {
            details.AddRange(DeepSearchScene(targetPath, node.asset));
            return details;
        }

        Material targetMaterial = targetAsset as Material;
        if (targetMaterial != null)
        {
            Texture texture = node.asset as Texture;
            if (texture != null)
            {
                details.AddRange(InspectMaterialTextureSlots(targetPath, texture));
                return details;
            }

            Shader shader = node.asset as Shader;
            if (shader != null && targetMaterial.shader == shader)
            {
                details.Add("Material shader");
                return details;
            }
        }

        return details;
    }

    private void EnsureNodeUsageState(NodeData node)
    {
        if (node == null || node.usageStateEvaluated)
            return;

        node.usageStateEvaluated = true;
        node.usageConfirmed = false;
        node.usageStateTitle = "추가 확인 필요";
        node.usageStateDescription = "자동 판별 정보가 부족해서 수동 확인이 필요합니다.";

        if (node.isTarget)
        {
            node.usageStateTitle = "참조 대상";
            node.usageStateDescription = "현재 가운데에 있는 기준 에셋입니다.";
            return;
        }

        if (node.details != null && node.details.Count > 0)
        {
            node.usageConfirmed = true;
            node.usageStateTitle = "실제 사용 확인됨";

            if (node.isReferencer)
                node.usageStateDescription = "이 에셋 안에서 현재 기준 에셋이 실제로 연결된 위치를 찾았습니다.";
            else
                node.usageStateDescription = "현재 기준 에셋 안에서 이 에셋이 실제로 연결된 위치를 찾았습니다.";

            return;
        }

        if (node.isReferencer)
        {
            if (node.kind == "Scene" || node.kind == "Prefab")
            {
                node.usageStateTitle = "미사용 가능성 있음";
                node.usageStateDescription = "직렬화 참조는 있지만 현재 활성 설정에서는 실제 사용 위치를 찾지 못했습니다. 예전 설정값이 남아있었을 수 있습니다.";
                return;
            }

            if (node.kind == "Material" && targetAsset is Texture)
            {
                node.usageStateTitle = "미사용 가능성 높음";
                node.usageStateDescription = "현재 셰이더의 텍스처 슬롯에서는 이 텍스처를 찾지 못했습니다. 예전 슬롯 데이터이거나 지금은 쓰지 않는 값일 수 있습니다.";
                return;
            }
        }
        else
        {
            if (targetAsset is Material && node.asset is Texture)
            {
                node.usageStateTitle = "미사용 가능성 높음";
                node.usageStateDescription = "현재 머티리얼의 텍스처 슬롯에서 이 텍스처를 찾지 못했습니다. 예전 데이터이거나 지금은 쓰지 않는 값일 수 있습니다.";
                return;
            }

            if (targetAsset is GameObject)
            {
                node.usageStateTitle = "미사용 가능성 있음";
                node.usageStateDescription = "현재 프리팹 안에서 이 에셋의 실제 연결 위치를 찾지 못했습니다. 비활성 설정값이나 예전 참조일 수 있습니다.";
                return;
            }

            if (targetAsset is SceneAsset)
            {
                node.usageStateTitle = "미사용 가능성 있음";
                node.usageStateDescription = "현재 씬 안에서 이 에셋의 실제 연결 위치를 찾지 못했습니다. 비활성 설정값이나 예전 참조일 수 있습니다.";
                return;
            }
        }
    }

    private static Color GetUsageStateColor(NodeData node)
    {
        if (node == null)
            return new Color(0.60f, 0.60f, 0.60f, 1f);

        if (node.usageConfirmed)
            return new Color(0.28f, 0.74f, 0.37f, 1f);

        if (string.Equals(node.usageStateTitle, "미사용 가능성 있음", StringComparison.Ordinal) ||
            string.Equals(node.usageStateTitle, "미사용 가능성 높음", StringComparison.Ordinal))
            return new Color(0.92f, 0.64f, 0.20f, 1f);

        return new Color(0.64f, 0.64f, 0.64f, 1f);
    }


    private void LayoutNodes()
    {
        if (targetNode == null)
            return;

        SortLevelNodes(referencerLevels);
        SortLevelNodes(dependencyLevels);

        Vector2 targetSize = GetNodeSize(targetNode);
        targetNode.graphRect = new Rect(-targetSize.x * 0.5f, -targetSize.y * 0.5f, targetSize.x, targetSize.y);

        float firstColumnOffset = 1480f;
        float columnSpacing = 1280f;

        for (int d = 0; d < referencerLevels.Count; d++)
        {
            float anchorX = -(firstColumnOffset + d * columnSpacing);
            LayoutColumn(referencerLevels[d], anchorX, true);
        }

        for (int d = 0; d < dependencyLevels.Count; d++)
        {
            float anchorX = firstColumnOffset + d * columnSpacing;
            LayoutColumn(dependencyLevels[d], anchorX, false);
        }
    }

    private void DrawConnections(Rect graphRect)
    {
        string activeNodeId = selectedNode != null ? selectedNode.id : (hoveredNode != null ? hoveredNode.id : null);

        Handles.BeginGUI();
        for (int i = 0; i < edges.Count; i++)
        {
            NodeData from;
            NodeData to;
            if (!nodesById.TryGetValue(edges[i].fromId, out from))
                continue;
            if (!nodesById.TryGetValue(edges[i].toId, out to))
                continue;

            Rect a = GraphToScreenRect(from.graphRect, graphRect);
            Rect b = GraphToScreenRect(to.graphRect, graphRect);

            Vector3 start = new Vector3(a.xMax - Mathf.Clamp(a.height * 0.055f, 3f, 5.5f) - 2f, a.center.y, 0f);
            Vector3 end = new Vector3(b.xMin + Mathf.Clamp(b.height * 0.055f, 3f, 5.5f) + 2f, b.center.y, 0f);

            float tangentLength = Mathf.Max(48f, 88f * graphZoom);
            Vector3 startTan = start + Vector3.right * tangentLength;
            Vector3 endTan = end + Vector3.left * tangentLength;

            Rect edgeBounds = GetBezierBounds(start, end, startTan, endTan, RenderCullPadding);
            if (!edgeBounds.Overlaps(graphRect))
                continue;

            bool isActiveEdge = !string.IsNullOrEmpty(activeNodeId) &&
                (edges[i].fromId == activeNodeId || edges[i].toId == activeNodeId);

            Color edgeColor = isActiveEdge
                ? GetSelectionOutlineColor()
                : new Color(197f / 255f, 210f / 255f, 190f / 255f, 0.82f);

            float edgeWidth = isActiveEdge
                ? Mathf.Clamp(9.6f * graphZoom, 4.0f, 16.0f)
                : Mathf.Clamp(4.8f * graphZoom, 2.4f, 8.0f);

            Handles.DrawBezier(start, end, startTan, endTan, edgeColor, null, edgeWidth);

            if (isActiveEdge)
            {
                Handles.DrawBezier(
                    start, end, startTan, endTan,
                    new Color(GetSelectionOutlineColor().r, GetSelectionOutlineColor().g, GetSelectionOutlineColor().b, 0.22f),
                    null,
                    edgeWidth * 2.2f
                );
            }
        }
        Handles.EndGUI();
    }

    private void DrawNodes(Rect graphRect)
    {
        DrawNode(graphRect, targetNode);

        for (int i = 0; i < referencers.Count; i++)
            DrawNode(graphRect, referencers[i]);

        for (int i = 0; i < dependencies.Count; i++)
            DrawNode(graphRect, dependencies[i]);
    }


    private static bool IsRectVisible(Rect rect, Rect viewport, float padding)
    {
        Rect expanded = new Rect(
            viewport.xMin - padding,
            viewport.yMin - padding,
            viewport.width + padding * 2f,
            viewport.height + padding * 2f);

        return rect.Overlaps(expanded);
    }

    private static Rect GetBezierBounds(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float padding)
    {
        float minX = Mathf.Min(Mathf.Min(p0.x, p1.x), Mathf.Min(p2.x, p3.x)) - padding;
        float minY = Mathf.Min(Mathf.Min(p0.y, p1.y), Mathf.Min(p2.y, p3.y)) - padding;
        float maxX = Mathf.Max(Mathf.Max(p0.x, p1.x), Mathf.Max(p2.x, p3.x)) + padding;
        float maxY = Mathf.Max(Mathf.Max(p0.y, p1.y), Mathf.Max(p2.y, p3.y)) + padding;
        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private void DrawNode(Rect graphRect, NodeData node)
    {
        if (node == null)
            return;

        Rect screenRect = RoundRect(GraphToScreenRect(node.graphRect, graphRect));
        if (!IsRectVisible(screenRect, graphRect, RenderCullPadding))
            return;

        bool isSelected = selectedNode == node;
        bool isHovered = hoveredNode == node;

        float cornerRadius = Mathf.Round(screenRect.height * 0.085f);
        float borderThickness = Mathf.Max(1.5f, screenRect.height * (isSelected ? 0.039f : (isHovered ? 0.016f : 0.010f)));

        Color headerColor = GetNodeHeaderColor(node.kind, node.isTarget);
        Color bodyColor = GetNodeBodyColor(node.isTarget);
        Color borderColor = new Color(headerColor.r * 0.9f, headerColor.g * 0.9f, headerColor.b * 0.9f, isHovered ? 0.95f : 0.75f);

        GUI.BeginGroup(screenRect);

        Rect localRect = new Rect(0f, 0f, screenRect.width, screenRect.height);
        float headerHeight = Mathf.Round(localRect.height * 0.30f);
        float bodyInset = 2f;

        DrawRoundedRect(localRect, cornerRadius, headerColor, Color.clear, 0f);

        Rect headerRect = new Rect(0f, 0f, localRect.width, headerHeight);
        DrawTopRoundedRect(headerRect, cornerRadius, headerColor);

        Rect bodyPanelRect = new Rect(
            bodyInset,
            Mathf.Max(headerHeight - 1f, bodyInset),
            localRect.width - bodyInset * 2f,
            localRect.height - Mathf.Max(headerHeight - 1f, bodyInset) - bodyInset);
        DrawRoundedBottomRect(bodyPanelRect, Mathf.Max(0f, cornerRadius - bodyInset), bodyColor);
        EditorGUI.DrawRect(new Rect(bodyInset, headerRect.yMax - 1f, localRect.width - bodyInset * 2f, 1f), new Color(1f, 1f, 1f, 0.05f));

        float sidePad = Mathf.Round(localRect.width * 0.055f);
        float titleGap = Mathf.Round(localRect.width * 0.026f);
        float iconSize = Mathf.Round(headerHeight * 0.34f);
        float headerVPad = Mathf.Round(headerHeight * 0.17f);

        Texture icon = GetNodeTypeIcon(node);
        Rect iconRect = RoundRect(new Rect(sidePad, Mathf.Round((headerHeight - iconSize) * 0.5f), iconSize, iconSize));
        if (icon != null)
        {
            Color prevColor = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
            GUI.color = prevColor;
        }

        float rightPad = Mathf.Round(localRect.width * 0.055f);
        Rect textRect = new Rect(
            iconRect.xMax + titleGap,
            headerVPad,
            localRect.width - (iconRect.xMax + titleGap) - rightPad,
            headerRect.height - headerVPad * 2f);

        GUIStyle title = new GUIStyle(EditorStyles.boldLabel);
        title.wordWrap = false;
        title.clipping = TextClipping.Clip;
        title.fontSize = Mathf.Clamp(Mathf.RoundToInt(headerHeight * 0.34f), 7, 32);
        ApplyTextColorToAllStates(title, Color.white);

        GUIStyle sub = new GUIStyle(EditorStyles.miniLabel);
        sub.wordWrap = false;
        sub.clipping = TextClipping.Clip;
        sub.fontSize = Mathf.Clamp(Mathf.RoundToInt(headerHeight * 0.24f), 6, 20);
        ApplyTextColorToAllStates(sub, new Color(1f, 1f, 1f, 0.80f));

        bool showSecondaryLine = graphZoom >= 0.30f && headerHeight >= 30f && textRect.width >= 120f;
        bool showTitle = graphZoom >= 0.12f && headerHeight >= 18f && textRect.width >= 56f;
        if (showTitle)
        {
            float titleHeight = title.CalcHeight(new GUIContent(node.displayName), textRect.width);
            float titleY = showSecondaryLine ? textRect.y : headerRect.center.y - titleHeight * 0.5f;
            Rect titleRect = new Rect(textRect.x, titleY, textRect.width, titleHeight);
            float shadowOffset = Mathf.Clamp(headerHeight * 0.040f, 1.2f, 2.4f);
            DrawLabelWithDropShadow(titleRect, node.displayName, title, new Vector2(shadowOffset, shadowOffset), new Color(0f, 0f, 0f, 0.592f));
            if (showSecondaryLine)
            {
                float subY = titleY + titleHeight - headerHeight * 0.05f;
                GUI.Label(new Rect(textRect.x, subY, textRect.width, Mathf.Max(14f, textRect.yMax - subY)), node.kind, sub);
            }
        }

        float bodyPadX = Mathf.Round(localRect.width * 0.024f);
        float bodyPadTop = Mathf.Round(localRect.height * 0.022f);
        float bodyPadBottom = Mathf.Round(localRect.height * 0.018f);
        Rect bodyRect = new Rect(
            bodyInset + bodyPadX,
            headerRect.yMax + bodyPadTop,
            localRect.width - (bodyInset + bodyPadX) * 2f,
            localRect.height - headerHeight - bodyPadTop - bodyPadBottom - bodyInset);

        bool showPreview = graphZoom >= 0.10f && bodyRect.width >= 28f && bodyRect.height >= 28f;
        if (showPreview)
        {
            float previewSize = Mathf.Min(bodyRect.width, bodyRect.height) * 0.98f;
            Rect previewRect = RoundRect(new Rect(
                bodyRect.center.x - previewSize * 0.5f,
                bodyRect.center.y - previewSize * 0.5f,
                previewSize,
                previewSize));

            DrawNodePreview(previewRect, node);
        }

        GUI.EndGroup();

        if (!isSelected)
            DrawRoundedOutline(screenRect, cornerRadius, borderColor, borderThickness);
    }


    private void DrawSelectedNodeOutline(Rect graphRect, NodeData node)
    {
        if (node == null)
            return;

        Rect screenRect = RoundRect(GraphToScreenRect(node.graphRect, graphRect));
        if (!IsRectVisible(screenRect, graphRect, RenderCullPadding))
            return;
        float cornerRadius = Mathf.Round(screenRect.height * 0.085f);
        float borderThickness = Mathf.Max(2.1f, screenRect.height * 0.0312f);
        Color topColor = GetSelectionOutlineGradientTopColor();
        Color bottomColor = GetSelectionOutlineGradientBottomColor();

        DrawSelectedOutlineTexture(screenRect, cornerRadius, topColor, bottomColor, borderThickness);
    }

    private Rect GetSelectedOverlayRect(Rect graphRect)
    {
        if (selectedNode == null)
            return new Rect(0f, 0f, 0f, 0f);

        float panelHeight = Mathf.Clamp(graphRect.height - 24f, 360f, 620f);
        return new Rect(graphRect.xMax - 400f, graphRect.y + 12f, 390f, panelHeight);
    }


    private bool CanCleanupSelectedNode(NodeData node)
    {
        if (node == null || node.isTarget)
            return false;

        string containerPath;
        UnityEngine.Object referencedAsset;
        if (!TryGetCleanupContext(node, out containerPath, out referencedAsset))
            return false;

        return IsCleanupSupported(containerPath, referencedAsset);
    }

    private bool TryGetCleanupContext(NodeData node, out string containerPath, out UnityEngine.Object referencedAsset)
    {
        RefreshNodeFromAsset(node);

        containerPath = null;
        referencedAsset = null;

        if (node == null || node.isTarget)
            return false;

        if (node.isReferencer)
        {
            containerPath = node.assetPath;
            referencedAsset = targetAsset;
        }
        else
        {
            containerPath = targetAsset != null ? AssetDatabase.GetAssetPath(targetAsset) : null;
            referencedAsset = node.asset;
        }

        return !string.IsNullOrEmpty(containerPath) && referencedAsset != null;
    }

    private static bool IsCleanupSupported(string containerPath, UnityEngine.Object referencedAsset)
    {
        if (string.IsNullOrEmpty(containerPath) || referencedAsset == null)
            return false;

        if (containerPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            return true;

        if (containerPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            return true;

        if (containerPath.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) && referencedAsset is Texture)
            return true;

        return false;
    }

    private void CleanupSelectedNodeUnusedReferences()
    {
        if (selectedNode == null)
            return;

        string containerPath;
        UnityEngine.Object referencedAsset;
        if (!TryGetCleanupContext(selectedNode, out containerPath, out referencedAsset))
        {
            statusText = "정리할 수 있는 대상이 아닙니다.";
            return;
        }

        if (!IsCleanupSupported(containerPath, referencedAsset))
        {
            statusText = "이 타입은 자동 정리를 아직 지원하지 않습니다.";
            return;
        }

        string containerName = Path.GetFileName(containerPath);
        string refName = referencedAsset != null ? referencedAsset.name : "Unknown";

        if (!EditorUtility.DisplayDialog(
            "미사용 참조 정리",
            string.Format("'{0}' 안에 들어 있는 '{1}' 관련 참조를 제거할까요?\n\n주의: 실제 사용 중인 참조도 제거될 수 있습니다. 제거 후에는 프리팹/씬/머티리얼이 깨질 수 있으니 반드시 확인해주세요.", containerName, refName),
            "정리",
            "취소"))
        {
            return;
        }

        int removedCount = 0;
        try
        {
            removedCount = CleanupUnusedReferences(containerPath, referencedAsset);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            statusText = "정리 중 오류가 발생했습니다. Console을 확인해주세요.";
            return;
        }

        if (removedCount <= 0)
        {
            statusText = "제거할 수 있는 참조를 찾지 못했습니다.";
            return;
        }

        statusText = string.Format("정리 완료 • 제거한 참조: {0}", removedCount);

        if (OctopoAssetReferenceIndex.Rebuild())
            RebuildGraph();
        else
            Repaint();
    }

    private int CleanupUnusedReferences(string containerPath, UnityEngine.Object referencedAsset)
    {
        if (string.IsNullOrEmpty(containerPath) || referencedAsset == null)
            return 0;

        if (containerPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            return CleanupPrefabUnusedReferences(containerPath, referencedAsset);

        if (containerPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            return CleanupSceneUnusedReferences(containerPath, referencedAsset);

        if (containerPath.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) && referencedAsset is Texture)
            return CleanupMaterialUnusedReferences(containerPath, referencedAsset as Texture);

        return 0;
    }

    private int CleanupPrefabUnusedReferences(string prefabPath, UnityEngine.Object referencedAsset)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
            return 0;

        int removedCount = 0;
        try
        {
            removedCount = CleanupGameObjectHierarchy(root, referencedAsset);
            if (removedCount > 0)
            {
                EditorUtility.SetDirty(root);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                AssetDatabase.SaveAssets();
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return removedCount;
    }

    private int CleanupSceneUnusedReferences(string scenePath, UnityEngine.Object referencedAsset)
    {
        bool openedByViewer;
        Scene opened = OpenSceneIfNeeded(scenePath, out openedByViewer);
        if (!opened.IsValid())
            return 0;

        int removedCount = 0;

        try
        {
            GameObject[] roots = opened.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                removedCount += CleanupGameObjectHierarchy(roots[i], referencedAsset);

            if (removedCount > 0)
            {
                EditorSceneManager.MarkSceneDirty(opened);
                EditorSceneManager.SaveScene(opened);
            }
        }
        finally
        {
            if (openedByViewer && opened.IsValid())
                EditorSceneManager.CloseScene(opened, true);
        }

        return removedCount;
    }

    private static int CleanupGameObjectHierarchy(GameObject go, UnityEngine.Object referencedAsset)
    {
        int removedCount = 0;
        if (go == null || referencedAsset == null)
            return 0;

        try
        {
            Component[] comps = go.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                Component c = comps[i];
                if (c == null)
                    continue;

                removedCount += CleanupObjectUnusedReferences(c, referencedAsset);
            }
        }
        catch
        {
        }

        foreach (Transform child in go.transform)
            removedCount += CleanupGameObjectHierarchy(child.gameObject, referencedAsset);

        return removedCount;
    }

    private static int CleanupObjectUnusedReferences(UnityEngine.Object obj, UnityEngine.Object referencedAsset)
    {
        if (obj == null || referencedAsset == null)
            return 0;

        int removedCount = 0;
        string targetAssetPath = AssetDatabase.GetAssetPath(referencedAsset);

        try
        {
            SerializedObject so = new SerializedObject(obj);
            SerializedProperty it = so.GetIterator();
            bool enterChildren = true;
            bool changed = false;

            while (it.Next(enterChildren))
            {
                enterChildren = true;

                if (it.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                if (!it.editable || IsUnsafeCleanupProperty(it.propertyPath))
                    continue;

                UnityEngine.Object referencedObject = it.objectReferenceValue;
                if (!IsReferenceMatch(referencedObject, referencedAsset, targetAssetPath))
                    continue;

                it.objectReferenceValue = null;
                removedCount++;
                changed = true;
            }

            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(obj);
            }
        }
        catch
        {
        }

        return removedCount;
    }

    private static bool IsUnsafeCleanupProperty(string propertyPath)
    {
        if (string.IsNullOrEmpty(propertyPath))
            return true;

        if (propertyPath == "m_Script" ||
            propertyPath == "m_GameObject" ||
            propertyPath == "m_PrefabAsset" ||
            propertyPath == "m_PrefabInstance" ||
            propertyPath == "m_CorrespondingSourceObject")
            return true;

        if (propertyPath.StartsWith("m_Prefab", StringComparison.Ordinal))
            return true;

        return false;
    }

    private static int CleanupMaterialUnusedReferences(string materialPath, Texture referencedTexture)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (mat == null || referencedTexture == null)
            return 0;

        int removedCount = 0;
        string targetAssetPath = AssetDatabase.GetAssetPath(referencedTexture);

        try
        {
            SerializedObject so = new SerializedObject(mat);
            SerializedProperty it = so.GetIterator();
            bool enterChildren = true;
            bool changed = false;

            while (it.Next(enterChildren))
            {
                enterChildren = true;

                if (it.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                if (!it.editable)
                    continue;

                if (it.propertyPath.IndexOf("m_SavedProperties.m_TexEnvs", StringComparison.Ordinal) < 0)
                    continue;

                UnityEngine.Object referencedObject = it.objectReferenceValue;
                if (!IsReferenceMatch(referencedObject, referencedTexture, targetAssetPath))
                    continue;

                it.objectReferenceValue = null;
                removedCount++;
                changed = true;
            }

            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();
            }
        }
        catch
        {
        }

        return removedCount;
    }

    private void DrawSelectedOverlay(Rect graphRect)
    {
        if (selectedNode == null)
            return;

        RefreshNodeFromAsset(selectedNode);
        EnsureNodeDetails(selectedNode);
        EnsureNodeUsageState(selectedNode);

        Rect panel = GetSelectedOverlayRect(graphRect);
        EditorGUI.DrawRect(panel, new Color(0.11f, 0.11f, 0.11f, 0.96f));
        DrawBorder(panel, new Color(1f, 1f, 1f, 0.08f), 1f);

        GUIStyle title = new GUIStyle(EditorStyles.boldLabel);
        title.normal.textColor = Color.white;
        title.wordWrap = true;

        GUIStyle sub = new GUIStyle(EditorStyles.miniLabel);
        sub.normal.textColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        sub.wordWrap = true;

        GUIStyle badge = new GUIStyle(EditorStyles.miniBoldLabel);
        badge.normal.textColor = Color.white;
        badge.alignment = TextAnchor.MiddleCenter;

        GUI.Label(new Rect(panel.x + 10f, panel.y + 8f, panel.width - 20f, 36f), selectedNode.displayName, title);
        GUI.Label(new Rect(panel.x + 10f, panel.y + 42f, panel.width - 20f, 18f), selectedNode.kind, sub);
        GUI.Label(new Rect(panel.x + 10f, panel.y + 58f, panel.width - 20f, 34f), selectedNode.assetPath, sub);

        if (GUI.Button(new Rect(panel.x + 10f, panel.y + 98f, 110f, 24f), "Open"))
        {
            AssetDatabase.OpenAsset(selectedNode.asset);
        }

        if (GUI.Button(new Rect(panel.x + 126f, panel.y + 98f, 140f, 24f), "Set as Target"))
        {
            SetTarget(selectedNode.asset);
        }

        GUI.Label(new Rect(panel.x + 10f, panel.y + 132f, panel.width - 20f, 18f), "참조 상태", title);

        Color usageColor = GetUsageStateColor(selectedNode);
        Rect badgeRect = new Rect(panel.x + 10f, panel.y + 154f, 126f, 22f);
        EditorGUI.DrawRect(badgeRect, usageColor);
        GUI.Label(badgeRect, selectedNode.usageStateTitle ?? "추가 확인 필요", badge);

        GUI.Label(new Rect(panel.x + 10f, panel.y + 182f, panel.width - 20f, 42f),
            selectedNode.usageStateDescription ?? "설명 없음",
            sub);

        if (CanCleanupSelectedNode(selectedNode))
        {
            if (GUI.Button(new Rect(panel.x + 10f, panel.y + 226f, 170f, 24f), "참조 제거"))
            {
                CleanupSelectedNodeUnusedReferences();
                return;
            }
        }

        GUI.Label(new Rect(panel.x + 10f, panel.y + 258f, panel.width - 20f, 18f), "상세 위치", title);

        Rect detailsRect = new Rect(panel.x + 10f, panel.y + 278f, panel.width - 20f, panel.height - 288f);
        float contentWidth = Mathf.Max(40f, detailsRect.width - 18f);
        float contentHeight = 0f;

        if (selectedNode.details == null || selectedNode.details.Count == 0)
        {
            contentHeight = 42f;
        }
        else
        {
            for (int i = 0; i < selectedNode.details.Count; i++)
            {
                float h = sub.CalcHeight(new GUIContent("• " + selectedNode.details[i]), contentWidth);
                contentHeight += h + 2f;
            }
        }

        Rect contentRect = new Rect(0f, 0f, contentWidth, Mathf.Max(detailsRect.height, contentHeight));
        detailsScrollPosition = GUI.BeginScrollView(detailsRect, detailsScrollPosition, contentRect);

        if (selectedNode.details == null || selectedNode.details.Count == 0)
        {
            GUI.Label(
                new Rect(0f, 0f, contentWidth, 42f),
                "자동 판별로 실제 사용 위치를 찾지 못했습니다.\n지금 상태에서는 안 쓰는 참조일 가능성이 있습니다.",
                sub);
        }
        else
        {
            float y = 0f;
            for (int i = 0; i < selectedNode.details.Count; i++)
            {
                float h = sub.CalcHeight(new GUIContent("• " + selectedNode.details[i]), contentWidth);
                GUI.Label(new Rect(0f, y, contentWidth, h), "• " + selectedNode.details[i], sub);
                y += h + 2f;
            }
        }

        GUI.EndScrollView();
    }

    private void DrawMiniMap(Rect graphRect)
    {
        Rect bounds = GetGraphBounds();
        if (bounds.width <= 0f || bounds.height <= 0f)
            return;

        Rect mini = new Rect(graphRect.xMax - 220f, graphRect.yMax - 150f, 210f, 140f);
        EditorGUI.DrawRect(mini, new Color(0f, 0f, 0f, 0.55f));
        DrawBorder(mini, new Color(1f, 1f, 1f, 0.12f), 1f);

        float pad = 8f;
        Rect inner = new Rect(mini.x + pad, mini.y + pad, mini.width - pad * 2f, mini.height - pad * 2f);

        float scaleX = inner.width / Mathf.Max(bounds.width, 1f);
        float scaleY = inner.height / Mathf.Max(bounds.height, 1f);
        float scale = Mathf.Min(scaleX, scaleY);

        DrawMiniMapNode(targetNode, bounds, inner, scale);

        for (int i = 0; i < referencers.Count; i++)
            DrawMiniMapNode(referencers[i], bounds, inner, scale);

        for (int i = 0; i < dependencies.Count; i++)
            DrawMiniMapNode(dependencies[i], bounds, inner, scale);

        Rect viewGraphRect = GetVisibleGraphRect(graphRect);
        Rect viewMini = new Rect(
            inner.x + (viewGraphRect.x - bounds.x) * scale,
            inner.y + (viewGraphRect.y - bounds.y) * scale,
            Mathf.Max(8f, viewGraphRect.width * scale),
            Mathf.Max(8f, viewGraphRect.height * scale));

        Color viewportColor = new Color(1f, 0.9f, 0.2f, 0.95f);
        Rect clippedViewMini = IntersectRects(viewMini, inner);
        if (clippedViewMini.width > 0f && clippedViewMini.height > 0f)
            DrawBorder(clippedViewMini, viewportColor, 1f);

        DrawMiniMapOverflowIndicators(viewMini, inner, viewportColor);

        GUIStyle zoomStyle = new GUIStyle(EditorStyles.miniBoldLabel);
        zoomStyle.alignment = TextAnchor.LowerRight;
        zoomStyle.normal.textColor = new Color(1f, 1f, 1f, 0.78f);
        GUI.Label(
            new Rect(inner.x + 4f, inner.yMax - 18f, inner.width - 8f, 16f),
            Mathf.RoundToInt(graphZoom * 100f) + "%",
            zoomStyle);

    }


    private static Rect IntersectRects(Rect a, Rect b)
    {
        float xMin = Mathf.Max(a.xMin, b.xMin);
        float yMin = Mathf.Max(a.yMin, b.yMin);
        float xMax = Mathf.Min(a.xMax, b.xMax);
        float yMax = Mathf.Min(a.yMax, b.yMax);

        if (xMax <= xMin || yMax <= yMin)
            return new Rect(0f, 0f, 0f, 0f);

        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private static void DrawMiniMapOverflowIndicators(Rect viewportRect, Rect inner, Color color)
    {
        const float arrowSize = 5f;
        const float edgeInset = 2f;

        Handles.BeginGUI();
        Handles.color = color;

        if (viewportRect.xMin < inner.xMin)
        {
            float y = Mathf.Clamp(viewportRect.center.y, inner.yMin + arrowSize, inner.yMax - arrowSize);
            Handles.DrawAAConvexPolygon(
                new Vector3(inner.xMin + edgeInset, y, 0f),
                new Vector3(inner.xMin + edgeInset + arrowSize, y - arrowSize, 0f),
                new Vector3(inner.xMin + edgeInset + arrowSize, y + arrowSize, 0f));
        }

        if (viewportRect.xMax > inner.xMax)
        {
            float y = Mathf.Clamp(viewportRect.center.y, inner.yMin + arrowSize, inner.yMax - arrowSize);
            Handles.DrawAAConvexPolygon(
                new Vector3(inner.xMax - edgeInset, y, 0f),
                new Vector3(inner.xMax - edgeInset - arrowSize, y - arrowSize, 0f),
                new Vector3(inner.xMax - edgeInset - arrowSize, y + arrowSize, 0f));
        }

        if (viewportRect.yMin < inner.yMin)
        {
            float x = Mathf.Clamp(viewportRect.center.x, inner.xMin + arrowSize, inner.xMax - arrowSize);
            Handles.DrawAAConvexPolygon(
                new Vector3(x, inner.yMin + edgeInset, 0f),
                new Vector3(x - arrowSize, inner.yMin + edgeInset + arrowSize, 0f),
                new Vector3(x + arrowSize, inner.yMin + edgeInset + arrowSize, 0f));
        }

        if (viewportRect.yMax > inner.yMax)
        {
            float x = Mathf.Clamp(viewportRect.center.x, inner.xMin + arrowSize, inner.xMax - arrowSize);
            Handles.DrawAAConvexPolygon(
                new Vector3(x, inner.yMax - edgeInset, 0f),
                new Vector3(x - arrowSize, inner.yMax - edgeInset - arrowSize, 0f),
                new Vector3(x + arrowSize, inner.yMax - edgeInset - arrowSize, 0f));
        }

        Handles.color = Color.white;
        Handles.EndGUI();
    }


    private static void DrawMiniMapNode(NodeData node, Rect bounds, Rect inner, float scale)
    {
        if (node == null)
            return;

        Rect r = node.graphRect;
        Rect mr = new Rect(
            inner.x + (r.x - bounds.x) * scale,
            inner.y + (r.y - bounds.y) * scale,
            Mathf.Max(4f, r.width * scale),
            Mathf.Max(4f, r.height * scale));

        EditorGUI.DrawRect(mr, GetNodeColor(node.kind, node.isTarget));
    }

    private Rect GetVisibleGraphRect(Rect graphRect)
    {
        Vector2 topLeft = ScreenToGraph(graphRect.min, graphRect);
        Vector2 bottomRight = ScreenToGraph(graphRect.max, graphRect);
        return Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
    }

    private Rect GetGraphBounds()
    {
        if (targetNode == null)
            return new Rect(0f, 0f, 1f, 1f);

        float minX = targetNode.graphRect.xMin;
        float minY = targetNode.graphRect.yMin;
        float maxX = targetNode.graphRect.xMax;
        float maxY = targetNode.graphRect.yMax;

        for (int i = 0; i < referencers.Count; i++)
        {
            minX = Mathf.Min(minX, referencers[i].graphRect.xMin);
            minY = Mathf.Min(minY, referencers[i].graphRect.yMin);
            maxX = Mathf.Max(maxX, referencers[i].graphRect.xMax);
            maxY = Mathf.Max(maxY, referencers[i].graphRect.yMax);
        }

        for (int i = 0; i < dependencies.Count; i++)
        {
            minX = Mathf.Min(minX, dependencies[i].graphRect.xMin);
            minY = Mathf.Min(minY, dependencies[i].graphRect.yMin);
            maxX = Mathf.Max(maxX, dependencies[i].graphRect.xMax);
            maxY = Mathf.Max(maxY, dependencies[i].graphRect.yMax);
        }

        return Rect.MinMaxRect(minX - 60f, minY - 60f, maxX + 60f, maxY + 60f);
    }

    private NodeData GetNodeAtPosition(Vector2 mousePosition, Rect graphRect)
    {
        if (targetNode != null && GraphToScreenRect(targetNode.graphRect, graphRect).Contains(mousePosition))
            return targetNode;

        for (int i = 0; i < referencers.Count; i++)
        {
            if (GraphToScreenRect(referencers[i].graphRect, graphRect).Contains(mousePosition))
                return referencers[i];
        }

        for (int i = 0; i < dependencies.Count; i++)
        {
            if (GraphToScreenRect(dependencies[i].graphRect, graphRect).Contains(mousePosition))
                return dependencies[i];
        }

        return null;
    }

    private Rect GraphToScreenRect(Rect graphSpaceRect, Rect graphRect)
    {
        Vector2 pos = graphRect.center + graphPan + graphSpaceRect.position * graphZoom;
        Vector2 size = graphSpaceRect.size * graphZoom;
        return new Rect(pos, size);
    }

    private Vector2 ScreenToGraph(Vector2 screenPoint, Rect graphRect)
    {
        return (screenPoint - graphRect.center - graphPan) / graphZoom;
    }


    private Texture GetNodeTypeIcon(NodeData node)
    {
        if (node == null)
            return null;

        string iconKey = "kind:" + (node.kind ?? "default");
        Texture cached;
        if (iconCache.TryGetValue(iconKey, out cached) && cached != null)
            return cached;

        string iconName = "DefaultAsset Icon";
        switch (node.kind)
        {
            case "Texture2D":
            case "Texture":
                iconName = "Texture Icon";
                break;
            case "Material":
                iconName = "Material Icon";
                break;
            case "Prefab":
                iconName = "Prefab Icon";
                break;
            case "Scene":
                iconName = "SceneAsset Icon";
                break;
            case "Shader":
                iconName = "Shader Icon";
                break;
            case "AnimatorController":
                iconName = "AnimatorController Icon";
                break;
            case "AnimationClip":
                iconName = "AnimationClip Icon";
                break;
            case "Model":
            case "Mesh":
                iconName = "Mesh Icon";
                break;
        }

        Texture icon = null;
        GUIContent content = EditorGUIUtility.IconContent(iconName);
        if (content != null)
            icon = content.image as Texture;

        if (icon == null)
            icon = AssetDatabase.GetCachedIcon(node.assetPath) as Texture;

        if (icon == null && node.asset != null)
            icon = AssetPreview.GetMiniThumbnail(node.asset);

        if (icon != null)
            iconCache[iconKey] = icon;

        return icon;
    }

    private Vector2 GetNodeSize(NodeData node)
    {
        float defaultWidth = node != null && node.isTarget ? 390f : 360f;
        float height = node != null && node.isTarget ? 244f : 226f;

        if (node == null)
            return new Vector2(defaultWidth, height);

        if (node.cachedSizeValid)
            return node.cachedSize;

        if (string.IsNullOrEmpty(node.displayName))
        {
            node.cachedSize = new Vector2(defaultWidth, height);
            node.cachedSizeValid = true;
            return node.cachedSize;
        }

        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
        titleStyle.fontSize = node.isTarget ? 24 : 22;
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.wordWrap = false;

        GUIStyle subStyle = new GUIStyle(EditorStyles.miniLabel);
        subStyle.fontSize = node.isTarget ? 14 : 13;
        subStyle.wordWrap = false;

        float iconAreaWidth = 56f;
        float horizontalPadding = 92f;
        float safetyPadding = 42f;
        float titleWidth = titleStyle.CalcSize(new GUIContent(node.displayName)).x;
        float kindWidth = subStyle.CalcSize(new GUIContent(node.kind ?? string.Empty)).x;
        float requiredWidth = Mathf.Ceil(Mathf.Max(titleWidth, kindWidth) + iconAreaWidth + horizontalPadding + safetyPadding);

        node.cachedSize = new Vector2(Mathf.Max(defaultWidth, requiredWidth), height);
        node.cachedSizeValid = true;
        return node.cachedSize;
    }

    private void SortLevelNodes(List<List<NodeData>> levels)
    {
        for (int i = 0; i < levels.Count; i++)
        {
            levels[i].Sort((a, b) =>
            {
                int kindCompare = string.Compare(a.kind, b.kind, StringComparison.OrdinalIgnoreCase);
                if (kindCompare != 0)
                    return kindCompare;

                return string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase);
            });
        }
    }


    private void LayoutColumn(List<NodeData> level, float anchorX, bool isReferencerSide)
    {
        if (level == null || level.Count == 0)
            return;

        float gap = Mathf.Clamp(48f - Mathf.Max(0, level.Count - 3) * 2f, 32f, 48f);
        float totalHeight = 0f;
        float maxWidth = 0f;
        for (int i = 0; i < level.Count; i++)
        {
            Vector2 size = GetNodeSize(level[i]);
            totalHeight += size.y;
            maxWidth = Mathf.Max(maxWidth, size.x);
        }

        totalHeight += gap * Mathf.Max(0, level.Count - 1);

        float y = -totalHeight * 0.5f;
        float leftX = isReferencerSide ? anchorX - maxWidth : anchorX;
        for (int i = 0; i < level.Count; i++)
        {
            NodeData node = level[i];
            Vector2 size = GetNodeSize(node);
            float verticalBias = (i - (level.Count - 1) * 0.5f) * 4f;
            node.graphRect = new Rect(
                leftX,
                y + verticalBias,
                size.x,
                size.y);

            y += size.y + gap;
        }
    }

    private static Rect RoundRect(Rect rect)
    {
        return new Rect(
            Mathf.Round(rect.x),
            Mathf.Round(rect.y),
            Mathf.Round(rect.width),
            Mathf.Round(rect.height));
    }


    private void DrawNodePreview(Rect rect, NodeData node)
    {
        if (node == null)
            return;

        Texture textureAsset = node.asset as Texture;
        if (textureAsset != null)
        {
            DrawFixedCheckerboard(rect, 8f);
            GUI.DrawTexture(rect, textureAsset, ScaleMode.ScaleToFit, true);
            return;
        }

        Texture icon = GetNodeTypeIcon(node);
        if (icon == null)
            return;

        float iconSize = Mathf.Min(rect.width, rect.height) * 0.72f;
        Rect iconRect = new Rect(
            rect.center.x - iconSize * 0.5f,
            rect.center.y - iconSize * 0.5f,
            iconSize,
            iconSize);

        GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
    }

    private static void DrawFixedCheckerboard(Rect rect, float tileSizePixels)
    {
        float tile = Mathf.Max(2f, tileSizePixels);
        Color c0 = new Color(0.32f, 0.32f, 0.32f, 1f);
        Color c1 = new Color(0.42f, 0.42f, 0.42f, 1f);

        int xCount = Mathf.CeilToInt(rect.width / tile);
        int yCount = Mathf.CeilToInt(rect.height / tile);

        for (int y = 0; y < yCount; y++)
        {
            for (int x = 0; x < xCount; x++)
            {
                Rect tileRect = new Rect(
                    rect.x + x * tile,
                    rect.y + y * tile,
                    Mathf.Min(tile, rect.xMax - (rect.x + x * tile)),
                    Mathf.Min(tile, rect.yMax - (rect.y + y * tile)));

                if (tileRect.width <= 0f || tileRect.height <= 0f)
                    continue;

                EditorGUI.DrawRect(tileRect, ((x + y) % 2 == 0) ? c0 : c1);
            }
        }
    }

    private static void DrawLabelWithDropShadow(Rect rect, string text, GUIStyle style, Vector2 shadowOffset, Color shadowColor)
    {
        if (string.IsNullOrEmpty(text) || style == null)
            return;

        Color prevGuiColor = GUI.color;
        Color prevContentColor = GUI.contentColor;

        GUIStyle shadowStyleA = new GUIStyle(style);
        GUIStyle shadowStyleB = new GUIStyle(style);
        GUIStyle shadowStyleC = new GUIStyle(style);
        GUIStyle mainStyle = new GUIStyle(style);

        ApplyTextColorToAllStates(shadowStyleA, new Color(shadowColor.r, shadowColor.g, shadowColor.b, shadowColor.a * 0.42f));
        ApplyTextColorToAllStates(shadowStyleB, new Color(shadowColor.r, shadowColor.g, shadowColor.b, shadowColor.a * 0.72f));
        ApplyTextColorToAllStates(shadowStyleC, new Color(shadowColor.r, shadowColor.g, shadowColor.b, shadowColor.a));
        ApplyTextColorToAllStates(mainStyle, style.normal.textColor.a > 0f ? style.normal.textColor : Color.white);

        GUI.color = Color.white;
        GUI.contentColor = Color.white;

        GUI.Label(new Rect(rect.x + shadowOffset.x * 2.0f, rect.y + shadowOffset.y * 2.0f, rect.width, rect.height), text, shadowStyleA);
        GUI.Label(new Rect(rect.x + shadowOffset.x * 1.4f, rect.y + shadowOffset.y * 1.4f, rect.width, rect.height), text, shadowStyleB);
        GUI.Label(new Rect(rect.x + shadowOffset.x, rect.y + shadowOffset.y, rect.width, rect.height), text, shadowStyleC);
        GUI.Label(rect, text, mainStyle);

        GUI.contentColor = prevContentColor;
        GUI.color = prevGuiColor;
    }

    private static void ApplyTextColorToAllStates(GUIStyle style, Color color)
    {
        if (style == null)
            return;

        style.normal.textColor = color;
        style.hover.textColor = color;
        style.active.textColor = color;
        style.focused.textColor = color;
        style.onNormal.textColor = color;
        style.onHover.textColor = color;
        style.onActive.textColor = color;
        style.onFocused.textColor = color;
    }

    private static void DrawTopRoundedRect(Rect rect, float radius, Color fill)
    {
        radius = Mathf.Clamp(radius, 0f, Mathf.Min(rect.width, rect.height));

        EditorGUI.DrawRect(new Rect(rect.x + radius, rect.y, rect.width - radius * 2f, rect.height), fill);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y + radius, radius, rect.height - radius), fill);
        EditorGUI.DrawRect(new Rect(rect.xMax - radius, rect.y + radius, radius, rect.height - radius), fill);

        Handles.BeginGUI();
        Handles.color = fill;
        Handles.DrawSolidDisc(new Vector3(rect.x + radius, rect.y + radius, 0f), Vector3.forward, radius);
        Handles.DrawSolidDisc(new Vector3(rect.xMax - radius, rect.y + radius, 0f), Vector3.forward, radius);
        Handles.color = Color.white;
        Handles.EndGUI();
    }


    private static void DrawRoundedBottomRect(Rect rect, float radius, Color fill)
    {
        radius = Mathf.Clamp(radius, 0f, Mathf.Min(rect.width, rect.height) * 0.5f);

        if (radius <= 0.01f)
        {
            EditorGUI.DrawRect(rect, fill);
            return;
        }

        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, rect.height - radius), fill);
        EditorGUI.DrawRect(new Rect(rect.x + radius, rect.y + rect.height - radius, rect.width - radius * 2f, radius), fill);

        Handles.BeginGUI();
        Handles.color = fill;
        Handles.DrawSolidDisc(new Vector3(rect.x + radius, rect.yMax - radius, 0f), Vector3.forward, radius);
        Handles.DrawSolidDisc(new Vector3(rect.xMax - radius, rect.yMax - radius, 0f), Vector3.forward, radius);
        Handles.color = Color.white;
        Handles.EndGUI();
    }

    private static Color GetSelectionOutlineColor()
    {
        return new Color(248f / 255f, 215f / 255f, 0f, 1f); // f8d700
    }

    private static Color GetSelectionOutlineGradientTopColor()
    {
        return GetSelectionOutlineColor();
    }

    private static Color GetSelectionOutlineGradientBottomColor()
    {
        return GetSelectionOutlineColor();
    }

    private static Color GetNodeHeaderColor(string kind, bool isTarget)
    {
        switch (kind)
        {
            case "Texture2D":
            case "Texture":
                return new Color(111f / 255f, 33f / 255f, 33f / 255f, 1f); // 6f2121
            case "Material":
                return new Color(33f / 255f, 111f / 255f, 33f / 255f, 1f); // 216f21
            case "Scene":
                return new Color(149f / 255f, 89f / 255f, 0f, 1f); // 955900
            case "Model":
            case "Mesh":
                return new Color(0f, 149f / 255f, 149f / 255f, 1f); // 009595
            case "Prefab":
                return new Color(32f / 255f, 98f / 255f, 140f / 255f, 1f);
            case "Shader":
                return new Color(76f / 255f, 55f / 255f, 122f / 255f, 1f);
            default:
                return new Color(72f / 255f, 72f / 255f, 72f / 255f, 1f);
        }
    }

    private static Color GetNodeBodyColor(bool isTarget)
    {
        return new Color(36f / 255f, 36f / 255f, 36f / 255f, 1f); // 242424
    }

    private static void DrawRoundedRect(Rect rect, float radius, Color fill, Color border, float borderThickness)
    {
        radius = Mathf.Clamp(radius, 0f, Mathf.Min(rect.width, rect.height) * 0.5f);

        if (radius <= 0.01f)
        {
            EditorGUI.DrawRect(rect, fill);
            if (borderThickness > 0f && border.a > 0f)
                DrawBorder(rect, border, borderThickness);
            return;
        }

        EditorGUI.DrawRect(new Rect(rect.x + radius, rect.y, rect.width - radius * 2f, rect.height), fill);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y + radius, radius, rect.height - radius * 2f), fill);
        EditorGUI.DrawRect(new Rect(rect.xMax - radius, rect.y + radius, radius, rect.height - radius * 2f), fill);

        Handles.BeginGUI();
        Handles.color = fill;
        Handles.DrawSolidDisc(new Vector3(rect.x + radius, rect.y + radius, 0f), Vector3.forward, radius);
        Handles.DrawSolidDisc(new Vector3(rect.xMax - radius, rect.y + radius, 0f), Vector3.forward, radius);
        Handles.DrawSolidDisc(new Vector3(rect.x + radius, rect.yMax - radius, 0f), Vector3.forward, radius);
        Handles.DrawSolidDisc(new Vector3(rect.xMax - radius, rect.yMax - radius, 0f), Vector3.forward, radius);
        Handles.color = Color.white;
        Handles.EndGUI();

        if (borderThickness > 0f && border.a > 0f)
            DrawRoundedOutline(rect, radius, border, borderThickness);
    }

    private void DrawSelectedOutlineTexture(Rect rect, float radius, Color topColor, Color bottomColor, float thickness)
    {
        int pad = Mathf.Max(3, Mathf.CeilToInt(thickness * 1.5f));
        int width = Mathf.Max(8, Mathf.RoundToInt(rect.width));
        int height = Mathf.Max(8, Mathf.RoundToInt(rect.height));

        EnsureSelectedOutlineTexture(width, height, pad, radius, thickness, topColor, bottomColor);
        if (selectedOutlineTextureCache == null)
            return;

        Rect drawRect = new Rect(rect.x - pad, rect.y - pad, width + pad * 2, height + pad * 2);
        GUI.DrawTexture(drawRect, selectedOutlineTextureCache, ScaleMode.StretchToFill, true);
    }

    private void EnsureSelectedOutlineTexture(int width, int height, int pad, float radius, float thickness, Color topColor, Color bottomColor)
    {
        bool cacheValid = selectedOutlineTextureCache != null &&
            selectedOutlineCacheWidth == width &&
            selectedOutlineCacheHeight == height &&
            selectedOutlineCachePad == pad &&
            Mathf.Abs(selectedOutlineCacheRadius - radius) < 0.01f &&
            Mathf.Abs(selectedOutlineCacheThickness - thickness) < 0.01f &&
            ApproximatelyColor(selectedOutlineCacheTopColor, topColor) &&
            ApproximatelyColor(selectedOutlineCacheBottomColor, bottomColor);

        if (cacheValid)
            return;

        DestroySelectedOutlineCache();

        int texWidth = width + pad * 2;
        int texHeight = height + pad * 2;
        Texture2D tex = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false, true);
        tex.name = "OctopoAssetReferenceViewer_SelectedOutline";
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[texWidth * texHeight];
        Vector2 outerSize = new Vector2(width, height);
        Vector2 innerSize = new Vector2(Mathf.Max(0.01f, width - thickness * 2f), Mathf.Max(0.01f, height - thickness * 2f));
        float innerRadius = Mathf.Max(0f, radius - thickness);
        float aa = 1.15f;

        for (int y = 0; y < texHeight; y++)
        {
            float gradientT = texHeight <= 1 ? 0f : 1f - ((float)y / (texHeight - 1));
            Color gradientColor = Color.Lerp(bottomColor, topColor, gradientT);

            for (int x = 0; x < texWidth; x++)
            {
                Vector2 outerP = new Vector2(x + 0.5f - pad, y + 0.5f - pad);
                float dOuter = SignedDistanceRoundedRect(outerP, outerSize, radius);
                float outerMask = 1f - Mathf.Clamp01((dOuter + aa) / (aa * 2f));

                Vector2 innerP = new Vector2(x + 0.5f - pad - thickness, y + 0.5f - pad - thickness);
                float dInner = SignedDistanceRoundedRect(innerP, innerSize, innerRadius);
                float innerMask = 1f - Mathf.Clamp01((dInner + aa) / (aa * 2f));

                float alpha = Mathf.Clamp01(outerMask - innerMask);
                if (alpha <= 0.0001f)
                    continue;

                pixels[y * texWidth + x] = new Color(
                    gradientColor.r,
                    gradientColor.g,
                    gradientColor.b,
                    gradientColor.a * alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false, true);

        selectedOutlineTextureCache = tex;
        selectedOutlineCacheWidth = width;
        selectedOutlineCacheHeight = height;
        selectedOutlineCachePad = pad;
        selectedOutlineCacheRadius = radius;
        selectedOutlineCacheThickness = thickness;
        selectedOutlineCacheTopColor = topColor;
        selectedOutlineCacheBottomColor = bottomColor;
    }

    private void DestroySelectedOutlineCache()
    {
        if (selectedOutlineTextureCache != null)
        {
            DestroyImmediate(selectedOutlineTextureCache);
            selectedOutlineTextureCache = null;
        }
    }

    private static bool ApproximatelyColor(Color a, Color b)
    {
        return Mathf.Abs(a.r - b.r) < 0.001f &&
               Mathf.Abs(a.g - b.g) < 0.001f &&
               Mathf.Abs(a.b - b.b) < 0.001f &&
               Mathf.Abs(a.a - b.a) < 0.001f;
    }

    private static float SignedDistanceRoundedRect(Vector2 p, Vector2 size, float radius)
    {
        radius = Mathf.Clamp(radius, 0f, Mathf.Min(size.x, size.y) * 0.5f);
        Vector2 half = size * 0.5f;
        Vector2 center = half;
        Vector2 q = new Vector2(Mathf.Abs(p.x - center.x), Mathf.Abs(p.y - center.y)) - (half - Vector2.one * radius);
        Vector2 maxQ = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f));
        float outside = maxQ.magnitude;
        float inside = Mathf.Min(Mathf.Max(q.x, q.y), 0f);
        return outside + inside - radius;
    }

    private static void DrawRoundedOutline(Rect rect, float radius, Color border, float borderThickness)
    {
        DrawRoundedOutlineClean(rect, radius, border, borderThickness);
    }


    private static void DrawRoundedOutlineClean(Rect rect, float radius, Color border, float borderThickness)
    {
        if (borderThickness <= 0f || border.a <= 0f)
            return;

        radius = Mathf.Clamp(radius, 0f, Mathf.Min(rect.width, rect.height) * 0.5f);
        if (radius <= 0.01f)
        {
            DrawBorder(rect, border, borderThickness);
            return;
        }

        DrawRoundedOutlineBezier(rect, radius, border, borderThickness);
    }

    private static void DrawRoundedOutlineBezier(Rect rect, float radius, Color border, float borderThickness)
    {
        float xMin = rect.xMin;
        float xMax = rect.xMax;
        float yMin = rect.yMin;
        float yMax = rect.yMax;
        float k = radius * 0.552284749831f;

        Vector3 topLeftStart = new Vector3(xMin + radius, yMin, 0f);
        Vector3 topRightStart = new Vector3(xMax - radius, yMin, 0f);
        Vector3 rightTopStart = new Vector3(xMax, yMin + radius, 0f);
        Vector3 rightBottomStart = new Vector3(xMax, yMax - radius, 0f);
        Vector3 bottomRightStart = new Vector3(xMax - radius, yMax, 0f);
        Vector3 bottomLeftStart = new Vector3(xMin + radius, yMax, 0f);
        Vector3 leftBottomStart = new Vector3(xMin, yMax - radius, 0f);
        Vector3 leftTopStart = new Vector3(xMin, yMin + radius, 0f);

        Handles.BeginGUI();
        Handles.color = border;

        if (rect.width > radius * 2f)
        {
            Handles.DrawAAPolyLine(borderThickness, new Vector3[] { topLeftStart, topRightStart });
            Handles.DrawAAPolyLine(borderThickness, new Vector3[] { bottomRightStart, bottomLeftStart });
        }

        if (rect.height > radius * 2f)
        {
            Handles.DrawAAPolyLine(borderThickness, new Vector3[] { rightTopStart, rightBottomStart });
            Handles.DrawAAPolyLine(borderThickness, new Vector3[] { leftBottomStart, leftTopStart });
        }

        DrawQuarterBezierTopRight(xMax - radius, yMin + radius, radius, k, borderThickness);
        DrawQuarterBezierBottomRight(xMax - radius, yMax - radius, radius, k, borderThickness);
        DrawQuarterBezierBottomLeft(xMin + radius, yMax - radius, radius, k, borderThickness);
        DrawQuarterBezierTopLeft(xMin + radius, yMin + radius, radius, k, borderThickness);

        Handles.color = Color.white;
        Handles.EndGUI();
    }

    private static void DrawQuarterBezierTopRight(float cx, float cy, float radius, float k, float thickness)
    {
        Vector3 p0 = new Vector3(cx, cy - radius, 0f);
        Vector3 p1 = new Vector3(cx + k, cy - radius, 0f);
        Vector3 p2 = new Vector3(cx + radius, cy - k, 0f);
        Vector3 p3 = new Vector3(cx + radius, cy, 0f);
        Handles.DrawBezier(p0, p3, p1, p2, Handles.color, null, thickness);
    }

    private static void DrawQuarterBezierBottomRight(float cx, float cy, float radius, float k, float thickness)
    {
        Vector3 p0 = new Vector3(cx + radius, cy, 0f);
        Vector3 p1 = new Vector3(cx + radius, cy + k, 0f);
        Vector3 p2 = new Vector3(cx + k, cy + radius, 0f);
        Vector3 p3 = new Vector3(cx, cy + radius, 0f);
        Handles.DrawBezier(p0, p3, p1, p2, Handles.color, null, thickness);
    }

    private static void DrawQuarterBezierBottomLeft(float cx, float cy, float radius, float k, float thickness)
    {
        Vector3 p0 = new Vector3(cx, cy + radius, 0f);
        Vector3 p1 = new Vector3(cx - k, cy + radius, 0f);
        Vector3 p2 = new Vector3(cx - radius, cy + k, 0f);
        Vector3 p3 = new Vector3(cx - radius, cy, 0f);
        Handles.DrawBezier(p0, p3, p1, p2, Handles.color, null, thickness);
    }

    private static void DrawQuarterBezierTopLeft(float cx, float cy, float radius, float k, float thickness)
    {
        Vector3 p0 = new Vector3(cx - radius, cy, 0f);
        Vector3 p1 = new Vector3(cx - radius, cy - k, 0f);
        Vector3 p2 = new Vector3(cx - k, cy - radius, 0f);
        Vector3 p3 = new Vector3(cx, cy - radius, 0f);
        Handles.DrawBezier(p0, p3, p1, p2, Handles.color, null, thickness);
    }

    private static void DrawBorder(Rect rect, Color color, float thickness)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }


    private static Color GetNodeColor(string kind, bool isTarget)
    {
        return GetNodeHeaderColor(kind, isTarget);
    }

    private void DrawGrid(Rect rect, float spacing, float opacity)
    {
        float scaledSpacing = spacing * graphZoom;
        if (scaledSpacing < 8f)
            return;

        Handles.BeginGUI();
        Handles.color = new Color(1f, 1f, 1f, opacity);

        float originX = rect.center.x + graphPan.x;
        float originY = rect.center.y + graphPan.y;
        float firstX = originX + Mathf.Floor((rect.xMin - originX) / scaledSpacing) * scaledSpacing;
        float firstY = originY + Mathf.Floor((rect.yMin - originY) / scaledSpacing) * scaledSpacing;

        for (float x = firstX; x <= rect.xMax; x += scaledSpacing)
            Handles.DrawLine(new Vector3(x, rect.yMin), new Vector3(x, rect.yMax));

        for (float y = firstY; y <= rect.yMax; y += scaledSpacing)
            Handles.DrawLine(new Vector3(rect.xMin, y), new Vector3(rect.xMax, y));

        Handles.color = Color.white;
        Handles.EndGUI();
    }

    private static string GetDisplayName(UnityEngine.Object asset, string path)
    {
        string displayName = asset != null && !string.IsNullOrEmpty(asset.name)
            ? asset.name
            : Path.GetFileNameWithoutExtension(path);

        if ((asset is Shader || path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrEmpty(displayName))
        {
            int slashIndex = displayName.LastIndexOf('/');
            if (slashIndex >= 0 && slashIndex < displayName.Length - 1)
                displayName = displayName.Substring(slashIndex + 1);
        }

        return displayName;
    }

    private static string GetKind(string path)
    {
        Type t = AssetDatabase.GetMainAssetTypeAtPath(path);
        if (t != null)
        {
            if (t == typeof(SceneAsset))
                return "Scene";

            if (t == typeof(GameObject))
            {
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    return "Prefab";
                if (path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                    return "Model";
            }

            return t.Name;
        }

        return Path.GetExtension(path);
    }

    private static List<string> DeepSearchScene(string scenePath, UnityEngine.Object target)
    {
        List<string> hits = new List<string>();
        bool openedByViewer;
        Scene opened = OpenSceneIfNeeded(scenePath, out openedByViewer);
        if (!opened.IsValid())
            return hits;

        try
        {
            GameObject[] roots = opened.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                SearchObjectHierarchy(roots[i], target, hits);
        }
        finally
        {
            if (openedByViewer && opened.IsValid())
                EditorSceneManager.CloseScene(opened, true);
        }
        return hits;
    }

    private static Scene OpenSceneIfNeeded(string scenePath, out bool openedByViewer)
    {
        openedByViewer = false;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.IsValid())
                continue;

            if (string.Equals(scene.path, scenePath, StringComparison.OrdinalIgnoreCase))
                return scene;
        }

        openedByViewer = true;
        return EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
    }

    private static List<string> DeepSearchPrefab(string prefabPath, UnityEngine.Object target)
    {
        List<string> hits = new List<string>();
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            SearchObjectHierarchy(root, target, hits);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return hits;
    }

    private static void SearchObjectHierarchy(GameObject go, UnityEngine.Object target, List<string> hits)
    {
        try
        {
            Component[] comps = go.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                Component c = comps[i];
                if (c == null)
                    continue;

                IEnumerable<string> details = FindObjectReferenceFields(c, target);
                foreach (string d in details)
                    hits.Add(GetGameObjectPath(go) + " ▸ " + c.GetType().Name + " ▸ " + d);
            }
        }
        catch
        {
        }

        foreach (Transform child in go.transform)
            SearchObjectHierarchy(child.gameObject, target, hits);
    }

    private static IEnumerable<string> FindObjectReferenceFields(UnityEngine.Object obj, UnityEngine.Object target)
    {
        List<string> found = new List<string>();
        if (obj == null || target == null)
            return found;

        ParticleSystemRenderer psr = obj as ParticleSystemRenderer;
        ParticleSystemRenderMode? psRenderMode = null;
        if (psr != null)
            psRenderMode = psr.renderMode;

        string targetAssetPath = AssetDatabase.GetAssetPath(target);

        try
        {
            SerializedObject so = new SerializedObject(obj);
            SerializedProperty it = so.GetIterator();
            bool enterChildren = true;

            while (it.NextVisible(enterChildren))
            {
                enterChildren = true;

                if (it.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                UnityEngine.Object referencedObject = it.objectReferenceValue;
                if (!IsReferenceMatch(referencedObject, target, targetAssetPath))
                    continue;

                string path = it.propertyPath;

                if (psr != null && psRenderMode.HasValue && psRenderMode.Value != ParticleSystemRenderMode.Mesh)
                {
                    if (path == "m_Mesh" ||
                        path.StartsWith("m_Mesh.", StringComparison.Ordinal) ||
                        path.StartsWith("m_Meshes", StringComparison.Ordinal))
                        continue;
                }

                found.Add(path);
            }
        }
        catch
        {
        }

        return found;
    }

    private static bool IsReferenceMatch(UnityEngine.Object referencedObject, UnityEngine.Object target, string targetAssetPath)
    {
        if (referencedObject == null || target == null)
            return false;

        if (referencedObject == target)
            return true;

        if (string.IsNullOrEmpty(targetAssetPath))
            return false;

        string referencedAssetPath = AssetDatabase.GetAssetPath(referencedObject);
        if (string.IsNullOrEmpty(referencedAssetPath))
            return false;

        return string.Equals(referencedAssetPath, targetAssetPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetGameObjectPath(GameObject go)
    {
        Stack<string> stack = new Stack<string>();
        Transform t = go.transform;
        while (t != null)
        {
            stack.Push(t.name);
            t = t.parent;
        }
        return string.Join("/", stack.ToArray());
    }

    private static List<string> InspectMaterialTextureSlots(string materialPath, Texture targetTexture)
    {
        List<string> details = new List<string>();
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (mat == null)
            return details;

        try
        {
            string[] props = mat.GetTexturePropertyNames();
            for (int i = 0; i < props.Length; i++)
            {
                if (mat.GetTexture(props[i]) == targetTexture)
                    details.Add("Material texture slot: " + props[i]);
            }
        }
        catch
        {
        }

        return details;
    }
}
