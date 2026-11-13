using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TDT16.PhotoObjects
{
    public class PhotoObjectViewer : MonoBehaviour
    {
        public Mesh reconstructedMesh;
        public Texture2D[] sourcePhotos;
        public TextAsset reconstructionReport;
        public Shader pointShader;
        Transform model, cameraTransform;
        Camera view;
        GameObject solid, pointsRoot;
        Material solidMaterial, pointMaterial, markerMaterial;
        readonly List<Mesh> pointMeshes = new List<Mesh>();
        readonly List<GameObject> markers = new List<GameObject>();
        Camera[] previousCameras;
        AudioListener[] previousListeners;
        float yaw = 35, pitch = 20, distance = 7;
        bool pointView, showPhoto = true, picking, closed, demoScale;
        int photoIndex, picked;
        Vector3 first, second;
        double volume, sourceDistance, centimetresPerUnit;
        string realLength = "10", message = "", sourceLabel;
        Vector2 panelScroll;
        [Serializable] class Report
        {
            public int photos_available, photos_used;
            public double relative_resolution_difference;
        }
        Report report;
        Rect Panel { get { return new Rect(12, 12, Mathf.Min(320, Screen.width * 0.42f), Screen.height - 24); } }

        void Start()
        {
            if (reconstructedMesh == null) { Debug.LogError("Assign the reconstructed dinosaur mesh."); enabled = false; return; }
            volume = MeshMeasurements.Volume(reconstructedMesh, out closed);
            report = reconstructionReport != null ? JsonUtility.FromJson<Report>(reconstructionReport.text) : new Report();
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) lit = Shader.Find("Standard");
            solidMaterial = new Material(lit);
            solidMaterial.color = new Color(0.7f, 0.8f, 0.77f);
            solidMaterial.SetFloat("_Smoothness", 0.25f);
            model = new GameObject("Reconstructed object (display scaling only)").transform;
            model.SetParent(transform, false);
            Bounds bounds = reconstructedMesh.bounds;
            float displayScale = 4f / Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            model.localScale = Vector3.one * displayScale;
            model.localPosition = -bounds.center * displayScale;
            solid = new GameObject("Solid reconstruction"); solid.transform.SetParent(model, false);
            solid.AddComponent<MeshFilter>().sharedMesh = reconstructedMesh;
            solid.AddComponent<MeshRenderer>().sharedMaterial = solidMaterial;
            solid.AddComponent<MeshCollider>().sharedMesh = reconstructedMesh;
            BuildPointView();
            previousCameras = Array.FindAll(FindObjectsByType<Camera>(FindObjectsSortMode.None), c => c.enabled);
            previousListeners = Array.FindAll(FindObjectsByType<AudioListener>(FindObjectsSortMode.None), a => a.enabled);
            foreach (Camera c in previousCameras) c.enabled = false;
            foreach (AudioListener a in previousListeners) a.enabled = false;
            GameObject cameraObject = new GameObject("Object orbit camera"); cameraObject.transform.SetParent(transform, false);
            view = cameraObject.AddComponent<Camera>(); cameraObject.AddComponent<AudioListener>();
            view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = new Color(0.025f,0.045f,0.065f);
            view.nearClipPlane = 0.02f;
            cameraTransform = view.transform;
            GameObject key = new GameObject("Object key light"); key.transform.SetParent(transform, false);
            Light light = key.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.7f;
            key.transform.rotation = Quaternion.Euler(35,-35,0);
            GameObject fill = new GameObject("Object fill light"); fill.transform.SetParent(transform, false);
            Light fillLight = fill.AddComponent<Light>(); fillLight.type = LightType.Directional; fillLight.intensity = 0.7f;
            fill.transform.rotation = Quaternion.Euler(-20,140,0);
            sourceLabel = "DinoRing: " + report.photos_available + " public photos; " + report.photos_used + " conservative silhouettes used.";
        }

        void BuildPointView()
        {
            pointsRoot = new GameObject("Sampled mesh vertices"); pointsRoot.transform.SetParent(model, false);
            if (pointShader == null) pointShader = Shader.Find("TDT16/Photo Object Points");
            if (pointShader == null) { pointsRoot.SetActive(false); return; }
            pointMaterial = new Material(pointShader); pointMaterial.SetFloat("_PointSize", 3);
            Vector3[] source = reconstructedMesh.vertices;
            int step = Mathf.Max(1, Mathf.CeilToInt(source.Length / 30000f));
            var vertices = new List<Vector3>();
            var unique = new HashSet<Vector3>();
            for (int i = 0; i < source.Length; i += step) if (unique.Add(source[i])) vertices.Add(source[i]);
            for (int start = 0; start < vertices.Count; start += 12000)
            {
                int n = Mathf.Min(12000, vertices.Count-start);
                Vector3[] p = new Vector3[n*4]; Vector2[] uv = new Vector2[n*4];
                Color32[] colour = new Color32[n*4]; int[] t = new int[n*6];
                Vector2[] corners = {new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,1)};
                for (int i = 0; i < n; i++)
                {
                    for (int j=0;j<4;j++) { p[i*4+j]=vertices[start+i]; uv[i*4+j]=corners[j]; colour[i*4+j]=new Color32(60,210,180,255); }
                    int v=i*4, k=i*6; t[k]=v;t[k+1]=v+1;t[k+2]=v+2;t[k+3]=v;t[k+4]=v+2;t[k+5]=v+3;
                }
                Mesh mesh = new Mesh {name="Object points"}; mesh.vertices=p;mesh.uv=uv;mesh.colors32=colour;mesh.triangles=t;mesh.RecalculateBounds();
                Bounds b=mesh.bounds;b.Expand(0.01f);mesh.bounds=b;
                GameObject chunk=new GameObject("Point chunk");chunk.transform.SetParent(pointsRoot.transform,false);
                chunk.AddComponent<MeshFilter>().sharedMesh=mesh;chunk.AddComponent<MeshRenderer>().sharedMaterial=pointMaterial;
                pointMeshes.Add(mesh);
            }
            pointsRoot.SetActive(false);
        }

        void LateUpdate()
        {
            if (view == null) return;
            Vector3 centre = transform.position;
            cameraTransform.position = centre + Quaternion.Euler(pitch,yaw,0)*new Vector3(0,0,-distance);
            cameraTransform.LookAt(centre);
        }

        void ResetPicks()
        {
            picked=0;picking=true;sourceDistance=0;
            foreach(GameObject marker in markers) Destroy(marker);
            markers.Clear(); message="Click two surface points with a known real distance between them.";
            pointView=false;solid.GetComponent<MeshRenderer>().enabled=true;pointsRoot.SetActive(false);
        }

        void Pick(Vector2 screen)
        {
            RaycastHit hit;
            if (!Physics.Raycast(view.ScreenPointToRay(screen),out hit) || hit.collider.gameObject!=solid) return;
            Vector3 local=model.InverseTransformPoint(hit.point);
            if(picked==0) first=local; else second=local;
            GameObject marker=GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(marker.GetComponent<Collider>());
            marker.transform.SetParent(model,false);marker.transform.localPosition=local;
            marker.transform.localScale=Vector3.one*(reconstructedMesh.bounds.size.magnitude*0.015f);
            if(markerMaterial==null) { Shader s=Shader.Find("Universal Render Pipeline/Unlit"); markerMaterial=new Material(s);markerMaterial.color=Color.yellow; }
            marker.GetComponent<MeshRenderer>().sharedMaterial=markerMaterial;markers.Add(marker);picked++;
            if(picked==2) { picking=false; sourceDistance=Vector3.Distance(first,second);message="Enter the real distance between the yellow points, then apply."; }
        }

        void OnGUI()
        {
            if (model==null) return;
            Color previous=GUI.color;GUI.color=new Color(0.025f,0.04f,0.06f,1);GUI.DrawTexture(Panel,Texture2D.whiteTexture);GUI.color=previous;
            GUIStyle text=new GUIStyle(GUI.skin.label){wordWrap=true};
            GUILayout.BeginArea(Panel,GUI.skin.box);panelScroll=GUILayout.BeginScrollView(panelScroll);
            GUILayout.Label("PHOTO TO 3D OBJECT",new GUIStyle(text){fontSize=20,fontStyle=FontStyle.Bold});
            GUILayout.Label(sourceLabel,text);
            GUILayout.Label("Reconstructed from silhouettes using supplied camera poses. Solid geometry, not a downloaded finished model.",text);
            GUILayout.Space(10);
            if(GUILayout.Button(pointView?"Switch to solid model":"Switch to point view"))
            {
                if(pointMaterial!=null) { pointView=!pointView;solid.GetComponent<MeshRenderer>().enabled=!pointView;pointsRoot.SetActive(pointView); }
            }
            showPhoto=GUILayout.Toggle(showPhoto,"Show source photo");
            if(sourcePhotos!=null && sourcePhotos.Length>0)
            {
                GUILayout.BeginHorizontal();
                if(GUILayout.Button("Previous photo")) photoIndex=(photoIndex+sourcePhotos.Length-1)%sourcePhotos.Length;
                if(GUILayout.Button("Next photo")) photoIndex=(photoIndex+1)%sourcePhotos.Length;
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(12);GUILayout.Label("VOLUME",new GUIStyle(text){fontStyle=FontStyle.Bold});
            GUILayout.Label(closed?"Closed edges + consistent winding: passed":"Mesh check failed: volume unavailable",text);
            if(closed)
            {
                GUILayout.Label(volume.ToString("G5",CultureInfo.InvariantCulture)+" dataset units cubed",text);
                if(centimetresPerUnit>0)
                {
                    double cm3=volume*Math.Pow(centimetresPerUnit,3);
                    GUILayout.Label(cm3.ToString("F2")+" cm3 / mL",new GUIStyle(text){fontSize=19});
                    GUILayout.Label(demoScale?"ILLUSTRATIVE scale, not a measured object volume.":"Calibrated using your entered reference distance.",text);
                }
                else GUILayout.Label("Physical scale not calibrated. No verified cm3 value yet.",text);
            }
            GUILayout.Label("Grid-resolution difference: "+(report.relative_resolution_difference*100).ToString("F2")+"%. This is not an accuracy guarantee.",text);
            GUILayout.Space(10);
            if(GUILayout.Button("Choose two scale-reference points")) ResetPicks();
            GUILayout.Label("Known distance between points (cm):",text);
            realLength=GUILayout.TextField(realLength);
            bool oldEnabled=GUI.enabled;GUI.enabled=picked==2 && sourceDistance>1e-10;
            if(GUILayout.Button("Apply real distance"))
            {
                double cm;
                if(double.TryParse(realLength.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out cm) && cm>0 && !double.IsInfinity(cm))
                {centimetresPerUnit=cm/sourceDistance;demoScale=false;message="Reference scale applied.";}
                else message="Enter a positive finite length in cm.";
            }
            GUI.enabled=oldEnabled;
            if(GUILayout.Button("Demo scale: longest side = 10 cm"))
            {Bounds b=reconstructedMesh.bounds;centimetresPerUnit=10/Math.Max(b.size.x,Math.Max(b.size.y,b.size.z));demoScale=true;message="Assumed size for demonstration only.";}
            if(GUILayout.Button("Clear physical scale")){centimetresPerUnit=0;demoScale=false;}
            GUILayout.Label(message,text);
            GUILayout.Space(10);
            GUILayout.Label("Right-drag: orbit | Scroll: zoom\nThe input photos are not textured onto this mesh. Concavities hidden in every silhouette cannot be recovered. Desktop prototype; AR not included.",text);
            GUILayout.EndScrollView();GUILayout.EndArea();

            if(showPhoto && sourcePhotos!=null && sourcePhotos.Length>0)
            {
                Texture2D photo=sourcePhotos[photoIndex];
                float w=Mathf.Min(260,Screen.width*0.24f), h=w*photo.height/photo.width;
                GUI.Box(new Rect(Screen.width-w-18,12,w+6,h+30),"");
                GUI.DrawTexture(new Rect(Screen.width-w-15,15,w,h),photo,ScaleMode.ScaleToFit);
                GUI.Label(new Rect(Screen.width-w-15,h+17,w,25),photo.name);
            }
            Event e=Event.current;
            bool overPhoto=showPhoto && e.mousePosition.x>Screen.width-Mathf.Min(260,Screen.width*0.24f)-18 && e.mousePosition.y<245;
            if(!Panel.Contains(e.mousePosition) && !overPhoto)
            {
                if(e.type==EventType.MouseDrag && e.button==1)
                {yaw+=e.delta.x*0.4f;pitch=Mathf.Clamp(pitch+e.delta.y*0.4f,-80,80);e.Use();}
                if(e.type==EventType.ScrollWheel){distance=Mathf.Clamp(distance+e.delta.y*0.3f,3,16);e.Use();}
                if(picking && e.type==EventType.MouseDown && e.button==0)
                {Pick(new Vector2(e.mousePosition.x,Screen.height-e.mousePosition.y));e.Use();}
            }
        }

        void OnDestroy()
        {
            foreach(Mesh mesh in pointMeshes) if(mesh!=null) Destroy(mesh);
            if(solidMaterial!=null)Destroy(solidMaterial);if(pointMaterial!=null)Destroy(pointMaterial);if(markerMaterial!=null)Destroy(markerMaterial);
            if(previousCameras!=null)foreach(Camera c in previousCameras)if(c!=null)c.enabled=true;
            if(previousListeners!=null)foreach(AudioListener a in previousListeners)if(a!=null)a.enabled=true;
            // All generated objects are children of this lab and disappear when
            // its scene closes or Play mode stops.
        }
    }
}
