#if UNITY_EDITOR
// versionDefines では「無いとき」のシンボルを作れないため、PRESENT の裏返しで作る
#if !AC_CHCT_TTT_PRESENT
#define TTT_ABSENT
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
#if !TTT_ABSENT
using net.rs64.TexTransTool.Decal;
#endif

namespace AtelierCloveria.CustomHairColorTool
{
    public sealed partial class CustomHairColorToolWindow : EditorWindow
    {
        [MenuItem(ToolInfo.MenuPath)]
        public static void Open()
        {
            var w = GetWindow<CustomHairColorToolWindow>(ToolInfo.ToolName);
            w.minSize = new Vector2(360, 620);
            w.Show();
        }

#if TTT_ABSENT
        void DrawTttMissingIMGUI()
        {
            GUILayout.Space(4);
#if AC_CHCT_TTT_INSTALLED
            // 確かめた範囲の外の版（TTT は同じマイナー版の間しか互換を約束していないため）
            GUILayout.Label("この版の TexTransTool では、まだ動作を確かめていません（" + ToolInfo.TestedTexTransTool + " で確かめています）。\nThis TexTransTool version has not been tested yet.", ImguiSkin.Warn);
            GUILayout.Label("このツールの更新をお待ちください。お問い合わせは Atelier Cloveria へお願いします（TexTransTool の作者さんではなく）。", ImguiSkin.Note);
#else
            GUILayout.Label("TexTransTool が必要です。\nTexTransTool is required.", ImguiSkin.Warn);
            EditorGUILayout.SelectableLabel(ToolInfo.TexTransToolUrl, ImguiSkin.Text, GUILayout.Height(18));
            if (GUILayout.Button("導入ページを開く / Open download page", ImguiSkin.Button, GUILayout.Height(26)))
            {
                Application.OpenURL(ToolInfo.TexTransToolUrl);
            }
#endif
        }
#else
        [Serializable]
        sealed class PartState
        {
            public float Hue;
            public Tone Tone = Tone.None;
            public Color Color = Color.white;
            public string BlendKey = "LinearLight";
            public bool Clear;
        }

        // グラデーションのモードの色は State(Gradation, 0〜2)、メッシュ・ツートンの色ごとのグラデは Stops に持つ
        [Serializable]
        sealed class GradState
        {
            public bool Three;
            public float Split = 0.5f;
            public float Split1 = 0.33f;
            public float Split2 = 0.66f;
            public bool MiddleKnown;     // まだなら3色にしたとき根元と毛先の間の色にする
            public PartState[] Stops = new PartState[3];

            public PartState Stop(int i)
            {
                if (Stops == null || Stops.Length < 3) Array.Resize(ref Stops, 3);
                if (Stops[i] == null) Stops[i] = new PartState();
                return Stops[i];
            }
        }

        [Serializable]
        sealed class OriginalValue
        {
            public int DecalInstanceId;
            public Color Color;
            public string BlendKey;
            public Gradient Gradient;
            public bool Clear;
        }

        sealed class TileEntry
        {
            public string Label;
            public PresetColor Preset; // null＝デフォルトの髪
            public Texture2D Thumbnail;
        }

        const int PartsPerMode = 3;
        const string NoneLabel = "デフォルトの髪";

        [SerializeField] ColoringMode _mode = ColoringMode.Default;
        [SerializeField] int _partIndex;
        // 古い _states は読まない（PartsPerMode を 2→3 にして添字の並びが変わったため）
        [SerializeField] PartState[] _partStates = new PartState[ColoringModes.All.Length * PartsPerMode];
        [SerializeField] GradState[] _gradStates = new GradState[ColoringModes.All.Length * PartsPerMode];
        [SerializeField] int _stopIndex;
        [SerializeField] int _lastCustomId;     // 替わったら MiddleKnown を捨てる（前のアバターの色を持ち越さないため）
        [SerializeField] Vector2 _scroll;
        [SerializeField] List<OriginalValue> _originals = new List<OriginalValue>();
        // 「色を選ぶ」に戻る先（RelPath）。空＝色を付けない
        [SerializeField] string _presetBeforeCustom = "";

        ColoringProduct _target;
        int _dragUndoGroup = -1;
        bool _showDetail;

        // Refresh で読む（毎フレーム Hierarchy や SerializedObject を見ないため）
        List<PresetColor> _tiles = new List<PresetColor>();
        int _selectedCount;
        bool _customOn;
        bool _anyGroupOff;         // 古い版の「カスタム有効」のまま保存されたシーン
        GameObject _inactiveNode;
        bool _bakedMaterialFound;
        bool _hasThumbnails;
        Texture2D _noneThumbnail;
        InfoState _infoState;
        string _infoNeed;
        string _productName;
        List<HairColoringTheme> _themes = new List<HairColoringTheme>();
        bool _customDiffers;       // 「前のカスタムの続きから」を出すか
        readonly Dictionary<string, Texture2D> _thumbCache = new Dictionary<string, Texture2D>();

        static bool? s_bakedToolPresent;
        static bool BakedToolPresent
        {
            get
            {
                if (!s_bakedToolPresent.HasValue)
                {
                    s_bakedToolPresent = AppDomain.CurrentDomain.GetAssemblies()
                        .Any(a => a.GetName().Name == ToolInfo.BakedToolAssemblyName);
                }
                return s_bakedToolPresent.Value;
            }
        }

        PartState State(ColoringMode mode, int index)
        {
            var need = ColoringModes.All.Length * PartsPerMode;
            if (_partStates == null || _partStates.Length < need) Array.Resize(ref _partStates, need);
            var i = (int)mode * PartsPerMode + index;
            if (_partStates[i] == null) _partStates[i] = new PartState();
            return _partStates[i];
        }

        GradState Grad(ColoringMode mode, int color)
        {
            var need = ColoringModes.All.Length * PartsPerMode;
            if (_gradStates == null || _gradStates.Length < need) Array.Resize(ref _gradStates, need);
            var i = (int)mode * PartsPerMode + (ColoringModes.Get(mode).IsGradient ? 0 : color);
            if (_gradStates[i] == null) _gradStates[i] = new GradState();
            return _gradStates[i];
        }

        PartState GradStop(ColoringMode mode, int color, int stop) => ColoringModes.Get(mode).IsGradient ? State(mode, stop) : Grad(mode, color).Stop(stop);

