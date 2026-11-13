using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TDT16.PhotoObjects
{
    public static class PhotoObjectSetup
    {
        [Serializable] class MeasurementReport { public double volume_dataset_units_cubed; }
        [MenuItem("Tools/Photo Object/Create Dinosaur Scene")]
        public static void CreateScene()
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            GameObject asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Archived/DinosaurDemo/Data/DinosaurReconstructed.obj");
            if(asset==null){Debug.LogError("Dinosaur OBJ has not finished importing.");return;}
            MeshFilter filter=asset.GetComponentInChildren<MeshFilter>();
            if(filter==null){Debug.LogError("No mesh found in dinosaur OBJ.");return;}
            string path=AssetDatabase.GenerateUniqueAssetPath("Assets/Archived/DinosaurDemo/DinosaurPhotoDemo.unity");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            PhotoObjectViewer viewer=new GameObject("Photo Object Lab").AddComponent<PhotoObjectViewer>();
            viewer.reconstructedMesh=filter.sharedMesh;
            viewer.reconstructionReport=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Archived/DinosaurDemo/Data/reconstruction_report.json");
            viewer.pointShader=Shader.Find("TDT16/Photo Object Points");
            string[] ids=AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/Archived/DinosaurDemo/Photos"});
            Array.Sort(ids,(a,b)=>string.CompareOrdinal(AssetDatabase.GUIDToAssetPath(a),AssetDatabase.GUIDToAssetPath(b)));
            viewer.sourcePhotos=new Texture2D[ids.Length];
            for(int i=0;i<ids.Length;i++)viewer.sourcePhotos[i]=AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(ids[i]));
            EditorSceneManager.SaveScene(viewer.gameObject.scene,path);Selection.activeGameObject=viewer.gameObject;
            Debug.Log("Dinosaur photo reconstruction ready. Press Play. Volume uses original mesh coordinates, independently of display scale.");
        }

        [MenuItem("Tools/Photo Object/Run Volume Checks")]
        public static void CheckVolume()
        {
            Debug.Log("Photo Object: starting volume and imported-mesh checks.");
            Mesh mesh=new Mesh();
            try
            {
                mesh.vertices=new[]{Vector3.zero,Vector3.right,Vector3.up,Vector3.forward};
                mesh.triangles=new[]{0,2,1,0,1,3,0,3,2,1,2,3};
                bool closed;double volume=MeshMeasurements.Volume(mesh,out closed);
                if(!closed || Math.Abs(volume-1.0/6)>1e-8)throw new Exception("Tetrahedron volume check failed.");
                mesh.vertices=new[]{Vector3.zero,Vector3.right*2,Vector3.up*2,Vector3.forward*2};
                volume=MeshMeasurements.Volume(mesh,out closed);
                if(!closed || Math.Abs(volume-8.0/6)>1e-8)throw new Exception("Cubic scale check failed.");
                mesh.triangles=new[]{0,2,1,0,1,3,0,3,2};
                MeshMeasurements.Volume(mesh,out closed);
                if(closed)throw new Exception("Open mesh must fail the closed-edge check.");
                GameObject asset=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Archived/DinosaurDemo/Data/DinosaurReconstructed.obj");
                Mesh dino=asset.GetComponentInChildren<MeshFilter>().sharedMesh;
                volume=MeshMeasurements.Volume(dino,out closed);
                if(!closed || volume<=0)throw new Exception("Imported dinosaur failed topology/volume check.");
                TextAsset json=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Archived/DinosaurDemo/Data/reconstruction_report.json");
                double expected=JsonUtility.FromJson<MeasurementReport>(json.text).volume_dataset_units_cubed;
                if(Math.Abs(volume-expected)/expected>0.0001)throw new Exception("Unity mesh volume differs from reconstruction output; check import scale and mesh splitting.");
                Debug.Log("Photo Object: ALL 4 VOLUME CHECKS PASSED. Dinosaur volume: "+volume.ToString("G12")+" dataset units cubed.");
            }
            finally{UnityEngine.Object.DestroyImmediate(mesh);}
        }
    }
}
