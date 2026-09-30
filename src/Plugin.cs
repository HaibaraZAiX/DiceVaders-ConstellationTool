using System;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using StarVaders;
using UnityEngine;
using UnityEngine.UI;

namespace DiceVaders.ConstellationTool
{
    /// <summary>
    /// 星座重掷工具 —— 在星座选择界面加一个「重掷全部」按钮，免去反复重开游戏刷开局。
    ///
    /// 核心事实（全部来自实测日志，别再踩）：
    ///   · 重掷入口 = ConstellationController.CreateConstellations()（协程），它往
    ///     EncounterModel.Constellations【追加】新项，而 UI 需要的数据量 = 未锁定槽位数。
    ///   · 追加项【未必落在末尾】—— 判断"哪项是新的"必须靠比对整个列表，不能靠索引差。
    ///   · 数据长度必须【等于】UI 未锁定槽位数，否则 UpdateConstellationView() 抛越界、卡片装配失败。
    ///   · 游戏按钮是多层叠加（BG/Top/Bot/Title 四子对象），单独取一层是纯白 —— 必须克隆整个容器
    ///     （实测容器名 RevealObject），且要【保持原版长宽比】，压缩高度会把上下边框挤出可见区。
    ///   · ConstellationController 在局内也存在，判断"星座界面是否打开"必须用 IsShowing 静态标志。
    ///   · 详情卡片由 OnPressReveal() 装配，不是 Initialize()；且只对 !isLocked 的槽位调用。
    ///
    /// v2.8 全量审查后整理：合并 Update 里重复的初始化块、缓存查找结果、文字校正改为缓存组件引用、
    ///      规范化不再硬编码兜底值、清掉历次改向留下的死代码、把单刷的数据落地挪到装配之前。
    /// </summary>
    [BepInPlugin(Guid, "DiceVaders Constellation Tool", "2.9.0")]
    public class Plugin : BasePlugin
    {
        public const string Guid = "dicevaders.constellationtool";

        internal static ManualLogSource Logger;
        internal static ConfigEntry<bool> UseNativeButton;
        internal static ConfigEntry<bool> ShowImGuiPanel;
        internal static ConfigEntry<bool> AutoDump;
        internal static ConfigEntry<float> RestoreDelay;
        internal static ConfigEntry<float> BtnRightOffset;
        internal static ConfigEntry<float> BtnTopOffset;
        internal static ConfigEntry<string> SourceButtonName;
        internal static ConfigEntry<bool> AnchorRight;
        internal static ConfigEntry<float> LeftOffset;
        internal static ConfigEntry<float> BottomOffset;
        internal static ConfigEntry<float> BtnWidth;
        internal static ConfigEntry<float> BtnHeight;
        internal static ConfigEntry<float> ForceFontSize;
        internal static ConfigEntry<bool> LogCandidates;
        internal static ConfigEntry<int> MaxRerollPerSession;
        internal static ConfigEntry<bool> AnchorBottom;

        public override void Load()
        {
            Logger = base.Log;

            UseNativeButton = Config.Bind("1-按钮", "UseNativeButton", true,
                "克隆游戏原生按钮做「重掷星座」按钮。");
            BtnRightOffset = Config.Bind("1-按钮", "RightOffset", 60f,
                new ConfigDescription("右上角锚点时：距屏幕右边缘像素。", new AcceptableValueRange<float>(0f, 800f)));
            BtnTopOffset = Config.Bind("1-按钮", "TopOffset", 300f,
                new ConfigDescription("右上角锚点时：距屏幕顶部像素。", new AcceptableValueRange<float>(0f, 1000f)));

            AnchorRight = Config.Bind("1-按钮", "AnchorRight", true,
                "true=靠右侧（默认）；false=靠左侧。");
            AnchorBottom = Config.Bind("1-按钮", "AnchorBottom", true,
                "配合 AnchorRight 决定四角：右+底=右下角（默认）/ 右+顶=右上角 / 左+底=左下角。" +
                "右下角与右上角都读 RightOffset；右下角用 BottomOffset，右上角用 TopOffset。");
            LeftOffset = Config.Bind("1-按钮", "LeftOffset", 120f,
                new ConfigDescription("左下角锚点时：距屏幕左边缘像素。", new AcceptableValueRange<float>(0f, 1200f)));
            BottomOffset = Config.Bind("1-按钮", "BottomOffset", 235f,
                new ConfigDescription("左下角锚点时：距屏幕底部像素（「星座系统」上方）。", new AcceptableValueRange<float>(0f, 1200f)));

            BtnWidth = Config.Bind("1-按钮", "Width", 350f,
                new ConfigDescription("按钮宽度。", new AcceptableValueRange<float>(100f, 700f)));
            BtnHeight = Config.Bind("1-按钮", "Height", 76f,
                new ConfigDescription("按钮高度。", new AcceptableValueRange<float>(30f, 200f)));
            ForceFontSize = Config.Bind("1-按钮", "FontSize", 42f,
                new ConfigDescription("自建按钮的字号，0=沿用取到的原字号。", new AcceptableValueRange<float>(0f, 120f)));
            SourceButtonName = Config.Bind("1-按钮", "SourceButtonName", "",
                "指定克隆哪个按钮（名字含该串即可）。留空=自动挑。日志会列出全部候选。");

            LogCandidates = Config.Bind("2-调试", "LogCandidates", true,
                "每次进星座界面时打印场景里的按钮候选清单（便于排查）。");

            MaxRerollPerSession = Config.Bind("3-安全", "MaxRerollPerSession", 20,
                new ConfigDescription(
                    "单次停留在星座界面的最大重掷次数。防止 CreateConstellations() 反复追加数据把本局搞崩。",
                    new AcceptableValueRange<int>(1, 200)));

            ShowImGuiPanel = Config.Bind("2-调试", "ShowDebugPanel", false,
                "显示屏幕左上角的调试面板（兜底用）。默认关。");
            AutoDump = Config.Bind("2-调试", "AutoDumpOnSceneOpen", true,
                "进入星座场景时自动 dump 状态到日志。");
            RestoreDelay = Config.Bind("2-调试", "DetailRestoreDelay", 0.65f,
                new ConfigDescription("重掷后等待多久再补详情（秒）。",
                    new AcceptableValueRange<float>(0f, 5f)));

            Logger.LogInfo("===== Constellation Tool v2.9.0 (移除单项重掷：反汇编证实游戏无按槽位重排接口) =====");

            ClassInjector.RegisterTypeInIl2Cpp<ConstellationUI>();
            var go = new GameObject("DiceVaders_ConstellationTool");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<ConstellationUI>();

            Logger.LogInfo("宿主已创建。");
        }
    }