        GradientShape ShapeOf(ColoringMode mode, int color)
        {
            var g = Grad(mode, color);
            return new GradientShape
            {
                ThreeColors = g.Three,
                Root = GradStop(mode, color, 0).Color,
                Middle = GradStop(mode, color, 1).Color,
                Tip = GradStop(mode, color, 2).Color,
                Split = g.Split,
                Split1 = g.Split1,
                Split2 = g.Split2,
                RootClear = GradStop(mode, color, 0).Clear,
                MiddleClear = GradStop(mode, color, 1).Clear,
                TipClear = GradStop(mode, color, 2).Clear,
            };
        }

        PartState Current => ColorGradOn ? Grad(_mode, _partIndex).Stop(_stopIndex) : State(_mode, _partIndex);
        bool IsGradientMode => ColoringModes.Get(_mode).IsGradient;
        SingleGradationDecal CurrentDecal => _target != null && _target.IsValid && !IsGradientMode ? _target.Decal(_mode, _partIndex) : null;
        List<SingleGradationDecal> ModeDecals => _target != null && _target.Has(_mode) ? _target.ModeParts[_mode].Decals : new List<SingleGradationDecal>();

        int CurrentStop
        {
            get => IsGradientMode ? _partIndex : _stopIndex;
            set { if (IsGradientMode) _partIndex = value; else _stopIndex = value; }
        }

        IEnumerable<SingleGradationDecal> WithFollowers(SingleGradationDecal decal) => WithFollowers(_mode, decal);

        IEnumerable<SingleGradationDecal> WithFollowers(ColoringMode mode, SingleGradationDecal decal)
        {
            yield return decal;
            if (_target == null || !_target.Has(mode) || !_target.ModeParts[mode].Followers.TryGetValue(decal, out var list)) yield break;
            foreach (var d in list) if (d != null) yield return d;
        }

        List<int> VisibleParts(ColoringMode mode)
        {
            if (ColoringModes.Get(mode).IsGradient) return Grad(mode, 0).Three ? new List<int> { 0, 1, 2 } : new List<int> { 0, 2 };
            var n = _target != null ? Mathf.Min(_target.DecalCount(mode), PartsPerMode) : 1;
            return Enumerable.Range(0, Mathf.Max(1, n)).ToList();
        }

        int GradColor => IsGradientMode ? 0 : _partIndex;

        GradientShape CurrentGradientShape() => ShapeOf(_mode, GradColor);

        // wantsMouseMove は要らない（ホバーは USS の :hover のため）
        void OnEnable()
        {
            LoadUiPrefs();
            _recent = RecentColors.Load();
            Undo.undoRedoPerformed += OnUndoRedo;
            Refresh();
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            FlushRecentPending();
            FlushGlowPending();
            StopPicking();
            DisposeGlowPreview();
        }

        void OnFocus() { _recent = RecentColors.Load(); } // ほかのウィンドウで増えた色も出すため

        // 最近の色に入れるのは手が止まってから（ドラッグ途中の色で埋めないため）
        // 光のつまみは書き込みを間引いているため、最後の値をここで書く
        void Update()
        {
            if (_recentPending && EditorApplication.timeSinceStartup - _recentPendingAt > RecentSettleSeconds) FlushRecentPending();
            if (AnyGlowPending && EditorApplication.timeSinceStartup - _glowLastWrite >= GlowWriteInterval) FlushGlowPending();
            TickGlowShot();
            TickFlowRepaint();
        }

        void OnSelectionChange() { Refresh(); Repaint(); }
        void OnHierarchyChange() { QueueRefresh(); }
        void OnProjectChange() { HairColoringInfos.Forget(); QueueRefresh(); }
        void OnUndoRedo() { _pickMessage = null; Refresh(); Repaint(); } // 「〜にしました」は Undo で古くなるため消す

        // 読むだけ（Hierarchy には書かない）
        void Refresh()
        {
            RefreshState();
            _refreshedSinceSync = true;
            RequestUiSync();
        }

        void RefreshState()
        {
            _tiles = new List<PresetColor>();
            _selectedCount = 0;
            _customOn = false;
            _anyGroupOff = false;
            _inactiveNode = null;
            _bakedMaterialFound = false;
            _hasThumbnails = false;
            _noneThumbnail = null;

            _infoState = InfoState.Ok;
            _infoNeed = null;
            _productName = null;
            _themes = new List<HairColoringTheme>();

            _target = HairColorCore.FindColoringProduct(Selection.activeGameObject);
            if (_target == null || !_target.IsValid) return;
            _infoState = HairColoringInfos.Check(HairColorCore.GetProductRoot(_target), out var info);
            if (_infoState != InfoState.Ok)
            {
                _infoNeed = info != null ? info.MinToolVersion : null;
                _target = null;
                return;
            }
            _productName = info != null ? info.ProductName : null;
            if (info != null) _themes = info.Themes;

            var customId = _target.Custom.GetInstanceID();
            if (customId != _lastCustomId)
            {
                _lastCustomId = customId;
                StopPicking();
                if (_gradStates != null) foreach (var g in _gradStates) if (g != null) g.MiddleKnown = false;
            }

            foreach (var mode in _target.AvailableModes)
            {
                if (ColoringModes.Get(mode).IsGradient) { SyncGradient(mode); continue; }
                for (var i = 0; i < PartsPerMode; i++)
                {
                    var decal = _target.Decal(mode, i);
                    if (decal == null) continue;
                    RememberOriginal(decal);
                    SyncFromDecal(State(mode, i), decal);
                    SyncColorGrad(mode, i);
                }
            }

            var mode2 = HairColorCore.GetColoringMode(_target);
            if (mode2.HasValue) _mode = mode2.Value;
            if (!_target.Has(_mode)) _mode = _target.FirstMode;
            var parts = VisibleParts(_mode);
            if (!parts.Contains(_partIndex)) _partIndex = parts[0];
            if (_stopIndex < 0 || _stopIndex > 2 || (_stopIndex == 1 && !IsGradientMode && !Grad(_mode, _partIndex).Three)) _stopIndex = 0;

            _tiles = HairColorCore.ListPresetColors(_target);
            LoadThumbnails();
            _customOn = _target.Custom.activeSelf;
            _selectedCount = _tiles.Count(t => t.Selected) + (_customOn ? 1 : 0);
            _anyGroupOff = HairColorCore.FindPresetColorGroups(_target).Any(g => !g.activeSelf);
            _inactiveNode = HairColorCore.FindInactiveProductAncestor(_target);
            var sig = HairColorCore.ComputeMaterialSignature(_target.Avatar);
            if (sig != _materialSig) { _materialSig = sig; _bakedCache = HairColorCore.HasBakedMaterial(_target.Avatar); }
            _bakedMaterialFound = _bakedCache;
            RefreshGlow();
            RefreshPick();
            if (_picking && !PickAvailable) StopPicking();
            RefreshPartShapes();
            _customDiffers = ComputeCustomDiffers();
            DecideFirstTopTab();
        }

