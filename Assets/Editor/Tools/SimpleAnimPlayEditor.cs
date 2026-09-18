using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

[CustomEditor(typeof(SimpleAnimPlay))]
public class SimpleAnimPlayEditor : Editor
{
    private static readonly Color SelColor = new Color(0.3f, 0.5f, 0.85f, 0.35f);
    private const float PadY = 2f;
    private const float PadH = 4f;
    private const float GripW = 16f;

    private SerializedProperty _pTargetNode;
    private SerializedProperty _pEnableScale;
    private SerializedProperty _pEnableAlpha;
    private SerializedProperty _pEnableRotation;
    private SerializedProperty _pEnablePosition;
    private SerializedProperty _pScaleKeys;
    private SerializedProperty _pAlphaKeys;
    private SerializedProperty _pRotationKeys;
    private SerializedProperty _pRotationAxis;
    private SerializedProperty _pPositionKeys;

    // 旧字段（迁移检测用）
    private SerializedProperty _pKeyTimes;
    private SerializedProperty _pKeyScales;

    private ReorderableList _scaleList;
    private ReorderableList _alphaList;
    private ReorderableList _rotationList;
    private ReorderableList _positionList;

    // 轨道折叠状态（纯 UI 展开收起，不影响运行时播放；enable 开关才控制播放与显隐）
    private bool _scaleFold = true;
    private bool _alphaFold = true;
    private bool _rotationFold = true;
    private bool _positionFold = true;

    private void OnEnable()
    {
        _pTargetNode      = serializedObject.FindProperty("targetNode");
        _pEnableScale     = serializedObject.FindProperty("enableScale");
        _pEnableAlpha     = serializedObject.FindProperty("enableAlpha");
        _pEnableRotation  = serializedObject.FindProperty("enableRotation");
        _pEnablePosition  = serializedObject.FindProperty("enablePosition");
        _pScaleKeys       = serializedObject.FindProperty("scaleKeys");
        _pAlphaKeys       = serializedObject.FindProperty("alphaKeys");
        _pRotationKeys    = serializedObject.FindProperty("rotationKeys");
        _pRotationAxis    = serializedObject.FindProperty("rotationAxis");
        _pPositionKeys    = serializedObject.FindProperty("positionKeys");
        _pKeyTimes        = serializedObject.FindProperty("keyTimes");
        _pKeyScales       = serializedObject.FindProperty("keyScales");

        _scaleList    = BuildList(_pScaleKeys,    "缩放关键帧",    DrawScaleRow,    22f);
        _alphaList    = BuildList(_pAlphaKeys,    "透明度关键帧",  DrawAlphaRow,    22f);
        _rotationList = BuildList(_pRotationKeys, "旋转关键帧",    DrawRotationRow, 22f);
        _positionList = BuildList(_pPositionKeys, "位移关键帧",    DrawPositionRow, 22f);
    }

    // 打开面板即自动把旧 keyTimes/keyScales 继承到缩放轨道（用户无感知，无需任何操作）：
    // 面板立即显示旧关键帧；保存场景/预制体时持久化。运行时 OnEnable 另有内存迁移兜底，
    // 未在编辑器打开过的场景/预制体播放时同样直接继承。
    private void TryAutoMigrateLegacy()
    {
        if (_pKeyTimes == null || _pKeyScales == null || _pScaleKeys == null) return;
        if (_pKeyTimes.arraySize == 0 || _pKeyScales.arraySize == 0) return;
        if (_pKeyTimes.arraySize != _pKeyScales.arraySize) return;
        if (_pScaleKeys.arraySize > 0) return; // 已迁移或已有新数据，不覆盖

        int n = _pKeyTimes.arraySize;
        for (int i = 0; i < n; i++)
        {
            _pScaleKeys.InsertArrayElementAtIndex(i);
            var elem = _pScaleKeys.GetArrayElementAtIndex(i);
            elem.FindPropertyRelative("time").floatValue   = _pKeyTimes.GetArrayElementAtIndex(i).floatValue;
            elem.FindPropertyRelative("scale").vector3Value = _pKeyScales.GetArrayElementAtIndex(i).vector3Value;
        }
        // 数据修复性质，不进 Undo 栈；旧 keyTimes/keyScales 原样保留作备份
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        Debug.Log($"[SimpleAnimPlay] 编辑器自动迁移 {n} 帧旧数据到缩放轨道：{(target as SimpleAnimPlay)?.name}");
    }