    public class ConstellationUI : MonoBehaviour
    {
        public ConstellationUI(IntPtr ptr) : base(ptr) { }

        private bool _wasInScene;
        private bool _wasShowing;
        private GameObject _nativeButton;
        private static GameObject _overlayCanvas;
        private float _restoreAt = -1f;
        private float _verifyAt = -1f;
        private int _modelSnapshot = -1;     // 重掷前 EncounterModel.Constellations 的项数
        private int _rerollCount;            // 本次停留已重掷次数
        private float _lastRerollAt = -9f;
        private readonly System.Collections.Generic.List<TMPro.TextMeshProUGUI> _textFixTmps = new System.Collections.Generic.List<TMPro.TextMeshProUGUI>();
        private readonly System.Collections.Generic.List<string> _textFixLabels = new System.Collections.Generic.List<string>();
        private string _status = "";
        private float _statusUntil;

        private static void Log(string m) => Plugin.Logger?.LogInfo(m);

        /// <summary>
        /// 自建 ScreenSpaceOverlay Canvas。
        /// 不借用游戏的 Canvas：实测游戏是 ScreenSpaceCamera 模式，挂进去后锚点算不出有效矩形
        /// （世界四角恒为 0,0）。Overlay 模式下 rect 就是屏幕像素，坐标确定、层级由 sortingOrder 决定。
        /// 不挂 CanvasScaler，让 scaleFactor 保持 1，这样 RightOffset/TopOffset 就是真实像素。
        /// </summary>
        private static GameObject EnsureOverlayCanvas()
        {
            if (_overlayCanvas != null) return _overlayCanvas;
            var go = new GameObject("DiceVaders_RerollCanvas");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;      // 压到最上层
            canvas.overrideSorting = true;

            _overlayCanvas = go;
            Log("  已创建独立 Overlay Canvas (sortingOrder=32000)");
            return go;
        }

        private void SetStatus(string s)
        {
            _status = s; _statusUntil = Time.realtimeSinceStartup + 8f; Log("[状态] " + s);
        }

        // ---------- IL2CPP 真实类型名 ----------

        private static string RealTypeName(Component comp)
        {
            try
            {
                var cls = IL2CPP.il2cpp_object_get_class(comp.Pointer);
                var ns = Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_namespace(cls));
                var nm = Marshal.PtrToStringAnsi(IL2CPP.il2cpp_class_get_name(cls));
                if (string.IsNullOrEmpty(nm)) return "<unknown>";
                return string.IsNullOrEmpty(ns) ? nm : ns + "." + nm;
            }
            catch (Exception e) { return "<err:" + e.Message + ">"; }
        }

        // ---------- 查找缓存 ----------
        // FindObjectOfType 是每次遍历全场景的昂贵调用，而 Update 每帧都要用它，
        // 120FPS 下就是每秒 120 次全场景扫描。这里加 0.25 秒的短缓存：
        // 场景切换的感知延迟远小于一帧，但扫描次数降到每秒 4 次。
        private static ConstellationController _ccCache;
        private static EncounterController _ecCache;
        private static float _ccCacheAt = -99f;
        private static float _ecCacheAt = -99f;
        private const float LookupInterval = 0.25f;

        private static ConstellationController FindController()
        {
            float now = Time.realtimeSinceStartup;
            if (_ccCache != null && now - _ccCacheAt < LookupInterval) return _ccCache;
            try { _ccCache = UnityEngine.Object.FindObjectOfType<ConstellationController>(); }
            catch { _ccCache = null; }
            _ccCacheAt = now;
            return _ccCache;
        }

        private static EncounterController FindEncounter()
        {
            float now = Time.realtimeSinceStartup;
            if (_ecCache != null && now - _ecCacheAt < LookupInterval) return _ecCache;
            try { _ecCache = UnityEngine.Object.FindObjectOfType<EncounterController>(); }
            catch { _ecCache = null; }
            _ecCacheAt = now;
            return _ecCache;
        }

        // ---------- 生命周期 ----------

        private void Update()
        {
            var cc = FindController();
            bool inScene = cc != null;

            // 关键：ConstellationController 在【局内也存在】，不能只看它是否存在，
            // 要用游戏的 IsShowing 标志判断"星座界面是否正在显示"，
            // 否则局内也会飘一个按钮盖住右侧面板。
            bool showing = false;
            if (inScene)
            {
                try { showing = ConstellationController.IsShowing; } catch { }
            }

            if (!inScene && _wasInScene) { _restoreAt = -1f; _verifyAt = -1f; _rerollCount = 0; _modelSnapshot = -1; }
            _wasInScene = inScene;

            // 每次重新打开星座界面做一次初始化。
            // 注意：重掷会让界面重新过渡并再次触发"显示"，所以这里【不能】重置重掷计数
            //       （否则每次点击都被记成「第 1 次」，v1.4 日志实证）。
            // 另：这个块曾经被复制成两份（v1.5 遗留），导致 Dump 每次跑两遍 —— 已合并。
            if (showing && !_wasShowing)
            {
                if (Plugin.AutoDump.Value) Dump("星座界面显示");
                if (Plugin.UseNativeButton.Value) TryBuildNativeButton(cc);
                // 进界面就清理一次历史累积（此前错误裁剪可能已把列表撑长）
                TrimModelConstellations(cc);
            }

            // 只在星座界面显示期间让按钮可见
            if (_nativeButton != null)
            {
                try
                {
                    if (_nativeButton.activeSelf != showing) _nativeButton.SetActive(showing);
                }
                catch { }
            }

            // 游戏会把克隆按钮的文字重写回「揭晓！」，每帧盯住
            if (showing) EnforceButtonTexts();
            _wasShowing = showing;

            CheckButtonClick();

            if (_verifyAt > 0f && Time.realtimeSinceStartup >= _verifyAt)
            {
                _verifyAt = -1f;
                if (_nativeButton != null) VerifyVisible(_nativeButton, "布局更新后");
            }

            if (_restoreAt > 0f && Time.realtimeSinceStartup >= _restoreAt)
            {
                _restoreAt = -1f;
                RestoreDetails(cc);
            }
        }

        // ---------- 原生按钮 ----------

