#if UNITY_EDITOR
// versionDefines では「無いとき」のシンボルを作れないため、PRESENT の裏返しで作る
#if !AC_CHCT_TTT_PRESENT
#define TTT_ABSENT
#endif

#if !TTT_ABSENT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace AtelierCloveria.CustomHairColorTool
{
    // 書くのはタイルを押したときだけ（1クリック＝Undo 1回）。絞り込みは見るだけ（EditorPrefs）
    public sealed partial class CustomHairColorToolWindow
    {
        const string NoneKey = "none";
        const string FilterAll = "all";
        const float CellChrome = 22f; // セルの余白 4×2 ＋ タイルの枠 2×2 ＋ タイルの内側の余白 5×2

        VisualElement _chipsHost, _gridHost, _strandNoticeBox;
        Label _pickStateNote, _strandNoticeText;
        string _strandNoticePending;
        readonly Dictionary<string, VisualElement> _tileEls = new Dictionary<string, VisualElement>();
        readonly List<VisualElement> _tileGrids = new List<VisualElement>();
        string _sigTiles, _sigTileState;

        void ResetPickSignatures() { _sigTiles = _sigTileState = null; }

        // Default・Main は「単色」にまとめる
        static string FilterKeyOf(string groupName)
        {
            var g = (groupName ?? "").ToLowerInvariant();
            return g == "default" || g == "main" ? "single" : g;
        }

        static string FilterLabel(string key)
        {
            switch (key)
            {
                case FilterAll: return "すべて";
                case "single": return "単色";
                case "inner": return "インナー";
                case "2ton": return "ツートン";
                case "mesh": return "メッシュ";
                case "gradation": return "グラデ";
                default: return key;
            }
        }

        static string GroupShortLabel(string groupName)
        {
            switch (FilterKeyOf(groupName))
            {
                case "single": return "単色";
                case "inner": return "インナー";
                case "2ton": return "ツートン";
                case "mesh": return "メッシュ";
                case "gradation": return "グラデーション";
                default: return groupName;
            }
        }

        List<string> AvailableFilters()
        {
            var list = new List<string> { FilterAll };
            foreach (var t in _tiles)
            {
                var k = FilterKeyOf(t.GroupName);
                if (!list.Contains(k)) list.Add(k);
            }
            return list;
        }

        // 覚えた絞り込みがこの商品に無ければ「すべて」（覚えた値は書き換えない）
        string ShownFilter => AvailableFilters().Contains(_pickFilter) ? _pickFilter : FilterAll;

        VisualElement BuildPickTab()
        {
            var card = Card(null);
            card.AddToClassList("chct-pick");
            _chipsHost = new VisualElement();
            _chipsHost.AddToClassList("chct-chips");
            card.Add(_chipsHost);
            _pickStateNote = new Label();
            _pickStateNote.style.display = DisplayStyle.None;
            card.Add(_pickStateNote);
            _strandNoticeBox = new VisualElement();
            _strandNoticeBox.AddToClassList("chct-warn");
            _strandNoticeText = new Label();
            _strandNoticeText.style.whiteSpace = WhiteSpace.Normal;
            _strandNoticeBox.Add(_strandNoticeText);
            _strandNoticeBox.Add(MakeButton("わかった", "chct-btn--small", () => Show(_strandNoticeBox, false)));
            _strandNoticeBox.style.display = DisplayStyle.None;
            card.Add(_strandNoticeBox);
            _gridHost = new VisualElement();
            card.Add(_gridHost);
            card.Add(Note("タイルを押すとその色になります。少し変えたいときは「カスタムする」へ（いまの色から始まります）。"));
            card.Add(Note("色を選んでも見た目が変わらないときは、メニューの Tools > NDM Framework > Configure Previews でプレビューがオフになっていないか確認してください。"));
            return card;
        }

        static string TileKey(int index, PresetColor t) => index + "|" + t.RelPath;

        static int Id(UnityEngine.Object o) => o != null ? o.GetInstanceID() : 0;

        bool SyncPickTab()
        {
            var sb = new StringBuilder();
            sb.Append(_target.Custom.GetInstanceID()).Append('|').Append(_hasThumbnails).Append('|').Append(Id(_noneThumbnail)).Append("|f=").Append(ShownFilter);
            foreach (var t in _tiles)
            {
                sb.Append('|').Append(t.RelPath).Append(':').Append(Id(t.Thumbnail)).Append(':').Append((int)t.Kind)
                  .Append(ColorUtility.ToHtmlStringRGB(t.ColorA)).Append(ColorUtility.ToHtmlStringRGB(t.ColorB));
            }
            var structure = sb.ToString();
            var rebuilt = false;
            if (structure != _sigTiles)
            {
                _sigTiles = structure;
                _sigTileState = null;
                BuildChips();
                BuildGrid();
                rebuilt = true;
            }

            var st = new StringBuilder();
            st.Append(_selectedCount).Append('|').Append(_anyGroupOff).Append('|').Append(_customOn).Append('|');
            foreach (var t in _tiles) st.Append(t.Selected ? '1' : '0');
            var state = st.ToString();
            if (state == _sigTileState) return rebuilt;
            var animate = _sigTileState != null; // 組み直した直後は弾ませない
            _sigTileState = state;
            ApplyTileState(animate);
            return true;
        }

        void BuildChips()
        {
            _chipsHost.Clear();
            var shown = ShownFilter;
            var filters = AvailableFilters();
            foreach (var f in filters)
            {
                var key = f;
                var chip = Chip(FilterLabel(f), () => OnFilterClicked(key));
                chip.EnableInClassList("is-selected", f == shown);
                chip.userData = f;
                _chipsHost.Add(chip);
            }
            Show(_chipsHost, filters.Count > 2);
        }

        void OnFilterClicked(string key)
        {
            if (string.IsNullOrEmpty(key) || _pickFilter == key) return;
            _pickFilter = key;
            SaveUiPrefs();
            SyncUI(false);
        }

        void BuildGrid()
        {
            _gridHost.Clear();
            _tileEls.Clear();
            _tileGrids.Clear();
            var f = ShownFilter;
            var first = NewGrid();
            first.Add(BuildTile(NoneKey, new TileEntry { Label = NoneLabel, Thumbnail = _noneThumbnail }));
            _gridHost.Add(first);
            if (f != FilterAll)
            {
                for (var i = 0; i < _tiles.Count; i++)
                {
                    var t = _tiles[i];
                    if (FilterKeyOf(t.GroupName) != f) continue;
                    first.Add(BuildTile(TileKey(i, t), new TileEntry { Label = t.Label, Preset = t, Thumbnail = t.Thumbnail }));
                }
                return;
            }
            // 同じ名前の色が別のグループにもあるため見出しで分ける
            string lastGroup = null;
            VisualElement grid = null;
            for (var i = 0; i < _tiles.Count; i++)
            {
                var t = _tiles[i];
                if (grid == null || t.GroupName != lastGroup)
                {
                    lastGroup = t.GroupName;
                    _gridHost.Add(GroupTitle(GroupShortLabel(t.GroupName)));
                    grid = NewGrid();
                    _gridHost.Add(grid);
                }
                grid.Add(BuildTile(TileKey(i, t), new TileEntry { Label = t.Label, Preset = t, Thumbnail = t.Thumbnail }));
            }
        }

        void ApplyTileState(bool animate)
        {
            for (var i = 0; i < _tiles.Count; i++) SetTileSelected(TileKey(i, _tiles[i]), _tiles[i].Selected, animate);
            SetTileSelected(NoneKey, _selectedCount == 0, animate);

            if (_pickStateNote == null) return;
            string text = null;
            var warn = false;
            if (_selectedCount >= 2) { text = "色が2つ以上オンになっています（重なって見えます）。1つ選ぶと直ります。"; warn = true; }
            else if (_anyGroupOff) text = "色のセット（Default など）がオフになっています。どれか色を選ぶとオンに戻ります。";
            else if (_customOn) text = "いまはカスタムの色です（「カスタムする」のタブ）。タイルを押すと、その色に替わります（作った色は残ります）。";
            _pickStateNote.text = text ?? "";
            _pickStateNote.EnableInClassList("chct-warn", warn);
            _pickStateNote.EnableInClassList("chct-info", !warn);
            Show(_pickStateNote, text != null);
        }

        void SetTileSelected(string key, bool on, bool animate)
        {
            if (!_tileEls.TryGetValue(key, out var tile)) return;
            var was = tile.ClassListContains("is-selected");
            tile.EnableInClassList("is-selected", on);
            if (on && !was && animate)
            {
                tile.experimental.animation
                    .Start(0.92f, 1f, 260, (e, v) => e.style.scale = new Scale(new Vector3(v, v, 1f)))
                    .Ease(Easing.OutBack);
            }
        }

        // プリセットは Refresh で読み直した一覧から引き直す
        void OnTileClicked(string key)
        {
            if (_target == null || !_target.IsValid) return;
            if (key == NoneKey) ChoosePreset(null);
            else
            {
                var bar = key.IndexOf('|');
                if (bar < 0 || !int.TryParse(key.Substring(0, bar), out var index)) return;
                if (index < 0 || index >= _tiles.Count || _tiles[index].RelPath != key.Substring(bar + 1)) return; // 一覧が変わっていたら何もしない（すぐ組み直す）
                ChoosePreset(_tiles[index]);
            }
            if (_strandNoticePending != null)
            {
                _strandNoticeText.text = _strandNoticePending + "はカスタムに残っています。「" + UiText.TabCustom + "」の「" + UiText.GateContinue + "」で戻せます。";
                Show(_strandNoticeBox, true);
                _strandNoticePending = null;
            }
            SyncUI(false);
        }

        VisualElement BuildTile(string key, TileEntry e)
        {
            var cell = new VisualElement();
            cell.AddToClassList("chct-cell");
            var tile = new VisualElement();
            tile.AddToClassList("chct-tile");
            cell.Add(tile);

            if (e.Thumbnail != null)
            {
                tile.AddToClassList("chct-tile--photo");
                var img = new Image { image = e.Thumbnail, scaleMode = ScaleMode.ScaleToFit };
                img.AddToClassList("chct-thumb");
                img.style.width = 90f;
                img.style.height = 90f;
                tile.Add(img);
                var strip = BuildSwatch(e, true);
                strip.style.width = 90f;
                tile.Add(strip);
            }
            else tile.Add(BuildSwatch(e, false));

            var label = new Label(e.Label) { tooltip = e.Preset != null ? GroupShortLabel(e.Preset.GroupName) + "「" + e.Label + "」" : null };
            label.AddToClassList("chct-tile__label");
            label.AddToClassList(HeadingClass);
            tile.Add(label);
            var check = new Label("✓");
            check.AddToClassList("chct-tile__check");
            check.pickingMode = PickingMode.Ignore;
            tile.Add(check);

            tile.AddManipulator(new Clickable(() => OnTileClicked(key)));
            _tileEls[key] = tile;
            return cell;
        }

        // strip：写真の下の細い帯（「なし」の文字は入らないため出さない）
        static VisualElement BuildSwatch(TileEntry e, bool strip)
        {
            var frame = new VisualElement();
            frame.AddToClassList("chct-swatch");
            if (strip) frame.AddToClassList("chct-swatch--strip");
            frame.pickingMode = PickingMode.Ignore;
            var preset = e.Preset;
            var paint = new PaintBox { Painter = (r, add) => PaintPreset(preset, r, add) };
            paint.AddToClassList("chct-fill");
            frame.Add(paint);
            if (!strip && preset == null)
            {
                var none = new Label("なし");
                none.AddToClassList("chct-swatch__text");
                none.pickingMode = PickingMode.Ignore;
                frame.Add(none);
            }
            return frame;
        }

        // null＝デフォルトの髪（市松）
        static void PaintPreset(PresetColor preset, Rect r, Action<Rect, Color> add)
        {
            if (preset == null) { Checker(r, add); return; }
            switch (preset.Kind)
            {
                case SwatchKind.TwoTone:
                    add(new Rect(r.x, r.y, r.width / 2f, r.height), preset.ColorA);
                    add(new Rect(r.x + r.width / 2f, r.y, r.width / 2f, r.height), preset.ColorB);
                    break;
                case SwatchKind.Gradient:
                    Slices(r, HairColorCore.GradientBarSteps, i => preset.Bar != null && i < preset.Bar.Length ? preset.Bar[i] : Color.white, add);
                    break;
                default:
                    add(r, preset.ColorA);
                    break;
            }
        }

        static void Checker(Rect r, Action<Rect, Color> add)
        {
            var light = new Color(0.80f, 0.80f, 0.80f);
            var dark = new Color(0.62f, 0.62f, 0.62f);
            add(r, light);
            var s = r.height / 2f;
            if (s < 1f) return;
            var nx = Mathf.CeilToInt(r.width / s);
            for (var x = 0; x < nx; x++)
            {
                for (var y = 0; y < 2; y++)
                {
                    if ((x + y) % 2 == 0) continue;
                    var w = Mathf.Min(s, r.xMax - (r.x + x * s));
                    add(new Rect(r.x + x * s, r.y + y * s, w, s), dark);
                }
            }
        }

        VisualElement NewGrid()
        {
            var g = new VisualElement();
            g.AddToClassList("chct-grid");
            g.RegisterCallback<GeometryChangedEvent>(_ => LayoutGrid(g));
            _tileGrids.Add(g);
            return g;
        }

        // サムネイルの大きさのつまみは置かない（写真の無い商品も同じ列の数）
        static int ColumnsFor(float windowWidth) => windowWidth < 470f ? 3 : windowWidth < 620f ? 4 : 5;

        float WindowWidth
        {
            get
            {
                var w = rootVisualElement.layout.width;
                return !float.IsNaN(w) && w > 1f ? w : position.width;
            }
        }

        // 幅が決まっていない（バッチ・組んだ直後）ときは何もしない
        void LayoutGrid(VisualElement g)
        {
            var w = g.contentRect.width;
            if (float.IsNaN(w) || w < 1f) return;
            var cols = ColumnsFor(WindowWidth);
            var pct = 100f / cols;
            foreach (var cell in g.Children())
            {
                var cw = cell.style.width;
                if (cw.keyword != StyleKeyword.Undefined || cw.value.unit != LengthUnit.Percent || !Mathf.Approximately(cw.value.value, pct))
                    cell.style.width = Length.Percent(pct);
            }
            var side = Mathf.Max(16f, Mathf.Floor(w / cols - CellChrome));
            g.Query<Image>(className: "chct-thumb").ForEach(img => SetSize(img, side, side));
            g.Query<VisualElement>(className: "chct-swatch--strip").ForEach(s => SetSize(s, side, -1f));
        }

        static void SetSize(VisualElement e, float width, float height)
        {
            if (!Mathf.Approximately(e.style.width.value.value, width) || e.style.width.keyword != StyleKeyword.Undefined) e.style.width = width;
            if (height >= 0f && (!Mathf.Approximately(e.style.height.value.value, height) || e.style.height.keyword != StyleKeyword.Undefined)) e.style.height = height;
        }
    }
}
#endif
#endif
