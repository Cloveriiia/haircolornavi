#if UNITY_EDITOR
// versionDefines では「無いとき」のシンボルを作れないため、PRESENT の裏返しで作る
#if !AC_CHCT_TTT_PRESENT
#define TTT_ABSENT
#endif

#if !TTT_ABSENT
using System.Collections.Generic;
using System.Linq;
using net.rs64.TexTransTool;
using net.rs64.TexTransTool.Decal;
using net.rs64.TexTransTool.IslandSelector;
using UnityEditor;
using UnityEngine;

namespace AtelierCloveria.CustomHairColorTool
{
    public sealed partial class CustomHairColorToolWindow
    {
        [SerializeField] List<int> _pinOrigIds = new List<int>(); // Unpack した商品で「最初に戻す」に使う
        [SerializeField] List<bool> _pinOrigOn = new List<bool>();
        Dictionary<int, bool> _pinOrig;
        readonly Dictionary<int, int> _pinBeforeUnpaint = new Dictionary<int, int>();

        bool _picking;
        ColoringMode _pickMode;
        List<StrandIsland> _pickIslands;
        StrandColorPins _pickMatchedTo;
        int _pickSignature;
        double _pickCheckedAt;
        readonly Dictionary<SceneView, bool> _pickMouseMoveBefore = new Dictionary<SceneView, bool>();
        int _pickHover = -1;
        string _pickMessage;
        double _pickMessageAt;
        [SerializeField] bool _pickHidTools; // スクリプトの読み直し後も戻せるよう保存する
        int[] _pickCounts = new int[5];
        // 選んでいる間は光を止める（毛束を選ぶたびに光の画像を作り直して重いため）
        // 側とモードは保存する（スクリプトの読み直しで終わっても戻せるように）
        [SerializeField] int _pickGlowSide = -1;
        [SerializeField] int _pickGlowMode;
        [System.NonSerialized] ColoringProduct _pickGlowTarget;
        bool _pickDiffers;
        GUIStyle _sceneLabelStyle;
        PickAction _pickAction = PickAction.Swap;

        const double PickMessageSeconds = 4.0;
        const double PickRecheckSeconds = 0.25;

        StrandColorPins CurrentPins => _target != null && _target.IsValid && _target.Has(_mode) ? _target.ModeParts[_mode].Pins : null;

        bool PaintPick => CurrentPins != null && CurrentPins.Kind == PinKind.Paint;

        bool PickAvailable => _picking && _target != null && _target.IsValid && _customOn && _mode == _pickMode && CurrentPins != null;

        Dictionary<int, bool> PinOriginals
        {
            get
            {
                if (_pinOrig != null) return _pinOrig;
                _pinOrig = new Dictionary<int, bool>();
                for (var i = 0; i < Mathf.Min(_pinOrigIds.Count, _pinOrigOn.Count); i++) _pinOrig[_pinOrigIds[i]] = _pinOrigOn[i];
                return _pinOrig;
            }
        }

        // つながりの無い目印は最初に見たときの値を覚える（ウィンドウの中だけ）
        void RefreshPick()
        {
            if (!_picking && _pickHidTools) { Tools.hidden = false; _pickHidTools = false; } // 選んでいる途中でスクリプトが読み直された
            if (!_picking && _pickGlowSide >= 0) ResumePickGlow();
            _pickCounts = new int[5];
            _pickDiffers = false;
            if (_target == null || !_target.IsValid) return;
            foreach (var mt in _target.ModeParts.Values)
            {
                if (mt.Pins == null) continue;
                foreach (var pin in mt.Pins.AllPins)
                {
                    if (pin == null || PrefabUtility.GetCorrespondingObjectFromSource(pin.gameObject) != null) continue;
                    var id = pin.gameObject.GetInstanceID();
                    if (PinOriginals.ContainsKey(id)) continue;
                    PinOriginals[id] = pin.gameObject.activeSelf;
                    _pinOrigIds.Add(id);
                    _pinOrigOn.Add(pin.gameObject.activeSelf);
                }
            }
            var ps = CurrentPins;
            if (ps == null) return;
            _pickCounts = HairColorCore.CountStrandsPerColor(ps);
            foreach (var pin in ps.AllPins)
            {
                var want = HairColorCore.GetOriginalStrandPinActive(pin, PinOriginals);
                if (want.HasValue && pin.gameObject.activeSelf != want.Value) { _pickDiffers = true; break; }
            }
        }