        // 最初に見た値か Prefab の値と1つでも違うか（グラデの Decal も見る）
        bool ComputeCustomDiffers()
        {
            foreach (var kv in _target.ModeParts)
            {
                var mt = kv.Value;
                var decals = new List<SingleGradationDecal>(mt.Decals);
                if (mt.Grad != null) { decals.AddRange(mt.Grad.Decals[0]); decals.AddRange(mt.Grad.Decals[1]); }
                foreach (var d in decals)
                {
                    if (d == null) continue;
                    var o = FindOriginal(d);
                    if (o != null && (HairColorCore.GetDecalBlendMode(d) != o.BlendKey || !SameGradient(HairColorCore.GetDecalGradientRaw(d), o.Gradient))) return true;
                    var src = PrefabUtility.GetCorrespondingObjectFromSource(d);
                    if (src != null && (HairColorCore.GetDecalBlendMode(d) != HairColorCore.GetDecalBlendMode(src) || !SameGradient(HairColorCore.GetDecalGradientRaw(d), HairColorCore.GetDecalGradientRaw(src)))) return true;
                }
                if (HairColorCore.IsAnyPerColorGradientOn(mt.Grad)) return true;
            }
            return false;
        }

        static bool SameGradient(Gradient a, Gradient b)
        {
            if (a == null || b == null) return a == b;
            var ka = a.colorKeys;
            var kb = b.colorKeys;
            if (ka.Length != kb.Length) return false;
            for (var i = 0; i < ka.Length; i++)
                if (!ColorEquals(ka[i].color, kb[i].color) || Mathf.Abs(ka[i].time - kb[i].time) > 1e-3f) return false;
            // 透明度キーも比べる（無色にしただけでも違いとするため）
            var aa = a.alphaKeys;
            var ab = b.alphaKeys;
            if (aa.Length != ab.Length) return false;
            for (var i = 0; i < aa.Length; i++)
                if (Mathf.Abs(aa[i].alpha - ab[i].alpha) > 1e-3f || Mathf.Abs(aa[i].time - ab[i].time) > 1e-3f) return false;
            return true;
        }

        int _materialSig;
        bool _bakedCache;
        bool _refreshQueued;

        // Refresh の中だけで読む（OnGUI では読まない）
        void LoadThumbnails()
        {
            var folder = HairColorCore.FindThumbnailFolder(_target);
            if (folder == null) return;
            foreach (var t in _tiles) t.Thumbnail = LoadThumbnail(folder + "/" + HairColorCore.PresetColorThumbnailFileName(t));
            _noneThumbnail = LoadThumbnail(folder + "/" + HairColorCore.NoneThumbnailFileName);
            _hasThumbnails = _noneThumbnail != null || _tiles.Any(t => t.Thumbnail != null);
        }

        // 消えた・読み直された Texture（== null）は読み直す
        Texture2D LoadThumbnail(string assetPath)
        {
            if (_thumbCache.TryGetValue(assetPath, out var cached) && cached != null) return cached;
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            _thumbCache[assetPath] = tex;
            return tex;
        }

        // 読み直しは1フレームに1回（Hierarchy の変更が1フレームに多数届くため）
        void QueueRefresh()
        {
            if (_refreshQueued) return;
            _refreshQueued = true;
            EditorApplication.delayCall += () =>
            {
                _refreshQueued = false;
                if (this == null) return;
                Refresh();
                Repaint();
            };
        }

        // 先頭の Decal だけ読む（全 Decal に同じグラデを書くため）
        void SyncGradient(ColoringMode mode)
        {
            var decals = _target.ModeParts[mode].Decals;
            foreach (var d in decals) RememberOriginal(d);
            SyncGradientFrom(decals, mode, 0);
        }

        void SyncGradientFrom(IList<SingleGradationDecal> decals, ColoringMode mode, int color)
        {
            var first = decals.FirstOrDefault(d => d != null);
            if (first == null) return;
            var g = Grad(mode, color);
            var shape = HairColorCore.GetDecalGradient(first);
            g.Three = shape.ThreeColors;
            if (shape.ThreeColors) { g.Split1 = shape.Split1; g.Split2 = shape.Split2; }
            else g.Split = shape.Split;
            SyncColor(GradStop(mode, color, 0), shape.Root);
            SyncColor(GradStop(mode, color, 2), shape.Tip);
            GradStop(mode, color, 0).Clear = shape.RootClear; // SyncColor は色が同じだと何もしないため別に入れる
            GradStop(mode, color, 2).Clear = shape.TipClear;
            if (shape.ThreeColors) { SyncColor(GradStop(mode, color, 1), shape.Middle); GradStop(mode, color, 1).Clear = shape.MiddleClear; g.MiddleKnown = true; }
            var blend = HairColorCore.GetDecalBlendMode(first);
            for (var i = 0; i < PartsPerMode; i++) GradStop(mode, color, i).BlendKey = blend;
        }

        void RememberOriginal(SingleGradationDecal decal)
        {
            var id = decal.GetInstanceID();
            if (_originals.Any(o => o.DecalInstanceId == id)) return;
            _originals.Add(new OriginalValue
            {
                DecalInstanceId = id,
                Color = HairColorCore.GetDecalColor(decal),
                BlendKey = HairColorCore.GetDecalBlendMode(decal),
                Gradient = HairColorCore.GetDecalGradientRaw(decal),
                Clear = HairColorCore.IsDecalColorless(decal),
            });
        }

        OriginalValue FindOriginal(SingleGradationDecal decal)
        {
            if (decal == null) return null;
            var id = decal.GetInstanceID();
            return _originals.FirstOrDefault(o => o.DecalInstanceId == id);
        }

        static void SyncFromDecal(PartState st, SingleGradationDecal decal)
        {
            SyncColor(st, HairColorCore.GetDecalColor(decal));
            st.BlendKey = HairColorCore.GetDecalBlendMode(decal);
            st.Clear = HairColorCore.IsDecalColorless(decal);
        }

        static void SyncColor(PartState st, Color c)
        {
            if (ColorEquals(c, st.Color)) return;
            st.Color = c;
            Color.RGBToHSV(c, out var h, out _, out _);
            st.Hue = h;
        }