    // ====================== 批量迁移（场景 + 指定特效文件夹的 Prefab） ======================
    // 背景：运行时 OnEnable 的内存迁移是幂等兜底（Play 修改不持久化，每次执行属正常行为，
    // 已静默）。要让旧数据真正落盘到新格式，需在编辑模式下批量写入资产文件。
    // 面板自动迁移只覆盖"被选中查看过"的物体；特效大多在 Prefab 里且常为禁用状态
    // （Timeline Activation 控制），所以提供本菜单一次性处理：
    // 1) 当前场景内所有 SimpleAnimPlay（含禁用物体，需手动 Ctrl+S 保存场景）
    // 2) 指定文件夹（含子文件夹）内的所有 Prefab 资产（自动 SaveAssets 持久化）
    // 文件夹指定方式：Project 窗口选中文件夹后点菜单 → 直接用它；未选中 → 弹出对话框选择。
    // 用法：非 Play 模式 → Tools → SimpleAnimPlay → 迁移旧动画数据（场景与指定文件夹Prefab）。
    [MenuItem("Tools/SimpleAnimPlay/迁移旧动画数据（场景与指定文件夹Prefab）")]
    private static void MigrateAllLegacyEverywhere()
    {
        // ---------- 0) 确定要扫描的文件夹 ----------
        // 遍历 Project 窗口当前选中的所有项，找第一个有效文件夹。
        // 用 Selection.assetGUIDs 而非 Selection.activeObject：前者明确只反映 Project 窗口的
        // 资产选中，不受 Hierarchy/Inspector 焦点影响，多选时也能逐个检查。
        string folder = null;
        foreach (var guid in Selection.assetGUIDs)
        {
            if (string.IsNullOrEmpty(guid)) continue;
            string selPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(selPath) && AssetDatabase.IsValidFolder(selPath))
            {
                folder = selPath;
                break;
            }
        }
        // 未选中任何文件夹 → 弹对话框让用户选（默认定位到 Assets）
        if (folder == null)
        {
            // 如果确实有选中项但都不是文件夹，提示一下，避免"我明明选中了却弹窗"的困惑
            string hint = Selection.assetGUIDs.Length > 0
                ? "（检测到选中项都不是文件夹，请在 Project 窗口选中一个文件夹后再运行本工具）"
                : "";

            string abs = EditorUtility.OpenFolderPanel(
                "选择特效文件夹（扫描其中所有 Prefab）" + hint,
                Application.dataPath, "");
            if (string.IsNullOrEmpty(abs)) return; // 取消：什么都不做

            // OpenFolderPanel 返回绝对路径，需转成 "Assets/..." 相对路径才能交给 AssetDatabase
            string projectRoot = Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length);
            string rel = abs.Replace('\\', '/').Substring(projectRoot.Length);
            if (rel != "Assets" && !rel.StartsWith("Assets/"))
            {
                EditorUtility.DisplayDialog("SimpleAnimPlay 迁移", "选择的文件夹不在本工程的 Assets 目录内，无法处理。", "好");
                return;
            }
            if (rel == "Assets")
            {
                // 选了 Assets 根 = 扫全工程，确认一下避免误操作慢扫描
                if (!EditorUtility.DisplayDialog("SimpleAnimPlay 迁移",
                    "选中的是 Assets 根目录，将扫描整个工程的所有 Prefab，可能较慢。\n继续吗？", "继续", "取消"))
                    return;
            }
            folder = rel;
        }

        int migratedComps = 0;
        int touchedPrefabs = 0;

        // ---------- 1) 当前场景（含禁用物体） ----------
        // includeInactive: true —— 覆盖 Timeline Activation 控制的禁用特效
        foreach (var comp in Object.FindObjectsOfType<SimpleAnimPlay>(true))
        {
            if (comp == null) continue;
            if (MigrateOneComponent(comp))
            {
                migratedComps++;
                EditorUtility.SetDirty(comp);
            }
        }

        // ---------- 2) 指定文件夹（含子文件夹）内的所有 Prefab ----------
        _missingScriptAsked = false; // 每次运行重置 missing script 询问状态
        var guids = AssetDatabase.FindAssets("t:prefab", new[] { folder });
        for (int g = 0; g < guids.Length; g++)
        {
            if (EditorUtility.DisplayCancelableProgressBar("SimpleAnimPlay 批量迁移",
                    $"[{folder}] 扫描 Prefab {g + 1}/{guids.Length}…", (float)g / guids.Length))
                break; // 用户取消：已处理的照常保存，剩余的留待下次

            string path = AssetDatabase.GUIDToAssetPath(guids[g]);
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (root == null) continue;

            // ---------- 2a) missing script 处理 ----------
            // Unity 不允许保存带丢失脚本引用的 Prefab（SaveAssets 会静默失败，磁盘数据不变），
            // 这是此前"迁移了却没保存成功"的原因。首次遇到时询问一次，选择对本次运行内所有 prefab 生效。
            bool changed = false;
            int missingCount = CountMissingScriptsRecursive(root);
            if (missingCount > 0)
            {
                if (!ResolveMissingScriptChoice(path, missingCount))
                {
                    Debug.LogWarning($"[SimpleAnimPlay] 跳过含丢失脚本引用的 Prefab（未迁移）：{path}");
                    continue;
                }
                int removed = RemoveMissingScriptsRecursive(root);
                changed = true;
                Debug.Log($"[SimpleAnimPlay] 已移除 {removed} 个丢失脚本引用：{path}");
            }

            // ---------- 2b) 迁移旧动画数据 ----------
            var comps = root.GetComponentsInChildren<SimpleAnimPlay>(true);
            foreach (var comp in comps)
            {
                if (comp == null) continue;
                if (MigrateOneComponent(comp))
                {
                    migratedComps++;
                    changed = true;
                    Debug.Log($"[SimpleAnimPlay] 已迁移 {comp.keyTimes.Length} 帧：{path} → {comp.name}");
                }
            }
            if (changed)
            {
                touchedPrefabs++;
                EditorUtility.SetDirty(root);
            }
        }
        EditorUtility.ClearProgressBar();
        AssetDatabase.SaveAssets(); // prefab 资产立即落盘；场景修改仍需 Ctrl+S

        // ---------- 汇总 ----------
        if (migratedComps > 0)
        {
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorUtility.DisplayDialog("SimpleAnimPlay 迁移完成",
                $"扫描范围：{folder}\n共迁移 {migratedComps} 个组件" + (touchedPrefabs > 0 ? $"，涉及 {touchedPrefabs} 个 Prefab（已自动保存）。\n\n场景内的修改请 Ctrl+S 保存。" : "。\n\n场景内的修改请 Ctrl+S 保存。"), "好");
        }
        else
        {
            EditorUtility.DisplayDialog("SimpleAnimPlay 迁移完成",
                $"扫描范围：{folder}\n该文件夹与场景内均没有需要迁移的旧数据。", "好");
        }
    }

    // ---------- missing script 处理（保存 Prefab 的前置条件） ----------
    // 询问结果缓存：同一次工具运行中只问一次，选择对所有后续 prefab 生效
    private static bool _missingScriptAsked = false;
    private static bool _missingScriptRemove = false;

    // 首次遇到含 missing script 的 prefab 时弹窗询问；之后沿用已记住的选择。
    // 返回 true = 清理并继续迁移该 prefab；false = 跳过该 prefab。
    private static bool ResolveMissingScriptChoice(string path, int count)
    {
        if (_missingScriptAsked) return _missingScriptRemove;

        _missingScriptAsked = true;
        _missingScriptRemove = EditorUtility.DisplayDialog("发现丢失的脚本引用",
            $"Prefab 里存在丢失的脚本引用（missing script，脚本文件已不在项目中，属历史遗留，与 SimpleAnimPlay 无关）：\n\n{path}（{count} 处）\n\n" +
            "Unity 不允许保存带 missing script 的 Prefab，不清理的话本次迁移将无法写入磁盘。\n\n" +
            "是否自动移除这些无效引用？\n（仅删除已丢失脚本的占位组件，其他正常组件不受影响；本次选择将应用于本次扫描的其余 Prefab）",
            "移除并继续迁移", "跳过这些 Prefab");
        return _missingScriptRemove;
    }

    // 统计整棵子树（含未激活节点）的丢失脚本引用数量
    private static int CountMissingScriptsRecursive(GameObject root)
    {
        int total = 0;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            total += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
        return total;
    }

    // 移除整棵子树（含未激活节点）的丢失脚本引用，返回实际移除数量
    private static int RemoveMissingScriptsRecursive(GameObject root)
    {
        int removed = 0;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            if (count > 0)
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        }
        return removed;
    }

    // 单组件迁移：旧 keyTimes/keyScales → scaleKeys（SerializedObject 方式，场景实例与
    // Prefab 资产实例通用）。返回是否实际发生迁移。
    private static bool MigrateOneComponent(SimpleAnimPlay comp)
    {
        var so = new SerializedObject(comp);
        var keyTimes  = so.FindProperty("keyTimes");
        var keyScales = so.FindProperty("keyScales");
        var scaleKeys = so.FindProperty("scaleKeys");
        if (keyTimes == null || keyScales == null || scaleKeys == null) return false;
        if (keyTimes.arraySize == 0 || keyScales.arraySize == 0) return false;
        if (keyTimes.arraySize != keyScales.arraySize) return false;
        if (scaleKeys.arraySize > 0) return false; // 已迁移或已有新数据

        for (int i = 0; i < keyTimes.arraySize; i++)
        {
            scaleKeys.InsertArrayElementAtIndex(i);
            var elem = scaleKeys.GetArrayElementAtIndex(i);
            elem.FindPropertyRelative("time").floatValue    = keyTimes.GetArrayElementAtIndex(i).floatValue;
            elem.FindPropertyRelative("scale").vector3Value = keyScales.GetArrayElementAtIndex(i).vector3Value;
        }
        // 数据修复性质，不进 Undo 栈；旧 keyTimes/keyScales 原样保留作备份
        so.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    private ReorderableList BuildList(SerializedProperty prop, string header,
        ReorderableList.ElementCallbackDelegate drawRow, float rowH)
    {
        var list = new ReorderableList(serializedObject, prop, true, true, true, true)
        {
            draggable = true,
            displayAdd = true,
            displayRemove = true,
            elementHeight = rowH,
            drawHeaderCallback = rect =>
            {
                EditorGUI.LabelField(rect, $"{header} ({prop.arraySize} 帧)", EditorStyles.boldLabel);
            },
            drawElementBackgroundCallback = (rect, index, active, focused) =>
            {
                // ReorderableList 默认选中高亮，这里加一层统一风格
                if (active) EditorGUI.DrawRect(rect, SelColor);
            },
            drawElementCallback = drawRow,
            onAddCallback = l =>
            {
                Undo.RecordObject(target, $"Add {header}");
                int idx = prop.arraySize;
                prop.InsertArrayElementAtIndex(idx);
                // 新帧默认 time = 上一帧 + 0.5f
                var newElem = prop.GetArrayElementAtIndex(idx);
                if (idx > 0)
                {
                    var prev = prop.GetArrayElementAtIndex(idx - 1);
                    newElem.FindPropertyRelative("time").floatValue =
                        prev.FindPropertyRelative("time").floatValue + 0.5f;
                }
                else
                {
                    newElem.FindPropertyRelative("time").floatValue = 0f;
                }

                // 默认值兜底：scale 默认 (1,1,1) 避免物体消失；alpha 默认 1；旋转默认 0
                var scaleProp = newElem.FindPropertyRelative("scale");
                if (scaleProp != null) scaleProp.vector3Value = Vector3.one;
                var alphaProp = newElem.FindPropertyRelative("alpha");
                if (alphaProp != null) alphaProp.floatValue = 1f;
                var rotProp = newElem.FindPropertyRelative("speedCircles");
                if (rotProp != null) rotProp.floatValue = 1f;
                var velProp = newElem.FindPropertyRelative("velocity");
                if (velProp != null) velProp.vector3Value = Vector3.zero;

                serializedObject.ApplyModifiedProperties();
            },
            onRemoveCallback = l =>
            {
                if (l.index < 0 || l.index >= prop.arraySize) return;
                Undo.RecordObject(target, $"Remove {header}");
                // ReorderableList 默认 onRemoveCallback 不删除，要手动调
                prop.DeleteArrayElementAtIndex(l.index);
                serializedObject.ApplyModifiedProperties();
            },
        };
        return list;
    }

    // ====================== 行绘制 ======================
    // 时间框绘制 + 递增约束：用户填入 ≤ 上一帧时间的值时，自动钳回为「上一帧 + 极小增量」，
    // 保证时间严格单调递增，从源头避免倒退时间导致播放错乱。纯编辑期逻辑，不进运行时、零性能开销。
    // 首帧（index==0）不做下限约束，允许任意时间作为起点。
    private void DrawTimeField(Rect timeRect, SerializedProperty keysProp, int index)
    {
        var timeProp = keysProp.GetArrayElementAtIndex(index).FindPropertyRelative("time");
        float prevTime = index > 0
            ? keysProp.GetArrayElementAtIndex(index - 1).FindPropertyRelative("time").floatValue
            : float.MinValue;
        EditorGUI.BeginChangeCheck();
        EditorGUI.PropertyField(timeRect, timeProp, GUIContent.none);
        if (EditorGUI.EndChangeCheck() && timeProp.floatValue <= prevTime)
        {
            timeProp.floatValue = prevTime + 0.001f;
            Debug.LogWarning($"[SimpleAnimPlay] 关键帧时间已自动修正为递增：第 {index} 帧时间被钳到上一帧 +0.001（时间必须单调递增）");
        }
    }

    private void DrawScaleRow(Rect rect, int index, bool active, bool focused)
    {
        var elem = _pScaleKeys.GetArrayElementAtIndex(index);
        rect.y += PadY;
        rect.height -= PadH;
        rect.x += GripW;
        rect.width -= GripW;

        float lineH = EditorGUIUtility.singleLineHeight;
        float timeW = 70f;
        float gap = 6f;
        float scaleW = rect.width - timeW - gap - 40f; // 留 label 空间

        var labelRect = new Rect(rect.x, rect.y, 24f, lineH);
        EditorGUI.LabelField(labelRect, index.ToString(), EditorStyles.miniLabel);

        // "t" 标签手动绘制，时间框用 GUIContent.none：避免 PropertyField 内嵌标签宽度不受控导致数值框被挤出重叠
        var tLabelRect = new Rect(rect.x + 26f, rect.y, 14f, lineH);
        EditorGUI.LabelField(tLabelRect, "t", EditorStyles.miniLabel);

        var timeRect = new Rect(rect.x + 42f, rect.y, timeW - 14f, lineH);
        var scaleRect = new Rect(rect.x + 28f + timeW + gap, rect.y, scaleW, lineH);

        DrawTimeField(timeRect, _pScaleKeys, index);
        EditorGUI.PropertyField(scaleRect, elem.FindPropertyRelative("scale"), GUIContent.none);
    }

    private void DrawAlphaRow(Rect rect, int index, bool active, bool focused)
    {
        var elem = _pAlphaKeys.GetArrayElementAtIndex(index);
        rect.y += PadY;
        rect.height -= PadH;
        rect.x += GripW;
        rect.width -= GripW;

        float lineH = EditorGUIUtility.singleLineHeight;
        float timeW = 70f;
        float gap = 6f;
        float alphaW = rect.width - timeW - gap - 40f;

        var labelRect = new Rect(rect.x, rect.y, 24f, lineH);
        EditorGUI.LabelField(labelRect, index.ToString(), EditorStyles.miniLabel);

        // "t" 标签手动绘制，时间框用 GUIContent.none：避免 PropertyField 内嵌标签宽度不受控导致数值框被挤出重叠
        var tLabelRect = new Rect(rect.x + 26f, rect.y, 14f, lineH);
        EditorGUI.LabelField(tLabelRect, "t", EditorStyles.miniLabel);

        var timeRect = new Rect(rect.x + 42f, rect.y, timeW - 14f, lineH);
        var alphaRect = new Rect(rect.x + 28f + timeW + gap, rect.y, alphaW, lineH);

        DrawTimeField(timeRect, _pAlphaKeys, index);
        EditorGUI.PropertyField(alphaRect, elem.FindPropertyRelative("alpha"), GUIContent.none);
    }

    private void DrawRotationRow(Rect rect, int index, bool active, bool focused)
    {
        var elem = _pRotationKeys.GetArrayElementAtIndex(index);
        rect.y += PadY;
        rect.height -= PadH;
        rect.x += GripW;
        rect.width -= GripW;

        float lineH = EditorGUIUtility.singleLineHeight;
        float timeW = 70f;
        float gap = 6f;
        float speedW = rect.width - timeW - gap - 40f;

        var labelRect = new Rect(rect.x, rect.y, 24f, lineH);
        EditorGUI.LabelField(labelRect, index.ToString(), EditorStyles.miniLabel);

        var tLabelRect = new Rect(rect.x + 26f, rect.y, 14f, lineH);
        EditorGUI.LabelField(tLabelRect, "t", EditorStyles.miniLabel);

        var timeRect = new Rect(rect.x + 42f, rect.y, timeW - 14f, lineH);
        var spdLabelRect = new Rect(rect.x + 28f + timeW + gap, rect.y, 28f, lineH);
        EditorGUI.LabelField(spdLabelRect, "圈/s", EditorStyles.miniLabel);
        var speedRect = new Rect(rect.x + 28f + timeW + gap + 30f, rect.y, speedW - 30f, lineH);

        DrawTimeField(timeRect, _pRotationKeys, index);
        // 最后一帧的速度无后续段驱动，不会参与播放：置灰并标"end"提示，避免用户误以为生效
        bool isLast = index >= _pRotationKeys.arraySize - 1;
        EditorGUI.BeginDisabledGroup(isLast);
        EditorGUI.PropertyField(speedRect, elem.FindPropertyRelative("speedCircles"), GUIContent.none);
        EditorGUI.EndDisabledGroup();
        if (isLast)
        {
            var endRect = new Rect(speedRect.xMax - 26f, speedRect.y, 26f, lineH);
            EditorGUI.LabelField(endRect, "end", EditorStyles.miniLabel);
        }
    }

    private void DrawPositionRow(Rect rect, int index, bool active, bool focused)
    {
        var elem = _pPositionKeys.GetArrayElementAtIndex(index);
        rect.y += PadY;
        rect.height -= PadH;
        rect.x += GripW;
        rect.width -= GripW;

        float lineH = EditorGUIUtility.singleLineHeight;
        float timeW = 70f;
        float gap = 6f;
        float velW = rect.width - timeW - gap - 50f; // 留 "u/s" 标签空间

        var labelRect = new Rect(rect.x, rect.y, 24f, lineH);
        EditorGUI.LabelField(labelRect, index.ToString(), EditorStyles.miniLabel);

        // "t" 标签手动绘制，时间框用 GUIContent.none
        var tLabelRect = new Rect(rect.x + 26f, rect.y, 14f, lineH);
        EditorGUI.LabelField(tLabelRect, "t", EditorStyles.miniLabel);

        var timeRect = new Rect(rect.x + 42f, rect.y, timeW - 14f, lineH);
        // 单位/秒 标签（标识单位，避免与时间框混淆）
        var velLabelRect = new Rect(rect.x + 28f + timeW + gap, rect.y, 28f, lineH);
        EditorGUI.LabelField(velLabelRect, "u/s", EditorStyles.miniLabel);
        var velRect = new Rect(rect.x + 28f + timeW + gap + 30f, rect.y, velW, lineH);

        DrawTimeField(timeRect, _pPositionKeys, index);
        // 最后一帧的速度无后续段驱动，不会参与播放：置灰并标"end"提示，避免用户误以为生效
        bool isLast = index >= _pPositionKeys.arraySize - 1;
        EditorGUI.BeginDisabledGroup(isLast);
        EditorGUI.PropertyField(velRect, elem.FindPropertyRelative("velocity"), GUIContent.none);
        EditorGUI.EndDisabledGroup();
        if (isLast)
        {
            var endRect = new Rect(velRect.xMax - 26f, velRect.y, 26f, lineH);
            EditorGUI.LabelField(endRect, "end", EditorStyles.miniLabel);
        }
    }
    // 拖拽重排后时间可能不再递增：按 time 升序整体重排四条轨道，保证时间严格单调递增。
    // 仅在确实失序时才写入（避免每帧无谓的 serializedObject 改动与 Undo 噪声）。纯编辑期逻辑。
    private void EnsureTimeSorted(SerializedProperty keysProp)
    {
        int n = keysProp.arraySize;
        if (n < 2) return;
        bool unsorted = false;
        for (int i = 1; i < n; i++)
        {
            float prev = keysProp.GetArrayElementAtIndex(i - 1).FindPropertyRelative("time").floatValue;
            float cur  = keysProp.GetArrayElementAtIndex(i).FindPropertyRelative("time").floatValue;
            if (cur < prev) { unsorted = true; break; }
        }
        if (!unsorted) return;

        // 收集 (time, index) 后按 time 排序，再用 InsertArrayElementAtIndex 重建顺序
        var order = new List<int>();
        for (int i = 0; i < n; i++) order.Add(i);
        order.Sort((a, b) =>
            keysProp.GetArrayElementAtIndex(a).FindPropertyRelative("time").floatValue.CompareTo(
            keysProp.GetArrayElementAtIndex(b).FindPropertyRelative("time").floatValue));

        for (int i = 0; i < n; i++)
        {
            int src = order[i];
            var srcElem = keysProp.GetArrayElementAtIndex(src);
            var dstElem = keysProp.GetArrayElementAtIndex(i);
            dstElem.FindPropertyRelative("time").floatValue = srcElem.FindPropertyRelative("time").floatValue;
            // 其余字段按类型复制
            var t = srcElem.FindPropertyRelative("scale");     if (t != null) dstElem.FindPropertyRelative("scale").vector3Value = t.vector3Value;
            t = srcElem.FindPropertyRelative("alpha");          if (t != null) dstElem.FindPropertyRelative("alpha").floatValue = t.floatValue;
            t = srcElem.FindPropertyRelative("speedCircles");   if (t != null) dstElem.FindPropertyRelative("speedCircles").floatValue = t.floatValue;
            t = srcElem.FindPropertyRelative("velocity");       if (t != null) dstElem.FindPropertyRelative("velocity").vector3Value = t.vector3Value;
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // 旧版 keyTimes/keyScales 自动继承（scaleKeys 为空且旧数据存在时，面板首帧即完成，无需用户操作）
        TryAutoMigrateLegacy();

        // 拖拽重排可能破坏时间单调性，统一按 time 升序规整（不影响数值，只排顺序）
        EnsureTimeSorted(_pScaleKeys);
        EnsureTimeSorted(_pAlphaKeys);
        EnsureTimeSorted(_pRotationKeys);
        EnsureTimeSorted(_pPositionKeys);

        // ---------- 目标节点（缩放/旋转共用；老版本指定的子节点在此可见/可改/可清除） ----------
        EditorGUILayout.PropertyField(_pTargetNode, new GUIContent("目标节点"));
        EditorGUILayout.LabelField("不指定则默认控制自身节点（缩放/旋转共用，透明度不受影响）", EditorStyles.miniLabel);

        EditorGUILayout.Space(4);

        // ---------- 四个 enable 开关（未勾选的轨道隐藏） ----------
        EditorGUILayout.BeginHorizontal();
        _pEnableScale.boolValue    = EditorGUILayout.ToggleLeft("启用缩放",    _pEnableScale.boolValue,    GUILayout.Width(80));
        _pEnableRotation.boolValue = EditorGUILayout.ToggleLeft("启用旋转",    _pEnableRotation.boolValue, GUILayout.Width(80));
        _pEnablePosition.boolValue = EditorGUILayout.ToggleLeft("启用位移",    _pEnablePosition.boolValue, GUILayout.Width(80));
        _pEnableAlpha.boolValue    = EditorGUILayout.ToggleLeft("启用透明度",  _pEnableAlpha.boolValue,    GUILayout.Width(90));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);

        // ---------- 四条轨道（未启用的直接隐藏） ----------
        if (_pEnableScale.boolValue)    DrawTrack("缩放轨道",   _scaleList,    ref _scaleFold);
        if (_pEnableRotation.boolValue)
        {
            // 旋转轨道共用一根旋转轴（速度语义下整条轨道绕同一轴持续旋转）
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("旋转轴");
            _pRotationAxis.enumValueIndex = (int)(SimpleAnimPlay.RotationAxis)EditorGUILayout.EnumPopup(
                (SimpleAnimPlay.RotationAxis)_pRotationAxis.enumValueIndex, GUILayout.Width(60));
            EditorGUILayout.LabelField("（整条轨道共用）", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            DrawTrack("旋转轨道", _rotationList, ref _rotationFold);
        }
        if (_pEnablePosition.boolValue) DrawTrack("位移轨道", _positionList, ref _positionFold);
        if (_pEnableAlpha.boolValue)    DrawTrack("透明度轨道", _alphaList,    ref _alphaFold);

        serializedObject.ApplyModifiedProperties();
    }

    // 透明背景 + 内容宽度折叠样式：去掉 foldoutHeader 的深色背景，按钮只占文字宽度
    private static GUIStyle _minimalFoldout;
    private static GUIStyle MinimalFoldout =>
        _minimalFoldout ??= new GUIStyle(EditorStyles.foldout)
        {
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(14, 6, 4, 4),   // 保留三角箭头空间
            margin  = new RectOffset(0, 0, 0, 0),
            fontStyle = FontStyle.Bold,
        };

    private void DrawTrack(string title, ReorderableList list, ref bool fold)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        // 折叠按钮：透明背景（融入面板），只占内容宽度，水平居中
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        fold = EditorGUILayout.Foldout(fold, title, true, MinimalFoldout);
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        if (fold)
        {
            var r = GUILayoutUtility.GetRect(0f, list.GetHeight(), GUILayout.ExpandWidth(true));
            list.DoList(r);
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(4);
    }
}
