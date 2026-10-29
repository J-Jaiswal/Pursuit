using UnityEngine;
using UnityEditor;
using TMPro;

public static class CreateButtonTextMaterials
{
    [MenuItem("Tools/UI/Create Button Text Materials")]
    public static void CreateMaterials()
    {
        const string folder = "Assets/Materials/UI";

        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");

        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/Materials", "UI");

        Shader shader = Shader.Find("TextMeshPro/Distance Field");

        if (shader == null)
        {
            EditorUtility.DisplayDialog(
                "TextMeshPro shader not found",
                "Import/enable TextMeshPro first, then run this tool again.",
                "OK"
            );
            return;
        }

        string[] names =
        {
            "ButtonText_Play",
            "ButtonText_Continue",
            "ButtonText_Guide",
            "ButtonText_Quit"
        };

        foreach (string name in names)
        {
            string path = $"{folder}/{name}.mat";

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }

            // Main text: warm off-white, matching the reference.
            if (mat.HasProperty("_FaceColor"))
                mat.SetColor("_FaceColor", new Color32(231, 227, 229, 255));

            // Very subtle dark edge for readability.
            if (mat.HasProperty("_OutlineColor"))
                mat.SetColor("_OutlineColor", new Color32(24, 21, 25, 210));

            if (mat.HasProperty("_OutlineWidth"))
                mat.SetFloat("_OutlineWidth", 0.035f);

            if (mat.HasProperty("_OutlineSoftness"))
                mat.SetFloat("_OutlineSoftness", 0.10f);

            // Soft shadow, similar to the reference image.
            if (mat.HasProperty("_UnderlayColor"))
                mat.SetColor("_UnderlayColor", new Color32(0, 0, 0, 90));

            if (mat.HasProperty("_UnderlayOffsetX"))
                mat.SetFloat("_UnderlayOffsetX", 0.8f);

            if (mat.HasProperty("_UnderlayOffsetY"))
                mat.SetFloat("_UnderlayOffsetY", -0.8f);

            if (mat.HasProperty("_UnderlayDilate"))
                mat.SetFloat("_UnderlayDilate", 0.0f);

            if (mat.HasProperty("_UnderlaySoftness"))
                mat.SetFloat("_UnderlaySoftness", 0.20f);

            EditorUtility.SetDirty(mat);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Button text materials created",
            "Created 4 TextMeshPro materials in Assets/Materials/UI.",
            "OK"
        );
    }
}
