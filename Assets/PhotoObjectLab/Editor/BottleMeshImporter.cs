using UnityEditor;

// Update the canonical mesh in place so scene references retain their GUID.
public static class BottleMeshImporter
{
    [MenuItem("Tools/Bottle/Import Aligned Mesh")]
    public static void Import() { ActivateCleanReconstruction.Install(); }
}
