using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Display markers share the point cloud's local frame. There are no rendering
// Camera components or physics colliders: selection ray-tests the camera bodies.
public sealed class ColmapCameraDisplay : IDisposable
{
    [Serializable] public sealed class CameraRecord
    {
        public int imageId;
        public string imageName;
        public Vector3 center;
        public Vector3[] corners;
        public Vector3 forwardTip;
        public Vector3 upTip;
        public int imageWidth, imageHeight;
        public float focalLength, principalX, principalY, radialK;
    }
    [Serializable] public sealed class CameraFile
    {
        public int version;
        public CameraRecord[] cameras;
    }

    GameObject root;
    Mesh markerMesh, bodyMesh;
    Material markerMaterial;
    CameraRecord[] records;
    Transform[] bodies;
    MeshRenderer[] bodyRenderers;
    Color32[] lineColors;
    int selectedIndex = -1;
    bool suppressed;
    static readonly Color32 Outline = new Color32(60, 215, 255, 255);
    static readonly Color32 Orientation = new Color32(255, 180, 45, 255);
    static readonly Color32 SelectedColor = new Color32(255, 235, 70, 255);
    public int Count { get; private set; }
    public bool Visible => root != null && root.activeSelf;
    public Transform RootTransform => root == null ? null : root.transform;
    public CameraRecord Selected => selectedIndex >= 0 && records != null ? records[selectedIndex] : null;

    public bool TrySetVisible(Transform coordinateFrame, bool visible, out string error)
    {
        error = null;
        try
        {
            if (visible && root == null) Build(coordinateFrame);
            if (root != null) root.SetActive(visible);
            if (!visible) ClearSelection();
            return true;
        }
        catch (Exception exception)
        {
            error = "COLMAP cameras: " + exception.Message;
            Dispose();
            return false;
        }
    }

    void Build(Transform coordinateFrame)
    {
        TextAsset source = Resources.Load<TextAsset>("ColmapCameras");
        if (source == null) throw new InvalidOperationException("Run Reconstruction/export_colmap_cameras.py first.");
        CameraFile data = JsonUtility.FromJson<CameraFile>(source.text);
        if (data == null || data.version != 1 || data.cameras == null || data.cameras.Length == 0)
            throw new InvalidOperationException("The camera pose export is empty or unsupported.");
        Shader shader = Resources.Load<Shader>("ColmapCameraLines");
        if (shader == null || !shader.isSupported) throw new InvalidOperationException("Camera marker shader is unavailable.");
        records = data.cameras;
        bodies = new Transform[records.Length];
        bodyRenderers = new MeshRenderer[records.Length];
        root = new GameObject("COLMAP Cameras");
        root.transform.SetParent(coordinateFrame, false);
        markerMaterial = new Material(shader) { name = "COLMAP Camera Markers" };
        bodyMesh = CameraBodyMesh();
        var vertices = new List<Vector3>();
        var colors = new List<Color32>();
        var indices = new List<int>();
        var imageIds = new HashSet<int>();
        for (int cameraIndex = 0; cameraIndex < records.Length; cameraIndex++)
        {
            CameraRecord camera = records[cameraIndex];
            if (!imageIds.Add(camera.imageId) || camera.corners == null || camera.corners.Length != 4)
                throw new InvalidOperationException("Invalid or duplicate image pose.");
            for (int i = 0; i < 4; i++)
            {
                AddLine(camera.center, camera.corners[i], Outline, vertices, colors, indices);
                AddLine(camera.corners[i], camera.corners[(i+1)%4], Outline, vertices, colors, indices);
            }
            AddLine(camera.center, camera.forwardTip, Orientation, vertices, colors, indices);
            AddLine(camera.corners[0], camera.upTip, Orientation, vertices, colors, indices);
            AddLine(camera.upTip, camera.corners[1], Orientation, vertices, colors, indices);

            Vector3 forward = camera.forwardTip - camera.center;
            Vector3 up = camera.upTip - (camera.corners[0] + camera.corners[1])*0.5f;
            if (!Finite(up) || forward.sqrMagnitude < 1e-12f || up.sqrMagnitude < 1e-12f)
                throw new InvalidOperationException("Invalid camera orientation.");
            var body = new GameObject("Camera_" + camera.imageId + "_" + camera.imageName);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = camera.center;
            body.transform.localRotation = Quaternion.LookRotation(forward, up);
            body.transform.localScale = Vector3.one * (forward.magnitude/1.8f)*0.85f;
            body.AddComponent<MeshFilter>().sharedMesh = bodyMesh;
            var renderer = body.AddComponent<MeshRenderer>();
            ConfigureRenderer(renderer);
            bodies[cameraIndex] = body.transform;
            bodyRenderers[cameraIndex] = renderer;
        }
        markerMesh = new Mesh { name = "COLMAP Registered Camera Frusta" };
        markerMesh.indexFormat = IndexFormat.UInt32;
        markerMesh.SetVertices(vertices);
        markerMesh.SetColors(colors);
        markerMesh.SetIndices(indices.ToArray(), MeshTopology.Lines, 0);
        markerMesh.RecalculateBounds();
        lineColors = colors.ToArray();
        root.AddComponent<MeshFilter>().sharedMesh = markerMesh;
        ConfigureRenderer(root.AddComponent<MeshRenderer>());
        Count = records.Length;
        Debug.Log($"Loaded {Count} selectable COLMAP camera markers.");
    }

