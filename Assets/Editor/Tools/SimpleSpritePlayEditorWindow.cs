using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

public class SimpleSpritePlayEditorWindow : EditorWindow
{
    private SimpleSpritePlay _target;
    private SerializedObject _serializedObj;
    private ReorderableList _reorderableList;
    private SerializedProperty _framesProp;

    private Vector2 _scrollPos;
    private Texture2D _previewBgTex;

    // 预览播放
    private bool _isPreviewPlaying = false;
    private float _previewTimer = 0f;
    private int _previewIndex = 0;
    private double _lastEditorTime = 0f;

    // 选中状态
    private int _selectedIndex = -1;
    private List<int> _selectedIndices = new List<int>();

    // 自动跟随选中
    private bool _autoFollowSelection = true;

    // === 统一视觉风格常量 ===
    // 颜色
    private static readonly Color SelColor = new Color(0.3f, 0.5f, 0.85f, 0.35f);
    private static readonly Color DragColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);

    // 行高
    private const float RowHeightN = 64f;
    private const float RowHeightC = 24f;

    // 拖动标识（仅合并模式）
    private const float GripW = 16f;

    // 内容内边距（非紧凑）
    private const float PadY = 2f;
    private const float PadH = 4f;

    // 序号
    private const float IndexX = 4f;
    private const float IndexWN = 28f;
    private const float IndexWC = 20f;

    // 缩略图（仅非紧凑）
    private const float ThumbSize = 56f;
    private const float ThumbX = 36f;

    // Sprite字段起始位置（从内容原点算）
    private const float SpriteXN = 100f; // 非紧凑：序号+缩略图后
    private const float SpriteXC = 24f;  // 紧凑：序号后

    // ×count（仅合并模式）
    private const float CountLblW = 16f;
    private const float CountWN = 40f;
    private const float CountWC = 30f;

    // 按钮
    private const float BtnGap = 4f;
    private const float BtnWN = 50f;
    private const float BtnWC = 28f;
    private const float BtnHN = 22f;

    // 显示模式：true=紧凑，false=正常
    private bool _compactMode = false;

    // 合并连续重复帧
    private bool _enableGrouping = true;
    private List<FrameGroup> _groups = new List<FrameGroup>();
    private bool _groupsDirty = true;
    private int _selectedGroupIndex = -1;
    private List<int> _selectedGroupIndices = new List<int>();

    // 列表/预览分隔条可拖动
    private float _previewHeight = 256f;
    private bool _previewHeightInitialized = false;
    private bool _isResizingSplitter = false;
    private const float SplitterHeight = 6f;
    private const float MinPreviewHeight = 80f;

    // 预览区自适应（缓存最大帧尺寸）
    private Vector2 _cachedMaxSpriteSize = Vector2.zero;

    // 拖拽排序状态跟踪
    private bool _isDraggingInList = false;
    private bool _pendingClickSelect = false; // 延迟选择：点击已选中项时等待确认是点击还是拖拽
    private List<int> _preDragSelectedIndices = new List<int>(); // 拖拽前选中的帧索引（用于非合并模式多选拖拽）

    // 非合并模式自定义拖拽排序
    private int _dragFrameIndex = -1;
    private bool _isFrameDragging = false;
    private float _dragFrameStartY = 0f;
    private int _dragFrameInsertIndex = -1;
    private List<Rect> _frameRowRects = new List<Rect>();

    // 分组拖拽排序
    private int _dragGroupIndex = -1;
    private bool _isGroupDragging = false;
    private float _dragStartY = 0f;
    private int _dragInsertIndex = -1;
    private List<Rect> _groupRowRects = new List<Rect>();
    private const float DragThreshold = 5f;

    private class FrameGroup
    {
        public Sprite sprite;
        public int count;
        public int startIndex;
    }

    [MenuItem("Tools/美术工具/SimpleSpritePlay编辑")]
    private static void OpenWindow()
    {
        var window = GetWindow<SimpleSpritePlayEditorWindow>("SimpleSpritePlay Editor");
        window.minSize = new Vector2(520, 480);
        window.Show();
    }

    private void OnEnable()
    {
        // 创建预览背景纹理（棋盘格）
        _previewBgTex = MakeCheckerTexture(8, 8);

        // 自动绑定当前选中
        if (_autoFollowSelection)
        {
            TryBindTarget(Selection.activeGameObject);
        }

        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    private void OnSelectionChange()
    {
        if (!_autoFollowSelection) return;
        TryBindTarget(Selection.activeGameObject);
        Repaint();
    }

    private void TryBindTarget(GameObject go)
    {
        if (go == null) return;
        var comp = go.GetComponent<SimpleSpritePlay>();
        if (comp == null) return;
        if (_target == comp) return;
        _target = comp;
        _serializedObj = new SerializedObject(_target);
        _framesProp = _serializedObj.FindProperty("spriteFrames");
        _selectedIndex = -1;
        _selectedIndices.Clear();
        _selectedGroupIndex = -1;
        _selectedGroupIndices.Clear();
        _groupsDirty = true;
        _previewHeightInitialized = false;
        RebuildList();
    }

    private void RebuildGroups()
    {
        _groups.Clear();
        if (_framesProp == null) { _groupsDirty = false; return; }

        for (int i = 0; i < _framesProp.arraySize; i++)
        {
            var sprite = _framesProp.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
            if (_groups.Count > 0)
            {
                var last = _groups[_groups.Count - 1];
                if ((last.sprite == null && sprite == null) ||
                    (last.sprite != null && last.sprite == sprite))
                {
                    last.count++;
                    continue;
                }
            }
            _groups.Add(new FrameGroup { sprite = sprite, count = 1, startIndex = i });
        }
        _groupsDirty = false;
    }

    private void RebuildList()
    {
        if (_framesProp == null) return;

        _reorderableList = new ReorderableList(_serializedObj, _framesProp, true, true, false, false)
        {
            draggable = true,
            displayAdd = false,
            displayRemove = false,
            multiSelect = false,
            elementHeightCallback = index => _compactMode ? RowHeightC : RowHeightN,
            drawHeaderCallback = rect =>
            {
                EditorGUI.LabelField(rect, $"帧列表 (共 {_framesProp.arraySize} 帧)   Ctrl+点击=多选   Shift+点击=范围选");
            },
            drawElementCallback = DrawListElement,
            drawElementBackgroundCallback = DrawElementBackground,
            onReorderCallbackWithDetails = (list, oldIndex, newIndex) =>
            {
                // 多选拖拽：如果拖拽前有多个选中项且拖拽的元素在选中列表中
                if (_preDragSelectedIndices.Count > 1 && _preDragSelectedIndices.Contains(oldIndex))
                {
                    // 撤销 ReorderableList 的单元素移动
                    _framesProp.MoveArrayElement(newIndex, oldIndex);
                    // 执行多元素移动
                    MoveMultipleFrames(_preDragSelectedIndices, oldIndex, newIndex);
                }
                else
                {
                    _selectedIndex = newIndex;
                    _selectedIndices.Clear();
                    _selectedIndices.Add(newIndex);
                    MarkDirty();
                }
            },
            onSelectCallback = list =>
            {
                // 拖拽进行中（鼠标按住并开始移动）时不修改选择状态
                if (_isDraggingInList) return;

                int newIndex = list.index;
                var evt = Event.current;
                bool ctrl = evt != null && (evt.control || evt.command);
                bool shift = evt != null && evt.shift;
                bool noModifier = !ctrl && !shift;

                // 点击已选中项且当前多选：延迟选择，等确认是点击还是拖拽（保留多选高亮用于整体拖拽）
                if (noModifier && _selectedIndices.Count > 1 && _selectedIndices.Contains(newIndex))
                {
                    _pendingClickSelect = true;
                    _selectedIndex = newIndex;
                    return;
                }
                _pendingClickSelect = false;

                if (ctrl)
                {
                    if (_selectedIndices.Contains(newIndex))
                        _selectedIndices.Remove(newIndex);
                    else
                        _selectedIndices.Add(newIndex);
                    _selectedIndices.Sort();
                }
                else if (shift && _selectedIndices.Count > 0)
                {
                    int anchor = _selectedIndices[0];
                    _selectedIndices.Clear();
                    int min = Mathf.Min(anchor, newIndex);
                    int max = Mathf.Max(anchor, newIndex);
                    for (int i = min; i <= max; i++) _selectedIndices.Add(i);
                }
                else
                {
                    _selectedIndices.Clear();
                    _selectedIndices.Add(newIndex);
                }
                _selectedIndex = newIndex;
            }
        };
    }

    private void DrawElementBackground(Rect rect, int index, bool isActive, bool isFocused)
    {
        if (_selectedIndices.Contains(index))
            EditorGUI.DrawRect(rect, SelColor);
    }

    private void DrawListElement(Rect rect, int index, bool isActive, bool isFocused)
    {
        if (_framesProp == null) return;
        var element = _framesProp.GetArrayElementAtIndex(index);
        var sprite = element.objectReferenceValue as Sprite;

        if (_compactMode)
        {
            DrawListElementCompact(rect, index, element, sprite);
        }
        else
        {
            DrawListElementNormal(rect, index, element, sprite);
        }
    }

    private void DrawListElementNormal(Rect rect, int index, SerializedProperty element, Sprite sprite)
    {
        // 非紧凑：内容内边距
        rect.y += PadY;
        rect.height -= PadH;

        float lineH = EditorGUIUtility.singleLineHeight;
        float cy = rect.y + 18; // 控件行Y

        // 序号
        var indexRect = new Rect(rect.x + IndexX, cy, IndexWN, lineH);
        EditorGUI.LabelField(indexRect, index.ToString(), EditorStyles.boldLabel);

        // 缩略图
        var thumbRect = new Rect(rect.x + ThumbX, rect.y + 2, ThumbSize, ThumbSize);
        DrawSpriteThumbnail(thumbRect, sprite);

        // 按钮（右对齐）
        float btnsW = BtnWN * 3 + BtnGap * 2;
        float btnX = rect.x + rect.width - btnsW;

        // Sprite 字段（缩短到2/3）
        float objWidth = (rect.width - SpriteXN - btnsW) * 2f / 3f;
        var objRect = new Rect(rect.x + SpriteXN, cy, objWidth, lineH);
        EditorGUI.BeginChangeCheck();
        EditorGUI.PropertyField(objRect, element, GUIContent.none);
        if (EditorGUI.EndChangeCheck())
            MarkDirty();

        if (GUI.Button(new Rect(btnX, cy, BtnWN, BtnHN), "复制"))
            DuplicateFrame(index);
        if (GUI.Button(new Rect(btnX + BtnWN + BtnGap, cy, BtnWN, BtnHN), "插入"))
            InsertEmptyFrame(index + 1);
        if (GUI.Button(new Rect(btnX + (BtnWN + BtnGap) * 2, cy, BtnWN, BtnHN), "删除"))
            DeleteFrame(index);
    }

    private void DrawListElementCompact(Rect rect, int index, SerializedProperty element, Sprite sprite)
    {
        // 紧凑：不显示缩略图，控件居中
        float h = rect.height;
        float lineH = EditorGUIUtility.singleLineHeight;
        float y = rect.y + (h - lineH) * 0.5f;

        // 序号
        var indexRect = new Rect(rect.x + IndexX, y, IndexWC, lineH);
        EditorGUI.LabelField(indexRect, index.ToString(), EditorStyles.miniLabel);

        // 按钮（右对齐）
        float btnsW = BtnWC * 3 + BtnGap * 2;
        float btnX = rect.x + rect.width - btnsW;

        // Sprite 字段（缩短到2/3）
        float objWidth = (rect.width - SpriteXC - btnsW) * 2f / 3f;
        var objRect = new Rect(rect.x + SpriteXC, y, objWidth, lineH);
        EditorGUI.BeginChangeCheck();
        EditorGUI.PropertyField(objRect, element, GUIContent.none);
        if (EditorGUI.EndChangeCheck())
            MarkDirty();

        if (GUI.Button(new Rect(btnX, y, BtnWC, lineH), "复"))
            DuplicateFrame(index);
        if (GUI.Button(new Rect(btnX + BtnWC + BtnGap, y, BtnWC, lineH), "插"))
            InsertEmptyFrame(index + 1);
        if (GUI.Button(new Rect(btnX + (BtnWC + BtnGap) * 2, y, BtnWC, lineH), "删"))
            DeleteFrame(index);
    }

    private void DrawSpriteThumbnail(Rect rect, Sprite sprite)
    {
        // 背景
        GUI.DrawTexture(rect, _previewBgTex, ScaleMode.StretchToFill);

        if (sprite == null)
        {
            var style = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10
            };
            EditorGUI.LabelField(rect, "空", style);
            return;
        }

        Texture2D tex = AssetPreview.GetAssetPreview(sprite);
        if (tex == null)
        {
            // 预览未生成时，先显示加载提示，并请求重绘
            EditorGUI.LabelField(rect, "加载中...");
            Repaint();
            return;
        }

        // 根据精灵实际可见区域裁剪绘制（保留长宽比）
        DrawSpriteInRect(rect, sprite, tex);
    }

    private void DrawSpriteInRect(Rect rect, Sprite sprite, Texture2D tex)
    {
        if (tex == null || tex.width <= 0 || tex.height <= 0) return;

        // 按 rect 的实际宽高比计算最大可容纳尺寸，保证不超出 rect 边界
        float aspect = (float)tex.width / tex.height;
        float rectAspect = rect.width / Mathf.Max(rect.height, 1f);

        float drawW, drawH;
        if (rectAspect > aspect)
        {
            // rect 更宽：按高度匹配
            drawH = rect.height;
            drawW = drawH * aspect;
        }
        else
        {
            // rect 更窄：按宽度匹配
            drawW = rect.width;
            drawH = drawW / aspect;
        }

        var drawRect = new Rect(
            rect.x + (rect.width - drawW) * 0.5f,
            rect.y + (rect.height - drawH) * 0.5f,
            drawW, drawH);
        GUI.DrawTexture(drawRect, tex, ScaleMode.ScaleToFit);
    }

    private void OnGUI()
    {
        if (_previewBgTex == null)
            _previewBgTex = MakeCheckerTexture(8, 8);

        DrawTargetField();
        if (_target == null)
        {
            DrawHintArea();
            return;
        }

        if (_serializedObj == null)
        {
            _serializedObj = new SerializedObject(_target);
            _framesProp = _serializedObj.FindProperty("spriteFrames");
            RebuildList();
        }

        _serializedObj.Update();
        if (_groupsDirty) RebuildGroups();

        DrawToolbar();
        DrawFrameListArea();
        DrawPreviewArea();

        _serializedObj.ApplyModifiedProperties();
        if (_groupsDirty) RebuildGroups();
    }

    private void DrawTargetField()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("目标物体（拖入挂载了 SimpleSpritePlay 的 GameObject）", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        var newTarget = (GameObject)EditorGUILayout.ObjectField(
            _target != null ? _target.gameObject : null,
            typeof(GameObject), true);

        _autoFollowSelection = EditorGUILayout.ToggleLeft("自动跟随选中", _autoFollowSelection, GUILayout.Width(120));

        EditorGUILayout.EndHorizontal();

        if (newTarget != null && (_target == null || newTarget != _target.gameObject))
        {
            var comp = newTarget.GetComponent<SimpleSpritePlay>();
            if (comp != null)
            {
                _target = comp;
                _serializedObj = new SerializedObject(_target);
                _framesProp = _serializedObj.FindProperty("spriteFrames");
                _selectedIndex = -1;
                _selectedIndices.Clear();
                _selectedGroupIndex = -1;
                _selectedGroupIndices.Clear();
                _groupsDirty = true;
                RebuildList();
            }
            else
            {
                EditorGUILayout.HelpBox("该物体没有挂载 SimpleSpritePlay 组件", MessageType.Warning);
            }
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawHintArea()
    {
        EditorGUILayout.HelpBox(
            "请选择一个挂载了 SimpleSpritePlay 组件的 GameObject，或从 Hierarchy 拖入上方 ObjectField。",
            MessageType.Info);
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("播放设置", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        var newFps = EditorGUILayout.FloatField("FPS", _target.fps, GUILayout.Width(180));
        var newLoop = EditorGUILayout.ToggleLeft("循环", _target.isLoop, GUILayout.Width(80));
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_target, "Change Play Settings");
            _target.fps = Mathf.Max(1, newFps);
            _target.isLoop = newLoop;
            EditorUtility.SetDirty(_target);
        }

        GUILayout.FlexibleSpace();

        if (GUILayout.Button(_isPreviewPlaying ? "暂停" : "播放", GUILayout.Width(60)))
        {
            _isPreviewPlaying = !_isPreviewPlaying;
            _previewTimer = 0f;
            _previewIndex = 0;
        }
        if (GUILayout.Button("重置", GUILayout.Width(60)))
        {
            _previewIndex = 0;
            _previewTimer = 0f;
            _isPreviewPlaying = false;
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        GUILayout.Space(4);

        // 工具按钮区
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        if (GUILayout.Button("批量导入 Sprite", GUILayout.Width(120)))
        {
            BatchImportSprites();
        }
        if (GUILayout.Button("清空所有帧", GUILayout.Width(100)))
        {
            ClearAllFrames();
        }
        GUILayout.Space(8);
        EditorGUI.BeginDisabledGroup(_selectedIndices.Count == 0);
        if (GUILayout.Button("↑ 上移", GUILayout.Width(60)))
        {
            MoveSelectedFramesUp();
        }
        if (GUILayout.Button("↓ 下移", GUILayout.Width(60)))
        {
            MoveSelectedFramesDown();
        }
        if (GUILayout.Button("批量复制", GUILayout.Width(75)))
        {
            DuplicateSelectedFrames();
        }
        if (GUILayout.Button("批量删除", GUILayout.Width(75)))
        {
            DeleteSelectedFrames();
        }
        EditorGUI.EndDisabledGroup();
        GUILayout.FlexibleSpace();
        EditorGUI.BeginChangeCheck();
        var newGrouping = EditorGUILayout.ToggleLeft("合并重复", _enableGrouping, GUILayout.Width(80));
        if (EditorGUI.EndChangeCheck())
        {
            _enableGrouping = newGrouping;
            _groupsDirty = true;
            _selectedGroupIndex = -1;
            _selectedGroupIndices.Clear();
        }
        EditorGUI.BeginChangeCheck();
        var newCompact = EditorGUILayout.ToggleLeft("紧凑显示", _compactMode, GUILayout.Width(80));
        if (EditorGUI.EndChangeCheck())
        {
            _compactMode = newCompact;
        }
        EditorGUILayout.LabelField($"已选 {_selectedIndices.Count} 帧", GUILayout.Width(80));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawFrameListArea()
    {
        if (_enableGrouping)
            DrawGroupedListArea();
        else
            DrawUngroupedListArea();
    }

    private void DrawUngroupedListArea()
    {
        if (_framesProp == null) return;

        // 表头
        var headerRect = GUILayoutUtility.GetRect(0, EditorGUIUtility.singleLineHeight + 4, GUILayout.ExpandWidth(true));
        var headerStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
        EditorGUI.LabelField(headerRect,
            $"帧列表 (共 {_framesProp.arraySize} 帧)   点击=选择  拖拽=排序  Ctrl+多选  Shift+范围选", headerStyle);

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));

        _frameRowRects.Clear();
        float rowHeight = _compactMode ? RowHeightC : RowHeightN;
        for (int i = 0; i < _framesProp.arraySize; i++)
        {
            var rect = GUILayoutUtility.GetRect(0, rowHeight, GUILayout.ExpandWidth(true));
            DrawFrameElement(rect, i);
        }

        var dropRect = GUILayoutUtility.GetRect(0, 60, GUILayout.ExpandWidth(true));
        DrawDragDropArea(dropRect);

        // 拖拽事件处理（在所有元素绘制后）
        HandleFrameDragEvents();

        // 拖拽插入线
        if (_isFrameDragging && _dragFrameInsertIndex >= 0)
            DrawFrameInsertionLine();

        EditorGUILayout.EndScrollView();
        DrawSplitter();
    }

    private void DrawFrameElement(Rect rect, int index)
    {
        var evt = Event.current;
        _frameRowRects.Add(rect);

        // 高亮选中项；拖拽中选中的项叠加 DragColor
        if (_selectedIndices.Contains(index))
            EditorGUI.DrawRect(rect, SelColor);
        if (_isFrameDragging && _selectedIndices.Contains(index))
            EditorGUI.DrawRect(rect, DragColor);

        if (_framesProp == null) return;
        var element = _framesProp.GetArrayElementAtIndex(index);
        var sprite = element.objectReferenceValue as Sprite;

        if (_compactMode)
            DrawListElementCompact(rect, index, element, sprite);
        else
            DrawListElementNormal(rect, index, element, sprite);

        HandleFrameMouseEvents(rect, index, evt);
    }

    private void HandleFrameMouseEvents(Rect rect, int index, Event evt)
    {
        // 拖拽启动：仅处理 MouseDown，MouseUp 统一在 HandleFrameDragEvents 中处理
        if (evt != null && evt.type == EventType.MouseDown && evt.button == 0
            && rect.Contains(evt.mousePosition) && GUIUtility.hotControl == 0)
        {
            _dragFrameIndex = index;
            _dragFrameStartY = evt.mousePosition.y;
            bool noModifier = !evt.control && !evt.command && !evt.shift;
            if (noModifier && _selectedIndices.Count > 1 && _selectedIndices.Contains(index))
                _pendingClickSelect = true;
            else
                _pendingClickSelect = false;
            evt.Use();
        }
    }

    private void HandleFrameDragEvents()
    {
        var evt = Event.current;
        if (evt == null) return;

        if (evt.type == EventType.MouseDrag && _dragFrameIndex >= 0)
        {
            if (!_isFrameDragging && Mathf.Abs(evt.mousePosition.y - _dragFrameStartY) > DragThreshold)
            {
                _isFrameDragging = true;
                _pendingClickSelect = false; // 取消延迟选择，保留多选用于整体拖拽
                // 如果拖拽的帧不在选中列表中，则只选它
                if (!_selectedIndices.Contains(_dragFrameIndex))
                {
                    _selectedIndices.Clear();
                    _selectedIndices.Add(_dragFrameIndex);
                    _selectedIndex = _dragFrameIndex;
                }
            }
            _dragFrameInsertIndex = CalculateFrameInsertIndex(evt.mousePosition.y);
            evt.Use();
        }
        else if (evt.type == EventType.MouseUp && _dragFrameIndex >= 0)
        {
            if (_isFrameDragging)
            {
                // 拖拽完成：执行移动
                if (_selectedIndices.Count > 1)
                {
                    MoveMultipleFramesTo(_dragFrameInsertIndex);
                }
                else
                {
                    int target = _dragFrameInsertIndex;
                    if (target > _dragFrameIndex) target--;
                    if (target >= 0 && target < _framesProp.arraySize && target != _dragFrameIndex)
                        MoveSingleFrame(_dragFrameIndex, target);
                }
            }
            else
            {
                // 点击未拖拽：在此统一处理选择
                bool ctrl = evt.control || evt.command;
                bool shift = evt.shift;
                if (_pendingClickSelect)
                    HandleFrameSelection(_dragFrameIndex, false, false);
                else
                    HandleFrameSelection(_dragFrameIndex, ctrl, shift);
            }
            _dragFrameIndex = -1;
            _isFrameDragging = false;
            _dragFrameInsertIndex = -1;
            _pendingClickSelect = false;
            evt.Use();
        }
    }

    private void HandleFrameSelection(int index, bool ctrl, bool shift)
    {
        if (ctrl)
        {
            if (_selectedIndices.Contains(index))
                _selectedIndices.Remove(index);
            else
                _selectedIndices.Add(index);
            _selectedIndices.Sort();
        }
        else if (shift && _selectedIndices.Count > 0)
        {
            int anchor = _selectedIndices[0];
            _selectedIndices.Clear();
            int min = Mathf.Min(anchor, index);
            int max = Mathf.Max(anchor, index);
            for (int i = min; i <= max; i++) _selectedIndices.Add(i);
        }
        else
        {
            _selectedIndices.Clear();
            _selectedIndices.Add(index);
        }
        _selectedIndices.Sort();
        _selectedIndex = _selectedIndices.Count > 0 ? _selectedIndices[0] : -1;
    }

    private int CalculateFrameInsertIndex(float mouseY)
    {
        for (int i = 0; i < _frameRowRects.Count; i++)
        {
            var r = _frameRowRects[i];
            if (mouseY < r.y + r.height * 0.5f)
                return i;
        }
        return _frameRowRects.Count;
    }

    private void DrawFrameInsertionLine()
    {
        if (_dragFrameInsertIndex < 0 || _dragFrameInsertIndex > _frameRowRects.Count) return;

        float y;
        float x;
        float w;
        if (_dragFrameInsertIndex < _frameRowRects.Count)
        {
            var r = _frameRowRects[_dragFrameInsertIndex];
            y = r.y - 1;
            x = r.x;
            w = r.width;
        }
        else if (_frameRowRects.Count > 0)
        {
            var r = _frameRowRects[_frameRowRects.Count - 1];
            y = r.y + r.height - 1;
            x = r.x;
            w = r.width;
        }
        else return;

        EditorGUI.DrawRect(new Rect(x, y, w, 2f), new Color(0.4f, 0.7f, 1f, 0.9f));
    }

    private void DrawGroupedListArea()
    {
        if (_groupsDirty) RebuildGroups();

        // 表头
        var headerRect = GUILayoutUtility.GetRect(0, EditorGUIUtility.singleLineHeight + 4, GUILayout.ExpandWidth(true));
        var headerStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
        int totalFrames = _framesProp != null ? _framesProp.arraySize : 0;
        EditorGUI.LabelField(headerRect,
            $"帧列表 (共 {totalFrames} 帧, {_groups.Count} 组)   点击=选择  拖拽=排序  Ctrl+多选  Shift+范围选", headerStyle);

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));

        _groupRowRects.Clear();
        float rowHeight = _compactMode ? RowHeightC : RowHeightN;
        for (int i = 0; i < _groups.Count; i++)
        {
            var rect = GUILayoutUtility.GetRect(0, rowHeight, GUILayout.ExpandWidth(true));
            if (_compactMode)
                DrawGroupElementCompact(rect, i);
            else
                DrawGroupElementNormal(rect, i);
        }

        var dropRect = GUILayoutUtility.GetRect(0, 60, GUILayout.ExpandWidth(true));
        DrawDragDropArea(dropRect);

        // 拖拽事件处理（在所有元素绘制后）
        HandleGroupDragEvents();

        // 拖拽插入线
        if (_isGroupDragging && _dragInsertIndex >= 0)
            DrawDragInsertionLine();

        EditorGUILayout.EndScrollView();
        DrawSplitter();
    }

    private void HandleGroupDragEvents()
    {
        var evt = Event.current;
        if (evt == null) return;

        if (evt.type == EventType.MouseDrag && _dragGroupIndex >= 0)
        {
            if (!_isGroupDragging && Mathf.Abs(evt.mousePosition.y - _dragStartY) > DragThreshold)
            {
                _isGroupDragging = true;
                _pendingClickSelect = false; // 取消延迟选择，保留多选用于整体拖拽
                // 如果拖拽的组不在选中列表中，则只选它
                if (!_selectedGroupIndices.Contains(_dragGroupIndex))
                {
                    _selectedGroupIndices.Clear();
                    _selectedGroupIndices.Add(_dragGroupIndex);
                    RebuildSelectedIndicesFromGroups();
                }
            }
            // 始终消费 MouseDrag（只要有待定拖拽），防止 ScrollView 干扰
            _dragInsertIndex = CalculateInsertIndex(evt.mousePosition.y);
            evt.Use();
        }
        else if (evt.type == EventType.MouseUp && _dragGroupIndex >= 0)
        {
            if (_isGroupDragging)
            {
                // 拖拽完成：执行移动
                if (_selectedGroupIndices.Count > 1)
                {
                    // 多选拖拽：整体移动
                    MoveMultipleGroups(_dragInsertIndex);
                }
                else
                {
                    // 单选拖拽
                    int target = _dragInsertIndex;
                    if (target > _dragGroupIndex) target--;
                    if (target >= 0 && target < _groups.Count && target != _dragGroupIndex)
                        MoveGroup(_dragGroupIndex, target);
                }
            }
            else
            {
                // 点击未拖拽：在此统一处理选择（而非在逐行的 HandleGroupMouseEvents 中）
                bool ctrl = evt.control || evt.command;
                bool shift = evt.shift;
                if (_pendingClickSelect)
                {
                    // 点击已选中项：确认是点击而非拖拽，重置为单选
                    HandleGroupSelection(_dragGroupIndex, false, false);
                }
                else
                {
                    HandleGroupSelection(_dragGroupIndex, ctrl, shift);
                }
            }
            // 统一重置所有拖拽状态
            _dragGroupIndex = -1;
            _isGroupDragging = false;
            _dragInsertIndex = -1;
            _pendingClickSelect = false;
            evt.Use();
        }
    }

    private int CalculateInsertIndex(float mouseY)
    {
        for (int i = 0; i < _groupRowRects.Count; i++)
        {
            var r = _groupRowRects[i];
            if (mouseY < r.y + r.height * 0.5f)
                return i;
        }
        return _groupRowRects.Count;
    }

    private void DrawDragInsertionLine()
    {
        if (_dragInsertIndex < 0 || _dragInsertIndex > _groupRowRects.Count) return;

        float y;
        float x;
        float w;
        if (_dragInsertIndex < _groupRowRects.Count)
        {
            var r = _groupRowRects[_dragInsertIndex];
            y = r.y - 1;
            x = r.x;
            w = r.width;
        }
        else if (_groupRowRects.Count > 0)
        {
            var r = _groupRowRects[_groupRowRects.Count - 1];
            y = r.y + r.height - 1;
            x = r.x;
            w = r.width;
        }
        else return;

        var lineRect = new Rect(x, y, w, 2f);
        EditorGUI.DrawRect(lineRect, new Color(0.4f, 0.7f, 1f, 0.9f));
    }

    private void DrawGroupElementNormal(Rect rect, int groupIndex)
    {
        var group = _groups[groupIndex];
        var evt = Event.current;
        _groupRowRects.Add(rect);

        float lineH = EditorGUIUtility.singleLineHeight;

        // 高亮
        if (_selectedGroupIndices.Contains(groupIndex))
            EditorGUI.DrawRect(rect, SelColor);
        if (_isGroupDragging && _dragGroupIndex == groupIndex)
            EditorGUI.DrawRect(rect, DragColor);

        // 拖动标识
        var gripRect = new Rect(rect.x, rect.y, GripW, rect.height);
        EditorGUI.LabelField(gripRect, "≡", new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter });

        // 内容原点（grip后）
        float cx = rect.x + GripW;
        float cw = rect.width - GripW;
        float cy = rect.y + PadY + 18;
        float thumbY = rect.y + PadY + 2;

        // 序号
        var indexRect = new Rect(cx + IndexX, cy, IndexWN, lineH);
        EditorGUI.LabelField(indexRect, group.startIndex.ToString(), EditorStyles.boldLabel);

        // 缩略图
        var thumbRect = new Rect(cx + ThumbX, thumbY, ThumbSize, ThumbSize);
        DrawSpriteThumbnail(thumbRect, group.sprite);

        // 按钮（右对齐）
        float btnsW = BtnWN * 3 + BtnGap * 2;
        float btnX = cx + cw - btnsW;

        // Sprite 字段（缩短到2/3）
        float objWidth = (cw - SpriteXN - btnsW) * 2f / 3f;
        var objRect = new Rect(cx + SpriteXN, cy, objWidth, lineH);
        EditorGUI.BeginChangeCheck();
        var newSprite = (Sprite)EditorGUI.ObjectField(objRect, group.sprite, typeof(Sprite), false);
        if (EditorGUI.EndChangeCheck())
            SetGroupSprite(groupIndex, newSprite);

        // 重复次数（紧靠Sprite右侧）
        float countX = cx + SpriteXN + objWidth + 4;
        var countLblRect = new Rect(countX, cy, CountLblW, lineH);
        var countStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft };
        EditorGUI.LabelField(countLblRect, "×", countStyle);
        var countRect = new Rect(countX + CountLblW, cy, CountWN, lineH);
        EditorGUI.BeginChangeCheck();
        var newCount = EditorGUI.IntField(countRect, group.count);
        if (EditorGUI.EndChangeCheck())
            ChangeGroupCount(groupIndex, newCount);

        // 按钮
        if (GUI.Button(new Rect(btnX, cy, BtnWN, BtnHN), "复制"))
            DuplicateGroup(groupIndex);
        if (GUI.Button(new Rect(btnX + BtnWN + BtnGap, cy, BtnWN, BtnHN), "插入"))
            InsertEmptyFrame(group.startIndex + group.count);
        if (GUI.Button(new Rect(btnX + (BtnWN + BtnGap) * 2, cy, BtnWN, BtnHN), "删除"))
            DeleteGroup(groupIndex);

        HandleGroupMouseEvents(rect, groupIndex, evt);
    }

    private void DrawGroupElementCompact(Rect rect, int groupIndex)
    {
        var group = _groups[groupIndex];
        var evt = Event.current;
        float h = rect.height;
        _groupRowRects.Add(rect);

        // 高亮
        if (_selectedGroupIndices.Contains(groupIndex))
            EditorGUI.DrawRect(rect, SelColor);
        if (_isGroupDragging && _dragGroupIndex == groupIndex)
            EditorGUI.DrawRect(rect, DragColor);

        // 拖动标识
        var gripRect = new Rect(rect.x, rect.y, GripW, h);
        EditorGUI.LabelField(gripRect, "≡", new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter });

        // 紧凑：不显示缩略图，控件居中
        float lineH = EditorGUIUtility.singleLineHeight;
        float y = rect.y + (h - lineH) * 0.5f;
        float cx = rect.x + GripW;
        float cw = rect.width - GripW;

        // 序号
        var indexRect = new Rect(cx + IndexX, y, IndexWC, lineH);
        EditorGUI.LabelField(indexRect, group.startIndex.ToString(), EditorStyles.miniLabel);

        // 按钮（右对齐）
        float btnsW = BtnWC * 3 + BtnGap * 2;
        float btnX = cx + cw - btnsW;

        // Sprite 字段（缩短到2/3）
        float objWidth = (cw - SpriteXC - btnsW) * 2f / 3f;
        var objRect = new Rect(cx + SpriteXC, y, objWidth, lineH);
        EditorGUI.BeginChangeCheck();
        var newSprite = (Sprite)EditorGUI.ObjectField(objRect, group.sprite, typeof(Sprite), false);
        if (EditorGUI.EndChangeCheck())
            SetGroupSprite(groupIndex, newSprite);

        // 重复次数（紧靠Sprite右侧）
        float countX = cx + SpriteXC + objWidth + 4;
        var countLblRect = new Rect(countX, y, CountLblW, lineH);
        var countStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
        EditorGUI.LabelField(countLblRect, "×", countStyle);
        var countRect = new Rect(countX + CountLblW, y, CountWC, lineH);
        EditorGUI.BeginChangeCheck();
        var newCount = EditorGUI.IntField(countRect, group.count);
        if (EditorGUI.EndChangeCheck())
            ChangeGroupCount(groupIndex, newCount);

        // 按钮
        if (GUI.Button(new Rect(btnX, y, BtnWC, lineH), "复"))
            DuplicateGroup(groupIndex);
        if (GUI.Button(new Rect(btnX + BtnWC + BtnGap, y, BtnWC, lineH), "插"))
            InsertEmptyFrame(group.startIndex + group.count);
        if (GUI.Button(new Rect(btnX + (BtnWC + BtnGap) * 2, y, BtnWC, lineH), "删"))
            DeleteGroup(groupIndex);

        HandleGroupMouseEvents(rect, groupIndex, evt);
    }

    private void HandleGroupMouseEvents(Rect rect, int groupIndex, Event evt)
    {
        // 拖拽启动：仅处理 MouseDown，MouseUp 统一在 HandleGroupDragEvents 中处理
        if (evt != null && evt.type == EventType.MouseDown && evt.button == 0
            && rect.Contains(evt.mousePosition) && GUIUtility.hotControl == 0)
        {
            _dragGroupIndex = groupIndex;
            _dragStartY = evt.mousePosition.y;
            // 如果点击已选中项且无修饰键，延迟选择重置（等确认是点击还是拖拽）
            bool noModifier = !evt.control && !evt.command && !evt.shift;
            if (noModifier && _selectedGroupIndices.Contains(groupIndex) && _selectedGroupIndices.Count > 1)
                _pendingClickSelect = true;
            else
                _pendingClickSelect = false;
            // 消费 MouseDown 事件，防止 ScrollView 或其他容器干扰事件流
            evt.Use();
        }
    }

    private void DrawSplitter()
    {
        // 获取分隔条 rect（用 GetRect 分配固定高度）
        var splitterRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
            GUILayout.ExpandWidth(true), GUILayout.Height(SplitterHeight));

        // 视觉：画一条细线
        var lineRect = new Rect(splitterRect.x, splitterRect.y + SplitterHeight * 0.5f - 1f, splitterRect.width, 2f);
        EditorGUI.DrawRect(lineRect, new Color(0.4f, 0.4f, 0.4f, 1f));
        EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeVertical);

        var evt = Event.current;
        if (evt == null) return;

        if (evt.type == EventType.MouseDown && splitterRect.Contains(evt.mousePosition))
        {
            _isResizingSplitter = true;
            evt.Use();
        }
        else if (evt.type == EventType.MouseUp && _isResizingSplitter)
        {
            _isResizingSplitter = false;
            evt.Use();
        }
        else if (evt.type == EventType.MouseDrag && _isResizingSplitter)
        {
            // 分割条位于帧列表与底部预览区之间：向下拖应让预览区变小（分割条下移）
            _previewHeight = Mathf.Max(MinPreviewHeight, _previewHeight - evt.delta.y);
            evt.Use();
            Repaint();
        }
    }

    private void DrawDragDropArea(Rect rect)
    {
        var style = new GUIStyle(EditorStyles.helpBox)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Italic
        };
        GUI.Box(rect, "拖拽 Sprite / Texture2D 到这里，可批量追加到末尾", style);

        // 处理拖拽事件
        var evt = Event.current;
        if (!rect.Contains(evt.mousePosition)) return;

        if (evt.type == EventType.DragUpdated)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            evt.Use();
        }
        else if (evt.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            var sprites = ExtractSpritesFromDragObjects(DragAndDrop.objectReferences);
            if (sprites.Count > 0)
            {
                AppendSprites(sprites);
            }
            evt.Use();
        }
    }

    private void DrawPreviewArea()
    {
        // 预览区固定高度，内容底对齐
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Height(_previewHeight));
        GUILayout.Label("预览", EditorStyles.boldLabel);

        // 原始最大尺寸（未限制）
        Vector2 originalMax = GetMaxSpriteSize();
        // 装饰高度：标题 + 信息行 + padding
        const float DecorationH = 44f;
        // 首次绘制：预览区默认高度匹配预览帧 100% 缩放（原始高度 + 装饰）
        if (!_previewHeightInitialized && originalMax.y > 0.01f)
        {
            _previewHeight = originalMax.y + DecorationH;
            _previewHeightInitialized = true;
        }
        // 预览帧填满可用高度，等比缩放
        float availW = position.width - 32f;
        float availH = _previewHeight - DecorationH;
        float heightScale = originalMax.y > 0.01f ? availH / originalMax.y : 1f;
        float previewW = originalMax.x * heightScale;
        float previewH = originalMax.y * heightScale;
        // 宽度限制
        if (previewW > availW && previewW > 0.01f) { float s = availW / previewW; previewW *= s; previewH *= s; }
        previewH = Mathf.Max(previewH, 16f);
        // 实际缩放百分比（相对原始图像尺寸）
        float displayScale = originalMax.x > 0.01f ? previewW / originalMax.x : 1f;

        // 弹性空间将预览内容推到底部
        GUILayout.FlexibleSpace();

        // 居中显示预览
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        var previewRect = GUILayoutUtility.GetRect(previewW, previewH,
            GUILayout.Width(previewW), GUILayout.Height(previewH));
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        // 用 BeginGroup/EndGroup 限制后续绘制不会超出预览框
        GUI.BeginGroup(previewRect);
        {
            var localRect = new Rect(0, 0, previewRect.width, previewRect.height);
            GUI.DrawTexture(localRect, _previewBgTex, ScaleMode.StretchToFill);

            Sprite spriteToShow = null;
            int displayFrameIndex = -1;
            int totalFrames = _target.spriteFrames != null ? _target.spriteFrames.Length : 0;

            if (_isPreviewPlaying && _target.spriteFrames != null && _target.spriteFrames.Length > 0)
            {
                int idx = Mathf.Clamp(_previewIndex, 0, _target.spriteFrames.Length - 1);
                spriteToShow = _target.spriteFrames[idx];
                displayFrameIndex = idx;
            }
            else if (_selectedIndex >= 0 && _selectedIndex < _target.spriteFrames.Length)
            {
                spriteToShow = _target.spriteFrames[_selectedIndex];
                displayFrameIndex = _selectedIndex;
            }

            if (spriteToShow != null)
            {
                Texture2D tex = AssetPreview.GetAssetPreview(spriteToShow);
                if (tex != null)
                {
                    DrawSpriteInRect(localRect, spriteToShow, tex);
                }
                else
                {
                    EditorGUI.LabelField(localRect, "预览加载中...", new GUIStyle(EditorStyles.label)
                    {
                        alignment = TextAnchor.MiddleCenter
                    });
                    Repaint();
                }
            }
            else
            {
                EditorGUI.LabelField(localRect, "无预览", new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleCenter
                });
            }
        }
        GUI.EndGroup();

        // 帧信息显示
        if (_target.spriteFrames != null && _target.spriteFrames.Length > 0)
        {
            int totalFrames = _target.spriteFrames.Length;
            float fps = Mathf.Max(_target.fps, 1);
            float totalDuration = totalFrames / fps;

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (_isPreviewPlaying)
            {
                float currentTime = _previewIndex / fps;
                EditorGUILayout.LabelField(
                    $"帧: {_previewIndex}/{totalFrames}    时间: {currentTime:F2}s / {totalDuration:F2}s    缩放: {displayScale*100:F0}%",
                    EditorStyles.miniLabel,
                    GUILayout.Width(360));
            }
            else
            {
                int curFrame = _selectedIndex >= 0 ? _selectedIndex : 0;
                float currentTime = curFrame / fps;
                EditorGUILayout.LabelField(
                    $"帧: {curFrame}/{totalFrames}    时间: {currentTime:F2}s / {totalDuration:F2}s    缩放: {displayScale*100:F0}%",
                    EditorStyles.miniLabel,
                    GUILayout.Width(360));
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
    }

    private Vector2 GetMaxSpriteSize()
    {
        if (_target == null || _target.spriteFrames == null || _target.spriteFrames.Length == 0)
            return new Vector2(128, 128);

        var max = Vector2.zero;
        bool any = false;
        foreach (var s in _target.spriteFrames)
        {
            if (s == null) continue;
            if (s.textureRect.width > max.x) max.x = s.textureRect.width;
            if (s.textureRect.height > max.y) max.y = s.textureRect.height;
            any = true;
        }
        if (!any) return new Vector2(128, 128);

        // 保证最小可见尺寸
        max.x = Mathf.Max(max.x, 64f);
        max.y = Mathf.Max(max.y, 64f);
        return max;
    }

    private void OnEditorUpdate()
    {
        if (!_isPreviewPlaying) return;
        if (_target == null || _target.spriteFrames == null || _target.spriteFrames.Length == 0) return;

        double now = EditorApplication.timeSinceStartup;
        float delta = (float)(now - _lastEditorTime);
        _lastEditorTime = now;

        float interval = _target.fps <= 0 ? float.MaxValue : 1f / _target.fps;
        _previewTimer += delta;
        if (_previewTimer >= interval)
        {
            _previewTimer = 0f;
            _previewIndex++;
            if (_previewIndex >= _target.spriteFrames.Length)
            {
                if (_target.isLoop)
                    _previewIndex = 0;
                else
                {
                    _previewIndex = _target.spriteFrames.Length - 1;
                    _isPreviewPlaying = false;
                }
            }
            Repaint();
        }
    }

    #region 帧操作

    // ==================== 分组操作 ====================

    private void HandleGroupSelection(int groupIndex, bool ctrl, bool shift)
    {
        if (ctrl)
        {
            if (_selectedGroupIndices.Contains(groupIndex))
                _selectedGroupIndices.Remove(groupIndex);
            else
                _selectedGroupIndices.Add(groupIndex);
            _selectedGroupIndices.Sort();
        }
        else if (shift && _selectedGroupIndices.Count > 0)
        {
            int anchor = _selectedGroupIndices[0];
            _selectedGroupIndices.Clear();
            int min = Mathf.Min(anchor, groupIndex);
            int max = Mathf.Max(anchor, groupIndex);
            for (int i = min; i <= max; i++) _selectedGroupIndices.Add(i);
        }
        else
        {
            _selectedGroupIndices.Clear();
            _selectedGroupIndices.Add(groupIndex);
        }
        _selectedGroupIndex = groupIndex;
        RebuildSelectedIndicesFromGroups();
    }

    private void RebuildSelectedIndicesFromGroups()
    {
        _selectedIndices.Clear();
        foreach (var gi in _selectedGroupIndices)
        {
            if (gi < 0 || gi >= _groups.Count) continue;
            var g = _groups[gi];
            for (int i = 0; i < g.count; i++)
                _selectedIndices.Add(g.startIndex + i);
        }
        _selectedIndices.Sort();
        _selectedIndex = _selectedIndices.Count > 0 ? _selectedIndices[0] : -1;
    }

    private void SyncGroupSelectionFromFrameIndices()
    {
        _selectedGroupIndices.Clear();
        foreach (var fi in _selectedIndices)
        {
            for (int gi = 0; gi < _groups.Count; gi++)
            {
                var g = _groups[gi];
                if (fi >= g.startIndex && fi < g.startIndex + g.count)
                {
                    if (!_selectedGroupIndices.Contains(gi))
                        _selectedGroupIndices.Add(gi);
                    break;
                }
            }
        }
        _selectedGroupIndices.Sort();
        _selectedGroupIndex = _selectedGroupIndices.Count > 0 ? _selectedGroupIndices[0] : -1;
    }

    private void SafeDeleteArrayElement(int index)
    {
        var elem = _framesProp.GetArrayElementAtIndex(index);
        if (elem.propertyType == SerializedPropertyType.ObjectReference && elem.objectReferenceValue != null)
            elem.objectReferenceValue = null;
        _framesProp.DeleteArrayElementAtIndex(index);
    }

    private void SetGroupSprite(int groupIndex, Sprite newSprite)
    {
        if (groupIndex < 0 || groupIndex >= _groups.Count) return;
        var group = _groups[groupIndex];
        Undo.RecordObject(_target, "Change Group Sprite");
        for (int i = 0; i < group.count; i++)
            _framesProp.GetArrayElementAtIndex(group.startIndex + i).objectReferenceValue = newSprite;
        _serializedObj.ApplyModifiedProperties();
        EditorUtility.SetDirty(_target);
        RebuildGroups();
        RebuildSelectedIndicesFromGroups();
    }

    private void ChangeGroupCount(int groupIndex, int newCount)
    {
        if (groupIndex < 0 || groupIndex >= _groups.Count) return;
        var group = _groups[groupIndex];
        newCount = Mathf.Max(1, newCount);
        if (newCount == group.count) return;

        Undo.RecordObject(_target, "Change Group Count");
        if (newCount > group.count)
        {
            int addCount = newCount - group.count;
            int insertPos = group.startIndex + group.count;
            for (int i = 0; i < addCount; i++)
            {
                _framesProp.InsertArrayElementAtIndex(insertPos + i);
                _framesProp.GetArrayElementAtIndex(insertPos + i).objectReferenceValue = group.sprite;
            }
        }
        else
        {
            int removeCount = group.count - newCount;
            for (int i = 0; i < removeCount; i++)
                SafeDeleteArrayElement(group.startIndex + newCount);
        }
        _serializedObj.ApplyModifiedProperties();
        EditorUtility.SetDirty(_target);
        RebuildGroups();
        RebuildSelectedIndicesFromGroups();
    }

    private void DuplicateGroup(int groupIndex)
    {
        if (groupIndex < 0 || groupIndex >= _groups.Count) return;
        var group = _groups[groupIndex];
        Undo.RecordObject(_target, "Duplicate Group");
        int insertPos = group.startIndex + group.count;
        for (int i = 0; i < group.count; i++)
        {
            _framesProp.InsertArrayElementAtIndex(insertPos + i);
            _framesProp.GetArrayElementAtIndex(insertPos + i).objectReferenceValue = group.sprite;
        }
        _serializedObj.ApplyModifiedProperties();
        EditorUtility.SetDirty(_target);
        RebuildGroups();
        _selectedGroupIndices.Clear();
        _selectedGroupIndices.Add(groupIndex + 1);
        _selectedGroupIndex = groupIndex + 1;
        RebuildSelectedIndicesFromGroups();
    }

    private void DeleteGroup(int groupIndex)
    {
        if (groupIndex < 0 || groupIndex >= _groups.Count) return;
        var group = _groups[groupIndex];
        Undo.RecordObject(_target, "Delete Group");
        for (int i = group.count - 1; i >= 0; i--)
            SafeDeleteArrayElement(group.startIndex + i);
        _serializedObj.ApplyModifiedProperties();
        EditorUtility.SetDirty(_target);
        RebuildGroups();
        _selectedGroupIndices.Clear();
        _selectedGroupIndex = -1;
        _selectedIndices.Clear();
        _selectedIndex = -1;
    }

    private void MoveMultipleGroups(int insertGroupIndex)
    {
        if (_selectedGroupIndices.Count == 0) return;
        var selected = new List<int>(_selectedGroupIndices);
        selected.Sort();
        var selectedSet = new HashSet<int>(selected);

        // 构建非选中组列表
        var nonSelected = new List<int>();
        for (int i = 0; i < _groups.Count; i++)
            if (!selectedSet.Contains(i))
                nonSelected.Add(i);

        // 计算 insertGroupIndex 在非选中列表中的对应位置
        int insertPos = 0;
        for (int i = 0; i < insertGroupIndex && i < _groups.Count; i++)
            if (!selectedSet.Contains(i))
                insertPos++;
        insertPos = Mathf.Clamp(insertPos, 0, nonSelected.Count);

        // 构建新组顺序
        var newOrder = new List<int>();
        for (int i = 0; i < insertPos; i++)
            newOrder.Add(nonSelected[i]);
        foreach (var gi in selected)
            newOrder.Add(gi);
        for (int i = insertPos; i < nonSelected.Count; i++)
            newOrder.Add(nonSelected[i]);

        // 检查顺序是否实际改变
        bool changed = false;
        for (int i = 0; i < newOrder.Count; i++)
        {
            if (newOrder[i] != i) { changed = true; break; }
        }
        if (!changed) return;

        // 按新顺序重建帧数组
        Undo.RecordObject(_target, "Reorder Multiple Groups");
        var newSprites = new List<Sprite>();
        foreach (var gi in newOrder)
        {
            var g = _groups[gi];
            for (int i = 0; i < g.count; i++)
                newSprites.Add(_framesProp.GetArrayElementAtIndex(g.startIndex + i).objectReferenceValue as Sprite);
        }

        _framesProp.arraySize = newSprites.Count;
        for (int i = 0; i < newSprites.Count; i++)
            _framesProp.GetArrayElementAtIndex(i).objectReferenceValue = newSprites[i];

        _serializedObj.ApplyModifiedProperties();
        EditorUtility.SetDirty(_target);
        RebuildGroups();

        // 恢复选中：移动后的组现在连续排列在 [insertPos, insertPos+count-1]
        _selectedGroupIndices.Clear();
        for (int i = 0; i < selected.Count; i++)
        {
            int newIdx = insertPos + i;
            if (newIdx < _groups.Count)
                _selectedGroupIndices.Add(newIdx);
        }
        _selectedGroupIndices.Sort();
        _selectedGroupIndex = _selectedGroupIndices.Count > 0 ? _selectedGroupIndices[0] : -1;
        RebuildSelectedIndicesFromGroups();
    }

    /// <summary>
    /// 非合并模式：将多个选中的帧整体移动到目标位置
    /// </summary>
    private void MoveMultipleFrames(List<int> selectedIndices, int oldIndex, int newIndex)
    {
        if (selectedIndices == null || selectedIndices.Count == 0) return;

        var selected = new List<int>(selectedIndices);
        selected.Sort();
        var selectedSet = new HashSet<int>(selected);

        // 提取选中帧和未选中帧的 Sprite
        var selectedSprites = new List<Sprite>();
        var nonSelectedSprites = new List<Sprite>();
        for (int i = 0; i < _framesProp.arraySize; i++)
        {
            var sprite = _framesProp.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
            if (selectedSet.Contains(i))
                selectedSprites.Add(sprite);
            else
                nonSelectedSprites.Add(sprite);
        }

        // 计算插入位置（在未选中帧列表中的位置）
        // newIndex 是拖拽后在修改数组中的位置
        // 向下拖（newIndex > oldIndex）：用户想插入到 newIndex 处，对应原始数组中 newIndex+1 的位置
        // 向上拖（newIndex < oldIndex）：用户想插入到 newIndex 处，对应原始数组中 newIndex 的位置
        int targetIndex = (newIndex > oldIndex) ? newIndex + 1 : newIndex;
        int insertPos = 0;
        for (int i = 0; i < targetIndex && i < _framesProp.arraySize; i++)
            if (!selectedSet.Contains(i))
                insertPos++;
        insertPos = Mathf.Clamp(insertPos, 0, nonSelectedSprites.Count);

        // 构建新顺序
        var newSprites = new List<Sprite>();
        for (int i = 0; i < insertPos; i++)
            newSprites.Add(nonSelectedSprites[i]);
        foreach (var s in selectedSprites)
            newSprites.Add(s);
        for (int i = insertPos; i < nonSelectedSprites.Count; i++)
            newSprites.Add(nonSelectedSprites[i]);

        // 检查是否实际改变
        bool changed = false;
        for (int i = 0; i < newSprites.Count; i++)
        {
            var oldSprite = _framesProp.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
            if (newSprites[i] != oldSprite) { changed = true; break; }
        }
        if (!changed) return;

        // 应用到序列化数组
        Undo.RecordObject(_target, "Reorder Multiple Frames");
        _framesProp.arraySize = newSprites.Count;
        for (int i = 0; i < newSprites.Count; i++)
            _framesProp.GetArrayElementAtIndex(i).objectReferenceValue = newSprites[i];
        _serializedObj.ApplyModifiedProperties();
        EditorUtility.SetDirty(_target);

        // 恢复选中：移动后的帧现在连续排列在 [insertPos, insertPos + count - 1]
        _selectedIndices.Clear();
        for (int i = 0; i < selectedSprites.Count; i++)
        {
            int newIdx = insertPos + i;
            if (newIdx < _framesProp.arraySize)
                _selectedIndices.Add(newIdx);
        }
        _selectedIndices.Sort();
        _selectedIndex = _selectedIndices.Count > 0 ? _selectedIndices[0] : -1;
        _reorderableList.index = _selectedIndex;
        MarkDirty();
    }

    /// <summary>
    /// 非合并模式自定义拖拽：将多个选中的帧整体移动到目标插入位置
    /// </summary>
    private void MoveMultipleFramesTo(int targetInsertIndex)
    {
        if (_selectedIndices.Count == 0 || _framesProp == null) return;
        var selected = new List<int>(_selectedIndices);
        selected.Sort();
        var selectedSet = new HashSet<int>(selected);

        var selectedSprites = new List<Sprite>();
        var nonSelectedSprites = new List<Sprite>();
        for (int i = 0; i < _framesProp.arraySize; i++)
        {
            var sprite = _framesProp.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
            if (selectedSet.Contains(i))
                selectedSprites.Add(sprite);
            else
                nonSelectedSprites.Add(sprite);
        }

        // 计算 targetInsertIndex 在非选中列表中的对应位置
        int insertPos = 0;
        for (int i = 0; i < targetInsertIndex && i < _framesProp.arraySize; i++)
            if (!selectedSet.Contains(i))
                insertPos++;
        insertPos = Mathf.Clamp(insertPos, 0, nonSelectedSprites.Count);

        var newSprites = new List<Sprite>();
        for (int i = 0; i < insertPos; i++)
            newSprites.Add(nonSelectedSprites[i]);
        foreach (var s in selectedSprites)
            newSprites.Add(s);
        for (int i = insertPos; i < nonSelectedSprites.Count; i++)
            newSprites.Add(nonSelectedSprites[i]);

        bool changed = false;
        for (int i = 0; i < newSprites.Count; i++)
        {
            var oldSprite = _framesProp.GetArrayElementAtIndex(i).objectReferenceValue as Sprite;
            if (newSprites[i] != oldSprite) { changed = true; break; }
        }
        if (!changed) return;

        Undo.RecordObject(_target, "Reorder Multiple Frames");
        _framesProp.arraySize = newSprites.Count;
        for (int i = 0; i < newSprites.Count; i++)
            _framesProp.GetArrayElementAtIndex(i).objectReferenceValue = newSprites[i];
        _serializedObj.ApplyModifiedProperties();
        EditorUtility.SetDirty(_target);

        // 恢复选中：移动后的帧连续排列在 [insertPos, insertPos + count - 1]
        _selectedIndices.Clear();
        for (int i = 0; i < selectedSprites.Count; i++)
        {
            int newIdx = insertPos + i;
            if (newIdx < _framesProp.arraySize)
                _selectedIndices.Add(newIdx);
        }
        _selectedIndices.Sort();
        _selectedIndex = _selectedIndices.Count > 0 ? _selectedIndices[0] : -1;
        MarkDirty();
    }

    private void MoveSingleFrame(int oldIndex, int newIndex)
    {
        if (oldIndex == newIndex) return;
        if (oldIndex < 0 || oldIndex >= _framesProp.arraySize) return;
        if (newIndex < 0 || newIndex >= _framesProp.arraySize) return;
        Undo.RecordObject(_target, "Reorder Frame");
        _framesProp.MoveArrayElement(oldIndex, newIndex);
        _serializedObj.ApplyModifiedProperties();
        EditorUtility.SetDirty(_target);
        _selectedIndices.Clear();
        _selectedIndices.Add(newIndex);
        _selectedIndex = newIndex;
        MarkDirty();
    }

    private void MoveGroup(int fromGroupIndex, int toGroupIndex)
    {
        if (fromGroupIndex == toGroupIndex) return;
        if (fromGroupIndex < 0 || fromGroupIndex >= _groups.Count) return;
        if (toGroupIndex < 0 || toGroupIndex >= _groups.Count) return;

        var group = _groups[fromGroupIndex];
        int currentStart = group.startIndex;
        int groupCount = group.count;

        Undo.RecordObject(_target, "Reorder Group");

        if (toGroupIndex < fromGroupIndex)
        {
            // 上移：将组上方的元素逐个移到组下方
            int targetStart = _groups[toGroupIndex].startIndex;
            while (currentStart > targetStart)
            {
                _framesProp.MoveArrayElement(currentStart - 1, currentStart + groupCount - 1);
                currentStart--;
            }
        }
        else
        {
            // 下移：将组下方的元素逐个移到组上方
            int targetEnd = _groups[toGroupIndex].startIndex + _groups[toGroupIndex].count;
            while (currentStart + groupCount < targetEnd)
            {
                _framesProp.MoveArrayElement(currentStart + groupCount, currentStart);
                currentStart++;
            }
        }

        _serializedObj.ApplyModifiedProperties();
        EditorUtility.SetDirty(_target);
        RebuildGroups();
        _selectedGroupIndices.Clear();
        _selectedGroupIndices.Add(toGroupIndex);
        _selectedGroupIndex = toGroupIndex;
        RebuildSelectedIndicesFromGroups();
    }

    // ==================== 帧操作 ====================

    private List<Sprite> ExtractSpritesFromDragObjects(UnityEngine.Object[] objs)
    {
        var result = new List<Sprite>();
        if (objs == null) return result;

        foreach (var obj in objs)
        {
            if (obj is Sprite s)
            {
                result.Add(s);
            }
            else if (obj is Texture2D tex)
            {
                // Texture2D 可能含多 Sprite（图集），通过 AssetDatabase 获取子资产
                var subAssets = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(tex));
                foreach (var sub in subAssets)
                {
                    if (sub is Sprite subSprite) result.Add(subSprite);
                }
            }
        }
        return result;
    }

    private void AppendSprites(List<Sprite> sprites)
    {
        if (_target == null || _framesProp == null) return;
        Undo.RecordObject(_target, "Append Sprites");
        int startIdx = _framesProp.arraySize;
        _framesProp.arraySize += sprites.Count;
        for (int i = 0; i < sprites.Count; i++)
        {
            _framesProp.GetArrayElementAtIndex(startIdx + i).objectReferenceValue = sprites[i];
        }
        _serializedObj.ApplyModifiedProperties();
        EditorUtility.SetDirty(_target);
        _groupsDirty = true;
    }

    private void BatchImportSprites()
    {
        // OpenFilePanelWithFilters 只支持单选，返回单个路径字符串（取消时返回空串）
        string path = EditorUtility.OpenFilePanelWithFilters(
            "选择 Sprite 资源", "Assets",
            new string[] { "Sprite/Texture", "png,jpg,tga,tif,bmp,psd" });
        if (string.IsNullOrEmpty(path)) return;

        string assetPath = "Assets" + path.Replace(Application.dataPath, "").Replace("\\", "/");
        var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);

        var sprites = new List<Sprite>();
        foreach (var asset in assets)
        {
            if (asset is Sprite s) sprites.Add(s);
        }

        if (sprites.Count > 0)
        {
            AppendSprites(sprites);
            ShowNotification(new GUIContent($"已导入 {sprites.Count} 个 Sprite"));
        }
        else
        {
            ShowNotification(new GUIContent("未发现 Sprite"));
        }
    }

    private void DuplicateFrame(int index)
    {
        if (_target == null || _framesProp == null) return;
        if (index < 0 || index >= _framesProp.arraySize) return;

        Undo.RecordObject(_target, "Duplicate Frame");
        _framesProp.InsertArrayElementAtIndex(index + 1);
        var src = _framesProp.GetArrayElementAtIndex(index).objectReferenceValue;
        _framesProp.GetArrayElementAtIndex(index + 1).objectReferenceValue = src;
        _selectedIndex = index + 1;
        _serializedObj.ApplyModifiedProperties();
        EditorUtility.SetDirty(_target);
        _groupsDirty = true;
    }

    private void InsertEmptyFrame(int index)
    {
        if (_target == null || _framesProp == null) return;
        index = Mathf.Clamp(index, 0, _framesProp.arraySize);

        Undo.RecordObject(_target, "Insert Frame");
        _framesProp.InsertArrayElementAtIndex(index);
        // 新插入的元素默认为 null
        _framesProp.GetArrayElementAtIndex(index).objectReferenceValue = null;
        _selectedIndex = index;
        _serializedObj.ApplyModifiedProperties();
        EditorUtility.SetDirty(_target);
        _groupsDirty = true;
    }

    private void DeleteFrame(int index)
    {
        if (_target == null || _framesProp == null) return;
        if (index < 0 || index >= _framesProp.arraySize) return;

        Undo.RecordObject(_target, "Delete Frame");
        _framesProp.DeleteArrayElementAtIndex(index);
        _serializedObj.ApplyModifiedProperties();
        EditorUtility.SetDirty(_target);

        if (_selectedIndex >= _framesProp.arraySize)
            _selectedIndex = _framesProp.arraySize - 1;
        _selectedIndices.Clear();
        _groupsDirty = true;
    }

    private void MoveSelectedFramesUp()
    {
        if (_target == null || _framesProp == null) return;
        if (_selectedIndices.Count == 0) return;
        _selectedIndices.Sort();
        if (_selectedIndices[0] <= 0) return;

        Undo.RecordObject(_target, "Move Frames Up");
        for (int i = 0; i < _selectedIndices.Count; i++)
        {
            int idx = _selectedIndices[i];
            _framesProp.MoveArrayElement(idx, idx - 1);
            _selectedIndices[i] = idx - 1;
        }
        _serializedObj.ApplyModifiedProperties();
        EditorUtility.SetDirty(_target);
        if (_selectedIndices.Count > 0)
        {
            _selectedIndex = _selectedIndices[0];
            if (_reorderableList != null)
                _reorderableList.index = _selectedIndex;
        }
        if (_enableGrouping) { RebuildGroups(); SyncGroupSelectionFromFrameIndices(); }
        else _groupsDirty = true;
    }

    private void MoveSelectedFramesDown()
    {
        if (_target == null || _framesProp == null) return;
        if (_selectedIndices.Count == 0) return;
        _selectedIndices.Sort();
        if (_selectedIndices[_selectedIndices.Count - 1] >= _framesProp.arraySize - 1) return;

        Undo.RecordObject(_target, "Move Frames Down");
        for (int i = _selectedIndices.Count - 1; i >= 0; i--)
        {
            int idx = _selectedIndices[i];
            _framesProp.MoveArrayElement(idx, idx + 1);
            _selectedIndices[i] = idx + 1;
        }
        _serializedObj.ApplyModifiedProperties();
        EditorUtility.SetDirty(_target);
        if (_selectedIndices.Count > 0)
        {
            _selectedIndex = _selectedIndices[0];
            if (_reorderableList != null)
                _reorderableList.index = _selectedIndex;
        }
        if (_enableGrouping) { RebuildGroups(); SyncGroupSelectionFromFrameIndices(); }
        else _groupsDirty = true;
    }

    private void DeleteSelectedFrames()
    {
        if (_target == null || _framesProp == null) return;
        if (_selectedIndices.Count == 0) return;
        if (!EditorUtility.DisplayDialog("批量删除",
            $"确定要删除选中的 {_selectedIndices.Count} 帧吗？此操作可通过 Ctrl+Z 撤销。",
            "确定删除", "取消"))
            return;

        Undo.RecordObject(_target, "Delete Selected Frames");
        _selectedIndices.Sort();
        // 从后往前删，保证索引有效
        for (int i = _selectedIndices.Count - 1; i >= 0; i--)
        {
            _framesProp.DeleteArrayElementAtIndex(_selectedIndices[i]);
        }
        _serializedObj.ApplyModifiedProperties();
        EditorUtility.SetDirty(_target);
        _selectedIndices.Clear();
        _selectedIndex = -1;
        _selectedGroupIndices.Clear();
        _selectedGroupIndex = -1;
        _groupsDirty = true;
    }

    private void DuplicateSelectedFrames()
    {
        if (_target == null || _framesProp == null) return;
        if (_selectedIndices.Count == 0) return;

        _selectedIndices.Sort();
        // 插入位置：选中范围末尾之后，避免插入过程影响选中帧索引
        int insertPos = _selectedIndices[_selectedIndices.Count - 1] + 1;

        // 先读取所有选中帧的 Sprite（插入会改变数组，提前快照）
        var spritesToCopy = new List<Sprite>(_selectedIndices.Count);
        foreach (var idx in _selectedIndices)
            spritesToCopy.Add(_framesProp.GetArrayElementAtIndex(idx).objectReferenceValue as Sprite);

        Undo.RecordObject(_target, "Duplicate Selected Frames");
        for (int i = 0; i < spritesToCopy.Count; i++)
        {
            _framesProp.InsertArrayElementAtIndex(insertPos + i);
            _framesProp.GetArrayElementAtIndex(insertPos + i).objectReferenceValue = spritesToCopy[i];
        }
        _serializedObj.ApplyModifiedProperties();
        EditorUtility.SetDirty(_target);

        // 选中新复制的帧（连续排列在 [insertPos, insertPos + count - 1]）
        _selectedIndices.Clear();
        for (int i = 0; i < spritesToCopy.Count; i++)
            _selectedIndices.Add(insertPos + i);
        _selectedIndices.Sort();
        _selectedIndex = _selectedIndices.Count > 0 ? _selectedIndices[0] : -1;

        if (_enableGrouping) { RebuildGroups(); SyncGroupSelectionFromFrameIndices(); }
        else _groupsDirty = true;
    }

    private void ClearAllFrames()
    {
        if (_target == null || _framesProp == null) return;
        if (_framesProp.arraySize == 0) return;
        if (!EditorUtility.DisplayDialog("清空所有帧",
            "确定要清空所有帧吗？此操作可通过 Ctrl+Z 撤销。",
            "确定清空", "取消"))
            return;

        Undo.RecordObject(_target, "Clear All Frames");
        _framesProp.ClearArray();
        _serializedObj.ApplyModifiedProperties();
        EditorUtility.SetDirty(_target);
        _selectedIndex = -1;
        _selectedIndices.Clear();
        _selectedGroupIndices.Clear();
        _selectedGroupIndex = -1;
        _isPreviewPlaying = false;
        _groupsDirty = true;
    }

    private void MarkDirty()
    {
        if (_target == null) return;
        EditorUtility.SetDirty(_target);
    }

    #endregion

    #region 工具

    private static Texture2D MakeCheckerTexture(int cellSize, int cellCount)
    {
        int size = cellSize * cellCount;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.filterMode = FilterMode.Point;
        var colors = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool dark = ((x / cellSize) + (y / cellSize)) % 2 == 0;
                colors[y * size + x] = dark ? new Color(0.3f, 0.3f, 0.3f, 1f) : new Color(0.5f, 0.5f, 0.5f, 1f);
            }
        }
        tex.SetPixels(colors);
        tex.Apply();
        return tex;
    }

    #endregion
}
