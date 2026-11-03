using UnityEditor;
using UnityEngine;

namespace RoadRage.Editor
{
    /// Every clip was imported as Vorbis at 100% quality, 45 MB of the phone build. Phones get
    /// Vorbis at PhoneQuality, which a phone speaker cannot tell apart; desktop keeps the full
    /// quality. A clip with its own phone settings keeps them.
    public sealed class PhoneAudioImporter : AssetPostprocessor
    {
        private const float PhoneQuality = 0.45f;

        public override uint GetVersion() => 1;

        private void OnPreprocessAudio()
        {
            var importer = (AudioImporter)assetImporter;
            foreach (var platform in new[] { "iOS", "Android" })
            {
                if (importer.ContainsSampleSettingsOverride(platform)) continue;
                var settings = importer.defaultSampleSettings;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = PhoneQuality;
                importer.SetOverrideSampleSettings(platform, settings);
            }
        }
    }
}
