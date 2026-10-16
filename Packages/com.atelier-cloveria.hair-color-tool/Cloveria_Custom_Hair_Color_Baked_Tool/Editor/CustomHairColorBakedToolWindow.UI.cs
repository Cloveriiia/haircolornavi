#if UNITY_EDITOR
#if !AC_CHBT_TTT_PRESENT
#define TTT_ABSENT
#endif

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AtelierCloveria.CustomHairColorBakedTool
{
    // USS はカスタムカラーツールのものを GUID で読む（コードは参照しない。2つのツールは必ず一緒に同梱する）
    public sealed partial class CustomHairColorBakedToolWindow
    {
        const string StyleSheetGuid = "2d429471e9d34a6ea07f301542dfed95";
        const string StyleSheetPath = "Assets/Atelier_Cloveria/Tools/Cloveria_Custom_Hair_Color_Tool/Editor/CustomHairColorTool.uss";
        const string HeadingClass = "chct-heading";

        // USS が読めないときの予備（帯と下地だけ）
        static readonly Color HeaderMint = new Color32(0x8F, 0xE3, 0xCF, 0xFF);
        static readonly Color HeaderInk = new Color32(0x1F, 0x23, 0x26, 0xFF);

        bool _uiBuilt;      // 組む前に Refresh が走っても木は触らない
        bool _syncQueued;
        ScrollView _uiScroll;
        Label _headerTarget;
        readonly List<IMGUIContainer> _imguis = new List<IMGUIContainer>();

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
            _imguis.Clear();
            var sheet = LoadStyleSheet();
            if (sheet != null && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            root.AddToClassList("chct-root");
            root.style.backgroundColor = HeaderInk;

            _uiScroll = new ScrollView(ScrollViewMode.Vertical);
            _uiScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _uiScroll.AddToClassList("chct-scroll");
            root.Add(_uiScroll);
            var body = _uiScroll.contentContainer;
            body.AddToClassList("chct-body");
            body.Add(BuildHeaderBand());
            return body;
        }

        // 正式名は長くて帯に入らないため、題名に重ねたときに出す
        VisualElement BuildHeaderBand()
        {
            var band = new VisualElement();
            band.AddToClassList("chct-header");
            band.style.backgroundColor = HeaderMint;
            var text = new VisualElement();
            text.AddToClassList("chct-header__text");
            var title = new Label(WindowTitle) { tooltip = BakedToolInfo.ToolName };
            title.AddToClassList("chct-header__title");
            title.AddToClassList(HeadingClass);
            title.style.color = HeaderInk;
            _headerTarget = new Label("");
            _headerTarget.AddToClassList("chct-header__target");
            _headerTarget.style.color = HeaderInk;
            text.Add(title);
            text.Add(_headerTarget);
            var badge = new Label("v" + BakedToolInfo.Version);
            badge.AddToClassList("chct-badge");
            band.Add(text);
            band.Add(badge);
            return band;
        }

        IMGUIContainer MakeImgui(Action draw, string name)
        {
            var c = new IMGUIContainer(() =>
            {
                draw();
                if (Event.current.type == EventType.Used) RequestUiSync();
            });
            c.name = name;
            c.AddToClassList("chct-imgui");
            _imguis.Add(c);
            return c;
        }

        static VisualElement WrapImgui(IMGUIContainer c)
        {
            var panel = new VisualElement();
            panel.AddToClassList("chct-imgui-panel");
            panel.Add(c);
            return panel;
        }

        static Label Note(string text)
        {
            var l = new Label(text);
            l.AddToClassList("chct-note");
            return l;
        }

        static void Show(VisualElement e, bool on)
        {
            if (e == null) return;
            var d = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (e.style.display != d) e.style.display = d;
        }

        // IMGUIContainer の描画中にも呼ばれるため、木は次のフレームで合わせる
        void RequestUiSync()
        {
            if (!_uiBuilt || _syncQueued) return;
            _syncQueued = true;
            rootVisualElement.schedule.Execute(() =>
            {
                _syncQueued = false;
                SyncUI();
            });
        }

        // 枠の高さが変わるため、描き直しと並べ直しの両方
        void MarkImguiDirty()
        {
            foreach (var c in _imguis)
            {
                c.MarkDirtyLayout();
                c.MarkDirtyRepaint();
            }
        }

#if TTT_ABSENT
        public void CreateGUI()
        {
            var body = BuildFrame();
#if AC_CHBT_TTT_INSTALLED
            _headerTarget.text = "未確認の TexTransTool の版です";
#else
            _headerTarget.text = "TexTransTool が必要です";
#endif
            body.Add(WrapImgui(MakeImgui(DrawTttMissingIMGUI, "chbt-ttt-missing")));
            _uiBuilt = true;
        }

        void SyncUI() { }
#else
        static readonly string[] CardTitles = { "対象のアバター", "焼き込み前の確認", "焼き込む", "結果" };
        VisualElement[] _cards;

        public void CreateGUI()
        {
            var body = BuildFrame();
            _cards = null;
            if (!BakedToolInfo.HasNdmf)
            {
                body.Add(WrapImgui(MakeImgui(DrawNdmfMissingIMGUI, "chbt-ndmf-missing")));
            }
            else
            {
                _cards = new VisualElement[CardTitles.Length];
                for (var i = 0; i < CardTitles.Length; i++)
                {
                    var card = new VisualElement();
                    card.AddToClassList("chct-card");
                    card.style.backgroundColor = ImguiSkin.Panel;
                    var title = new Label(CardTitles[i]);
                    title.AddToClassList("chct-card__title");
                    title.AddToClassList(HeadingClass);
                    card.Add(title);
                    _cards[i] = card;
                    body.Add(card);
                }
                _cards[0].Add(MakeImgui(DrawTargetInfo, "chbt-target"));
                _cards[1].Add(MakeImgui(DrawPreview, "chbt-preview"));
                _cards[2].Add(MakeImgui(DrawExportButton, "chbt-export"));
                _cards[3].Add(MakeImgui(DrawResults, "chbt-results"));
            }

            _uiScroll.verticalScroller.valueChanged += v => _scroll = new Vector2(0f, v);
            var y = _scroll.y;
            _uiScroll.schedule.Execute(() => _uiScroll.scrollOffset = new Vector2(0f, y)).StartingIn(30); // 中身の高さが決まってから戻す

            _uiBuilt = true;
            SyncUI();
        }

        void SyncUI()
        {
            if (!_uiBuilt) return;
            _headerTarget.text = _avatar != null ? "対象：" + _avatar.name : "対象：見つかりません";
            if (_cards != null)
            {
                var ready = _avatar != null && _product != null;
                var show = new[]
                {
                    true,
                    ready,
                    ready,
                    ready && (_exportedPaths.Count > 0 || _exportMessages.Count > 0),
                };
                for (var i = 0; i < _cards.Length; i++) Show(_cards[i], show[i]);
            }
            MarkImguiDirty();
        }
#endif
    }
}
#endif