        // オフだと TTT はその Decal で何も塗らない（単色も目印で塗る毛束を決めるため単色にも効く）
        GameObject SelectorOffNode()
        {
            if (_target == null || !_target.IsValid || !_customOn || !_target.Has(_mode)) return null;
            var product = HairColorCore.GetProductRoot(_target);
            if (product == null || !product.activeInHierarchy) return null; // 商品ごとオフのときは別の注意（_inactiveNode）
            foreach (var d in _target.ModeParts[_mode].Decals)
            {
                var sel = d != null ? d.IslandSelector : null;
                if (sel == null || sel.gameObject.activeInHierarchy) continue;
                GameObject top = null;
                for (var t = sel.transform; t != null && t != product.transform; t = t.parent) if (!t.gameObject.activeSelf) top = t.gameObject;
                if (top != null) return top;
            }
            return null;
        }

        string FreshPickMessage => !string.IsNullOrEmpty(_pickMessage) && EditorApplication.timeSinceStartup - _pickMessageAt < PickMessageSeconds ? _pickMessage : null;

        void StartPicking()
        {
            var ps = CurrentPins;
            if (ps == null) return;
            _pickMode = _mode;
            _pickIslands = null;
            _pickMatchedTo = null;
            _pickHover = -1;
            if (!EnsurePickIslands() || _pickIslands.Count == 0)
            {
                SetPickMessage("Scene でクリックできる髪が見つかりません（髪の Renderer がオフになっていないか確かめてください）。");
                return;
            }
            if (!_picking)
            {
                SceneView.duringSceneGui -= OnPickSceneGUI;
                SceneView.duringSceneGui += OnPickSceneGUI;
                EditorApplication.playModeStateChanged -= OnPickPlayModeChanged;
                EditorApplication.playModeStateChanged += OnPickPlayModeChanged; // ドメインを読み直さない設定でも Play に入ったら終える
                if (!Tools.hidden) { Tools.hidden = true; _pickHidTools = true; } // ハンドルにクリックを取られないため（終えたら戻す）
                PausePickGlow();
            }
            _picking = true;
            SetPickMessage(null);
            SceneView.RepaintAll();
            Repaint();
        }

        void StopPicking()
        {
            if (_picking)
            {
                SceneView.duringSceneGui -= OnPickSceneGUI;
                EditorApplication.playModeStateChanged -= OnPickPlayModeChanged;
                if (_pickHidTools) { Tools.hidden = false; _pickHidTools = false; }
            }
            ResumePickGlow();
            foreach (var kv in _pickMouseMoveBefore) if (kv.Key != null) kv.Key.wantsMouseMove = kv.Value;
            _pickMouseMoveBefore.Clear();
            _picking = false;
            _pickHover = -1;
            _pickIslands = null;
            _pickMatchedTo = null;
            SceneView.RepaintAll();
            Repaint();
        }

        void PausePickGlow()
        {
            if (_target == null || !_target.IsValid || !_target.Has(_mode)) return;
            var side = HairColorCore.GetGlowingColorIndex(_target.ModeParts[_mode].Glow);
            if (side < 0) return;
            _pickGlowSide = side;
            _pickGlowMode = (int)_mode;
            _pickGlowTarget = _target;
            HairColorCore.SetGlowingColorIndex(_target, _mode, -1);
            QueueRefresh(); // Refresh の中から呼ばれることがあるため
        }

        // 選んでいる間に光らせる側を自分で選んだら戻さない
        void ResumePickGlow()
        {
            if (_pickGlowSide < 0) return;
            var side = _pickGlowSide;
            var mode = (ColoringMode)_pickGlowMode;
            var target = _pickGlowTarget != null && _pickGlowTarget.IsValid ? _pickGlowTarget : _target;
            _pickGlowSide = -1;
            _pickGlowTarget = null;
            if (target == null || !target.IsValid || !target.Has(mode)) return;
            var g = target.ModeParts[mode].Glow;
            if (g == null || HairColorCore.GetGlowingColorIndex(g) >= 0) return;
            HairColorCore.SetGlowingColorIndex(target, mode, side);
            QueueRefresh();
        }

