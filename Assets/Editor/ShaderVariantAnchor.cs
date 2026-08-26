using UnityEditor;
using UnityEngine;

namespace RoadRage.Editor
{
    /// Biome materials are built at runtime with `new Material(...)`, so nothing in the
    /// project references the URP Lit variants they enable. The URP shader stripper then
    /// drops those variants from the build, EnableKeyword silently does nothing, and every
    /// surface-mapped material falls back to its plain float values - which is why the
    /// canyon rendered as chrome. Materials under Resources/ are always included, so this
    /// asset exists purely to keep the keyword combination alive in player builds.
    public static class ShaderVariantAnchor
    {
        private const string AssetPath = "Assets/Resources/SurfaceVariantAnchor.mat";

        /// Untextured particles render as hard-edged quads - visible as grey squares in
        /// the wheel spray. A radial alpha falloff turns them into soft droplets.
        private static Texture2D SoftDot()
        {
            const string path = "Assets/Resources/SoftDot.png";
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = (x + 0.5f) / size - 0.5f;
                var dy = (y + 0.5f) / size - 0.5f;
                var falloff = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) * 2f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, falloff * falloff));
            }
            texture.Apply();
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public static void Generate()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError("RR_ANCHOR could not find URP Lit shader");
                return;
            }

            // Must be real project assets: URP's material validator re-derives the
            // keywords from the assigned texture slots when the asset is saved, so
            // runtime-only textures (Texture2D.whiteTexture) leave the keywords empty.
            const string dir = "Assets/Resources/Biomes/RedCanyon/Textures/";
            Texture Load(string file) => AssetDatabase.LoadAssetAtPath<Texture>(dir + file);

            var material = new Material(shader) { name = "SurfaceVariantAnchor" };
            material.SetTexture("_BaseMap", Load("T_rock_01_D.png"));
            material.SetTexture("_MetallicGlossMap", Load("T_rock_01_MSO.png"));
            material.SetTexture("_OcclusionMap", Load("T_rock_01_MSO.png"));
            material.SetTexture("_BumpMap", Load("T_rock_01_N.png"));
            material.SetTexture("_EmissionMap", Load("T_rock_01_D.png"));
            material.SetColor("_EmissionColor", Color.black);
            foreach (var keyword in new[]
                     {
                         "_METALLICSPECGLOSSMAP", "_OCCLUSIONMAP", "_NORMALMAP",
                         "_EMISSION", "_ALPHATEST_ON"
                     })
                material.EnableKeyword(keyword);

            AssetDatabase.CreateAsset(material, AssetPath);

            // _ALPHATEST_ON and _EMISSION are derived by URP's material validator from
            // _AlphaClip / _EmissionColor rather than from EnableKeyword, so they need
            // their own anchors or foliage renders opaque and emissive surfaces go flat.
            var cutout = new Material(shader) { name = "CutoutVariantAnchor" };
            cutout.SetTexture("_BaseMap", Load("T_leafs_D.png"));
            cutout.SetTexture("_BumpMap", Load("T_leafs_N.png"));
            cutout.SetFloat("_AlphaClip", 1f);
            cutout.SetFloat("_Cutoff", 0.4f);
            cutout.SetFloat("_Cull", 0f);
            cutout.EnableKeyword("_ALPHATEST_ON");
            cutout.EnableKeyword("_NORMALMAP");
            cutout.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            AssetDatabase.CreateAsset(cutout, "Assets/Resources/CutoutVariantAnchor.mat");

            var emissive = new Material(shader) { name = "EmissiveVariantAnchor" };
            emissive.SetTexture("_BaseMap", Load("T_rock_01_D.png"));
            emissive.SetTexture("_EmissionMap", Load("T_rock_01_D.png"));
            emissive.SetColor("_EmissionColor", new Color(1.5f, 1.5f, 1.5f));
            emissive.EnableKeyword("_EMISSION");
            emissive.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            AssetDatabase.CreateAsset(emissive, "Assets/Resources/EmissiveVariantAnchor.mat");

            // Particle material for rain/snow/spray. Must be a real asset: a runtime
            // transparent unlit material would lose its blend variant to stripping the
            // same way the surface keywords did.
            var particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                                 ?? Shader.Find("Universal Render Pipeline/Unlit");
            var particle = new Material(particleShader) { name = "WeatherParticle" };
            particle.SetTexture("_BaseMap", SoftDot());
            particle.SetFloat("_Surface", 1f);      // transparent
            particle.SetFloat("_Blend", 0f);        // alpha blend
            particle.SetFloat("_ZWrite", 0f);
            particle.SetColor("_BaseColor", Color.white);
            particle.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            particle.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            particle.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            AssetDatabase.CreateAsset(particle, "Assets/Resources/WeatherParticle.mat");

            // Transparent Lit anchor for road/wall decals. Same stripping trap as the
            // surface keywords: a runtime-built transparent material loses its blend
            // variant and the decals would render as opaque black rectangles.
            var decal = new Material(shader) { name = "DecalVariantAnchor" };
            decal.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture>("Assets/Resources/Decals/D_tyre_streak.png"));
            decal.SetFloat("_Surface", 1f);
            decal.SetFloat("_Blend", 0f);
            decal.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            decal.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            decal.SetFloat("_ZWrite", 0f);
            decal.SetFloat("_Cull", 0f);
            decal.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            decal.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            AssetDatabase.CreateAsset(decal, "Assets/Resources/DecalVariantAnchor.mat");

            // Anchor the custom terrain shader too - same stripping rule as URP Lit, and
            // a stripped splat shader would fall back to pink or to flat Lit.
            var splatShader = Shader.Find("RoadRage/TerrainSplat");
            if (splatShader != null)
            {
                var splat = new Material(splatShader) { name = "SplatVariantAnchor" };
                splat.SetTexture("_Splat0", Load("T_sand_D.png"));
                splat.SetTexture("_Splat1", Load("T_rock_ground_D.png"));
                splat.SetTexture("_Splat2", Load("T_stones_D.png"));
                splat.SetTexture("_Normal0", Load("T_sand_N.png"));
                splat.SetTexture("_Normal1", Load("T_rock_ground_N.png"));
                splat.SetTexture("_Normal2", Load("T_stones_N.png"));
                AssetDatabase.CreateAsset(splat, "Assets/Resources/SplatVariantAnchor.mat");
            }
            else Debug.LogWarning("RR_ANCHOR TerrainSplat shader not found");

            // URP/Unlit anchor for the runtime lit-window materials. The _EMISSION keyword
            // path on URP Lit proved unreliable in player builds (windows stayed dark while
            // bright albedo read as glow), so windows use Unlit - but Unlit has no other
            // reference in the project and would be stripped entirely without this asset.
            var unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader != null)
            {
                var unlit = new Material(unlitShader) { name = "UnlitVariantAnchor" };
                unlit.SetTexture("_BaseMap", Load("T_rock_01_D.png"));
                AssetDatabase.CreateAsset(unlit, "Assets/Resources/UnlitVariantAnchor.mat");
            }
            else Debug.LogWarning("RR_ANCHOR URP/Unlit shader not found");

            AssetDatabase.SaveAssets();
            Debug.Log($"RR_ANCHOR particle shader={particleShader.name} " +
                      $"surface=[{string.Join(",", material.shaderKeywords)}] " +
                      $"cutout=[{string.Join(",", cutout.shaderKeywords)}] " +
                      $"emissive=[{string.Join(",", emissive.shaderKeywords)}]");
        }
    }
}
