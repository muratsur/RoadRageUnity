using System.IO;
using UnityEditor;
using UnityEngine;

/// Road Rage > Set Up Rodin Models: gives every model exported from Hyper3D Rodin under
/// Assets/Resources/TestAssets a proper URP material.
///
/// Rodin's FBX export comes with loose maps - texture_diffuse, texture_normal,
/// texture_metallic, texture_roughness - and a material that only knows the colour.
/// URP's Lit takes metallic in red and smoothness (1 - roughness) in alpha of one
/// texture, and a normal map only works once it is imported as one. Imported as they
/// come, the models looked flat, like painted plastic.
///
/// For each folder holding a texture_diffuse.png, this marks the normal map as a normal
/// map, packs metallic and smoothness into texture_metallic_smoothness.png, makes one
/// URP Lit material next to them and points every material of each FBX in that folder
/// at it. Safe to run again after adding more models.
public static class SetUpRodinModelsMenu
{
    private const string Root = "Assets/Resources/TestAssets";

    [MenuItem("Road Rage/Set Up Rodin Models")]
    public static void SetUp()
    {
        if (!AssetDatabase.IsValidFolder(Root))
        {
            Debug.LogError($"Set Up Rodin Models: no {Root} folder.");
            return;
        }
        var done = 0;
        foreach (var diffusePath in Directory.GetFiles(Root, "texture_diffuse.png", SearchOption.AllDirectories))
        {
            var folder = Path.GetDirectoryName(diffusePath).Replace('\\', '/');
            if (SetUpFolder(folder)) done++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"Set Up Rodin Models: {done} model folder(s) set up under {Root}.");
    }

    private static bool SetUpFolder(string folder)
    {
        var name = Path.GetFileName(folder);
        var diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/texture_diffuse.png");
        if (diffuse == null) return false;

        var normalPath = $"{folder}/texture_normal.png";
        if (AssetImporter.GetAtPath(normalPath) is TextureImporter normalImporter &&
            normalImporter.textureType != TextureImporterType.NormalMap)
        {
            normalImporter.textureType = TextureImporterType.NormalMap;
            normalImporter.SaveAndReimport();
        }
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);

        var maskPath = $"{folder}/texture_metallic_smoothness.png";
        var mask = PackMetallicSmoothness(folder, maskPath);

        var materialPath = $"{folder}/{name}_urp.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.SetTexture("_BaseMap", diffuse);
        material.SetColor("_BaseColor", Color.white);
        if (normal != null)
        {
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_BumpScale", 1f);
            material.EnableKeyword("_NORMALMAP");
        }
        if (mask != null)
        {
            material.SetTexture("_MetallicGlossMap", mask);
            material.SetFloat("_Smoothness", 1f);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        else
        {
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.2f);
        }
        EditorUtility.SetDirty(material);

        foreach (var fbx in Directory.GetFiles(folder, "*.fbx"))
        {
            var path = fbx.Replace('\\', '/');
            if (!(AssetImporter.GetAtPath(path) is ModelImporter model)) continue;
            model.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            foreach (var asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
                if (asset is Material embedded)
                    model.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name), material);
            model.SaveAndReimport();
            Debug.Log($"Set Up Rodin Models: {path} -> {materialPath}");
        }
        return true;
    }

    /// Metallic in red, smoothness (1 - roughness) in alpha, as URP Lit reads them.
    /// Read from the PNG files directly, so the imported textures need not be readable.
    private static Texture2D PackMetallicSmoothness(string folder, string maskPath)
    {
        var roughnessFile = $"{folder}/texture_roughness.png";
        if (!File.Exists(roughnessFile)) return null;
        var roughness = new Texture2D(2, 2);
        roughness.LoadImage(File.ReadAllBytes(roughnessFile));
        Texture2D metallic = null;
        var metallicFile = $"{folder}/texture_metallic.png";
        if (File.Exists(metallicFile))
        {
            metallic = new Texture2D(2, 2);
            metallic.LoadImage(File.ReadAllBytes(metallicFile));
            if (metallic.width != roughness.width || metallic.height != roughness.height) metallic = null;
        }

        var rough = roughness.GetPixels32();
        var metal = metallic != null ? metallic.GetPixels32() : null;
        var packed = new Color32[rough.Length];
        for (var i = 0; i < rough.Length; i++)
        {
            var m = metal != null ? metal[i].r : (byte)0;
            packed[i] = new Color32(m, m, m, (byte)(255 - rough[i].r));
        }
        var result = new Texture2D(roughness.width, roughness.height, TextureFormat.RGBA32, false);
        result.SetPixels32(packed);
        File.WriteAllBytes(maskPath, result.EncodeToPNG());
        Object.DestroyImmediate(result);
        Object.DestroyImmediate(roughness);
        if (metallic != null) Object.DestroyImmediate(metallic);

        AssetDatabase.ImportAsset(maskPath);
        if (AssetImporter.GetAtPath(maskPath) is TextureImporter importer && importer.sRGBTexture)
        {
            importer.sRGBTexture = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
    }
}