        void SetPickMessage(string msg)
        {
            _pickMessage = msg;
            _pickMessageAt = EditorApplication.timeSinceStartup;
        }

        bool EnsurePickIslands()
        {
            var ps = CurrentPins;
            if (ps == null) return false;
            var renderers = HairColorCore.FindStrandPickRenderers(_target, ps, out var materials);
            var sig = PickSignature(renderers, _pickIslands);
            if (_pickIslands == null || sig != _pickSignature)
            {
                _pickIslands = StrandPicker.BuildStrandIslands(renderers, materials);
                _pickMatchedTo = null;
                _pickHover = -1;
                sig = PickSignature(renderers, _pickIslands);
            }
            _pickSignature = sig;
            _pickCheckedAt = EditorApplication.timeSinceStartup;
            if (_pickMatchedTo != ps)
            {
                StrandPicker.RefreshPairPoints(ps.Pairs); // 組の位置は Refresh 時点の値のため、毛束と同じ時点で取り直す
                if (!SamePairPoints(_pickMatchedTo, ps)) StrandPicker.MatchIslandsToPairs(_pickIslands, ps.Pairs);
                _pickMatchedTo = ps;
            }
            return true;
        }

        bool EnsurePickIslandsOnMove()
        {
            if (_pickIslands != null && _pickMatchedTo != null && _pickMatchedTo == CurrentPins && EditorApplication.timeSinceStartup - _pickCheckedAt < PickRecheckSeconds) return true;
            return EnsurePickIslands();
        }

        static bool SamePairPoints(StrandColorPins before, StrandColorPins now)
        {
            if (before == null || before.Pairs.Count != now.Pairs.Count) return false;
            for (var i = 0; i < now.Pairs.Count; i++)
            {
                var a = before.Pairs[i];
                var b = now.Pairs[i];
                if (a.Name != b.Name || a.Point != b.Point || a.Alt.Count != b.Alt.Count) return false;
                for (var k = 0; k < a.Alt.Count; k++) if (a.Alt[k] != b.Alt[k]) return false;
            }
            return true;
        }

        static int PickSignature(List<Renderer> renderers, List<StrandIsland> islands)
        {
            unchecked
            {
                var h = 17;
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    h = h * 31 + r.GetInstanceID();
                    var mf = r.GetComponent<MeshFilter>();
                    var mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : mf != null ? mf.sharedMesh : null;
                    h = h * 31 + (mesh != null ? mesh.GetInstanceID() : 0);
                    foreach (var m in r.sharedMaterials) h = h * 31 + (m != null ? m.GetInstanceID() : 0);
                }
                if (islands == null) return h;
                foreach (var r in islands.Select(i => i.Renderer).Distinct())
                {
                    if (r == null) continue;
                    h = h * 31 + r.transform.localToWorldMatrix.GetHashCode();
                    if (!(r is SkinnedMeshRenderer s)) continue;
                    if (s.rootBone != null) h = h * 31 + s.rootBone.localToWorldMatrix.GetHashCode();
                    foreach (var b in s.bones) if (b != null) h = h * 31 + b.localToWorldMatrix.GetHashCode();
                    var n = s.sharedMesh != null ? s.sharedMesh.blendShapeCount : 0;
                    for (var i = 0; i < n; i++) h = h * 31 + s.GetBlendShapeWeight(i).GetHashCode();
                }
                return h;
            }
        }