        static bool ColorEquals(Color a, Color b)
        {
            const float eps = 1f / 512f;
            return Mathf.Abs(a.r - b.r) < eps && Mathf.Abs(a.g - b.g) < eps && Mathf.Abs(a.b - b.b) < eps;
        }

        // IMGUIContainer の先頭と画面全体の MouseUp から呼ぶ
        void OnMouseReleased()
        {
            FlushGlowPending();
            if (_ringDragged) { _ringDragged = false; if (!Current.Clear) PushRecent(Current.Color); }
            if (_dragUndoGroup >= 0)
            {
                Undo.CollapseUndoOperations(_dragUndoGroup);
                _dragUndoGroup = -1;
            }
        }

        static void SectionHeader(string text)
        {
            GUILayout.Label(text, ImguiSkin.Bold);
        }

        static string PartLabel(ColoringMode mode, int index)
        {
            var labels = ColoringModes.Get(mode).PartLabels;
            return index < labels.Length ? labels[index] : (index + 1) + "色目";
        }

        string AutoFoundMessage()
        {
            if (_target == null || !_target.IsValid || _target.FoundInSelection) return null;
            return Selection.activeGameObject != null && !EditorUtility.IsPersistent(Selection.activeGameObject)
                ? "選択中のアバターに髪色の設定が見つからなかったため、Scene 内から自動で見つけました。別のアバターにしたいときは、Hierarchy でそのアバターを選んでください。"
                : "Hierarchy で選んでいないので、シーンの中から自動で見つけました。別のアバターにしたいときは、そのアバターを Hierarchy で選んでください。";
        }

        static string BuildNotFoundMessage()
        {
            var sel = Selection.activeGameObject;
            if (sel != null && EditorUtility.IsPersistent(sel)) sel = null;
            if (sel == null)
            {
                return "髪色の「" + HairColorCore.CustomNodeName + "」が見つかりません。\n" +
                       "Hierarchy でアバターを選んでください。髪色プレハブがアバターの中（子）に入っているかも確認してください。";
            }
            var avatar = AvatarLookup.FindAvatarRoot(sel);
            return "アバター「" + avatar.name + "」の中に髪色の「" + HairColorCore.CustomNodeName + "」が見つかりません。\n" +
                   "髪色プレハブをアバターの中（子）に入れてから、「もう一度さがす」を押してください。";
        }

        static string GetHierarchyPath(Transform t)
        {
            var names = new List<string>();
            for (; t != null; t = t.parent) names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }

        // ---- 状態の注意 ----
        void DrawStateWarnings()
        {
            // HelpBox はテーマの色で描くため自前の枠にする
            if (_bakedMaterialFound)
            {
                GUILayout.Label(BakedToolPresent
                        ? "焼き込んだマテリアルが髪に設定されています。色を変えるときは、先に焼き込みツールで「マテリアルを元に戻す」を押してください。"
                        : "焼き込んだマテリアルが髪に設定されています。色を変えるときは、焼き込みツールを入れて「マテリアルを元に戻す」を押すか、髪のマテリアルを元のものに戻してください。",
                    ImguiSkin.Warn);
                if (BakedToolPresent && GUILayout.Button("焼き込みツールを開く", ImguiSkin.Button, GUILayout.Height(26))) OpenBakedTool();
            }

            if (_inactiveNode != null)
            {
                if (_bakedMaterialFound) GUILayout.Space(4);
                var isProduct = _inactiveNode == HairColorCore.GetProductRoot(_target);
                var msg = isProduct
                    ? "色のセット「" + _inactiveNode.name + "」がオフになっています（焼き込みツールを使ったあとなど）。オンにしないと、ここで選んだ色は髪に出ません。"
                    : "アバターの中の「" + _inactiveNode.name + "」がオフになっているので、色が髪に出ません。";
                GUILayout.Label(msg, ImguiSkin.Warn);
                using (new EditorGUI.DisabledScope(_bakedMaterialFound)) // 焼き込んだ色に重ねて塗らないため
                if (GUILayout.Button("「" + _inactiveNode.name + "」をオンにする", ImguiSkin.Button, GUILayout.Height(26)))
                {
                    Undo.IncrementCurrentGroup();
                    Undo.SetCurrentGroupName(HairColorCore.UndoLabel);
                    HairColorCore.SetActiveWithUndo(_inactiveNode, true);
                    SceneView.RepaintAll();
                    Refresh();
                }
            }

            // 単色でも見る（単色も目印で塗る毛束を決めるため）
            var selOff = SelectorOffNode();
            if (selOff != null)
            {
                if (_bakedMaterialFound || _inactiveNode != null) GUILayout.Space(4);
                GUILayout.Label("毛束の指定「" + selOff.name + "」がオフになっているので、" + ColoringModes.Get(_mode).Label + "の色が髪に出ません。Hierarchy で「" + selOff.name + "」をオンにしてください。", ImguiSkin.Warn);
            }
        }

        // ---- 色を選ぶ ----
        void ChoosePreset(PresetColor tile)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(HairColorCore.UndoLabel);
            Undo.RecordObject(this, HairColorCore.UndoLabel);
            HairColorCore.SelectPresetColor(_target, _tiles, tile, false);
            SceneView.RepaintAll();
            Refresh();
        }