        /// <summary>
        /// 挑克隆源。v0.7 实测：场景里 9 个 ButtonView 都没名字含 continue/constellation 的，
        /// 退化成了 82x82 的 ProceedButton（小图标按钮），导致外观与「星座系统」不一致。
        /// 改为三级策略：cfg 指定名 > 尺寸匹配(像长条按钮) > 第一个可见。
        /// </summary>
        private GameObject FindButtonSource(ConstellationController cc)
        {
            var visible = new System.Collections.Generic.List<GameObject>();
            try
            {
                var arr = UnityEngine.Object.FindObjectsOfType<StarVaders.ButtonView>();
                Log($"  场景中 ButtonView 数量 = {arr.Length}");
                foreach (var bv in arr)
                {
                    if (bv == null) continue;
                    var go = bv.gameObject;
                    if (go == null) continue;
                    var rt = go.GetComponent<RectTransform>();
                    bool act = go.activeInHierarchy;
                    var sz = (rt != null) ? rt.sizeDelta : Vector2.zero;
                    bool hasTmp = go.GetComponentInChildren<TMPro.TextMeshProUGUI>() != null;
                    Log($"   候选 '{go.name}' active={act} size=({sz.x:0},{sz.y:0}) tmp={hasTmp}");
                    if (act) visible.Add(go);
                }
            }
            catch (Exception e) { Log("  遍历 ButtonView 失败: " + e.Message); }

            // 1) cfg 指定名字
            var want = (Plugin.SourceButtonName?.Value ?? "").Trim();
            if (want.Length > 0)
            {
                foreach (var go in visible)
                    if ((go.name ?? "").IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0)
                    { Log($"  -> 选中 '{go.name}' (cfg 指定 '{want}')"); return go; }
                Log($"  cfg 指定的 '{want}' 没找到，继续自动挑");
            }

            // 2) 名字匹配
            foreach (var go in visible)
            {
                var n = (go.name ?? "").ToLowerInvariant();
                if (n.Contains("continue") || n.Contains("constellation"))
                { Log($"  -> 选中 '{go.name}' (名字匹配)"); return go; }
            }

            // 3) 尺寸匹配：长条按钮（宽 250~520，高 55~110），且带 TMP 的优先
            GameObject best = null; float bestScore = -1f;
            foreach (var go in visible)
            {
                var rt = go.GetComponent<RectTransform>();
                if (rt == null) continue;
                var sz = rt.sizeDelta;
                if (sz.x < 250f || sz.x > 520f) continue;
                if (sz.y < 55f || sz.y > 110f) continue;
                bool hasTmp = go.GetComponentInChildren<TMPro.TextMeshProUGUI>() != null;
                float score = sz.x + (hasTmp ? 200f : 0f);
                if (score > bestScore) { bestScore = score; best = go; }
            }
            if (best != null) { Log($"  -> 选中 '{best.name}' (尺寸匹配)"); return best; }

            // 4) 兜底
            if (visible.Count > 0) { Log($"  -> 选中 '{visible[0].name}' (第一个可见)"); return visible[0]; }
            return null;
        }

        /// <summary>从游戏里取一份可用字体（否则自建按钮的中文出不来）。</summary>
        private TMPro.TMP_FontAsset FindAnyFont(out float size)
        {
            size = 42f;
            // 1) 星座卡片容器（实测它有 TMP，显示的是星座名）
            try
            {
                var cc = FindController();
                if (cc != null && cc.ConstellationButtonObject != null)
                {
                    var tmp = cc.ConstellationButtonObject.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                    if (tmp != null && tmp.font != null)
                    {
                        if (tmp.fontSize > 0f) size = tmp.fontSize;
                        Log($"  字体取自 ConstellationButtonObject: {tmp.font.name} (size={size})");
                        return tmp.font;
                    }
                }
            }
            catch { }

            // 2) 场景里任意 TMP
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<TMPro.TextMeshProUGUI>();
                foreach (var t in all)
                {
                    if (t != null && t.font != null)
                    {
                        if (t.fontSize > 0f) size = t.fontSize;
                        Log($"  字体取自场景 TMP '{t.name}': {t.font.name} (size={size})");
                        return t.font;
                    }
                }
            }
            catch (Exception e) { Log("  搜索场景字体失败: " + e.Message); }

