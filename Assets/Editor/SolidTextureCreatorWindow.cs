// Assets/Editor/SolidTextureCreatorWindow.cs
using UnityEngine;
using UnityEditor;
using System.IO;

public class SolidTextureCreatorWindow : EditorWindow
{
    private Color color = Color.white;
    private int size = 1;              // 1,2,4,8... 권장
    private bool useAlpha = false;
    private float alpha = 1f;

    private string fileName = "SolidColor";
    private string saveFolder = "Assets";
    private bool createTerrainLayer = true;
    private bool assignToSelectedTerrain = true;

    // Import settings
    private bool sRGB = true;
    private TextureImporterCompression compression = TextureImporterCompression.Uncompressed;
    private TextureWrapMode wrapMode = TextureWrapMode.Repeat;
    private FilterMode filterMode = FilterMode.Bilinear;

    [MenuItem("Tools/Terrain/Solid Texture Creator")]
    private static void ShowWindow()
    {
        var wnd = GetWindow<SolidTextureCreatorWindow>("Solid Texture Creator");
        wnd.minSize = new Vector2(340, 320);
        wnd.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Color", EditorStyles.boldLabel);
        color = EditorGUILayout.ColorField("Albedo", color);
        useAlpha = EditorGUILayout.Toggle("Use Alpha", useAlpha);
        EditorGUI.indentLevel++;
        using (new EditorGUI.DisabledScope(!useAlpha))
        {
            alpha = EditorGUILayout.Slider("Alpha", alpha, 0f, 1f);
        }
        EditorGUI.indentLevel--;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Texture", EditorStyles.boldLabel);
        size = Mathf.Max(1, EditorGUILayout.IntField("Size (px)", size));
        if (!Mathf.IsPowerOfTwo(size))
            EditorGUILayout.HelpBox("가능하면 1,2,4,8,16...처럼 2의 거듭제곱을 권장합니다.", MessageType.Info);

        fileName = EditorGUILayout.TextField("File Name", fileName);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("Save Folder");
        EditorGUILayout.SelectableLabel(saveFolder, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        if (GUILayout.Button("Select...", GUILayout.Width(90)))
        {
            var path = EditorUtility.OpenFolderPanel("Select Folder under Assets", Application.dataPath, "");
            if (!string.IsNullOrEmpty(path))
            {
                // 프로젝트 상대 경로로 변환
                if (path.StartsWith(Application.dataPath))
                    saveFolder = "Assets" + path.Substring(Application.dataPath.Length);
                else
                    EditorUtility.DisplayDialog("경고", "Assets 폴더 내부만 선택할 수 있습니다.", "OK");
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Import Settings", EditorStyles.boldLabel);
        sRGB = EditorGUILayout.Toggle("sRGB (Color Texture)", sRGB);
        wrapMode = (TextureWrapMode)EditorGUILayout.EnumPopup("Wrap Mode", wrapMode);
        filterMode = (FilterMode)EditorGUILayout.EnumPopup("Filter Mode", filterMode);
        compression = (TextureImporterCompression)EditorGUILayout.EnumPopup("Compression", compression);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Terrain", EditorStyles.boldLabel);
        createTerrainLayer = EditorGUILayout.Toggle("Create TerrainLayer", createTerrainLayer);
        using (new EditorGUI.DisabledScope(!createTerrainLayer))
        {
            assignToSelectedTerrain = EditorGUILayout.Toggle("Assign to Selected Terrain", assignToSelectedTerrain);
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Create", GUILayout.Height(28)))
            CreateTextureAndAssets();
    }

    private void CreateTextureAndAssets()
    {
        if (string.IsNullOrEmpty(fileName))
        {
            EditorUtility.DisplayDialog("오류", "File Name을 입력하세요.", "OK");
            return;
        }
        if (!AssetDatabase.IsValidFolder(saveFolder))
        {
            EditorUtility.DisplayDialog("오류", "Save Folder가 유효하지 않습니다.", "OK");
            return;
        }

        // 1) 텍스처 생성
        var fmt = TextureFormat.RGBA32; // 알파 유무와 무관하게 RGBA32로 저장(단색이라 용량 매우 작음)
        var tex = new Texture2D(size, size, fmt, false, false);
        var c = color;
        if (useAlpha) c.a = alpha;
        else c.a = 1f;

        // 한 번에 채우기
        var pixels = tex.GetPixels32();
        var c32 = (Color32)c;
        for (int i = 0; i < pixels.Length; i++) pixels[i] = c32;
        tex.SetPixels32(pixels);
        tex.Apply();

        // 2) PNG로 저장
        var png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);

        string pngPath = $"{saveFolder}/{fileName}.png";
        File.WriteAllBytes(pngPath, png);
        AssetDatabase.ImportAsset(pngPath);

        // 3) 임포트 설정 적용
        var importer = (TextureImporter)AssetImporter.GetAtPath(pngPath);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = sRGB;
        importer.wrapMode = wrapMode;
        importer.filterMode = filterMode;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.textureCompression = compression;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();

        var texAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);

        // 4) TerrainLayer 생성(선택)
        TerrainLayer tl = null;
        if (createTerrainLayer)
        {
            tl = new TerrainLayer();
            tl.diffuseTexture = texAsset;

            // 단색은 타일 사이즈가 의미 없지만, 기본값은 1x1로
            tl.tileSize = new Vector2(1, 1);

            string tlPath = $"{saveFolder}/{fileName}_TerrainLayer.asset";
            AssetDatabase.CreateAsset(tl, tlPath);
            AssetDatabase.SaveAssets();

            if (assignToSelectedTerrain)
            {
                var terrain = Selection.activeGameObject ? Selection.activeGameObject.GetComponent<Terrain>() : null;
                if (terrain != null)
                {
                    var layers = terrain.terrainData.terrainLayers;
                    ArrayUtility.Add(ref layers, tl);
                    terrain.terrainData.terrainLayers = layers;
                }
                else
                {
                    Debug.LogWarning("선택된 오브젝트에 Terrain이 없습니다. TerrainLayer만 생성합니다.");
                }
            }
        }

        EditorGUIUtility.PingObject(createTerrainLayer ? (Object)tl : texAsset);
        EditorUtility.DisplayDialog("완료", $"생성됨:\n{pngPath}" + (createTerrainLayer ? $"\nTerrainLayer 포함" : ""), "OK");
    }
}