        // 「前のカスタムの続きから」もこれ（色は写さない）
        void ChooseCustom()
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(HairColorCore.UndoLabel);
            Undo.RecordObject(this, HairColorCore.UndoLabel);
            ChooseCustomCore();
            SceneView.RepaintAll();
            Refresh();
        }

        // Undo のグループは呼ぶ側
        void ChooseCustomCore()
        {
            if (!_customOn)
            {
                // 戻り先はオフ→オンのときだけ覚える（押し直しで上書きしないため）
                var selected = _tiles.Where(t => t.Selected).ToList();
                if (selected.Count == 0) selected = _tiles.Where(t => t.Node != null && t.Node.activeSelf).ToList();
                _presetBeforeCustom = selected.Count == 1 ? selected[0].RelPath : "";
            }
            HairColorCore.SelectPresetColor(_target, _tiles, null, true);
            HairColorCore.SetColoringMode(_target, _mode);
        }

        // プリセットのモードと色を Custom に写してからカスタムをオンにする（Undo 1回）
        // モードが商品に無い色（引退した Inner など）は写さない
        void StartCustomFrom(PresetColor tile)
        {
            if (_target == null || !_target.IsValid) return;
            var mode = tile != null ? HairColorCore.ColoringModeOfPresetGroup(_target, tile.GroupName) : null;
            if (!mode.HasValue) { ChooseCustom(); return; }
            var m = mode.Value;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(HairColorCore.UndoLabel);
            var group = Undo.GetCurrentGroup();
            Undo.RecordObject(this, HairColorCore.UndoLabel);
            if (_mode != m) StopPicking();
            _mode = m;
            _partIndex = 0;
            _stopIndex = 0;
            _subTab = SubTab.Color;
            SaveUiPrefs();
            CopyPresetToCustom(tile, m);
            ChooseCustomCore();
            Undo.CollapseUndoOperations(group);
            SceneView.RepaintAll();
            Refresh();
        }

        void StartCustomFromDefault()
        {
            if (_target == null || !_target.IsValid || !_target.Has(ColoringMode.Default)) { ChooseCustom(); return; }
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(HairColorCore.UndoLabel);
            var group = Undo.GetCurrentGroup();
            Undo.RecordObject(this, HairColorCore.UndoLabel);
            if (_mode != ColoringMode.Default) StopPicking();
            _mode = ColoringMode.Default;
            _partIndex = 0;
            _stopIndex = 0;
            _subTab = SubTab.Color;
            SaveUiPrefs();
            var decals = _target.ModeParts[ColoringMode.Default].Decals;
            for (var c = 0; c < decals.Count; c++)
            {
                if (decals[c] == null) continue;
                foreach (var d in WithFollowers(ColoringMode.Default, decals[c])) HairColorCore.SetDecalColor(d, HairColorCore.GetDecalColor(d), true);
                if (c < PartsPerMode) State(ColoringMode.Default, c).Clear = true;
            }
            ChooseCustomCore();
            Undo.CollapseUndoOperations(group);
            SceneView.RepaintAll();
            Refresh();
        }

        // Undo のグループは呼ぶ側
        void CopyPresetToCustom(PresetColor tile, ColoringMode m)
        {
            var src = HairColorCore.GetPresetColorDecals(tile);
            if (src.Count == 0 || !_target.Has(m)) return;
            var info = ColoringModes.Get(m);
            var mt = _target.ModeParts[m];
            var theme = _themes.FirstOrDefault(t => t.Colors.Contains(tile.RelPath));
            if (theme != null && mt.Glow != null) HairColorCore.SetGlowPattern(mt.Glow, theme.GlowPattern);
            if (info.IsGradient)
            {
                var sample = HairColorCore.PickGradientSampleDecal(src);
                foreach (var d in mt.Decals)
                {
                    if (d == null) continue;
                    var same = src.FirstOrDefault(s => string.Equals(s.name, d.name, StringComparison.OrdinalIgnoreCase)) ?? sample;
                    var g = HairColorCore.GetDecalGradientRaw(same);
                    if (g != null) HairColorCore.SetDecalGradientRaw(d, g);
                    HairColorCore.SetDecalBlendMode(d, HairColorCore.GetDecalBlendMode(same));
                }
                for (var i = 0; i < PartsPerMode; i++) State(m, i).Tone = Tone.None;
                return;
            }

            SingleGradationDecal[] from;
            if (info.PartLabels.Length >= 2 && HairColorCore.OrderPresetColorDecals(tile, src, out var first, out var second, out var third)) from = third != null ? new[] { first, second, third } : new[] { first, second };
            else from = new[] { src[0] };
            for (var c = 0; c < mt.Decals.Count; c++)
            {
                var lead = mt.Decals[c];
                if (lead == null) continue;
                var s = from[Mathf.Min(c, from.Length - 1)];
                var color = HairColorCore.GetDecalColor(s);
                color.a = 1f;
                var key = HairColorCore.GetDecalBlendMode(s);
                // 単色の Decal にも根元の色を書く（グラデをオフにしたとき根元の色に戻すため）
                var gradName = c >= from.Length && from.Length == 2 ? mt.Decals[1].name : lead.name;
                var grad = mt.Grad != null && c < mt.Grad.ColorCount ? HairColorCore.GetPresetPerColorGradientDecals(tile, s, gradName) : new List<SingleGradationDecal>();
                bool gradOn = grad.Count > 0 && mt.Grad.Decals[c].Count > 0;
                if (mt.Grad != null && c < mt.Grad.ColorCount && HairColorCore.IsPerColorGradientOn(mt.Grad, c) != gradOn) SetColorGradNodes(m, c, gradOn);
                if (gradOn)
                {
                    foreach (var d in mt.Grad.Decals[c])
                    {
                        var same = grad.FirstOrDefault(x => string.Equals(x.name, d.name, StringComparison.OrdinalIgnoreCase)) ?? grad[0];
                        var g = HairColorCore.GetDecalGradientRaw(same);
                        if (g != null) HairColorCore.SetDecalGradientRaw(d, g);
                        HairColorCore.SetDecalBlendMode(d, HairColorCore.GetDecalBlendMode(same));
                    }
                }
                // 無色は根元の透明度で見る（色キーには隠した色が残っているため）
                var clear = HairColorCore.GetDecalGradient(s).RootClear;
                foreach (var d in WithFollowers(m, lead))
                {
                    HairColorCore.SetDecalColor(d, color, clear);
                    HairColorCore.SetDecalBlendMode(d, key);
                }
                if (c >= PartsPerMode) continue;
                var st = State(m, c);
                SyncColor(st, color);
                st.Clear = clear;
                st.Tone = Tone.None;
                st.BlendKey = key;
                SyncGlowColorToHair(m, c);
            }
        }

        // 覚えていない（古い版で保存したシーン）ときは、オンの色のノードが1つだけならそこ
        PresetColor BackTile()
        {
            if (!string.IsNullOrEmpty(_presetBeforeCustom)) return _tiles.FirstOrDefault(t => t.RelPath == _presetBeforeCustom);
            var on = _tiles.Where(t => t.Node != null && t.Node.activeSelf).ToList();
            return on.Count == 1 ? on[0] : null;
        }

        void LeaveCustom()
        {
            var back = BackTile();
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(HairColorCore.UndoLabel);
            Undo.RecordObject(this, HairColorCore.UndoLabel);
            _presetBeforeCustom = "";
            HairColorCore.SelectPresetColor(_target, _tiles, back, false);
            SceneView.RepaintAll();
            Refresh();
        }

        // ---- カスタムする ----
        // 枠は UI Toolkit のカードが描くため、ここでは囲まない
        void DrawColorDecideIMGUI()
        {
            if (_target == null || !_target.IsValid || !_customOn) return;
            var width = ImguiWidth; // ウィンドウの幅ではない
            SectionHeader("色");
            // なしの間も下は触れる（触ったら色を塗るに戻すため）
            // DisabledScope だと押せず戻れないため、薄くするのは GUI.color で
            var clear = ClearAvailable && Current.Clear;
            var keep = GUI.color;
            if (clear) GUI.color = new Color(keep.r, keep.g, keep.b, keep.a * 0.45f);
            try
            {
                DrawRing();
                GUILayout.Space(4);
                DrawTone();
                GUILayout.Space(4);
                using (new EditorGUI.DisabledScope(clear)) DrawBlend(); // 乗せ方はなしの間は効かないため押せなくする
                GUILayout.Space(6);
                DrawRecentSection(width);
            }
            finally { GUI.color = keep; }
        }

        static void OpenBakedTool()
        {
            if (!EditorApplication.ExecuteMenuItem(ToolInfo.BakedToolMenuPath))
            {
                Debug.LogWarning("[" + ToolInfo.ToolName + "] 焼き込みツールのメニューが見つかりません：" + ToolInfo.BakedToolMenuPath);
            }
        }

        void SetGradientThree(ColoringMode mode, int color, bool on)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(HairColorCore.UndoLabel);
            Undo.RecordObject(this, HairColorCore.UndoLabel);
            var g = Grad(mode, color);
            var decals = GradDecalsOf(mode, color);
            if (on)
            {
                foreach (var d in decals) { var s = PartShape(d, g); var def = DefaultSplits(mode, color, d); s.Split1 = def.Split1; s.Split2 = def.Split2; }
                var first = decals.Count > 0 ? DefaultSplits(mode, color, decals[0]) : EvenSplits;
                g.Split1 = first.Split1;
                g.Split2 = first.Split2;
                var mid = GradStop(mode, color, 1);
                if (!g.MiddleKnown)
                {
                    mid.Color = Color.Lerp(GradStop(mode, color, 0).Color, GradStop(mode, color, 2).Color, 0.5f);
                    Color.RGBToHSV(mid.Color, out var h, out _, out _);
                    mid.Hue = h;
                    mid.Tone = Tone.None;
                    mid.Clear = false;
                    g.MiddleKnown = true;
                }
                mid.BlendKey = GradStop(mode, color, 0).BlendKey;
            }
            else
            {
                g.Split = Mathf.Clamp((g.Split1 + g.Split2) / 2f, 0.05f, 0.95f);
                foreach (var d in decals) { var s = PartShape(d, g); s.Split = Mathf.Clamp((s.Split1 + s.Split2) / 2f, 0.05f, 0.95f); }
                if (ColoringModes.Get(mode).IsGradient) { if (_partIndex == 1) _partIndex = 0; }
                else if (_stopIndex == 1) _stopIndex = 0;
            }
            g.Three = on;
            foreach (var d in decals) PartShape(d, g).ThreeColors = on;
            PushGradient();
        }

        // まとめるのは手を離したとき（OnMouseReleased）
        void BeginDragUndo()
        {
            if (_dragUndoGroup >= 0) return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(HairColorCore.UndoLabel);
            _dragUndoGroup = Undo.GetCurrentGroup();
        }

        void SetMode(ColoringMode mode)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(HairColorCore.UndoLabel);
            Undo.RecordObject(this, HairColorCore.UndoLabel);
            StopPicking(); // 選べるのは今のモードの毛束だけのため
            _mode = mode;
            _partIndex = 0;
            _stopIndex = 0;
            HairColorCore.SetColoringMode(_target, mode);
            SceneView.RepaintAll();
            Refresh();
        }

        void SetPart(int index)
        {
            Undo.RecordObject(this, HairColorCore.UndoLabel);
            _partIndex = index;
            _stopIndex = 0;
            Refresh();
        }

        void DrawRing()
        {
            var st = Current;
            var size = Mathf.Clamp(ImguiWidth - 12f, 160f, 260f);
            var rect = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(true));
            var hue = st.Hue;
            Color.RGBToHSV(st.Color, out _, out var sat, out var val); // 色合いは st.Hue から（灰色・黒で色合いを失わないため）
            var e = Event.current;
            var clearClick = st.Clear && e.type == EventType.MouseDown && e.button == 0 && HueRing.Hits(rect, e.mousePosition); // Draw がイベントを使う前に見る
            var moved = HueRing.Draw(rect, ref hue, ref sat, ref val);
            DrawRingCorners(rect);
            if (!moved)
            {
                if (clearClick) SetClear(false);
                return;
            }

            BeginDragUndo();
            Undo.RecordObject(this, HairColorCore.UndoLabel);
            st.Hue = hue;
            var c = Color.HSVToRGB(Mathf.Repeat(hue, 1f), sat, val, false);
            c.a = 1f;
            st.Color = c;
            st.Tone = ToneTable.Match(sat, val) ?? Tone.None;
            st.Clear = false;
            _ringDragged = true;
            PushColor();
        }

        void DrawTone()
        {
            var st = Current;
            GUILayout.Label("トーン", ImguiSkin.Bold);
            var cells = GridCells(ToneTable.Order.Length);
            for (var i = 0; i < ToneTable.Order.Length; i++)
            {
                var tone = ToneTable.Order[i];
                var on = ToneOf(st) == tone;
                var pressed = GUI.Toggle(cells[i], on, ToneTable.Label(tone), ButtonStyle(i, ToneTable.Order.Length));
                // 選ばれているトーンを押したときは、無色の間だけ色をつけるに戻す
                if (pressed == on || (on && !st.Clear)) continue;
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(this, HairColorCore.UndoLabel);
                st.Tone = tone;
                st.Color = ToneTable.Compose(st.Hue, st.Tone);
                st.Clear = false;
                PushColor();
                PushRecent(st.Color);
            }
        }

        [NonSerialized] float _blendCellWidth;

        void DrawBlend()
        {
            var st = Current;
            GUILayout.Label("色の重ね方（元の髪の影とのなじませ方）", ImguiSkin.Bold);
            var count = BlendPresets.Order.Length + 1;
            var cells = GridCells(count);
            var current = BlendPresets.FromKey(st.BlendKey);
            for (var i = 0; i < BlendPresets.Order.Length; i++)
            {
                var p = BlendPresets.Order[i];
                var on = current.HasValue && current.Value == p;
                if (GUI.Toggle(cells[i], on, BlendPresets.Label(p), ButtonStyle(i, count)) && !on) SetBlend(BlendPresets.Key(p));
            }
            // 列の幅に入らない長い名前は「その他」のままにして、下に名前を出す
            var moreName = BlendPresets.MoreLabel(st.BlendKey);
            var moreText = BlendPresets.MoreButtonLabel(st.BlendKey);
            // 前の描画で測った幅で決める（Layout と Repaint で行の数を変えないため）
            var fits = _blendCellWidth <= 0f || ImguiSkin.Button.CalcSize(new GUIContent(moreText)).x <= _blendCellWidth;
            if (Event.current.type == EventType.Repaint) _blendCellWidth = cells[count - 1].width;
            if (!fits) moreText = BlendPresets.MoreButtonLabel(null);
            var moreOn = moreName != null;
            if (GUI.Toggle(cells[count - 1], moreOn, moreText, ButtonStyle(count - 1, count)) != moreOn) ShowBlendMenu(cells[count - 1]);
            if (moreOn && !fits) GUILayout.Label("いまの重ね方：" + moreName, ImguiSkin.Note);

            if (BlendPresets.IsLight(st.BlendKey))
                GUILayout.Label("ライト系は、明るい髪だと白っぽくなったり、グラデーションの境目に色の線が出たりすることがあります。", ImguiSkin.Note);
            if (!BlendPresets.FromKey(st.BlendKey).HasValue && BlendPresets.MoreLabel(st.BlendKey) == null && !string.IsNullOrEmpty(st.BlendKey))
            {
                GUILayout.Label("いまの設定：" + st.BlendKey + "（この画面のボタンにない設定です）", ImguiSkin.Note);
            }
        }

        void ShowBlendMenu(Rect r)
        {
            var menu = new GenericMenu();
            var current = Current.BlendKey;
            for (var g = 0; g < BlendPresets.MoreGroups.Length; g++)
            {
                if (g > 0) menu.AddSeparator("");
                menu.AddDisabledItem(new GUIContent(BlendPresets.MoreGroups[g].Key));
                foreach (var p in BlendPresets.MoreGroups[g].Value)
                {
                    var key = p.Key;
                    menu.AddItem(new GUIContent(p.Value), current == key, () =>
                    {
                        SetBlend(key);
                        MarkImguiDirty();
                    });
                }
            }
            menu.DropDown(r);
        }

        void SetBlend(string key)
        {
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(this, HairColorCore.UndoLabel);
            Current.BlendKey = key;
            PushBlend();
        }

        static GUIStyle ButtonStyle(int index, int count) => ImguiSkin.Button;

        // 同じ幅で1行に並べる（字の長さで幅が変わらないため）
        static Rect[] GridCells(int count)
        {
            var style = ImguiSkin.Button;
            var height = style.CalcSize(new GUIContent("標準")).y;
            var row = GUILayoutUtility.GetRect(GUIContent.none, style, GUILayout.ExpandWidth(true), GUILayout.Height(height));
            var gap = style.margin.left + style.margin.right;
            var w = (row.width - gap * (count - 1)) / count;
            var cells = new Rect[count];
            for (var i = 0; i < count; i++) cells[i] = new Rect(row.x + i * (w + gap), row.y, w, row.height);
            return cells;
        }

        // only：その Decal だけに書く（ほかのパーツが読み直しで揺れないため）
        // _Cut の Decal は真ん中で切る毛束を単色で塗るため、根元の色を書く
        void PushGradient(SingleGradationDecal only = null)
        {
            HairColorCore.SetColoringMode(_target, _mode);
            var g = Grad(_mode, GradColor);
            if (IsGradientMode)
            {
                HairColorCore.SetDecalsGradientKeepSplits(only != null ? new[] { only } : (IList<SingleGradationDecal>)ModeDecals, CurrentGradientShape(), d => PartShape(d, g));
                SceneView.RepaintAll();
                return;
            }
            var grad = _target.ModeParts[_mode].Grad;
            if (grad == null || _partIndex > 1) return;
            var shape = CurrentGradientShape();
            HairColorCore.SetDecalsGradientKeepSplits(only != null ? new[] { only } : (IList<SingleGradationDecal>)grad.Decals[_partIndex], shape, d => PartShape(d, g));
            // 単色の側は根元の色にそろえる（帯と光の追従に使うため）
            var root = GradStop(_mode, _partIndex, 0);
            var st = State(_mode, _partIndex);
            st.Color = root.Color;
            st.Hue = root.Hue;
            st.Clear = root.Clear;
            var lead = CurrentDecal;
            if (lead != null && _target.ModeParts[_mode].Followers.TryGetValue(lead, out var followers))
                foreach (var d in followers) if (d != null) HairColorCore.SetDecalColor(d, root.Color, root.Clear);
            SyncGlowColorToHair(_mode, _partIndex, throttle: true);
            SceneView.RepaintAll();
        }

        void PushColor()
        {
            if (IsGradientMode || ColorGradOn) { PushGradient(); return; }
            var decal = CurrentDecal;
            if (decal == null) return;
            HairColorCore.SetColoringMode(_target, _mode);
            foreach (var d in WithFollowers(decal)) HairColorCore.SetDecalColor(d, Current.Color, Current.Clear);
            SyncGlowColorToHair(_mode, _partIndex, throttle: true);
            SceneView.RepaintAll();
        }

        // ---- 無色 ----
        bool ClearAvailable => _target != null && _target.IsValid && _customOn
                               && (IsGradientMode || ColorGradOn || CurrentDecal != null);

        void SetClear(bool clear)
        {
            if (!ClearAvailable || Current.Clear == clear) return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(HairColorCore.UndoLabel);
            Undo.RecordObject(this, HairColorCore.UndoLabel);
            Current.Clear = clear;
            PushColor();
        }

        void PushBlend()
        {
            if (IsGradientMode)
            {
                // 混ぜ方は全部の色で共通（Decal ごとに1つしか持てないため）
                HairColorCore.SetColoringMode(_target, _mode);
                for (var i = 0; i < PartsPerMode; i++) State(_mode, i).BlendKey = Current.BlendKey;
                foreach (var d in ModeDecals) HairColorCore.SetDecalBlendMode(d, Current.BlendKey);
                SceneView.RepaintAll();
                return;
            }
            var decal = CurrentDecal;
            if (decal == null) return;
            var key = Current.BlendKey;
            HairColorCore.SetColoringMode(_target, _mode);
            if (ColorGradOn)
            {
                // オフの単色の Decal にも書く（グラデをやめたときにそろえるため）
                for (var i = 0; i < PartsPerMode; i++) GradStop(_mode, _partIndex, i).BlendKey = key;
                State(_mode, _partIndex).BlendKey = key;
                foreach (var d in _target.ModeParts[_mode].Grad.Decals[_partIndex]) HairColorCore.SetDecalBlendMode(d, key);
            }
            foreach (var d in WithFollowers(decal)) HairColorCore.SetDecalBlendMode(d, key);
            SceneView.RepaintAll();
        }

        // ---- リングの四隅と最近使った色 ----
        const double RecentSettleSeconds = 0.8;
        const float RecentSwatchSize = 24f;
        List<Color> _recent = new List<Color>();
        bool _ringDragged;
        bool _recentPending;
        Color _recentPendingColor;
        double _recentPendingAt;
        static GUIStyle s_noneLabel;

        static void CornerLabel(Rect under, string text)
        {
            if (s_noneLabel == null) s_noneLabel = new GUIStyle(ImguiSkin.Note) { alignment = TextAnchor.UpperCenter, wordWrap = false, padding = new RectOffset(), margin = new RectOffset() };
            GUI.Label(new Rect(under.x - 10f, under.yMax + 1f, under.width + 20f, 14f), text, s_noneLabel);
        }

        void DrawRingCorners(Rect rect)
        {
            var st = Current;
            var side = Mathf.Min(rect.width, rect.height);
            var sq = new Rect(rect.x + (rect.width - side) * 0.5f, rect.y + (rect.height - side) * 0.5f, side, side);
            const float swatch = 22f;
            Color? picked = null;

            var cur = new Rect(sq.x, sq.y, swatch + 22f, swatch); // 右端にスポイトのボタンが付くため
            EditorGUI.BeginChangeCheck();
            var c = EditorGUI.ColorField(cur, GUIContent.none, st.Color, true, false, false);
            if (EditorGUI.EndChangeCheck()) picked = c;
            CornerLabel(new Rect(cur.x, cur.y, swatch, swatch), "今の色");

            if (ClearAvailable)
            {
                var none = new Rect(sq.xMax - swatch, sq.y, swatch, swatch);
                var keep = GUI.color;
                if (st.Clear) GUI.color = Color.white;
                NoneSwatch.Draw(none, st.Clear);
                CornerLabel(none, NoneSwatch.Label);
                GUI.color = keep;
                EditorGUIUtility.AddCursorRect(none, MouseCursor.Link);
                var tip = st.Clear ? NoneSwatch.Label + "（もう一度押すと前の色に戻ります）" : NoneSwatch.Label + "（" + NoneSwatch.Hint + "）";
                if (GUI.Button(none, new GUIContent("", tip), GUIStyle.none)) SetClear(!st.Clear);
            }

            const float codeWidth = 64f;
            var code = new Rect(Mathf.Min(sq.xMax - codeWidth + 14f, rect.xMax - codeWidth), sq.yMax - 18f, codeWidth, 18f);
            GUI.Label(new Rect(code.x - 12f, code.y, 12f, code.height), "#", ImguiSkin.Label);
            var now = ColorUtility.ToHtmlStringRGB(st.Color);
            var typed = EditorGUI.DelayedTextField(code, now);
            if (typed != now && ColorUtility.TryParseHtmlString("#" + typed.Trim().TrimStart('#'), out var fromCode)) picked = fromCode;

            if (picked.HasValue) ApplyPickedColor(picked.Value);
        }

        void PushRecent(Color c)
        {
            _recentPending = false;
            RecentColors.Push(c);
            _recent = RecentColors.Load();
            Repaint();
        }

        void FlushRecentPending()
        {
            if (!_recentPending) return;
            PushRecent(_recentPendingColor);
        }

        void ApplyPickedColor(Color p)
        {
            var st = Current;
            p.a = 1f;
            Undo.RecordObject(this, HairColorCore.UndoLabel);
            st.Color = p;
            Color.RGBToHSV(p, out var h, out var sat, out var val);
            if (sat > 0f) st.Hue = h; // 灰色・黒・白は色合いを持たないため前のまま
            st.Tone = ToneTable.Match(sat, val) ?? Tone.None;
            st.Clear = false;
            PushColor();
            _recentPending = true;
            _recentPendingColor = p;
            _recentPendingAt = EditorApplication.timeSinceStartup;
        }

        static Tone? ToneOf(PartState st)
        {
            Color.RGBToHSV(st.Color, out _, out var sat, out var val);
            return ToneTable.Match(sat, val);
        }

        void DrawRecentSection(float width)
        {
            var st = Current;
            if (_recent.Count == 0) return;
            GUILayout.Label("最近使った色", ImguiSkin.Bold);
            var perRow = Mathf.Max(1, Mathf.FloorToInt((width - 28f) / (RecentSwatchSize + 4f)));
            int clickedIndex = -1;
            bool alt = false;
            for (var start = 0; start < _recent.Count; start += perRow)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (var i = start; i < Mathf.Min(_recent.Count, start + perRow); i++)
                    {
                        var r = GUILayoutUtility.GetRect(RecentSwatchSize, RecentSwatchSize, GUILayout.Width(RecentSwatchSize), GUILayout.Height(RecentSwatchSize));
                        if (Event.current.type == EventType.Repaint)
                        {
                            EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.4f));
                            EditorGUI.DrawRect(new Rect(r.x + 1f, r.y + 1f, r.width - 2f, r.height - 2f), _recent[i]);
                        }
                        EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
                        var tip = "#" + ColorUtility.ToHtmlStringRGB(_recent[i]) + "（クリックでこの色にする。Alt＋クリックで最近の色から外す）";
                        if (GUI.Button(r, new GUIContent("", tip), GUIStyle.none)) { clickedIndex = i; alt = Event.current.alt; }
                    }
                    GUILayout.FlexibleSpace();
                }
            }
            GUILayout.Label("クリックでこの色にします。Alt＋クリックで最近の色から外します。", ImguiSkin.Note);
            if (clickedIndex < 0) return;
            if (alt)
            {
                RecentColors.RemoveAt(clickedIndex);
                _recent = RecentColors.Load();
                Repaint();
                return;
            }
            var pick = _recent[clickedIndex];
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(HairColorCore.UndoLabel);
            Undo.RecordObject(this, HairColorCore.UndoLabel);
            st.Color = pick;
            Color.RGBToHSV(pick, out var hue, out _, out _);
            st.Hue = hue;
            st.Tone = Tone.None;
            st.Clear = false;
            PushColor();
        }

#endif
    }
}
#endif
