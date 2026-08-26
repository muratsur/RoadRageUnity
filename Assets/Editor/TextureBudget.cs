using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RoadRage.Editor
{
    /// Textures were 93.9% of a 1.25 GB build because every map shipped at 2048 with no
    /// per-role budget. Resolution is assigned by what each map actually carries:
    ///   albedo  - the only map with high-frequency detail the player reads directly
    ///   normal  - halves cleanly; surface detail survives at 1024/512
    ///   MSO     - AO/roughness/metallic are low-frequency; 512 is indistinguishable
    ///   emissive- sign glow, no fine detail needed
    /// "Hero" surfaces (road, ground planes, anything covering a large part of screen)
    /// keep a tier more resolution than props.
    public static class TextureBudget
    {
        // Substrings identifying large-area surfaces the camera sits right on top of.
        private static readonly string[] HeroSurfaces =
        {
            "asphalt", "t_sand", "rock_ground", "t_snow", "ground_texture",
            "concrete_floor", "t_ground", "t_concrete_03", "street_module",
        };

        private const int HeroAlbedo = 2048;
        private const int PropAlbedo = 1024;
        private const int HeroNormal = 1024;
        private const int PropNormal = 512;
        private const int SurfaceMap = 512;

        private static bool IsHero(string lower) => HeroSurfaces.Any(lower.Contains);

        private static int BudgetFor(string path)
        {
            var lower = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            var hero = IsHero(lower);
            if (lower.EndsWith("_mso")) return SurfaceMap;
            if (lower.EndsWith("_e")) return SurfaceMap;
            if (lower.EndsWith("_n")) return hero ? HeroNormal : PropNormal;
            return hero ? HeroAlbedo : PropAlbedo;
        }

        public static void Apply()
        {
            var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".png"))
                .OrderBy(p => p)
                .ToList();

            var changed = 0;
            var byBudget = new Dictionary<int, int>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in paths)
                {
                    if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
                    var budget = BudgetFor(path);
                    byBudget[budget] = byBudget.GetValueOrDefault(budget) + 1;

                    var lower = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                    var dirty = false;

                    if (importer.maxTextureSize != budget) { importer.maxTextureSize = budget; dirty = true; }
                    if (importer.textureCompression != TextureImporterCompression.CompressedHQ)
                    {
                        importer.textureCompression = TextureImporterCompression.CompressedHQ;
                        dirty = true;
                    }
                    // Crunch shrinks the shipped file well beyond the GPU format; the cost
                    // is import time, paid once here rather than by every download.
                    if (!importer.crunchedCompression) { importer.crunchedCompression = true; dirty = true; }
                    if (importer.compressionQuality != 50) { importer.compressionQuality = 50; dirty = true; }

                    // Normal maps must stay in the normal-map type so URP unpacks them
                    // correctly; MSO/emissive stay linear default textures.
                    var wantNormal = lower.EndsWith("_n");
                    var isNormal = importer.textureType == TextureImporterType.NormalMap;
                    if (wantNormal != isNormal)
                    {
                        importer.textureType = wantNormal
                            ? TextureImporterType.NormalMap
                            : TextureImporterType.Default;
                        dirty = true;
                    }

                    if (!dirty) continue;
                    importer.SaveAndReimport();
                    changed++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            var summary = string.Join(", ", byBudget.OrderByDescending(p => p.Key).Select(p => $"{p.Key}px x{p.Value}"));
            Debug.Log($"RR_TEXBUDGET retargeted {changed}/{paths.Count} textures -> {summary}");
        }
    }
}
