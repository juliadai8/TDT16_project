using UnityEditor;

namespace TDT16.PhotoObjects
{
    public class PhotoObjectImportSettings : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if(!assetPath.StartsWith("Assets/PhotoObjectLab/Data/Dinosaur")) return;
            ModelImporter importer=(ModelImporter)assetImporter;
            importer.isReadable=true;
            importer.globalScale=1;
            importer.meshCompression=ModelImporterMeshCompression.Off;
            importer.materialImportMode=ModelImporterMaterialImportMode.None;
            importer.indexFormat=ModelImporterIndexFormat.UInt32;
        }
    }
}
