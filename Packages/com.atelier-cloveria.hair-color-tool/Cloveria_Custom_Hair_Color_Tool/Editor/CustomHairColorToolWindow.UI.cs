#if UNITY_EDITOR
// versionDefines では「無いとき」のシンボルを作れないため、PRESENT の裏返しで作る
#if !AC_CHCT_TTT_PRESENT
#define TTT_ABSENT
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AtelierCloveria.CustomHairColorTool
{
    // 画面の言葉（変えるのはここだけ）
    static class UiText
    {
        public const string TabPick = "色を選ぶ";
        public const string TabCustom = "カスタムする";
        public const string SubColor = "色";
        public const string SubGlow = "発光調整";
        public const string StrandSuffix = "調整"; // モードの名前に付ける
        public const string StrandTabPaint = "毛束調整"; // 単色・グラデーション用
        public const string ResetSplits = "境目を初期位置に戻す";
        public const string GateStartFrom = "をベースにする";
        public const string GateStartDefault = "デフォルトの髪をベースにする";
        public const string GateContinue = "前回の続きから編集";
        public const string GateContinueNone = "（まだありません）";
        public const string InfoMissing = "この髪の色のセットが見つかりません。BOOTH で買った商品の unitypackage を入れてください。";
        public const string InfoShop = "BOOTH のショップを開く";
        public static string InfoToolTooOld(string need) => "この商品には道具 " + need + " 以上が必要です。VCC（ALCOM）で Cloveria Custom Hair Color Tool を更新してください。";
    }

    // 注意・色・発光調整の注意と見本は IMGUIContainer のまま、ほかは UI Toolkit
    // タブや絞り込みは EditorPrefs の見るだけの状態。Hierarchy に書くのはボタン・タイル・つまみを押したときだけ
    public sealed partial class CustomHairColorToolWindow
    {
        // どちらでも見つからなければ Unity 標準の見た目（文字は読める）
        const string StyleSheetGuid = "2d429471e9d34a6ea07f301542dfed95";
        const string StyleSheetPath = "Assets/Atelier_Cloveria/Tools/Cloveria_Custom_Hair_Color_Tool/Editor/CustomHairColorTool.uss";

        // フォントは USS の .chct-heading で指定
        const string HeadingClass = "chct-heading";

        const string DisplayName = ToolInfo.ToolName;

        static readonly Color HeaderMint = new Color32(0x8F, 0xE3, 0xCF, 0xFF);
        static readonly Color HeaderInk = new Color32(0x1F, 0x23, 0x26, 0xFF);

        bool _uiBuilt; // バッチの CreateInstance では false のまま（UI Toolkit の側を触らない）
        ScrollView _uiScroll;
        Label _headerTarget;
        float _imguiWidth;

        // 列の数・リングの大きさは position.width でなく IMGUIContainer の中の幅で決める
        float ImguiWidth => _imguiWidth > 1f ? _imguiWidth : 300f;

        static StyleSheet LoadStyleSheet()
        {
            var path = AssetDatabase.GUIDToAssetPath(StyleSheetGuid);
            var sheet = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (sheet == null) sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            return sheet;
        }

        VisualElement BuildFrame()
        {
            var root = rootVisualElement;
            root.Clear();
            var sheet = LoadStyleSheet();
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            root.AddToClassList("chct-root");

            _uiScroll = new ScrollView(ScrollViewMode.Vertical);
            _uiScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _uiScroll.AddToClassList("chct-scroll");
            root.Add(_uiScroll);
            var body = _uiScroll.contentContainer;
            body.AddToClassList("chct-body");
            body.Add(BuildHeaderBand());
            return body;
        }

        VisualElement BuildHeaderBand()
        {
            var band = new VisualElement();
            band.AddToClassList("chct-header");
            band.style.backgroundColor = HeaderMint; // USS が読めないときの予備
            var text = new VisualElement();
            text.AddToClassList("chct-header__text");
            var title = new Label(DisplayName) { tooltip = ToolInfo.ToolName };
            title.AddToClassList("chct-header__title");
            title.AddToClassList(HeadingClass);
            title.style.color = HeaderInk;
            _headerTarget = new Label("");
            _headerTarget.AddToClassList("chct-header__target");
            _headerTarget.style.color = HeaderInk;
            text.Add(title);
            text.Add(_headerTarget);
            var badge = new Label("v" + ToolInfo.Version);
            badge.AddToClassList("chct-badge");
            band.Add(text);
            band.Add(badge);
            return band;
        }

        IMGUIContainer MakeImgui(Action draw, string name)
        {
            IMGUIContainer c = null;
            c = new IMGUIContainer(() =>
            {
                var w = c.contentRect.width;
                if (!float.IsNaN(w) && w > 1f) _imguiWidth = w;
#if !TTT_ABSENT
                if (Event.current.rawType == EventType.MouseUp) OnMouseReleased();
#endif
                draw();
#if !TTT_ABSENT
                if (Event.current.type == EventType.Used) RequestUiSync(); // IMGUI で押したら UI Toolkit の側を合わせる
#endif
            });
            c.name = name;
            c.AddToClassList("chct-imgui");
            return c;
        }

        static VisualElement WrapImgui(IMGUIContainer c)
        {
            var panel = new VisualElement();
            panel.AddToClassList("chct-imgui-panel");
            panel.Add(c);
            return panel;
        }

        static void Show(VisualElement e, bool on)
        {
            if (e == null) return;
            var d = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (e.style.display != d) e.style.display = d;
        }

#if TTT_ABSENT
        public void CreateGUI()
        {
            var body = BuildFrame();
#if AC_CHCT_TTT_INSTALLED
            if (_headerTarget != null) _headerTarget.text = "未確認の TexTransTool の版です";
#else
            if (_headerTarget != null) _headerTarget.text = "TexTransTool が必要です";
#endif
            body.Add(WrapImgui(MakeImgui(DrawTttMissingIMGUI, "chct-ttt-missing")));
        }
#else
        // ---- 見るだけの状態（[SerializeField] にしない。Undo.RecordObject(this) の巻き戻しでタブが戻るため）----
        enum TopTab { Pick = 0, Custom = 1 }
        enum SubTab { Color = 0, Grad = 1, Strand = 2, Glow = 3 } // Grad は未使用（EditorPrefs の値の並びを保つため残す）

        const string PrefTopTab = ToolInfo.ToolName + ".UI.TopTab";
        const string PrefSubTab = ToolInfo.ToolName + ".UI.SubTab";
        const string PrefPickFilter = ToolInfo.ToolName + ".UI.PickFilter";
        const string PrefPickAction = ToolInfo.ToolName + ".UI.PickAction";

        TopTab _topTab = TopTab.Pick;
        bool _topTabDecided; // 覚えた値が無ければ最初の Refresh で Custom のオン・オフから決める
        SubTab _subTab = SubTab.Color;
        string _pickFilter = FilterAll;

        void LoadUiPrefs()
        {
            _topTabDecided = EditorPrefs.HasKey(PrefTopTab);
            _topTab = EditorPrefs.GetInt(PrefTopTab, 0) == 1 ? TopTab.Custom : TopTab.Pick;
            var sub = EditorPrefs.GetInt(PrefSubTab, 0);
            _subTab = sub >= 0 && sub <= 3 ? (SubTab)sub : SubTab.Color;
            _pickFilter = EditorPrefs.GetString(PrefPickFilter, FilterAll);
            if (string.IsNullOrEmpty(_pickFilter)) _pickFilter = FilterAll;
            _pickAction = EditorPrefs.GetInt(PrefPickAction, 0) == 1 ? PickAction.Unpaint : PickAction.Swap;
        }

        void SaveUiPrefs()
        {
            EditorPrefs.SetInt(PrefTopTab, (int)_topTab);
            EditorPrefs.SetInt(PrefSubTab, (int)_subTab);
            EditorPrefs.SetString(PrefPickFilter, _pickFilter ?? FilterAll);
            EditorPrefs.SetInt(PrefPickAction, (int)_pickAction);
        }

        // 初回は Custom がオンなら「カスタムする」。2回目からは覚えた方（Refresh では切り替えない）
        void DecideFirstTopTab()
        {
            if (_topTabDecided || _target == null || !_target.IsValid) return;
            _topTab = _customOn ? TopTab.Custom : TopTab.Pick;
            _topTabDecided = true;
        }

        // 一度だけ組む（描いている途中の IMGUIContainer を消さないため）
        VisualElement _detailHost, _notFoundHost, _tabsRow, _pickRoot, _customRoot, _bakeCard, _panelWarnings;
        readonly List<IMGUIContainer> _imguis = new List<IMGUIContainer>();
        readonly List<KeyValuePair<TopTab, VisualElement>> _topTabEls = new List<KeyValuePair<TopTab, VisualElement>>();
        string _sigTarget;
        bool _syncQueued;

        public void CreateGUI()
        {
            _imguis.Clear();
            var body = BuildFrame();

            _detailHost = new VisualElement();
            body.Add(_detailHost);
            _notFoundHost = new VisualElement();
            _notFoundHost.AddToClassList("chct-card");
            body.Add(_notFoundHost);

            _panelWarnings = AddImgui(body, DrawWarningsIMGUI, "chct-warnings");

            _tabsRow = BuildTopTabs();
            body.Add(_tabsRow);

            _pickRoot = BuildPickTab();
            body.Add(_pickRoot);
            _customRoot = BuildCustomTab();
            body.Add(_customRoot);

            _bakeCard = BuildBakeCard();
            body.Add(_bakeCard);

            // 画面のどこで手を離しても後始末する（IMGUIContainer の先頭でも呼ぶ。2回目は何もしない）
            rootVisualElement.RegisterCallback<MouseUpEvent>(_ => OnMouseReleased(), TrickleDown.TrickleDown);
            rootVisualElement.RegisterCallback<GeometryChangedEvent>(_ => { foreach (var g in _tileGrids) LayoutGrid(g); });

            var restore = _scroll.y;
            _uiScroll.verticalScroller.valueChanged += v => _scroll.y = v;
            _uiScroll.schedule.Execute(() => { if (_uiScroll != null) _uiScroll.scrollOffset = new Vector2(0f, restore); }).StartingIn(30);

            // 保険の見比べ（読むだけ）。メッシュ調整のメッセージを消すのもここ
            rootVisualElement.schedule.Execute(() => SyncUI(false)).Every(500);

            _uiBuilt = true;
            _sigTarget = null;
            ResetPickSignatures();
            ResetCustomSignatures();
            SyncUI(true);
        }

        // IMGUIContainer の中から呼ばれることがあるため、木はその場で触らず次のフレームで合わせる
        void RequestUiSync()
        {
            if (!_uiBuilt || _syncQueued) return;
            _syncQueued = true;
            rootVisualElement.schedule.Execute(() =>
            {
                _syncQueued = false;
                SyncUI(false);
            });
        }

        bool _refreshedSinceSync;

        // 読むだけ（Hierarchy にも Prefab にも書かない）。IMGUI の欄の並べ直しは変化があったときだけ
        void SyncUI(bool force)
        {
            if (!_uiBuilt || _detailHost == null) return;
            var valid = _target != null && _target.IsValid;
            var changed = SyncTarget(valid);
            Show(_panelWarnings, valid && (_bakedMaterialFound || _inactiveNode != null || SelectorOffNode() != null));
            Show(_tabsRow, valid);
            var pick = valid && _topTab == TopTab.Pick;
            var custom = valid && _topTab == TopTab.Custom;
            if (_pickRoot.style.display != (pick ? DisplayStyle.Flex : DisplayStyle.None) || _customRoot.style.display != (custom ? DisplayStyle.Flex : DisplayStyle.None)) changed = true;
            Show(_pickRoot, pick);
            Show(_customRoot, custom);
            foreach (var kv in _topTabEls) kv.Value.EnableInClassList("is-selected", kv.Key == _topTab);
            if (pick) changed |= SyncPickTab();
            if (custom) changed |= SyncCustomTab();
            Show(_bakeCard, custom && BakedToolPresent);
            if (changed || force || _refreshedSinceSync) MarkImguiDirty();
            _refreshedSinceSync = false;
        }

        void MarkImguiDirty()
        {
            foreach (var c in _imguis)
            {
                c.MarkDirtyLayout();
                c.MarkDirtyRepaint();
            }
        }

        VisualElement AddImgui(VisualElement parent, Action draw, string name)
        {
            var c = MakeImgui(draw, name);
            _imguis.Add(c);
            var panel = WrapImgui(c);
            parent.Add(panel);
            return panel;
        }

        void DrawWarningsIMGUI()
        {
            if (_target == null || !_target.IsValid) return;
            DrawStateWarnings();
        }

        // ---- 帯の対象・詳細 ----
        bool SyncTarget(bool valid)
        {
            var sig = valid
                ? "ok|" + AvatarName() + "|" + _productName + "|" + AutoFoundMessage() + "|" + GetHierarchyPath(_target.Custom.transform)
                : "nf|" + _infoState + "|" + _infoNeed + "|" + BuildNotFoundMessage();
            if (sig == _sigTarget) return false;
            _sigTarget = sig;
            _detailHost.Clear();
            _notFoundHost.Clear();
            Show(_notFoundHost, !valid);
            Show(_detailHost, valid);

            if (!valid && _infoState != InfoState.Ok)
            {
                _headerTarget.text = "対象：見つかりません";
                var text = new Label(_infoState == InfoState.ToolTooOld ? UiText.InfoToolTooOld(_infoNeed) : UiText.InfoMissing);
                text.AddToClassList("chct-info");
                _notFoundHost.Add(text);
                if (_infoState == InfoState.Missing) _notFoundHost.Add(MakeButton(UiText.InfoShop, null, () => Application.OpenURL(ToolInfo.ShopUrl)));
                return true;
            }

            if (!valid)
            {
                _headerTarget.text = "対象：見つかりません";
                var info = new Label(BuildNotFoundMessage());
                info.AddToClassList("chct-info");
                _notFoundHost.Add(info);
                _notFoundHost.Add(MakeButton("もう一度さがす", null, () => { Refresh(); SyncUI(false); }));
                return true;
            }

            _headerTarget.text = "対象：" + AvatarName();
            var fold = new Foldout { text = "詳細", value = _showDetail };
            fold.AddToClassList("chct-detail");
            fold.RegisterValueChangedCallback(e => { if (e.target == fold) _showDetail = e.newValue; });
            var auto = AutoFoundMessage();
            if (auto != null) fold.Add(Note(auto));
            if (!string.IsNullOrEmpty(_productName)) fold.Add(Note("商品：" + _productName));
            fold.Add(Note("Hierarchy の場所：" + GetHierarchyPath(_target.Custom.transform)));
            fold.Add(MakeButton("Hierarchy で「Custom」を選択", "chct-btn--small", () =>
            {
                if (_target != null && _target.IsValid) Selection.activeGameObject = _target.Custom;
            }));
            _detailHost.Add(fold);
            return true;
        }

        string AvatarName() => _target != null && _target.Avatar != null ? _target.Avatar.name : "(名前なし)";

        // ---- 上のタブ ----
        VisualElement BuildTopTabs()
        {
            var row = new VisualElement();
            row.AddToClassList("chct-tabs");
            _topTabEls.Clear();
            foreach (var t in new[] { TopTab.Pick, TopTab.Custom })
            {
                var tab = t;
                var el = new Label(t == TopTab.Pick ? UiText.TabPick : UiText.TabCustom);
                el.AddToClassList("chct-tab");
                el.AddToClassList(HeadingClass);
                el.AddManipulator(new Clickable(() => OnTopTabClicked((int)tab)));
                row.Add(el);
                _topTabEls.Add(new KeyValuePair<TopTab, VisualElement>(t, el));
            }
            return row;
        }

        void OnTopTabClicked(int tab)
        {
            var t = tab == 1 ? TopTab.Custom : TopTab.Pick;
            _topTabDecided = true;
            if (_topTab == t) return;
            _topTab = t;
            Show(_strandNoticeBox, false);
            SaveUiPrefs();
            SyncUI(false);
        }

        // ---- 焼き込み ----
        VisualElement BuildBakeCard()
        {
            var card = Card(null);
            var fold = new Foldout { text = "※シェイプキーにより色がずれる方用", value = false };
            fold.AddToClassList("chct-fold");
            fold.Add(MakeButton("焼き込みツールを開く", null, OpenBakedTool));
            card.Add(fold);
            return card;
        }

        // ---- 部品 ----
        static VisualElement Card(string title)
        {
            var card = new VisualElement();
            card.AddToClassList("chct-card");
            if (string.IsNullOrEmpty(title)) return card;
            var h = new Label(title);
            h.AddToClassList("chct-card__title");
            h.AddToClassList(HeadingClass);
            card.Add(h);
            return card;
        }

        static Label Note(string text)
        {
            var l = new Label(text);
            l.AddToClassList("chct-note");
            return l;
        }

        static Label Strong(string text)
        {
            var l = new Label(text);
            l.AddToClassList("chct-strong");
            l.AddToClassList(HeadingClass);
            return l;
        }

        static Label GroupTitle(string text)
        {
            var l = new Label(text);
            l.AddToClassList("chct-group-title");
            l.AddToClassList(HeadingClass);
            return l;
        }

        static Label MakeButton(string text, string extraClass, Action onClick)
        {
            var b = new Label(text);
            b.AddToClassList("chct-btn");
            b.AddToClassList(HeadingClass);
            if (!string.IsNullOrEmpty(extraClass)) b.AddToClassList(extraClass);
            b.AddManipulator(new Clickable(onClick));
            return b;
        }

        static Label Chip(string text, Action onClick)
        {
            var c = new Label(text);
            c.AddToClassList("chct-chip");
            c.AddToClassList(HeadingClass);
            c.AddManipulator(new Clickable(onClick));
            return c;
        }

        static VisualElement Row(string extraClass = null)
        {
            var r = new VisualElement();
            r.AddToClassList("chct-row");
            if (!string.IsNullOrEmpty(extraClass)) r.AddToClassList(extraClass);
            return r;
        }

        static void Slices(Rect r, int n, Func<int, Color> colorAt, Action<Rect, Color> add)
        {
            var w = r.width / n;
            for (var i = 0; i < n; i++) add(new Rect(r.x + i * w, r.y, w + 0.5f, r.height), colorAt(i));
        }
#endif

        sealed class PaintBox : VisualElement
        {
            public Action<Rect, Action<Rect, Color>> Painter;
            readonly List<KeyValuePair<Rect, Color>> _quads = new List<KeyValuePair<Rect, Color>>();

            public PaintBox()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
            }

            void Draw(MeshGenerationContext ctx)
            {
                var r = new Rect(0f, 0f, layout.width, layout.height);
                if (Painter == null || float.IsNaN(r.width) || r.width < 1f || r.height < 1f) return;
                _quads.Clear();
                Painter(r, (q, c) => _quads.Add(new KeyValuePair<Rect, Color>(q, c)));
                if (_quads.Count == 0) return;
                var mesh = ctx.Allocate(_quads.Count * 4, _quads.Count * 6);
                for (var i = 0; i < _quads.Count; i++)
                {
                    var q = _quads[i].Key;
                    var c = _quads[i].Value;
                    c.a = 1f;
                    mesh.SetNextVertex(new Vertex { position = new Vector3(q.xMin, q.yMin, Vertex.nearZ), tint = c });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(q.xMax, q.yMin, Vertex.nearZ), tint = c });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(q.xMax, q.yMax, Vertex.nearZ), tint = c });
                    mesh.SetNextVertex(new Vertex { position = new Vector3(q.xMin, q.yMax, Vertex.nearZ), tint = c });
                    var b = (ushort)(i * 4);
                    mesh.SetNextIndex(b);
                    mesh.SetNextIndex((ushort)(b + 1));
                    mesh.SetNextIndex((ushort)(b + 2));
                    mesh.SetNextIndex(b);
                    mesh.SetNextIndex((ushort)(b + 2));
                    mesh.SetNextIndex((ushort)(b + 3));
                }
            }
        }
    }
}
#endif