            Log("  未取到字体，使用 TMP 默认");
            return null;
        }

        /// <summary>
        /// 找游戏原版按钮底图。实测 bundle 里有：
        ///   T_MM_plainbutton9slicebase（普通按钮九宫格底）★
        ///   T_MM_playbutton9slicebase / T_MM_morebuttonbase / UiButton
        /// 这些已在场景里加载，可直接用 Resources.FindObjectsOfTypeAll 取到。
        /// </summary>
        private static UnityEngine.Sprite FindButtonSprite()
        {
            try
            {
                var all = UnityEngine.Resources.FindObjectsOfTypeAll<UnityEngine.Sprite>();
                if (all == null) return null;

                // 1) 精确名优先
                foreach (var s in all)
                {
                    if (s == null) continue;
                    if ((s.name ?? "") == "T_MM_plainbutton9slicebase") return s;
                }
                // 2) 含 9slice 的按钮底
                foreach (var s in all)
                {
                    if (s == null) continue;
                    var n = (s.name ?? "").ToLowerInvariant();
                    if (n.Contains("button") && n.Contains("9slice")) return s;
                }
                // 3) 任意 buttonbase
                foreach (var s in all)
                {
                    if (s == null) continue;
                    var n = (s.name ?? "").ToLowerInvariant();
                    if (n.Contains("buttonbase") || n.Contains("button_base")) return s;
                }
                Log($"  未找到原版按钮底图（当前已加载 Sprite 数 = {all.Length}）");
            }
            catch (Exception e) { Log("  查找按钮底图失败: " + e.Message); }
            return null;
        }

        /// <summary>
        /// 从场景里找一个现成的长条按钮，复制它的 sprite 与 color 当模板。
        /// v1.0 教训：T_MM_plainbutton9slicebase 疑似纯白九宫格（靠 Image.color 染色），
        /// 我写死 Color.white 就成了白底白字全白块。
        /// </summary>
        private UnityEngine.UI.Image FindButtonImageTemplate()
        {
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Image>();
                Log($"  场景 Image 总数 = {all.Length}");
                UnityEngine.UI.Image best = null;
                float bestScore = -1f;
                foreach (var img in all)
                {
                    if (img == null || !img.enabled) continue;
                    var go = img.gameObject;
                    if (go == null || !go.activeInHierarchy) continue;
                    var rt = go.GetComponent<RectTransform>();
                    if (rt == null) continue;
                    var sz = rt.sizeDelta;
                    // 长条按钮：宽 250~560，高 55~120
                    if (sz.x < 250f || sz.x > 560f) continue;
                    if (sz.y < 55f || sz.y > 120f) continue;
                    if (img.sprite == null) continue;

                    Log($"    候选 '{go.name}' size=({sz.x:0},{sz.y:0}) sprite={img.sprite.name} color={img.color}");
                    float score = sz.x * sz.y;
                    if (score > bestScore) { bestScore = score; best = img; }
                }
                if (best != null)
                    Log($"  -> 按钮模板: '{best.gameObject.name}' sprite={best.sprite.name} color={best.color}");
                else
                    Log("  -> 没找到合适的长条按钮模板，退回默认素材");
                return best;
            }
            catch (Exception e) { Log("  找按钮模板失败: " + e.Message); }
            return null;
        }

        /// <summary>
        /// 兜底：自己拼一个按钮，完全不依赖游戏对象。
        /// 底图与配色优先复制场景里现成的长条按钮；字体来自游戏 TMP。
        /// </summary>
        private GameObject CreateOwnButton(TMPro.TMP_FontAsset font, float fontSize, string label, float widthScale = 1f)
        {
            var go = new GameObject("DiceVaders_RerollButton_" + label);
            var rt = go.AddComponent<RectTransform>();
            go.layer = 5;   // UI

            rt.sizeDelta = new Vector2(Plugin.BtnWidth.Value * widthScale, Plugin.BtnHeight.Value * widthScale);
            ApplyAnchorForOffset(rt, 0f);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;

            var img = go.AddComponent<UnityEngine.UI.Image>();

            var tpl = FindButtonImageTemplate();
            if (tpl != null && tpl.sprite != null)
            {
                img.sprite = tpl.sprite;
                img.type = tpl.type;                       // 沿用原按钮的拉伸方式
                img.pixelsPerUnitMultiplier = tpl.pixelsPerUnitMultiplier;
                img.color = tpl.color;                     // ★ 关键：复制原按钮配色，不再写死白色
                img.material = tpl.material;
                Log($"  自建按钮沿用模板: sprite={tpl.sprite.name} color={tpl.color} type={tpl.type}");
            }
            else
            {
                var sp = FindButtonSprite();
                if (sp != null)
                {
                    img.sprite = sp;
                    img.type = Image.Type.Sliced;
                    img.color = new Color(0.10f, 0.34f, 0.40f, 0.95f);   // 深青，不写白
                    Log($"  自建按钮用默认底图+深青色: {sp.name}");
                }
                else
                {
                    img.color = new Color(0.10f, 0.34f, 0.40f, 0.95f);
                    Log("  自建按钮使用纯深青色底（未取到素材）");
                }
            }

            var tgo = new GameObject("Text");
            tgo.layer = 5;
            tgo.transform.SetParent(go.transform, false);
            var trt = tgo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10f, 4f);
            trt.offsetMax = new Vector2(-10f, -4f);

            var tmp = tgo.AddComponent<TMPro.TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            float want = Plugin.ForceFontSize.Value;
            if (want > 0f) tmp.fontSize = want;
            else if (fontSize > 0f) tmp.fontSize = fontSize;
            tmp.text = label;
            tmp.color = Color.white;     // 底图已是深色，白字可读
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            try { tmp.enableAutoSizing = false; } catch { }

            Log($"  已建按钮 '{label}'（font={(font != null ? font.name : "默认")} size={tmp.fontSize} 尺寸={rt.sizeDelta}）");
            return go;
        }

        /// <summary>
        /// 找游戏现成的「揭晓！」按钮对象（实测名字是 RevealObject）。
        /// ★ 这是整个 mod 里最关键的一处发现：
        ///   游戏按钮是【多层叠加】做的 —— 一个容器装着四个子对象：
        ///     'BG'    sprite=T_CT_constellationproceed        底
        ///     'Top'   sprite=T_CT_constellationproceedbuttop  上边
        ///     'Bot'   sprite=T_CT_constellationproceedbuttom  下边
        ///     'Title' sprite=T_MM_playbutton9slicebase + TMP  标题
        ///   单独取任何一层都是纯白（color 全是 1,1,1,1），拼出来就是空白方块 ——
        ///   v0.6~v2.3 一直在犯这个错。必须克隆【整个容器】，美术才会跟着过来。
        /// </summary>
        private GameObject FindRevealButton()
        {
            try
            {
                var arr = UnityEngine.Object.FindObjectsOfType<StarVaders.ButtonView>();
                GameObject fallback = null;
                foreach (var bv in arr)
                {
                    if (bv == null) continue;
                    var go = bv.gameObject;
                    if (go == null || !go.activeInHierarchy) continue;
                    var n = (go.name ?? "").ToLowerInvariant();
                    var rt = go.GetComponent<RectTransform>();
                    var sz = rt != null ? rt.sizeDelta : Vector2.zero;
                    bool hasTmp = go.GetComponentInChildren<TMPro.TextMeshProUGUI>() != null;

                    if (n.Contains("reveal")) return go;                    // 首选
                    if (fallback == null && hasTmp && sz.x > 200f) fallback = go;   // 次选：带文字的长条按钮
                }
                return fallback;
            }
            catch (Exception e) { Log("  查找揭晓按钮失败: " + e.Message); }
            return null;
        }

        /// <summary>
        /// 把克隆出来的按钮改成我们的文案，并登记到"持续校正"名单。
        /// ★ v2.4 教训：克隆体的 TMP 文字设好之后会被游戏某处每帧重写回原值
        ///   （实测日志显示改字成功，画面上却还是「揭晓！」），所以只能每帧盯着它。
        /// </summary>
        private void RetextButton(GameObject go, string label)
        {
            try
            {
                int n = 0;
                var tmps = go.GetComponentsInChildren<TMPro.TextMeshProUGUI>();
                if (tmps != null)
                    foreach (var t in tmps)
                    {
                        if (t == null) continue;
                        t.text = label;
                        try { t.ForceMeshUpdate(); } catch { }
                        n++;
                    }
                if (n > 0)
                {
                    Log($"  已改文字为 '{label}'（{n} 个 TMP），并入校正名单");
                    foreach (var t in tmps)
                    {
                        if (t == null) continue;
                        _textFixTmps.Add(t);
                        _textFixLabels.Add(label);
                    }
                }
                else Log("  克隆体里没有 TMP，需要补文字层");
            }
            catch (Exception e) { Log("  改文字失败: " + e.Message); }
        }

        /// <summary>
        /// 每帧校正按钮文字 —— 游戏会把它重写回「揭晓！」。
        /// 只比较缓存的 TMP 引用（不再 GetComponentsInChildren），每帧开销降到几次字符串比较。
        /// </summary>
        private void EnforceButtonTexts()
        {
            for (int i = 0; i < _textFixTmps.Count; i++)
            {
                var t = _textFixTmps[i];
                if (t == null) continue;
                string want = _textFixLabels[i];
                try
                {
                    if (t.text != want)
                    {
                        t.text = want;
                        try { t.ForceMeshUpdate(); } catch { }
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// 在指定"沿排列方向的叠加偏移"处应用锚点（主按钮 extra=0，小按钮依次叠开）。
        /// </summary>
        private void ApplyAnchorForOffset(RectTransform rt, float extra)
        {
            if (Plugin.AnchorRight.Value && Plugin.AnchorBottom.Value)
            {
                // ★ 右下角（默认）
                rt.anchorMin = new Vector2(1f, 0f);
                rt.anchorMax = new Vector2(1f, 0f);
                rt.pivot = new Vector2(1f, 0f);
                rt.anchoredPosition = new Vector2(-Plugin.BtnRightOffset.Value,
                                                  Plugin.BottomOffset.Value + extra);
            }
            else if (Plugin.AnchorRight.Value)
            {
                // 右上角
                rt.anchorMin = new Vector2(1f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.anchoredPosition = new Vector2(-Plugin.BtnRightOffset.Value,
                                                  -Plugin.BtnTopOffset.Value - extra);
            }
            else
            {
                // 左下角
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(0f, 0f);
                rt.anchoredPosition = new Vector2(Plugin.LeftOffset.Value,
                                                  Plugin.BottomOffset.Value + extra);
            }
        }

        private void TryBuildNativeButton(ConstellationController cc)
        {
            try
            {
                if (_nativeButton != null) { _nativeButton.SetActive(true); return; }

                // 重建按钮时清空文字校正名单（主按钮与所有小按钮会重新登记）
                _textFixTmps.Clear();
                _textFixLabels.Clear();

                var host = EnsureOverlayCanvas();
                if (host == null) { SetStatus("Overlay Canvas 创建失败"); return; }

                // v0.8 教训：克隆路已证死 ——
                //   'Button'(421x87) 的 Image.sprite 实测为 null，底图其实靠 ButtonView 渲染，
                //   而 ButtonView 必须 Strip 掉（否则点它会触发原逻辑），剥完就只剩白块。
                // 所以不再克隆，一律自建：原版九宫格底图 + 游戏字体。
                if (Plugin.LogCandidates.Value)
                {
                    try { FindButtonSource(cc); } catch { }   // 只打印候选清单，不用结果
                }

                float fontSize = 42f;
                TMPro.TMP_FontAsset font = FindAnyFont(out fontSize);

                // ★ 优先克隆游戏现成的「揭晓！」按钮容器 —— 它自带全套分层美术
                var revealSrc = FindRevealButton();
                if (revealSrc != null)
                {
                    Log($"  克隆源: '{revealSrc.name}'（完整按钮容器）");
                    try
                    {
                        _nativeButton = UnityEngine.Object.Instantiate(revealSrc, host.transform);
                        _nativeButton.name = "DiceVaders_RerollButton_All";
                        _nativeButton.layer = 5;
                        _nativeButton.SetActive(true);
                        _nativeButton.transform.SetAsLastSibling();

                        StripGameComponents(_nativeButton);   // 只删 StarVaders 逻辑组件，子对象的美术保留
                        RetextButton(_nativeButton, "重掷全部");

                        var nrt = _nativeButton.GetComponent<RectTransform>();
                        if (nrt != null)
                        {
                            // ★ 保持源按钮的长宽比 —— 它的上/下边框是独立子对象、按原高度定位，
                            //   压缩高度会把它们推出可见区域（v2.5 实测：只剩白底、看不见边框）
                            var srt = revealSrc.GetComponent<RectTransform>();
                            float baseW = (srt != null && srt.sizeDelta.x > 1f) ? srt.sizeDelta.x : 342.8f;
                            float baseH = (srt != null && srt.sizeDelta.y > 1f) ? srt.sizeDelta.y : 123.3f;
                            float k = Plugin.BtnWidth.Value / baseW;
                            nrt.sizeDelta = new Vector2(baseW * k, baseH * k);
                            Log($"  主按钮尺寸: 源 {baseW:0}x{baseH:0} × {k:0.00} = {nrt.sizeDelta.x:0}x{nrt.sizeDelta.y:0}");
                            ApplyAnchorForOffset(nrt, 0f);
                            nrt.localScale = Vector3.one;
                            nrt.localRotation = Quaternion.identity;
                        }
                    }
                    catch (Exception e)
                    {
                        Log("  克隆揭晓按钮失败，退回自建: " + e.Message);
                        if (_nativeButton != null) { try { UnityEngine.Object.Destroy(_nativeButton); } catch { } }
                        _nativeButton = null;
                    }
                }

                if (_nativeButton == null)
                {
                    _nativeButton = CreateOwnButton(font, fontSize, "重掷全部");
                    if (_nativeButton != null)
                    {
                        _nativeButton.transform.SetParent(host.transform, false);
                        _nativeButton.transform.SetAsLastSibling();
                        _nativeButton.SetActive(true);
                    }
                    else Log("  !! 自建按钮也失败");
                }

                _verifyAt = Time.realtimeSinceStartup + 0.5f;
            }
            catch (Exception e)
            {
                Log($"创建按钮失败: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// 只删除 StarVaders 命名空间的游戏逻辑脚本，其余（Image/TMP/AllIn1Shader/UIParticle 等视觉组件）一律保留。
        /// 反转白名单是有意的：游戏 UI 大量依赖 AllIn1SpriteShader 与 Coffee.UIExtensions，
        /// 之前用白名单会把这些效果组件一起删掉，导致按钮视觉异常。
        /// </summary>
        private void StripGameComponents(GameObject go)
        {
            try
            {
                var comps = go.GetComponentsInChildren<Component>(true);
                int killed = 0;
                foreach (var comp in comps)
                {
                    if (comp == null) continue;
                    var full = RealTypeName(comp);

                    // 只碰游戏自己的脚本
                    if (!full.StartsWith("StarVaders")) continue;
                    // Transform 不能删
                    if (full.EndsWith("RectTransform") || full.EndsWith("Transform")) continue;

                    try { UnityEngine.Object.Destroy(comp); killed++; }
                    catch (Exception e) { Log($"  Strip '{full}' 失败: {e.Message}"); }
                }
                Log($"  Strip 完成：删除 {killed} 个 StarVaders 逻辑组件");
            }
            catch (Exception e) { Log("  StripGameComponents 失败: " + e.Message); }
        }

        /// <summary>强制渲染状态可见：源按钮 active=False，克隆体可能继承了禁用的 Image / CanvasRenderer。</summary>
        private void ForceVisible(GameObject go)
        {
            try
            {
                var img = go.GetComponent<UnityEngine.UI.Image>();
                if (img != null)
                {
                    img.enabled = true;
                    var c = img.color; c.a = 1f; img.color = c;
                    Log($"  Image: enabled=True alpha={img.color.a} sprite={(img.sprite != null ? img.sprite.name : "null")}");
                }
                else Log("  !! 没有 Image 组件");
            }
            catch (Exception e) { Log("  修 Image 失败: " + e.Message); }

            try
            {
                var cr = go.GetComponent<CanvasRenderer>();
                if (cr != null) cr.SetAlpha(1f);
            }
            catch (Exception e) { Log("  修 CanvasRenderer 失败: " + e.Message); }

            try
            {
                var cg = go.GetComponent<CanvasGroup>();
                if (cg != null) { cg.alpha = 1f; cg.blocksRaycasts = true; }
            }
            catch { }

            try
            {
                var tmp = go.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                if (tmp != null) { tmp.enabled = true; Log($"  TMP: enabled=True text='{tmp.text}'"); }
            }
            catch (Exception e) { Log("  修 TMP 失败: " + e.Message); }
        }

        /// <summary>
        /// 可见性诊断 —— 刻意避开 GetWorldCorners（interop 下数组回填不可靠，恒返回 0,0 误导判断）。
        /// 改用 rect / position / 屏幕坐标这些标量与小结构。
        /// </summary>
        private void VerifyVisible(GameObject go, string tag)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"--- 可见性 [{tag}] ---");
                sb.AppendLine($"  activeSelf={go.activeSelf} activeInHierarchy={go.activeInHierarchy} layer={go.layer}");

                var rt = go.GetComponent<RectTransform>();
                if (rt == null) { sb.AppendLine("  !! 无 RectTransform"); Log(sb.ToString()); return; }

                var r = rt.rect;
                sb.AppendLine($"  rect: x={r.x:0} y={r.y:0} w={r.width:0} h={r.height:0}");
                sb.AppendLine($"  anchorMin={rt.anchorMin} anchorMax={rt.anchorMax} pivot={rt.pivot}");
                sb.AppendLine($"  sizeDelta={rt.sizeDelta} anchoredPos={rt.anchoredPosition} localScale={rt.localScale}");
                sb.AppendLine($"  worldPos={rt.position}");

                try
                {
                    var sp = RectTransformUtility.WorldToScreenPoint(null, rt.position);
                    sb.AppendLine($"  屏幕坐标(Overlay 直读)=({sp.x:0},{sp.y:0})   屏幕尺寸=({Screen.width}x{Screen.height})");
                }
                catch (Exception e) { sb.AppendLine("  屏幕坐标失败: " + e.Message); }

                var img = go.GetComponent<UnityEngine.UI.Image>();
                sb.AppendLine($"  Image: {(img == null ? "无" : $"enabled={img.enabled} alpha={img.color.a:0.00}")}");

                var tmp = go.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                sb.AppendLine($"  TMP: {(tmp == null ? "无" : $"enabled={tmp.enabled} text='{tmp.text}'")}");

                var cv = go.GetComponentInParent<Canvas>();
                if (cv != null)
                    sb.AppendLine($"  Canvas: renderMode={cv.renderMode} sortingOrder={cv.sortingOrder} scaleFactor={cv.scaleFactor}");

                Log(sb.ToString());
            }
            catch (Exception e) { Log("  VerifyVisible 失败: " + e.Message); }
        }

        private void DumpObject(string tag, GameObject go)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"--- {tag}: '{go.name}' active={go.activeSelf} ---");
            try
            {
                var comps = go.GetComponents<Component>();
                sb.AppendLine($"  组件 ({comps.Length}):");
                foreach (var c in comps)
                {
                    if (c == null) { sb.AppendLine("    <null>"); continue; }
                    sb.AppendLine($"    {RealTypeName(c)}");
                }
            }
            catch (Exception e) { sb.AppendLine("  列组件失败: " + e.Message); }
            try
            {
                var rt = go.GetComponent<RectTransform>();
                if (rt != null)
                    sb.AppendLine($"  RT anchorMin={rt.anchorMin} anchorMax={rt.anchorMax} pivot={rt.pivot} sizeDelta={rt.sizeDelta} pos={rt.anchoredPosition}");
            }
            catch { }
            Log(sb.ToString());
        }

        // ---------- 点击检测 ----------

        private void CheckButtonClick()
        {
            // 安全：只在星座界面显示中才响应点击（局内绝不响应）
            bool showing = false;
            try { showing = ConstellationController.IsShowing; } catch { }
            if (!showing) return;

            // 主按钮：重掷全部
            if (HitTest(_nativeButton))
            {
                Log("「重掷全部」被点击");
                DoReroll();
            }
        }

        /// <summary>命中测试：鼠标左键这一帧是否点在指定 UI 上。</summary>
        private bool HitTest(GameObject go)
        {
            if (go == null || !go.activeInHierarchy) return false;
            try
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return false;

                var pos = mouse.position.ReadValue();
                var rt = go.GetComponent<RectTransform>();
                if (rt == null) return false;

                // 自建 Overlay Canvas 传 null 即可；若挂到别的 Canvas 则要带相机
                Camera cam = null;
                try
                {
                    var cvs = go.GetComponentInParent<Canvas>();
                    if (cvs != null && cvs.renderMode != RenderMode.ScreenSpaceOverlay) cam = cvs.worldCamera;
                }
                catch { }

                return RectTransformUtility.RectangleContainsScreenPoint(rt, pos, cam);
            }
            catch (Exception e) { Log("  命中测试异常: " + e.Message); return false; }
        }

        // ---------- 重掷 ----------

        private void DoReroll()
        {
            var cc = FindController();
            if (cc == null) { SetStatus("不在星座场景"); return; }

            // ---- 安全拦截：绝不在局内 / 过渡中重掷 ----
            // v0.8 崩溃记录：CreateConstellations() 是往 EncounterModel.Constellations【追加】，
            // 局内调用会把本局数据堆坏，任务引擎直接崩（游戏提示"删除本局"）。
            bool showing = false, transitioning = false;
            try { showing = ConstellationController.IsShowing; } catch { }
            try { transitioning = cc.IsTransitioning; } catch { }

            if (!showing) { SetStatus("已拦截：当前不在星座界面"); Log("[安全] 拒绝重掷 —— IsShowing=false"); return; }
            if (transitioning) { SetStatus("已拦截：界面过渡中"); Log("[安全] 拒绝重掷 —— IsTransitioning=true"); return; }
            if (Time.realtimeSinceStartup - _lastRerollAt < 0.45f) return;   // 防连点

            _rerollCount++;
            if (_rerollCount > Plugin.MaxRerollPerSession.Value)
            {
                SetStatus($"已拦截：本次停留重掷已达上限 {Plugin.MaxRerollPerSession.Value} 次");
                Log($"[安全] 重掷次数超限 {_rerollCount}");
                return;
            }

            try
            {
                // 记录当前项数，稍后把 CreateConstellations() 追加的多余项裁掉。
                // v1.3 的"重掷前恢复初始快照"已撤除 —— 那会让游戏的同步逻辑
                // 立刻把 UI 刷回快照值（表现为"文本一闪就变回去"）。
                _modelSnapshot = -1;
                try
                {
                    var ec = FindEncounter();
                    if (ec != null && ec.EncounterModel != null && ec.EncounterModel.Constellations != null)
                        _modelSnapshot = ec.EncounterModel.Constellations.Count;
                }
                catch { }

                Log($"调用 CreateConstellations() ... (第 {_rerollCount} 次, 快照 {_modelSnapshot})");
                var routine = cc.CreateConstellations();
                if (routine == null) { SetStatus("CreateConstellations() 返回 null"); return; }
                cc.StartCoroutine(routine);
                _lastRerollAt = Time.realtimeSinceStartup;
                _restoreAt = Time.realtimeSinceStartup + Plugin.RestoreDelay.Value;
                SetStatus("已重掷，正在补详情…");
            }
            catch (Exception e) { SetStatus("重掷失败: " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>
        /// 打开每个 Constellation 的卡片承载部件。
        /// 这四个字段是 private，但 Il2CppInterop 会为它们生成同名属性：
        ///   UnitSetObject(0x40) / ArtifactSetObject(0x48) / PanelDescription(0x50) / RevealTransform(0x70)
        /// </summary>
        private void ActivateCardParts(ConstellationController cc)
        {
            int on = 0, fail = 0;
            try
            {
                var list = cc.Constellations;
                if (list == null) return;
                for (int i = 0; i < list.Count; i++)
                {
                    var c = list[i];
                    if (c == null) continue;
                    try
                    {
                        if (c.UnitSetObject != null) { c.UnitSetObject.SetActive(true); on++; }
                        if (c.ArtifactSetObject != null) { c.ArtifactSetObject.SetActive(true); on++; }
                        if (c.PanelDescription != null) { c.PanelDescription.enabled = true; on++; }
                    }
                    catch (Exception e) { fail++; Log($"  激活卡片部件[{i}] 失败: {e.GetType().Name}: {e.Message}"); }
                }
                Log($"  卡片部件激活: 打开 {on} 个 / 失败 {fail}");
            }
            catch (Exception e) { Log("  ActivateCardParts 异常: " + e.Message); }
        }

        /// <summary>尝试整表刷新。此前 UI 3 项 vs 数据 2 项导致越界，现在数量一致后再试。</summary>
        private void TryUpdateView(ConstellationController cc)
        {
            try
            {
                cc.UpdateConstellationView();
                Log("  UpdateConstellationView() 调用成功");
            }
            catch (Exception e)
            {
                Log($"  UpdateConstellationView() 仍失败（可忽略）: {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>
        /// 把 EncounterModel.Constellations 规范化到「UI 实际需要的数据量」。
        ///
        /// 演进史（三次都栽在同一个地方）：
        ///   v0.9 按"重掷前快照"裁【末尾】—— 删掉了新生成的星座，UI 永远不变。
        ///   v1.5 改为裁【开头】保留末尾 —— 名字能变了，但长度靠"快照差值"维护，
        ///        而快照本身会漂（实测列表漂到 8 项，UI 只需 2 项），
        ///        长度不匹配 -> UpdateConstellationView() 抛 ArgumentOutOfRangeException
        ///        -> 详情卡片装配不上。
        ///   v1.7 改为按 UI 需求数规范化：数一遍未锁定槽位，把列表裁到那个长度。
        ///        基准是"界面需要几个"，不是"上次是几个"，所以不会漂。
        /// </summary>
        private void TrimModelConstellations(ConstellationController cc)
        {
            try
            {
                var ec = FindEncounter();
                if (ec == null || ec.EncounterModel == null) return;
                var cl = ec.EncounterModel.Constellations;
                if (cl == null) return;

                // UI 需要多少个星座数据 = 未锁定的槽位数
                int need = 0;
                try
                {
                    var ui = cc.Constellations;
                    if (ui != null)
                    {
                        for (int i = 0; i < ui.Count; i++)
                        {
                            var c = ui[i];
                            if (c == null) continue;
                            bool locked = false;
                            try { locked = c.isLocked; } catch { }
                            if (!locked) need++;
                        }
                    }
                }
                catch (Exception e2) { Log("  统计 UI 槽位失败: " + e2.Message); }
                // 统计不到就放弃裁剪 —— 早先这里硬编码 need=2，在已解锁 3 个槽位的局里会误删真实数据。
                if (need <= 0) { Log("  UI 槽位数统计不到，跳过本次规范化（不猜）"); return; }

                int before = cl.Count;
                if (before > need)
                {
                    int remove = before - need;
                    try
                    {
                        cl.RemoveRange(0, remove);
                        Log($"  规范化: {before} -> {cl.Count}（裁掉开头 {remove} 项，UI 需 {need}）");
                    }
                    catch (Exception e3)
                    {
                        for (int i = 0; i < remove && cl.Count > need; i++)
                        {
                            try { cl.RemoveAt(0); } catch { break; }
                        }
                        Log($"  规范化(退化路径): {before} -> {cl.Count}   原因: {e3.Message}");
                    }
                }
                else
                {
                    Log($"  无需规范化（{before} 项，UI 需 {need}）");
                }
            }
            catch (Exception e) { Log("  规范化失败: " + e.Message); }
        }

        /// <summary>
        /// 重掷后把每个 Constellation 的「连线图 + 详情卡片」重新装配。
        /// v0.6 只调了 Initialize（仅更新标题），实测贴图与详情不变 —— 因为它们是独立部件：
        ///   ShapeVisualizer.SetConstellation(ArtifactName) + Build() + Play()   画星座连线
        ///   SendMessage("SetConstellationToArtifact", ArtifactModel)            填详情卡片（私有方法）
        /// </summary>
        private void RestoreDetails(ConstellationController cc)
        {
            if (cc == null) { Log("补详情：controller 没了"); return; }
            int ok = 0, fail = 0, none = 0, shape = 0, detail = 0, reveal = 0, skipLocked = 0;
            try
            {
                var list = cc.Constellations;
                if (list == null) { Log("补详情：列表 null"); return; }

                // 先规范化数据长度，再装配 UI。
                TrimModelConstellations(cc);

                for (int i = 0; i < list.Count; i++)
                {
                    var c = list[i];
                    if (c == null) { fail++; continue; }
                    ArtifactModel am = null;
                    try { am = c.ArtifactModel; } catch { }
                    if (am == null) { none++; continue; }

                    // 0) v1.1 修正：不再把 isRevealed 置 false。
                    //    实测「揭晓！」按钮 = 未揭晓状态，置 false 反而把详情卡片收起来了（方向搞反）。
                    //    改为在装配完成后自动揭晓（见下方第 5 步）。

                    // 1) 基础字段（标题等）
                    try { c.Initialize(am, cc); ok++; }
                    catch (Exception e) { fail++; Log($"  补[{i}] Initialize 失败: {e.GetType().Name}: {e.Message}"); }

                    // 2) 星座连线图
                    //    v1.2 试过 ResetAndPlay，未见改善；回退到最简的 Build+Play。
                    try
                    {
                        var sv = c.ShapeVisualizer;
                        if (sv != null)
                        {
                            sv.SetConstellation(am.ArtifactName);
                            sv.Build();
                            try { sv.Play(); } catch { }
                            shape++;
                        }
                    }
                    catch (Exception e) { Log($"  补[{i}] 连线失败: {e.GetType().Name}: {e.Message}"); }

                    // 3) 详情卡片（私有方法，用 SendMessage 触发）
                    try
                    {
                        c.gameObject.SendMessage("SetConstellationToArtifact", am, SendMessageOptions.DontRequireReceiver);
                        detail++;
                    }
                    catch (Exception e) { Log($"  补[{i}] 卡片失败: {e.GetType().Name}: {e.Message}"); }

                    // 4) Initialize / SetConstellationToArtifact 之后可能又被置回 true，再兜一次
                    try { if (c.isRevealed) c.isRevealed = false; } catch { }

                    // 5) 自动揭晓（仅限已解锁星座）★ 详情卡片的真正装配入口
                    //    用户实测事实：游戏原版是"一次性生成"，重掷后详情卡片不再装配。
                    //    对策：先把揭示状态复位，再走完整的 OnPressReveal() 流程迫使它重建。
                    //    只对未锁定星座做 —— 对 locked 的做会把未解锁卡片揭到错误位置（v1.1 教训）。
                    if (!c.isLocked)
                    {
                        try
                        {
                            c.isRevealed = false;
                            c.isRevealing = false;
                            c.OnPressReveal();
                            reveal++;
                        }
                        catch (Exception e) { Log($"  揭晓[{i}] 失败: {e.GetType().Name}: {e.Message}"); }
                    }
                    else skipLocked++;
                }
                SetStatus($"OK {ok} / 连线 {shape} / 卡片 {detail} / 揭晓 {reveal} / 锁定跳过 {skipLocked} / 空 {none} / 失败 {fail}");
                Log($"RestoreDetails: ok={ok} shape={shape} detail={detail} reveal={reveal} skipLocked={skipLocked} none={none} fail={fail}");

                // ④ 手动把卡片的承载部件打开 —— Initialize 这条路径不会打开它们
                ActivateCardParts(cc);

                // ⑤ 数量现已一致，再试一次整表刷新（此前因 3 vs 2 越界而失败）
                TryUpdateView(cc);

                Dump("补详情后");
            }
            catch (Exception e) { Log("RestoreDetails 异常: " + e.Message); }
        }

        // ---------- dump ----------

        private void Dump(string tag)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"===== 星座状态 [{tag}] =====");
            var cc = FindController();
            if (cc == null) sb.AppendLine("  不在星座场景");
            else
            {
                sb.AppendLine($"  IsShowing={Safe(() => ConstellationController.IsShowing.ToString())} IsTransitioning={Safe(() => cc.IsTransitioning.ToString())}");
                try
                {
                    var list = cc.Constellations;
                    sb.AppendLine($"  cc.Constellations.Count = {(list == null ? -1 : list.Count)}");
                    if (list != null)
                        for (int i = 0; i < list.Count; i++)
                        {
                            var c = list[i];
                            if (c == null) { sb.AppendLine($"    [{i}] null"); continue; }
                            string am = "null";
                            try { if (c.ArtifactModel != null) am = c.ArtifactModel.ArtifactName.ToString(); } catch { }
                            sb.AppendLine($"    [{i}] '{Safe(() => c.name)}' locked={Safe(() => c.isLocked.ToString())} revealed={Safe(() => c.isRevealed.ToString())} Artifact={am}");
                        }
                }
                catch (Exception e) { sb.AppendLine("  读失败: " + e.Message); }
            }

            var ec = FindEncounter();
            if (ec != null)
            {
                try
                {
                    var em = ec.EncounterModel;
                    if (em != null)
                    {
                        var cl = em.Constellations;
                        sb.AppendLine($"  EncounterModel.Constellations.Count = {(cl == null ? -1 : cl.Count)}");
                        if (cl != null)
                            for (int i = 0; i < cl.Count; i++)
                                try { sb.AppendLine($"    [{i}] {cl[i]}"); } catch { sb.AppendLine($"    [{i}] <err>"); }
                        sb.AppendLine($"  Difficulty={Safe(() => em.CurrentDifficulty.ToString())} Challenge={Safe(() => em.CurrentChallenge.ToString())}");
                    }
                }
                catch (Exception e) { sb.AppendLine("  读 EncounterModel 失败: " + e.Message); }
            }
            Log(sb.ToString());
        }

        private static string Safe(Func<string> f)
        {
            try { return f(); } catch (Exception e) { return "<err:" + e.Message + ">"; }
        }

        // ---------- 兜底调试面板 ----------

        private void OnGUI()
        {
            if (Plugin.ShowImGuiPanel == null || !Plugin.ShowImGuiPanel.Value) return;

            var cc = FindController();
            bool inScene = cc != null;
            const float W = 210f, H = 26f;
            float x = 12f, y = 12f;
            var old = GUI.color;

            GUI.color = inScene ? Color.white : new Color(1f, 1f, 1f, 0.5f);
            GUI.Label(new Rect(x, y, W, 20f), inScene ? "星座工具（调试）" : "非星座场景");
            y += 22f;
            GUI.enabled = inScene;

            GUI.color = new Color(0.2f, 0.85f, 0.5f, 1f);
            if (GUI.Button(new Rect(x, y, W, H), "重掷星座")) DoReroll();
            y += H + 4f;
            GUI.color = new Color(0.4f, 0.6f, 0.9f, 1f);
            if (GUI.Button(new Rect(x, y, W, H), "补详情")) RestoreDetails(cc);
            y += H + 4f;
            GUI.color = new Color(0.9f, 0.75f, 0.3f, 1f);
            if (GUI.Button(new Rect(x, y, W, H), "重建按钮")) { _nativeButton = null; if (cc != null) TryBuildNativeButton(cc); }
            y += H + 4f;
            GUI.color = new Color(0.55f, 0.55f, 0.55f, 1f);
            if (GUI.Button(new Rect(x, y, W, H), "打印状态")) Dump("手动");
            y += H + 4f;

            GUI.enabled = true;
            if (!string.IsNullOrEmpty(_status) && Time.realtimeSinceStartup < _statusUntil)
            {
                GUI.color = Color.white;
                GUI.Label(new Rect(x, y, W, 46f), _status);
            }
            GUI.color = old;
        }
    }
}
