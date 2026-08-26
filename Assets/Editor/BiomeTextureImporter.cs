using UnityEditor;

namespace RoadRage.Editor
{
    /// Biome textures are copied in by prepare_unity_biomes.py, so they arrive with
    /// Unity's default import settings - which treats everything as sRGB colour.
    /// That silently breaks two things:
    ///   * normal maps get gamma-decoded and read as weak/incorrect surface detail
    ///   * the repacked _MSO maps (metallic/AO/smoothness) are data, not colour, and
    ///     must stay linear or roughness comes out wrong across the whole surface
    public sealed class BiomeTextureImporter : AssetPostprocessor
    {
        /// Unity caches import results, so editing the rules above does nothing to
        /// already-imported textures. Bumping this forces a reimport of everything
        /// this postprocessor touches.
        public override uint GetVersion() => 4;

        private void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/Resources/Biomes/") && !assetPath.Contains("/Resources/Hideout/")) return;

            var importer = (TextureImporter)assetImporter;
            var name = System.IO.Path.GetFileNameWithoutExtension(assetPath);

            if (name.EndsWith("_N") || name.EndsWith("_normal"))
            {
                importer.textureType = TextureImporterType.NormalMap;
            }
            else if (name.EndsWith("_MSO") || name.EndsWith("_AORM") || name.EndsWith("_ORM"))
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
            }

            ApplyMobileBudget(importer);
        }

        /// Source art ships at 2048 for desktop, but that put the Windows build at 1.17 GB.
        /// Phones get a 1024 override at import time - a runtime quality tier cannot shrink
        /// files that are already in the build, so this has to happen here.
        private static void ApplyMobileBudget(TextureImporter importer)
        {
            foreach (var platform in new[] { "Android", "iPhone" })
            {
                var settings = importer.GetPlatformTextureSettings(platform);
                settings.overridden = true;
                // 1024 produced a 242 MB APK - over Play's 200 MB cap and iOS's
                // cellular-download threshold. 512 is what actually ships.
                settings.maxTextureSize = 512;
                settings.format = TextureImporterFormat.ASTC_6x6;
                settings.compressionQuality = 50;
                importer.SetPlatformTextureSettings(settings);
            }
        }
    }
}