    void ConfigureRenderer(MeshRenderer renderer)
    {
        renderer.sharedMaterial = markerMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    public bool TryPick(Ray worldRay, out int index)
    {
        index = -1;
        if (!Visible || suppressed || bodyMesh == null) return false;
        float nearest = float.PositiveInfinity;
        Bounds bounds = bodyMesh.bounds;
        bounds.Expand(0.10f); // Small picking margin around the visible body and lens.
        for (int i = 0; i < bodies.Length; i++)
        {
            Transform body = bodies[i];
            Ray localRay = new Ray(body.InverseTransformPoint(worldRay.origin),
                                   body.InverseTransformVector(worldRay.direction).normalized);
            float distance;
            if (!bounds.IntersectRay(localRay, out distance)) continue;
            Vector3 worldHit = body.TransformPoint(localRay.GetPoint(Mathf.Max(0, distance)));
            float worldDistance = Vector3.Distance(worldRay.origin, worldHit);
            if (worldDistance < nearest) { nearest = worldDistance; index = i; }
        }
        return index >= 0;
    }

    public bool SelectFromRay(Ray ray)
    {
        if (TryPick(ray, out int index)) { Select(index); return true; }
        ClearSelection();
        return false;
    }

    public void ClearSelection() { Select(-1); }

    public void SetSuppressed(bool value)
    {
        suppressed = value;
        if (root == null) return;
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            renderer.enabled = !value;
    }

    void Select(int index)
    {
        if (index == selectedIndex) return;
        Highlight(selectedIndex, false);
        selectedIndex = index;
        Highlight(selectedIndex, true);
        if (markerMesh != null) markerMesh.colors32 = lineColors;
    }

    void Highlight(int index, bool selected)
    {
        if (index < 0 || bodies == null) return;
        var properties = new MaterialPropertyBlock();
        properties.SetFloat("_Selected", selected ? 1 : 0);
        bodyRenderers[index].SetPropertyBlock(properties);
        for (int i = 0; i < 22; i++)
            lineColors[index*22+i] = selected ? SelectedColor : (i < 16 ? Outline : Orientation);
    }

    static void AddLine(Vector3 a, Vector3 b, Color32 color, List<Vector3> vertices,
                        List<Color32> colors, List<int> indices)
    {
        if (!Finite(a) || !Finite(b)) throw new InvalidOperationException("Non-finite camera marker position.");
        indices.Add(vertices.Count); vertices.Add(a); colors.Add(color);
        indices.Add(vertices.Count); vertices.Add(b); colors.Add(color);
    }

    // Compact camera icon in local space: body, grip, top housing, lens barrel,
    // and front glass. All instances share this mesh and the same material.
    static Mesh CameraBodyMesh()
    {
        var v = new List<Vector3>(); var c = new List<Color32>(); var t = new List<int>();
        Box(new Vector3(0,0,-.40f), new Vector3(1.10f,.72f,.48f), new Color32(45,125,155,255), v,c,t);
        Box(new Vector3(.50f,0,-.38f), new Vector3(.24f,.78f,.53f), new Color32(24,65,80,255), v,c,t);
        Box(new Vector3(0,.44f,-.40f), new Vector3(.38f,.22f,.27f), new Color32(60,170,190,255), v,c,t);
        Cylinder(-.17f,.18f,.27f,new Color32(35,48,60,255),v,c,t);
        Cylinder(.18f,.24f,.29f,new Color32(100,180,195,255),v,c,t);
        Cylinder(.241f,.25f,.22f,new Color32(20,100,145,255),v,c,t);
        Mesh mesh = new Mesh { name = "COLMAP Camera Body and Lens" };
        mesh.SetVertices(v); mesh.SetColors(c); mesh.SetTriangles(t,0); mesh.RecalculateBounds();
        return mesh;
    }
    static void Box(Vector3 center, Vector3 size, Color32 color, List<Vector3> v, List<Color32> c, List<int> t)
    {
        Vector3 h = size*.5f;
        Vector3[] p = { new Vector3(-h.x,-h.y,-h.z),new Vector3(h.x,-h.y,-h.z),new Vector3(h.x,h.y,-h.z),new Vector3(-h.x,h.y,-h.z),
                        new Vector3(-h.x,-h.y,h.z),new Vector3(h.x,-h.y,h.z),new Vector3(h.x,h.y,h.z),new Vector3(-h.x,h.y,h.z) };
        int[,] faces = {{0,3,2,1},{4,5,6,7},{0,4,7,3},{1,2,6,5},{3,7,6,2},{0,1,5,4}};
        for (int face=0;face<6;face++)
        {
            int start=v.Count; float shade=face==4?1.25f:face==5?.65f:face==1?1.05f:.85f;
            Color32 shaded = new Color(color.r/255f*shade,color.g/255f*shade,color.b/255f*shade,1);
            for(int i=0;i<4;i++){v.Add(center+p[faces[face,i]]);c.Add(shaded);}
            t.Add(start);t.Add(start+1);t.Add(start+2);t.Add(start);t.Add(start+2);t.Add(start+3);
        }
    }
    static void Cylinder(float back,float front,float radius,Color32 color,List<Vector3> v,List<Color32> c,List<int> t)
    {
        const int sides=16;
        for(int i=0;i<sides;i++)
        {
            float a=i*Mathf.PI*2/sides,b=(i+1)*Mathf.PI*2/sides;
            Vector3 p=new Vector3(Mathf.Cos(a)*radius,Mathf.Sin(a)*radius,back);
            Vector3 q=new Vector3(Mathf.Cos(b)*radius,Mathf.Sin(b)*radius,back);
            int start=v.Count;
            v.Add(p);v.Add(q);v.Add(new Vector3(q.x,q.y,front));v.Add(new Vector3(p.x,p.y,front));
            Color32 shaded = (Color)color * (0.75f+0.25f*Mathf.Sin(a)); shaded.a=255;
            for(int k=0;k<4;k++)c.Add(shaded);
            t.Add(start);t.Add(start+1);t.Add(start+2);t.Add(start);t.Add(start+2);t.Add(start+3);
            start=v.Count;v.Add(new Vector3(0,0,front));v.Add(new Vector3(p.x,p.y,front));v.Add(new Vector3(q.x,q.y,front));
            c.Add(color);c.Add(color);c.Add(color);t.Add(start);t.Add(start+1);t.Add(start+2);
        }
    }
    static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

    public void Dispose()
    {
        Release(root); Release(markerMesh); Release(bodyMesh); Release(markerMaterial);
        root=null;markerMesh=null;bodyMesh=null;markerMaterial=null;records=null;bodies=null;bodyRenderers=null;lineColors=null;
        Count=0;selectedIndex=-1;suppressed=false;
    }
    static void Release(UnityEngine.Object value)
    {
        if(value==null)return;
        if(Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
    }
}