        void OnPickPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode) StopPicking();
        }

        // Scene のクリックとバッチの確かめの両方から呼ぶ。1クリック＝Undo 1回
        PickResult PickAt(Ray ray, PickAction action)
        {
            if (!EnsurePickIslands()) return PickResult.NoPin;
            var i = StrandPicker.RaycastStrand(_pickIslands, ray, out _);
            if (i < 0) return PickResult.NoChange;
            return PickIslandAt(i, action == PickAction.Unpaint);
        }

        // Alt＋クリックは何もしない（Scene の回転に譲る。Use もしない）
        bool TryPickClick(Event e, Ray ray)
        {
            if (e == null || e.type != EventType.MouseDown || e.button != 0) return false;
            if (e.alt) return false;
            PickAt(ray, _pickAction);
            try { e.Use(); } catch (System.Exception) { } // バッチの確かめで作った Event でも落ちないように
            return true;
        }

        void SetPickAction(PickAction a)
        {
            if (_pickAction == a) return;
            _pickAction = a;
            SaveUiPrefs();
            SceneView.RepaintAll();
        }

        PickResult PickIslandAt(int islandIndex, bool exclude)
        {
            // Pairs の添字は _pickMatchedTo（EnsurePickIslands のあとの今の StrandColorPins）
            var ps = _pickMatchedTo;
            if (ps == null || ps != CurrentPins || _pickIslands == null || islandIndex < 0 || islandIndex >= _pickIslands.Count) return PickResult.NoPin;
            var isl = _pickIslands[islandIndex];
            var name = isl.Pairs.Count > 0 ? ps.Pairs[isl.Pairs[0]].Name : isl.Renderer != null ? isl.Renderer.name : "";

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(HairColorCore.UndoLabel);
            var group = Undo.GetCurrentGroup();
            var result = HairColorCore.ToggleStrandColor(ps, isl.Pairs, exclude, out var side, PinOriginals, _pinBeforeUnpaint);
            Undo.CollapseUndoOperations(group);

            switch (result)
            {
                case PickResult.Cut:
                    SetPickMessage(name + "：この毛束は真ん中で分かれる毛束なので、ここでは変えられません。");
                    break;
                case PickResult.NoPin:
                    SetPickMessage("この毛束の目印が見つかりません（ポーズ・シェイプキーを初期に戻してから、もう一度「シーン上で毛束を選ぶ」を押してください）。");
                    break;
                case PickResult.NoChange:
                    SetPickMessage(name + "：もう" + SideText(side) + "になっています。");
                    break;
                default:
                    SetPickMessage(name + "：" + (ps.Kind == PinKind.Paint && side == 0 ? "塗るに戻しました" : SideText(side) + "にしました") + "（Ctrl+Z で戻せます）。");
                    _strandNoticePending = ps.Kind == PinKind.Paint ? StrandTabLabel(_mode) + "で選んだ毛束" : ColoringModes.Get(_mode).Label + "の分け方";
                    break;
            }
            SceneView.RepaintAll();
            Refresh();
            Repaint();
            return result;
        }

        string SideText(int side) => PaintPick ? (side == 0 ? "塗る" : "デフォルトの髪") : side >= 0 ? PartLabel(_mode, side) : "デフォルトの髪";

        void ResetPick()
        {
            var ps = CurrentPins;
            if (ps == null) return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(HairColorCore.UndoLabel);
            var group = Undo.GetCurrentGroup();
            var n = HairColorCore.ResetStrandColors(ps, PinOriginals);
            Undo.CollapseUndoOperations(group);
            _pinBeforeUnpaint.Clear();
            _strandNoticePending = null;
            SetPickMessage(n > 0 ? StrandTabLabel(_mode) + "前に戻しました（" + n + " か所）。" : StrandTabLabel(_mode) + "前のままです。");
            SceneView.RepaintAll();
            Refresh();
            Repaint();
        }

        string HoverText()
        {
            var ps = _pickMatchedTo;
            if (ps == null || ps != CurrentPins || _pickIslands == null || _pickHover < 0 || _pickHover >= _pickIslands.Count) return null;
            var isl = _pickIslands[_pickHover];
            if (isl.Pairs.Count == 0) return (isl.Renderer != null ? isl.Renderer.name : "") + "（この毛束の目印が見つかりません。ポーズ・シェイプキーを初期に戻してください）";
            var p = ps.Pairs[isl.Pairs[0]];
            if (isl.Pairs.Any(i => ps.Pairs[i].IsCut)) return p.Name + "（真ん中で分かれる毛束。変えられません）";
            if (ps.Kind == PinKind.Paint) return p.Name + (p.OnA ? "（いま 塗る → クリックでデフォルトの髪）" : "（いま デフォルトの髪 → クリックで塗る）");
            var now = p.OnC ? 2 : p.OnB ? 1 : p.OnA ? 0 : -1;
            if (_pickAction == PickAction.Unpaint) return p.Name + (now < 0 ? "（いま デフォルトの髪 → クリックで前の色に戻す）" : "（いま " + SideText(now) + " → クリックでデフォルトの髪）");
            var next = ps.ColorCount > 2 ? (now < 0 ? 0 : (now + 1) % 3) : now == 0 ? 1 : 0;
            return p.Name + "（いま " + SideText(now) + " → クリックで " + SideText(next) + "）";
        }

        GUIStyle SceneLabelStyle
        {
            get
            {
                if (_sceneLabelStyle == null) _sceneLabelStyle = new GUIStyle(EditorStyles.helpBox) { fontSize = 12, fontStyle = FontStyle.Bold, wordWrap = false };
                return _sceneLabelStyle;
            }
        }

        void OnPickSceneGUI(SceneView sv)
        {
            if (this == null) { SceneView.duringSceneGui -= OnPickSceneGUI; return; }
            if (!PickAvailable) { StopPicking(); RequestUiSync(); return; }
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { e.Use(); StopPicking(); RequestUiSync(); return; }

            if (!_pickMouseMoveBefore.ContainsKey(sv)) _pickMouseMoveBefore[sv] = sv.wantsMouseMove;
            sv.wantsMouseMove = true; // カーソルを動かすだけで MouseMove を届けるため（終えたら戻す）
            var controlId = GUIUtility.GetControlID(FocusType.Passive); // 毎イベント取る（イベントごとに数がずれないため）
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(controlId); // Scene の選択を取らせない

            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            {
                if (EnsurePickIslandsOnMove())
                {
                    var over = StrandPicker.RaycastStrand(_pickIslands, HandleUtility.GUIPointToWorldRay(e.mousePosition), out _);
                    if (over != _pickHover) { _pickHover = over; sv.Repaint(); }
                }
            }

            if (e.type == EventType.MouseDown && e.button == 0) TryPickClick(e, HandleUtility.GUIPointToWorldRay(e.mousePosition));

            if (e.type == EventType.Repaint && _pickIslands != null && _pickHover >= 0 && _pickHover < _pickIslands.Count)
                StrandPicker.DrawStrandOutline(_pickIslands[_pickHover], new Color(1f, 0.9f, 0f, 0.8f), sv.camera.transform.position);

            Handles.BeginGUI();
            var y = 10f;
            void Line(string s)
            {
                if (string.IsNullOrEmpty(s)) return;
                var size = SceneLabelStyle.CalcSize(new GUIContent(s));
                GUI.Label(new Rect(10f, y, Mathf.Min(size.x + 8f, sv.position.width - 20f), 24f), s, SceneLabelStyle);
                y += 28f;
            }
            if (PaintPick)
            {
                Line(StrandTabLabel(_mode) + "：髪をクリックして、塗らない毛束（デフォルトの髪）を選択中（Esc で完了）");
                Line("クリック＝塗る ⇄ デフォルトの髪 を切り替える");
            }
            else
            {
                Line(StrandTabLabel(_mode) + "：髪をクリックして分け方を変更中（Esc で完了）");
                Line(_pickAction == PickAction.Unpaint
                    ? "クリック＝デフォルトの髪にする（もう一度クリックで戻す）"
                    : CurrentPins.ColorCount > 2
                        ? "クリック＝" + PartLabel(_mode, 0) + " → " + PartLabel(_mode, 1) + " → " + PartLabel(_mode, 2) + " の順に変える"
                        : "クリック＝" + PartLabel(_mode, 0) + " ⇄ " + PartLabel(_mode, 1) + " を入れ替える");
            }
            Line(HoverText());
            if (!string.IsNullOrEmpty(_pickMessage) && EditorApplication.timeSinceStartup - _pickMessageAt < PickMessageSeconds) Line(_pickMessage);
            Handles.EndGUI();
        }
    }
}
#endif
#endif
