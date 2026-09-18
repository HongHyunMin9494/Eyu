using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SimpleAnimPlay : MonoBehaviour
{
    // ====================== 关键帧容器 ======================
    [System.Serializable]
    public class ScaleKeyframe
    {
        public float time;
        public Vector3 scale = Vector3.one;
    }

    [System.Serializable]
    public class AlphaKeyframe
    {
        public float time;
        [Range(0f, 1f)] public float alpha = 1f;
    }

    [System.Serializable]
    public class RotationKeyframe
    {
        public float time;
        // 旋转速度语义：每秒绕指定轴转多少圈（面板直接填圈数，直观）。
        // 运行时按 speedCircles × 360°/s 持续累加角度，跨段天然连续、不倒转。
        // 例：1 = 每秒 1 圈，2 = 每秒 2 圈，0.5 = 每秒半圈；负值反向。
        public float speedCircles = 0f;
    }

    [System.Serializable]
    public class PositionKeyframe
    {
        public float time;
        // 位移速度语义：每秒沿局部坐标系各轴移动多少单位（面板填 Vector3，单位/秒）。
        // 运行时按 localPosition += velocity * dt 持续累加，跨段天然连续。
        // 例：(0,0,5) = 每秒沿 +Z 移动 5 个单位；(1,0,0) = 每秒沿 +X 移动 1 个单位。
        // 位移作用于 TargetNode（与缩放/旋转共用目标节点），使用局部坐标系。
        public Vector3 velocity = Vector3.zero;
    }

    // ====================== 公共字段 ======================
    // 可选：要控制的目标节点（缩放/旋转共用）。留空 = 脚本所在节点自身
    public Transform targetNode;

    // ---------- 旧字段（向前兼容，不在面板显示，OnEnable 自动迁移到 scaleKeys） ----------
    [HideInInspector] public float[] keyTimes;
    [HideInInspector] public Vector3[] keyScales;

    // ---------- 三条独立轨道 ----------
    [Header("缩放轨道")]
    public bool enableScale = true;
    public List<ScaleKeyframe> scaleKeys = new List<ScaleKeyframe>(8);

    [Header("透明度轨道")]
    public bool enableAlpha = true;
    public List<AlphaKeyframe> alphaKeys = new List<AlphaKeyframe>(8);

    [Header("旋转轨道")]
    public bool enableRotation = true;
    // 绕哪根局部轴旋转（速度语义下整个旋转轨道共用一个轴）。
    // 大多数特效弹道/贴花旋转绕 Y 或 Z；默认 Y。
    public enum RotationAxis { X, Y, Z }
    public RotationAxis rotationAxis = RotationAxis.Y;
    public List<RotationKeyframe> rotationKeys = new List<RotationKeyframe>(8);

    [Header("位移轨道")]
    public bool enablePosition = true;
    // 位移速度语义：每帧 localPosition += velocity * dt（局部坐标系，与缩放/旋转共用 TargetNode）
    public List<PositionKeyframe> positionKeys = new List<PositionKeyframe>(8);

    // ====================== 默认目标 ======================
    // targetNode 留空时回退到脚本自身节点，无需手动拖拽
    private Transform TargetNode => targetNode != null ? targetNode : transform;

    // ====================== shader -> 颜色属性名 映射 ======================
    private static readonly Dictionary<string, string> ShaderAlphaPropMap = new Dictionary<string, string>
    {
        { "OG/Effect/Base_Particle", "_FixColor" },
        { "OG/Effect/UV_Base",       "_MainColor" },
        { "OG/Effect/UV_Base_Mask",  "_MainColor" },
    };

    private static string GetAlphaPropertyName(Material mat)
    {
        if (mat == null || mat.shader == null) return null;
        string prop;
        return ShaderAlphaPropMap.TryGetValue(mat.shader.name, out prop) ? prop : null;
    }

    // ====================== 运行时缓存（非序列化，性能优化） ======================
    // OnEnable 一次性收集自身及子节点 Renderer 的材质槽并缓存：避免每帧查表 + 每帧 GetColor
    // 写入用 MaterialPropertyBlock（按 Renderer 实例），不触碰共享材质资产——
    // 多个子弹陆续发射时各自独立淡入淡出，互不影响
    //
    // 重要：Unity 反序列化（进 Play 加载场景 / domain reload / 热重载）会绕过构造函数和
    // 字段初始化器，非序列化字段在那种路径下是 null。因此不用"readonly + 初始化器"，
    // 改为惰性初始化属性，任何创建路径下访问都保证非 null（此前多次 NRE 的共同根因）。
    private List<Renderer> _alphaRenderers;  // 目标 Renderer
    private List<int>      _alphaSlots;      // Renderer 的材质槽位
    private List<string>   _alphaPropNames;  // 对应颜色属性名
    private List<Color>    _alphaBaseColors; // 仅 rgb，a 在运行时被覆盖
    private MaterialPropertyBlock _mpb;
    private HashSet<int> _warnedSlots;       // 已失效槽位的去重警告记录（索引）
    private float _lastAppliedAlpha = float.NaN;

    // 惰性初始化访问器：任何生命周期（构造/反序列化/热重载）下都保证非 null
    private List<Renderer> AlphaRenderers  => _alphaRenderers  ??= new List<Renderer>(8);
    private List<int>      AlphaSlots      => _alphaSlots      ??= new List<int>(8);
    private List<string>   AlphaPropNames  => _alphaPropNames  ??= new List<string>(8);
    private List<Color>    AlphaBaseColors => _alphaBaseColors ??= new List<Color>(8);
    private MaterialPropertyBlock Mpb => _mpb ??= new MaterialPropertyBlock();
    private HashSet<int> WarnedSlots => _warnedSlots ??= new HashSet<int>();

    void OnEnable()
    {
        MigrateLegacyData();
        BuildAlphaCaches();
        StartAnimations();
    }

    private void OnDisable()
    {
        StopAllCoroutines();
    }

    // ====================== 旧数据迁移 ======================
    // 内存级幂等兜底：Play 中的修改不持久化，所以"旧数据未落盘"的物体每次进 Play 都会
    // 执行到这里——这是正常行为而非错误，静默执行即可（批量落盘用编辑器菜单，见 Editor）。
    private void MigrateLegacyData()
    {
        if (keyTimes == null || keyScales == null || keyTimes.Length == 0) return;
        if (keyTimes.Length != keyScales.Length) return;
        if (scaleKeys != null && scaleKeys.Count > 0) return; // 已有新数据，不覆盖

        if (scaleKeys == null) scaleKeys = new List<ScaleKeyframe>(keyTimes.Length);
        else scaleKeys.Clear();

        for (int i = 0; i < keyTimes.Length; i++)
            scaleKeys.Add(new ScaleKeyframe { time = keyTimes[i], scale = keyScales[i] });
    }

    // ====================== Alpha 缓存构建 ======================
    private void BuildAlphaCaches()
    {
        AlphaRenderers.Clear();
        AlphaSlots.Clear();
        AlphaPropNames.Clear();
        AlphaBaseColors.Clear();
        WarnedSlots.Clear();
        _lastAppliedAlpha = float.NaN;

        // 收集自身及子节点各 Renderer 的所有材质槽（含未激活节点）。
        // 按槽位缓存（天然唯一），运行时用 MPB 写入，不影响共享材质资产。
        // 未命中映射表的材质静默跳过，避免警告刷屏。
        var renderers = GetComponentsInChildren<Renderer>(true);
        for (int r = 0; r < renderers.Length; r++)
        {
            var rend = renderers[r];
            if (rend == null) continue;
            var mats = rend.sharedMaterials;
            for (int m = 0; m < mats.Length; m++)
            {
                var mat = mats[m];
                if (mat == null) continue;
                string prop = GetAlphaPropertyName(mat);
                if (prop == null) continue;
                AlphaRenderers.Add(rend);
                AlphaSlots.Add(m);
                AlphaPropNames.Add(prop);
                AlphaBaseColors.Add(mat.GetColor(prop));
            }
        }
    }

    /// <summary>
    /// 运行时子树 Renderer/材质变化后手动刷新缓存。正常情况下 OnEnable 自动构建，无需调用。
    /// </summary>
    public void RefreshAlphaCaches()
    {
        BuildAlphaCaches();
    }

    // ====================== 启动协程 ======================
    private void StartAnimations()
    {
        if (enableScale && CheckScaleValid())
            StartCoroutine(PlayScaleCoroutine());
        if (enableAlpha && CheckAlphaValid())
            StartCoroutine(PlayAlphaCoroutine());
        if (enableRotation && CheckRotationValid())
            StartCoroutine(PlayRotationCoroutine());
        if (enablePosition && CheckPositionValid())
            StartCoroutine(PlayPositionCoroutine());
    }

    // ====================== 独立校验 ======================
    // 与旧版行为对齐：0 帧 = 未配置，静默跳过；1 帧 = 警告后跳过。
    // 时间不强制单调——非正时长段会直接吸附到该段末值（协程内处理），与旧脚本完全一致。
    private bool CheckScaleValid()
    {
        if (scaleKeys == null || scaleKeys.Count == 0) return false;
        if (scaleKeys.Count == 1) { Debug.LogWarning("[SimpleAnimPlay] 缩放轨道只有 1 个关键帧，无法插值，已跳过"); return false; }
        return true;
    }

    private bool CheckAlphaValid()
    {
        if (alphaKeys == null || alphaKeys.Count == 0) return false;
        if (alphaKeys.Count == 1) { Debug.LogWarning("[SimpleAnimPlay] 透明度轨道只有 1 个关键帧，无法插值，已跳过"); return false; }
        if (AlphaPropNames.Count == 0) { Debug.LogError("[SimpleAnimPlay] 透明度轨道未找到可控制材质（请检查子节点 Renderer 使用的 shader 是否在映射表内）"); return false; }
        return true;
    }

    private bool CheckRotationValid()
    {
        if (rotationKeys == null || rotationKeys.Count == 0) return false;
        if (rotationKeys.Count == 1) { Debug.LogWarning("[SimpleAnimPlay] 旋转轨道只有 1 个关键帧，无法插值，已跳过"); return false; }
        return true;
    }

    private bool CheckPositionValid()
    {
        if (positionKeys == null || positionKeys.Count == 0) return false;
        if (positionKeys.Count == 1) { Debug.LogWarning("[SimpleAnimPlay] 位移轨道只有 1 个关键帧，无法插值，已跳过"); return false; }
        return true;
    }

    // ====================== 协程：缩放 ======================
    IEnumerator PlayScaleCoroutine()
    {
        int count = scaleKeys.Count;
        for (int i = 0; i < count - 1; i++)
        {
            float startTime = scaleKeys[i].time;
            float endTime = scaleKeys[i + 1].time;
            Vector3 startScale = scaleKeys[i].scale;
            Vector3 endScale = scaleKeys[i + 1].scale;

            float duration = endTime - startTime;
            if (duration <= 0f) { TargetNode.localScale = endScale; continue; }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float percent = elapsed / duration;
                TargetNode.localScale = Vector3.Lerp(startScale, endScale, percent);
                yield return null;
            }
            TargetNode.localScale = endScale;
        }
    }

    // ====================== 协程：透明度（零 GetColor，全用缓存） ======================
    IEnumerator PlayAlphaCoroutine()
    {
        int matCount = AlphaPropNames.Count;
        int frameCount = alphaKeys.Count;

        for (int i = 0; i < frameCount - 1; i++)
        {
            float startTime = alphaKeys[i].time;
            float endTime = alphaKeys[i + 1].time;
            float startAlpha = alphaKeys[i].alpha;
            float endAlpha = alphaKeys[i + 1].alpha;

            float duration = endTime - startTime;
            if (duration <= 0f)
            {
                ApplyAlphaToAll(endAlpha, matCount);
                continue;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float percent = elapsed / duration;
                float a = Mathf.LerpUnclamped(startAlpha, endAlpha, percent);
                ApplyAlphaToAll(a, matCount);
                yield return null;
            }
            ApplyAlphaToAll(endAlpha, matCount);
        }
    }

    private void ApplyAlphaToAll(float alpha, int count)
    {
        if (alpha == _lastAppliedAlpha) return; // 值未变化时跳过，避免重复写 MPB
        _lastAppliedAlpha = alpha;

        // MPB 按实例写入：共享材质资产不动，各实例互不影响。
        // 缓存是 OnEnable 时的快照，运行中子树可能变化，这里做全链路防御：
        // 任何一处异常状态都只跳过该槽位、不中断协程，且每槽位仅警告一次。
        int n = Mathf.Min(Mathf.Min(count, AlphaRenderers.Count),
                          Mathf.Min(AlphaPropNames.Count, AlphaBaseColors.Count));
        n = Mathf.Min(n, AlphaSlots.Count);
        for (int i = 0; i < n; i++)
        {
            var rend = AlphaRenderers[i];
            string pname = AlphaPropNames[i];
            int slot = AlphaSlots[i];

            bool ok = rend != null
                   && !string.IsNullOrEmpty(pname)
                   && slot >= 0
                   && slot < rend.sharedMaterials.Length;
            if (!ok)
            {
                if (WarnedSlots.Add(i))
                    Debug.LogWarning($"[SimpleAnimPlay] 透明度缓存槽位 {i} 已失效（Renderer销毁/材质槽变化/属性名异常），跳过：{name}", this);
                continue;
            }

            Color c = AlphaBaseColors[i];
            c.a = alpha;
            Mpb.SetColor(pname, c);
            rend.SetPropertyBlock(Mpb, slot);
        }
    }

    // ====================== 协程：旋转（速度累加式，比 Lerp 更省） ======================
    // 每帧只做：角度 += 速度 × dt，相比旧 Lerp 方式省去 Vector3.Lerp + 每帧除法。
    // 角度持续累加，跨段天然连续，不会出现"角度跳变倒转"。
    IEnumerator PlayRotationCoroutine()
    {
        // 预先确定旋转轴分量索引（避免每帧 switch）
        int axisIndex = rotationAxis == RotationAxis.X ? 0
                      : rotationAxis == RotationAxis.Y ? 1 : 2;

        // 从当前姿态进入：累加起点用节点当前的该轴角度
        var e0 = TargetNode.localEulerAngles;
        float currentAngle = e0[axisIndex];

        int count = rotationKeys.Count;
        for (int i = 0; i < count - 1; i++)
        {
            float startTime = rotationKeys[i].time;
            float endTime   = rotationKeys[i + 1].time;
            float speed     = rotationKeys[i].speedCircles; // 本段速度（圈/秒）

            float duration = endTime - startTime;
            if (duration <= 0f) continue; // 零长度段：无累积，直接进入下一段

            // 预计算：度/秒（循环外一次，循环内只剩加法）
            float degPerSec = speed * 360f;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                float dt = Time.deltaTime;
                elapsed += dt;
                // 帧时长超出段末则钳到段末，避免下一段首帧多转
                if (elapsed > duration) dt -= (elapsed - duration);
                currentAngle += degPerSec * dt;

                var e = TargetNode.localEulerAngles;
                e[axisIndex] = currentAngle;
                TargetNode.localEulerAngles = e;
                yield return null;
            }
        }
    }

    // ====================== 协程：位移（速度累加式，与旋转同构） ======================
    // 每帧只做：localPosition += velocity * dt，局部坐标系，作用于 TargetNode。
    // 跨段天然连续，速度变化时位移不跳变。
    IEnumerator PlayPositionCoroutine()
    {
        // 累加起点用节点当前局部位置
        Vector3 pos = TargetNode.localPosition;

        int count = positionKeys.Count;
        for (int i = 0; i < count - 1; i++)
        {
            float startTime = positionKeys[i].time;
            float endTime   = positionKeys[i + 1].time;
            Vector3 vel     = positionKeys[i].velocity; // 本段速度（单位/秒）

            float duration = endTime - startTime;
            if (duration <= 0f) continue; // 零长度段：无累积，直接进入下一段

            float elapsed = 0f;
            while (elapsed < duration)
            {
                float dt = Time.deltaTime;
                elapsed += dt;
                // 帧时长超出段末则钳到段末，避免下一段首帧多移
                if (elapsed > duration) dt -= (elapsed - duration);
                pos += vel * dt;
                TargetNode.localPosition = pos;
                yield return null;
            }
        }
    }
}
