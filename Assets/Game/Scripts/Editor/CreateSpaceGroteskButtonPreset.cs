using UnityEngine;
using UnityEditor;
using TMPro;

public static class CreateSpaceGroteskButtonPreset
{
    [MenuItem("Tools/UI/Create SpaceGrotesk Button Preset")]
    public static void CreatePreset()
    {
        // Select the existing SpaceGrotesk-Regular SDF Material in the Project window
        // before running this menu item.
        Object selected = Selection.activeObject;

        if (selected == null)
        {
            EditorUtility.DisplayDialog(
                "Select the TMP material first",
                "Select SpaceGrotesk-Regular SDF Material in the Project window, then run this tool again.",
                "OK");
            return;
        }

        string sourcePath = AssetDatabase.GetAssetPath(selected);
        Material source = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);

        if (source == null || source.shader == null ||
            source.shader.name != "TextMeshPro/Distance Field")
        {
            EditorUtility.DisplayDialog(
                "Wrong material selected",
                "Please select your SpaceGrotesk-Regular SDF Material (the material currently shown in the TextMeshPro Material Preset field).",
                "OK");
            return;
        }

        string sourceFolder = System.IO.Path.GetDirectoryName(sourcePath).Replace("\\", "/");
        string presetPath = sourceFolder + "/ButtonText_SpaceGrotesk.mat";

        // If it already exists, overwrite it by deleting the old preset.
        if (AssetDatabase.LoadAssetAtPath<Material>(presetPath) != null)
        {
            AssetDatabase.DeleteAsset(presetPath);
        }

        if (!AssetDatabase.CopyAsset(sourcePath, presetPath))
        {
            EditorUtility.DisplayDialog(
                "Could not create preset",
                "Unity could not duplicate the selected material.",
                "OK");
            return;
        }

        AssetDatabase.ImportAsset(presetPath);
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(presetPath);

        // Reference-style appearance: warm white text with a subtle dark outline
        // and soft shadow.
        SetColor(mat, "_FaceColor", new Color32(231, 227, 229, 255));
        SetColor(mat, "_OutlineColor", new Color32(24, 21, 25, 210));
        SetFloat(mat, "_OutlineWidth", 0.035f);
        SetFloat(mat, "_OutlineSoftness", 0.10f);

        SetColor(mat, "_UnderlayColor", new Color32(0, 0, 0, 90));
        SetFloat(mat, "_UnderlayOffsetX", 0.8f);
        SetFloat(mat, "_UnderlayOffsetY", -0.8f);
        SetFloat(mat, "_UnderlayDilate", 0.0f);
        SetFloat(mat, "_UnderlaySoftness", 0.20f);

        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeObject = mat;

        EditorUtility.DisplayDialog(
            "Button material created",
            "Created ButtonText_SpaceGrotesk.mat.\n\nAssign it to the Material Preset field of your PLAY, CONTINUE, GUIDE and QUIT TextMeshPro objects.",
            "OK");
    }

    static void SetColor(Material mat, string property, Color value)
    {
        if (mat.HasProperty(property))
            mat.SetColor(property, value);
    }

    static void SetFloat(Material mat, string property, float value)
    {
        if (mat.HasProperty(property))
            mat.SetFloat(property, value);
    }
}
