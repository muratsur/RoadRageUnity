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
        if (name.StartsWith("car", System.StringComparison.OrdinalIgnoreCase))
            SetUpCarPaint(folder, name, material);

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

    /// A car's paint, split from the rest of its texture so traffic can repaint it.
    ///
    /// The paint is found as the most common strong hue in the texture. Where it is,
    /// texture_paint_base.png holds grey at half brightness, keeping the paint's own
    /// light and shade, and texture_paint_mask.png holds how much of the pixel is paint
    /// (in alpha). <name>_paint.mat is the car's URP material with those two and URP
    /// Lit's detail colour, which doubles and multiplies over the mask: a detail colour
    /// C paints the body C, and the game swaps C per car. Glass, tyres, lights and
    /// chrome - grey or dark, or another hue - are outside the mask and keep their own
    /// colour. A white, silver or black car has no hue to find and is left as it is.
    private static void SetUpCarPaint(string folder, string name, Material source)
    {
        var diffuseFile = $"{folder}/texture_diffuse.png";
        var diffuse = new Texture2D(2, 2);
        diffuse.LoadImage(File.ReadAllBytes(diffuseFile));
        var pixels = diffuse.GetPixels32();
        var width = diffuse.width;
        var height = diffuse.height;
        Object.DestroyImmediate(diffuse);
        var count = pixels.Length;
        var hue = new float[count];
        var sat = new float[count];
        var val = new float[count];

        // The paint's hue: a histogram of the strong colours, weighted by how strong.
        const int bins = 36;
        var histogram = new float[bins];
        for (var i = 0; i < count; i++)
        {
            Color.RGBToHSV(pixels[i], out hue[i], out sat[i], out val[i]);
            if (sat[i] > 0.3f && val[i] > 0.15f) histogram[Mathf.Min(bins - 1, (int)(hue[i] * bins))] += sat[i] * val[i];
        }
        var peak = 0;
        for (var b = 1; b < bins; b++)
            if (histogram[b] > histogram[peak]) peak = b;
        // The peak and its neighbours, for the hue at the centre of the paint.
        float x = 0f, y = 0f;
        for (var d = -1; d <= 1; d++)
        {
            var b = (peak + d + bins) % bins;
            var angle = (b + 0.5f) / bins * 2f * Mathf.PI;
            x += Mathf.Cos(angle) * histogram[b];
            y += Mathf.Sin(angle) * histogram[b];
        }
        var paintHue = Mathf.Repeat(Mathf.Atan2(y, x) / (2f * Mathf.PI), 1f);

        var mask = new float[count];
        float maskSum = 0f, valueSum = 0f;
        var colourSum = Vector3.zero;
        for (var i = 0; i < count; i++)
        {
            var apart = Mathf.Abs(Mathf.DeltaAngle(hue[i] * 360f, paintHue * 360f));
            var m = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(18f, 34f, apart))) *
                    Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.14f, 0.3f, sat[i])) *
                    Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.14f, val[i]));
            mask[i] = m;
            maskSum += m;
            valueSum += m * val[i];
            colourSum += m * new Vector3(pixels[i].r, pixels[i].g, pixels[i].b) / 255f;
        }

        var materialPath = $"{folder}/{name}_paint.mat";
        // Too little of it to be a body: a white, silver or black car.
        if (maskSum < count * 0.03f)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) != null) AssetDatabase.DeleteAsset(materialPath);
            Debug.LogWarning($"Set Up Rodin Models: {name} - no paint colour found " +
                             $"({100f * maskSum / count:0.#}% of the texture), it keeps its own colour in traffic.");
            return;
        }
        var meanValue = valueSum / maskSum;
        var meanColour = colourSum / maskSum;

        var basePixels = new Color32[count];
        var maskPixels = new Color32[count];
        for (var i = 0; i < count; i++)
        {
            var grey = Mathf.Clamp01(0.5f * val[i] / meanValue);
            var p = pixels[i];
            var m = mask[i];
            var g = grey * 255f;
            basePixels[i] = new Color32((byte)Mathf.Lerp(p.r, g, m), (byte)Mathf.Lerp(p.g, g, m),
                (byte)Mathf.Lerp(p.b, g, m), p.a);
            var a = (byte)(m * 255f);
            maskPixels[i] = new Color32(a, a, a, a);
        }
        var basePath = $"{folder}/texture_paint_base.png";
        var maskPath = $"{folder}/texture_paint_mask.png";
        var colourPath = $"{folder}/texture_paint_colour.png";
        WritePng(basePath, width, height, basePixels);
        WritePng(maskPath, width, height, maskPixels);
        // The car's own paint colour, until the game picks another.
        var own = new Color(meanColour.x, meanColour.y, meanColour.z, 1f);
        var ownPixels = new Color32[16];
        for (var i = 0; i < ownPixels.Length; i++) ownPixels[i] = own;
        WritePng(colourPath, 4, 4, ownPixels);

        AssetDatabase.ImportAsset(basePath);
        AssetDatabase.ImportAsset(maskPath);
        AssetDatabase.ImportAsset(colourPath);
        if (AssetImporter.GetAtPath(maskPath) is TextureImporter maskImporter)
        {
            maskImporter.sRGBTexture = false;
            maskImporter.alphaSource = TextureImporterAlphaSource.FromInput;
            maskImporter.alphaIsTransparency = false;
            maskImporter.SaveAndReimport();
        }
        if (AssetImporter.GetAtPath(colourPath) is TextureImporter colourImporter)
        {
            colourImporter.mipmapEnabled = false;
            colourImporter.textureCompression = TextureImporterCompression.Uncompressed;
            colourImporter.SaveAndReimport();
        }

        var paint = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (paint == null)
        {
            paint = new Material(source);
            AssetDatabase.CreateAsset(paint, materialPath);
        }
        else paint.CopyPropertiesFromMaterial(source);
        paint.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(basePath));
        paint.SetTexture("_DetailMask", AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath));
        paint.SetTexture("_DetailAlbedoMap", AssetDatabase.LoadAssetAtPath<Texture2D>(colourPath));
        paint.SetFloat("_DetailAlbedoMapScale", 1f);
        paint.EnableKeyword("_DETAIL_MULX2");
        // Drawn from both sides, as at the test spot: a gap in the shell shows the body.
        paint.SetFloat("_Cull", 0f);
        paint.doubleSidedGI = true;
        EditorUtility.SetDirty(paint);
        Debug.Log($"Set Up Rodin Models: {name} paint hue {paintHue * 360f:0} deg, " +
                  $"{100f * maskSum / count:0.#}% of the texture is paint -> {materialPath}");
    }

    private static void WritePng(string path, int width, int height, Color32[] pixels)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
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
