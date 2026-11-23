// CreateWingCMaterials.cs
// Put this file in an Editor folder, e.g. Assets/BetScript/Editor/
// Menu: Tools > Greenhouse > Create Wing C Materials
// Creates URP Lit materials in Assets/_Greenhouse/WingC_Nursery/Materials
// Existing materials with the same name are NOT overwritten.

using UnityEditor;
using UnityEngine;

public static class CreateWingCMaterials
{
    const string Folder = "Assets/_Greenhouse/WingC_Nursery/Materials";

    // name, hex color, alpha
    static readonly (string name, string hex, float alpha)[] Palette =
    {
        ("M_Leaf",        "#4CAF50", 1f),    // leaves, bushes
        ("M_LeafDark",    "#1B7F3A", 1f),    // stems, dark leaves
        ("M_Soil",        "#6D4C41", 1f),    // soil
        ("M_SoilDark",    "#4E342E", 1f),    // bottom soil layer
        ("M_SoilLight",   "#8D6E63", 1f),    // top soil layer
        ("M_Wood",        "#A1887F", 1f),    // tables, shelves, benches
        ("M_Terracotta",  "#C8693F", 1f),    // pots
        ("M_Frame",       "#F5F5F5", 1f),    // greenhouse frame
        ("M_Glass",       "#E3F4FF", 0.15f), // glass panes (transparent)
        ("M_Petal",       "#F06292", 1f),    // flower petals
        ("M_Pollen",      "#FFC107", 1f),    // flower center, seeds
        ("M_Root",        "#EFEBE9", 1f),    // roots
        ("M_Floor",       "#E8E0D6", 1f),    // Wing C floor
        ("M_Dark",        "#263238", 1f),    // lamps, tool handles
    };

    [MenuItem("Tools/Greenhouse/Create Wing C Materials")]
    static void Create()
    {
        EnsureFolder(Folder);

        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
        {
            EditorUtility.DisplayDialog("Create Wing C Materials",
                "URP Lit shader not found. Is this project using URP?", "OK");
            return;
        }

        int created = 0, skipped = 0;
        foreach (var (name, hex, alpha) in Palette)
        {
            string path = $"{Folder}/{name}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) { skipped++; continue; }

            ColorUtility.TryParseHtmlString(hex, out Color c);
            c.a = alpha;

            var m = new Material(lit) { name = name };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", name == "M_Glass" ? 0.9f : 0.2f);
            m.SetFloat("_Metallic", 0f);

            if (alpha < 1f) MakeTransparent(m);

            AssetDatabase.CreateAsset(m, path);
            created++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Wing C] Materials created: {created}, skipped (already exist): {skipped}. Folder: {Folder}");
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>(Folder);
    }

    // URP Lit: Surface Type = Transparent, Blend = Alpha
    static void MakeTransparent(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        m.SetShaderPassEnabled("ShadowCaster", false);
    }

    static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string cur = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = cur + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
            cur = next;
        }
    }
}