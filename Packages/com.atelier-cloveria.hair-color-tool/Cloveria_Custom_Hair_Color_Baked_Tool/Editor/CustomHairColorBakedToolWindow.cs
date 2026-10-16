#if UNITY_EDITOR
#if !AC_CHBT_TTT_PRESENT
#define TTT_ABSENT
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AtelierCloveria.CustomHairColorBakedTool
{
    /// <summary>画面上の名前は WindowTitle だけ。メニューのパスとフォルダ名（BakedToolInfo.ToolName）は変えない</summary>
    public sealed partial class CustomHairColorBakedToolWindow : EditorWindow
    {
        const string WindowTitle = "焼き込みツール";

        [MenuItem(BakedToolInfo.MenuPath)]
        public static void Open()
        {
            var w = GetWindow<CustomHairColorBakedToolWindow>(WindowTitle);
            w.minSize = new Vector2(360, 480);
            w.Show();
        }

#if TTT_ABSENT
        void DrawTttMissingIMGUI()
        {
            GUILayout.Space(4);
#if AC_CHBT_TTT_INSTALLED
            // versionDefines の範囲外の版
            GUILayout.Label("この版の TexTransTool では、まだ動作を確かめていません（TexTransTool " + BakedToolInfo.TestedTexTransTool + " で確かめています）。\nThis TexTransTool version has not been tested yet.", ImguiSkin.Warn);
            GUILayout.Label("このツールの更新をお待ちください。お問い合わせは Atelier Cloveria へお願いします（TexTransTool の作者さんではなく）。", ImguiSkin.Note);
#else
            GUILayout.Label("TexTransTool が必要です。\nTexTransTool is required.", ImguiSkin.Warn);
            EditorGUILayout.SelectableLabel(BakedToolInfo.TexTransToolUrl, ImguiSkin.Text, GUILayout.Height(18));
            if (GUILayout.Button("導入ページを開く / Open download page", ImguiSkin.Button, GUILayout.Height(26)))
            {
                Application.OpenURL(BakedToolInfo.TexTransToolUrl);
            }
#endif
        }
#else
        // カスタムカラーツールの型は参照せず、アセンブリ名とメニューのパスで結ぶ
        const string ColorToolAssemblyName = "AtelierCloveria.CustomHairColorTool.Editor";
        const string ColorToolMenuPath = "Tools/Atelier_Cloveria/Cloveria_Custom_Hair_Color_Tool";
        const float LabelWidth = 72f;

        [SerializeField] string _saveFolder = "";
        [SerializeField] List<string> _exportedPaths = new List<string>();
        [SerializeField] List<string> _exportMessages = new List<string>();
        [SerializeField] int _productId;
        [SerializeField] int _resultAvatarId;
        [SerializeField] Vector2 _scroll;
        [SerializeField] bool _applyAfterExport = true;
        [SerializeField] bool _disableProductAfterApply = true;

        GameObject _avatar;
        bool _foundInSelection;
        List<ProductCandidate> _products = new List<ProductCandidate>();
        ProductCandidate _product;
        // ドメインリロードで消える（消えたら「適用」ボタンは出ない）
        List<ExportResult> _lastResults;
        GameObject _lastProduct;
        // アセットの印から作るため、再起動後も分かる
        List<RestoreEntry> _applied = new List<RestoreEntry>();

        List<ExportTarget> _preview = new List<ExportTarget>();
        List<string> _previewWarnings = new List<string>();

        static bool? s_colorToolPresent;
        static bool ColorToolPresent
        {
            get
            {
                if (!s_colorToolPresent.HasValue)
                {
                    s_colorToolPresent = AppDomain.CurrentDomain.GetAssemblies()
                        .Any(a => a.GetName().Name == ColorToolAssemblyName);
                }
                return s_colorToolPresent.Value;
            }
        }

        void OnEnable() { Refresh(); }
        void OnSelectionChange() { Refresh(); Repaint(); }
        void OnHierarchyChange() { Refresh(); Repaint(); }

        void Refresh()
        {
            _avatar = null;
            _foundInSelection = false;

            var sel = Selection.activeGameObject;
            if (sel != null)
            {
                var a = CustomHairColorBakedTool.FindAvatarRoot(sel);
                if (CustomHairColorBakedTool.FindProducts(a).Count > 0) { _avatar = a; _foundInSelection = true; }
            }
            if (_avatar == null)
            {
                var roots = new List<GameObject>();
                for (var i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (scene.isLoaded) roots.AddRange(scene.GetRootGameObjects());
                }
                _avatar = roots
                    .Where(r => r.name.IndexOf("(" + BakedToolInfo.ToolName + " temp)", StringComparison.Ordinal) < 0)
                    .OrderByDescending(CustomHairColorBakedTool.IsAvatarRoot)
                    .FirstOrDefault(r => CustomHairColorBakedTool.FindProducts(r).Count > 0);
            }

            _products = CustomHairColorBakedTool.FindProducts(_avatar);
            _product = _products.FirstOrDefault(p => p.Root.GetInstanceID() == _productId) ?? _products.FirstOrDefault();
            _productId = _product != null ? _product.Root.GetInstanceID() : 0;
            _applied = CustomHairColorBakedTool.FindApplied(_avatar);

            _preview = new List<ExportTarget>();
            _previewWarnings = new List<string>();
            if (_avatar != null && _product != null && _product.ActiveDecals > 0)
                _preview = CustomHairColorBakedTool.Detect(_avatar, _product.Root, _previewWarnings);

            var avatarId = _avatar != null ? _avatar.GetInstanceID() : 0;
            if (avatarId != _resultAvatarId)
            {
                _resultAvatarId = avatarId;
                _exportedPaths.Clear();
                _exportMessages.Clear();
                _lastResults = null;
                _lastProduct = null;
            }
            RequestUiSync();
        }

        void DrawNdmfMissingIMGUI()
        {
            GUILayout.Label("NDMF が見つかりません。TexTransTool を入れると一緒に入ります。\n" + BakedToolInfo.NdmfUrl, ImguiSkin.Warn);
            if (GUILayout.Button("NDMF のページを開く", ImguiSkin.Button, GUILayout.Height(26))) Application.OpenURL(BakedToolInfo.NdmfUrl);
        }

        static void Row(string name, string value, GUIStyle valueStyle)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(name, ImguiSkin.Label, GUILayout.Width(LabelWidth));
                GUILayout.Label(value, valueStyle);
            }
        }

        // ---- 対象のアバター ----
        void DrawTargetInfo()
        {
            if (_avatar == null)
            {
                GUILayout.Label(
                    "色を付けたアバターが見つかりません。\n" +
                    "Hierarchy でアバターを選んでください。色のセット（髪色のプレハブ）がアバターの中（子）に入っているかも確認してください。",
                    ImguiSkin.Info);
                if (GUILayout.Button("もう一度さがす", ImguiSkin.Button, GUILayout.Height(26))) Refresh();
                return;
            }

            Row("アバター", _avatar.name, ImguiSkin.Bold);
            if (!_foundInSelection)
            {
                GUILayout.Label(
                    "Hierarchy で選んでいないので、シーンの中から自動で見つけました。別のアバターにしたいときは、そのアバターを Hierarchy で選んでください。",
                    ImguiSkin.Note);
            }

            if (_products.Count == 1)
            {
                Row("色のセット", _product.Root.name, ImguiSkin.Label);
            }
            else
            {
                var labels = _products.Select(p => p.Root.name + "（オンの色 " + p.ActiveDecals + "）").ToArray();
                var index = Mathf.Max(0, _products.IndexOf(_product));
                int picked;
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("色のセット", ImguiSkin.Label, GUILayout.Width(LabelWidth));
                    picked = EditorGUILayout.Popup(index, labels);
                }
                if (picked != index && picked >= 0 && picked < _products.Count)
                {
                    _product = _products[picked];
                    _productId = _product.Root.GetInstanceID();
                    Refresh();
                    GUIUtility.ExitGUI(); // 部品の数が変わるため、このフレームの描画を打ち切る
                }
            }
            GUILayout.Label(
                "色のセット＝アバターの中に入れた髪色のプレハブです。このセットで、いまオンになっている色を焼き込みます。",
                ImguiSkin.Note);

            if (_product != null && _product.ActiveDecals == 0)
            {
                if (_applied.Count > 0)
                {
                    GUILayout.Label(
                        "色のセット「" + _product.Root.name + "」はオフになっています（焼き込み済みのため）。\n" +
                        "別の色にしたいときは、下の「元に戻す」で先に戻してから、色を選び直してください。",
                        ImguiSkin.Info);
                }
                else
                {
                    GUILayout.Label(
                        "色のセット「" + _product.Root.name + "」で、オンになっている色がありません。\n" +
                        "先にカスタムカラーツールで色を選ぶか、Hierarchy でセットの中の色をオンにしてください。",
                        ImguiSkin.Warn);
                    if (ColorToolPresent && GUILayout.Button("カスタムカラーツールを開く", ImguiSkin.Button, GUILayout.Height(26))) OpenColorTool();
                }
            }
        }

        static void OpenColorTool()
        {
            if (!EditorApplication.ExecuteMenuItem(ColorToolMenuPath))
            {
                Debug.LogWarning("[" + BakedToolInfo.ToolName + "] カスタムカラーツールのメニューが見つかりません：" + ColorToolMenuPath);
            }
        }

        // ---- 焼き込み前の確認 ----
        void DrawPreview()
        {
            if (_avatar == null || _product == null) return;
            if (_applied.Count > 0 && _product.ActiveDecals > 0)
            {
                GUILayout.Label(
                    "焼き込んだ色がすでに髪に付いています。このまま焼き込むと色が二重に乗ります。\n先に下の「マテリアルを元に戻す」を押してください。",
                    ImguiSkin.Warn);
            }

            if (_product.ActiveDecals == 0)
            {
                GUILayout.Label("オンになっている色が無いので、いまは焼き込めません。", ImguiSkin.Note);
                return;
            }

            if (_preview.Count > 0)
            {
                var colors = _preview.Select(t => t.Label).Where(l => !string.IsNullOrEmpty(l)).Distinct().ToList();
                GUILayout.Label("焼き込みたい色：" + string.Join("、", colors), ImguiSkin.Text);
                GUILayout.Space(4);
                DrawOutputFolder();
            }
            else
            {
                GUILayout.Label("色を付ける先（髪のメッシュ）が見つかりませんでした。", ImguiSkin.Warn);
            }

            foreach (var w in _previewWarnings)
                GUILayout.Label(w, ImguiSkin.Warn);
        }

        string SaveFolder => string.IsNullOrEmpty(_saveFolder) ? CustomHairColorBakedTool.AvatarOutputFolder(_avatar) : _saveFolder;

        void DrawOutputFolder()
        {
            GUILayout.Label("保存先", ImguiSkin.Label);
            using (new EditorGUILayout.HorizontalScope())
            {
                var before = SaveFolder;
                GUILayout.Label(before + "/", ImguiSkin.Text, GUILayout.MinWidth(40f));
                if (GUILayout.Button("選ぶ…", ImguiSkin.Button, GUILayout.Width(56)))
                {
                    var picked = EditorUtility.OpenFolderPanel("保存先フォルダ（Assets の中）", Application.dataPath, "");
                    if (!string.IsNullOrEmpty(picked))
                    {
                        picked = picked.Replace('\\', '/');
                        var data = Application.dataPath.Replace('\\', '/');
                        if (picked.StartsWith(data, StringComparison.OrdinalIgnoreCase))
                            _saveFolder = "Assets" + picked.Substring(data.Length);
                        else
                            EditorUtility.DisplayDialog(WindowTitle, "Assets フォルダの中を選んでください。", "OK");
                    }
                }
                if (before != SaveFolder) Refresh();
            }
        }

        // ---- 焼き込む ----
        void DrawExportButton()
        {
            if (_avatar == null || _product == null) return;
            GUILayout.Label("焼き込んだテクスチャは自動複製したマテリアルに適用され髪に付与されます。", ImguiSkin.Text);
            GUILayout.Label("※焼き込んだ後元に戻すには「マテリアルを元に戻す」を選択してください。", ImguiSkin.Note);
            GUILayout.Space(4);
            var avatarActive = _avatar.activeInHierarchy;
            var canBake = avatarActive && _product.ActiveDecals > 0;
            using (new EditorGUI.DisabledScope(!canBake))
            {
                var label = _applyAfterExport ? "いまの色を髪に焼き込む" : "いまの色を画像に保存する";
                if (GUILayout.Button(label, ImguiSkin.ButtonPrimary, GUILayout.Height(36))) RunExport();
            }

            if (!avatarActive)
            {
                GUILayout.Label("アバターがオフになっているので押せません。Hierarchy でアバターをオンにしてください。", ImguiSkin.Note);
            }
            else if (_product.ActiveDecals == 0)
            {
                GUILayout.Label(_applied.Count > 0
                        ? "焼き込み済みです。別の色にするには、「マテリアルを元に戻す」を押してから色を選び直してください。"
                        : "オンになっている色が無いので押せません。「対象のアバター」を確認してください。",
                    ImguiSkin.Note);
            }

            if (!_applyAfterExport && _lastResults != null && _lastResults.Any(r => r.Success))
            {
                GUILayout.Space(2);
                if (GUILayout.Button("前回保存した画像を髪に付ける", ImguiSkin.Button, GUILayout.Height(26))) RunApply();
            }

            GUILayout.Space(4);
            using (new EditorGUI.DisabledScope(_applied.Count == 0))
            {
                if (GUILayout.Button("マテリアルを元に戻す", ImguiSkin.Button, GUILayout.Height(30))) RunRestore();
            }
        }

        // ---- 結果 ----
        void DrawResults()
        {
            var pngs = _exportedPaths.Where(p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).ToList();
            var mats = _exportedPaths.Where(p => p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)).ToList();

            if (pngs.Count > 0 || mats.Count > 0)
            {
                var summary = pngs.Count > 0 ? "画像 " + pngs.Count + " 枚を保存" : "";
                if (mats.Count > 0) summary += (summary.Length > 0 ? "し、" : "") + "マテリアル " + mats.Count + " 枚を髪に付け";
                GUILayout.Label("✓ " + summary + "ました。", ImguiSkin.Text);
            }

            if (pngs.Count > 0)
            {
                GUILayout.Label("保存した画像", ImguiSkin.Bold);
                foreach (var p in pngs) DrawResultRow(p);
            }
            if (mats.Count > 0)
            {
                GUILayout.Label("髪に付けた複製マテリアル", ImguiSkin.Bold);
                foreach (var p in mats) DrawResultRow(p);
            }
            if (pngs.Count > 0 || mats.Count > 0)
            {
                GUILayout.Label("「表示」で Project ウィンドウのそのファイルを示します。", ImguiSkin.Note);
            }

            // 先頭の印：「~」は案内、「!」は注意、無ければ失敗
            foreach (var m in _exportMessages)
                GUILayout.Label(m.TrimStart('!', '~'), m.StartsWith("~") ? ImguiSkin.Info : ImguiSkin.Warn);
        }

        static void DrawResultRow(string assetPath)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(new GUIContent(System.IO.Path.GetFileName(assetPath), assetPath), ImguiSkin.Label, GUILayout.ExpandWidth(true), GUILayout.MinWidth(40f));
                if (GUILayout.Button("表示", ImguiSkin.Button, GUILayout.Width(44)))
                    EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath));
            }
        }

        void RunRestore()
        {
            var errors = new List<string>();
            int n = CustomHairColorBakedTool.Restore(_avatar, _applied, true, errors);
            var msg = n > 0
                ? n + " 箇所を焼き込む前のマテリアルに戻しました。色のセットもオンに戻しています。\n保存した画像と複製マテリアルは残しています。戻す前の状態には Ctrl+Z で戻れます。"
                : "戻せるマテリアルがありませんでした。";
            if (errors.Count > 0) msg += "\n\n" + string.Join("\n", errors);
            SceneView.RepaintAll();
            Refresh();
            EditorUtility.DisplayDialog(WindowTitle, msg, "OK");
            Repaint();
        }

        void RunExport()
        {
            if (_avatar == null || _product == null) return;
            _exportedPaths.Clear();
            _exportMessages.Clear();

            var warnings = new List<string>();
            var targets = CustomHairColorBakedTool.Detect(_avatar, _product.Root, warnings);
            if (targets.Count == 0)
            {
                var why = warnings.Count > 0 ? string.Join("\n", warnings) : "焼き込む先（髪のメッシュ）が見つかりませんでした。";
                EditorUtility.DisplayDialog(WindowTitle, why, "OK");
                return;
            }
            foreach (var w in warnings) _exportMessages.Add("!" + w);

            var existing = CustomHairColorBakedTool.ExistingOutputs(_avatar, targets, SaveFolder);
            if (existing.Count > 0)
            {
                var list = string.Join("\n", existing.Take(8));
                if (existing.Count > 8) list += "\n…ほか " + (existing.Count - 8) + " 件";
                if (!EditorUtility.DisplayDialog(WindowTitle, "同じ名前の画像が既にあります。上書きしますか？\n\n" + list, "上書きする", "やめる"))
                    return;
            }

            List<ExportResult> results;
            try
            {
                results = CustomHairColorBakedTool.Export(_avatar, targets, SaveFolder,
                    (f, s) => EditorUtility.DisplayProgressBar(WindowTitle, s, f));
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            foreach (var r in results)
            {
                if (r.Success)
                {
                    if (!_exportedPaths.Contains(r.PngAssetPath)) _exportedPaths.Add(r.PngAssetPath);
                    foreach (var m in r.Maps)
                        if (!_exportedPaths.Contains(m.PngAssetPath)) _exportedPaths.Add(m.PngAssetPath);
                    if (r.SizeDiffers)
                        _exportMessages.Add("!" + r.Target.Material.name + " の画像の大きさが元（" + r.OriginalSize.x + "×" + r.OriginalSize.y + "）と違います（" + r.Size.x + "×" + r.Size.y + "）。ほかの最適化ツールが効いている可能性があります。");
                }
                else if (!string.IsNullOrEmpty(r.Note))
                {
                    _exportMessages.Add("~" + r.Note);
                }
                else
                {
                    _exportMessages.Add(r.Target.DisplayName + "\n" + r.Error);
                }
            }
            // 光らせる設定はアップロード時にカスタムカラーツールが入れるため、それが動かない環境では描かれない
            if (results.Any(r => r.Success && r.Target.EmptyMapProperties.Count > 0 && r.Maps.Count == 0))
                _exportMessages.Add("~光る毛束は描かれませんでした。色だけを焼き込んでいます（この環境では、アップロードしても光りません。カスタムカラーツールが必要とする NDMF の版を確かめてください）。");
            _lastResults = results;
            _lastProduct = _product.Root;

            int ok = _exportedPaths.Count;
            int ng = results.Count(r => !string.IsNullOrEmpty(r.Error));
            var msg = ok > 0
                ? "画像 " + ok + " 枚を保存しました。\n保存先: " + SaveFolder
                : "画像を保存できませんでした。";
            if (ng > 0) msg += "\n\n失敗 " + ng + " 件（理由はウィンドウの「結果」に表示しています）";

            if (ok > 0 && _applyAfterExport)
            {
                msg += "\n\n" + ApplyAndDescribe();
            }
            else if (ok > 0)
            {
                msg += "\n\n髪に付けるには、ウィンドウの「前回保存した画像を髪に付ける」を押してください。\n手で付ける場合は、髪のマテリアルのメインテクスチャと輪郭線テクスチャの両方に画像を入れ、色のセットをオフにします。";
            }
            EditorUtility.DisplayDialog(WindowTitle, msg, "OK");
            Repaint();
            RequestUiSync();
        }

        void RunApply()
        {
            if (_lastResults == null) return;
            var msg = ApplyAndDescribe();
            EditorUtility.DisplayDialog(WindowTitle, msg, "OK");
            Repaint();
            RequestUiSync();
        }

        string ApplyAndDescribe()
        {
            var applied = CustomHairColorBakedTool.Apply(_avatar, _lastResults, SaveFolder, _lastProduct, _disableProductAfterApply);

            var matPaths = new List<string>();
            var props = new HashSet<string>();
            int ng = 0;
            foreach (var a in applied)
            {
                if (a.Success)
                {
                    if (!matPaths.Contains(a.MaterialAssetPath)) matPaths.Add(a.MaterialAssetPath);
                    foreach (var p in a.ReplacedProperties) props.Add(p);
                }
                else
                {
                    ng++;
                    _exportMessages.Add(a.Target.DisplayName + "\n" + a.Error);
                }
            }
            foreach (var m in matPaths) if (!_exportedPaths.Contains(m)) _exportedPaths.Add(m);

            var text = matPaths.Count > 0
                ? "マテリアル " + matPaths.Count + " 枚を複製して髪に付けました（入れ替えた画像: " + string.Join(", ", props.OrderBy(p => p)) + "）。"
                : "マテリアルを髪に付けられませんでした。";
            if (_disableProductAfterApply && _lastProduct != null && !_lastProduct.activeSelf)
                text += "\n色のセット「" + _lastProduct.name + "」はオフにしました（色が二重に乗らないように）。";
            if (ng > 0) text += "\n付けられなかったもの " + ng + " 件（理由はウィンドウの「結果」に表示しています）";
            text += "\n色を選び直したいときは、ウィンドウの「マテリアルを元に戻す」を押してください。";
            SceneView.RepaintAll();
            _applied = CustomHairColorBakedTool.FindApplied(_avatar);
            return text;
        }
#endif
    }
}
#endif
