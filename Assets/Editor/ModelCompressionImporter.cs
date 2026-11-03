using UnityEditor;

namespace RoadRage.Editor
{
    /// Meshes were 59% of the phone build (366 MB): 1,100 models imported with no mesh
    /// compression. Medium stores positions, normals and UVs in fewer bits, which scenery and
    /// cars seen from a chase camera do not show. A model that already chose a level keeps it.
    public sealed class ModelCompressionImporter : AssetPostprocessor
    {
        public override uint GetVersion() => 1;

        private void OnPreprocessModel()
        {
            var importer = (ModelImporter)assetImporter;
            if (importer.meshCompression == ModelImporterMeshCompression.Off)
                importer.meshCompression = ModelImporterMeshCompression.Medium;
        }
    }
}
