using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

// Sampled OpenMVS adaptation of Neumann et al. (2025), equation 9.
// All four normalized components are precomputed from actual images/geometry.
public sealed class RqfDisplay : IDisposable
{
    [Serializable] public sealed class Proposal
    {
        public int sample;
        public Vector3 position, target;
        public float priority, verticalFov, aspect;
    }
    [Serializable] sealed class ProposalFile { public int version; public Proposal[] proposals; }
    public sealed class Sample
    {
        public Vector3 position, normal;
        public float[] metrics;
        public float observations, density, edgeDistance, texture;
    }
    static readonly string[] Names = { "Coverage need", "Density need", "Boundary need", "Texture need", "Combined RQF" };
    const string resourcePrefix = "BottleRQF";
    MeshFilter filter;
    MeshRenderer renderer;
    Mesh original, colored, markerMesh, targetMesh;
    Material[] originalMaterials;
    Material material, markerMaterial, targetMaterial;
    BottlePhotoTextureDisplay photoBackground;
    string photoBackgroundError;
    GameObject markerRoot, targetRoot;
    Transform frame;
    Vector3 displayAxis, displayCenter;
    float displayExtent;
    Color32[][] palettes;
    Color32[][] contrastPalettes;
    float[] minimumMetric, maximumMetric;
    Sample[] samples;
    Proposal[] proposals;
    List<Proposal> reachable = new List<Proposal>();
    int selectedFilter = 4, proposalIndex;
    bool shown, showCamera = true, includeBelow, contrast = false;
    bool showHeatmap = true, showFocus = true, showRays = true;
    float minimumY;
    Vector2 scroll;
    bool showWhy, showAdvanced;
    static readonly int[] FilterOrder = { 4, 0, 1, 2, 3 };
    static readonly string[] FilterHints = {
        "Fewer existing cameras can see the surface.",
        "Fewer nearby points in the dense reconstruction.",
        "Closer to open reconstruction edges.",
        "Weaker local detail in the original images.",
        "Combined need from all four indicators."
    };
    public int SampleCount => samples == null ? 0 : samples.Length;
    public int CameraCount { get; private set; }
    public int ProposalCount => reachable.Count;
    public Rect Panel => new Rect(Mathf.Max(330, Screen.width - 355), 15, 340,
        Mathf.Min(Mathf.Max(100, Screen.height - 30),
            650 + (showWhy ? 260 : 0) + (showAdvanced ? 205 : 0)));
    public bool Visible => shown;
    public Proposal Selected => proposalIndex < reachable.Count ? reachable[proposalIndex] : null;
    public Sample SelectedSample => Selected == null ? null : samples[Selected.sample];
    public string FilterName => Names[selectedFilter];

    public bool TryShow(GameObject target, Transform coordinateFrame, out string error)
    {
        error = null;
        try
        {
            if (colored == null) Load(target, coordinateFrame);
            shown = true;
            ApplyBackground();
            ApplyPalette();
            markerRoot.SetActive(true);
            targetRoot.SetActive(showFocus);
            return true;
        }
        catch (Exception e) { error = "RQF: " + e.Message; Dispose(); return false; }
    }

    void Load(GameObject target, Transform coordinateFrame)
    {
        frame = coordinateFrame;
        filter = target.GetComponentInChildren<MeshFilter>(true);
        if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable)
            throw new InvalidDataException("A readable bottle mesh is required.");
        renderer = filter.GetComponent<MeshRenderer>();
        original = filter.sharedMesh; originalMaterials = renderer.sharedMaterials;
        FindDisplayAxis();
        var binary = Resources.Load<TextAsset>(resourcePrefix);
        var views = Resources.Load<TextAsset>(resourcePrefix + "Views");
        if (binary == null || views == null)
            throw new FileNotFoundException("Run python Reconstruction/rqf_metrics.py first.");
        using (var reader = new BinaryReader(new MemoryStream(binary.bytes)))
        {
            if (reader.ReadUInt32() != 0x31465152 || reader.ReadUInt32() != 1)
                throw new InvalidDataException("Unsupported RQF export.");
            int n = reader.ReadInt32(), m = reader.ReadInt32(); CameraCount = reader.ReadInt32();
            if (n != original.vertexCount || m < 1 || CameraCount < 1 || reader.BaseStream.Length != 20L + 32L * n + 60L * m)
                throw new InvalidDataException("RQF data size does not match the mesh.");
            Vector3[] vertices = original.vertices;
            minimumY = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                Vector3 expected = ReadVector(reader);
                if (!Finite(expected) || (expected - vertices[i]).sqrMagnitude > 1e-10f)
                    throw new InvalidDataException("RQF vertex alignment mismatch. Regenerate the export for this mesh.");
                minimumY = Mathf.Min(minimumY, filter.transform.TransformPoint(vertices[i]).y);
            }
            palettes = new Color32[5][];
            contrastPalettes = new Color32[5][];
            minimumMetric = new float[5]; maximumMetric = new float[5];
            var values = new float[5][];
            for (int j = 0; j < 5; j++)
            {
                palettes[j] = new Color32[n]; contrastPalettes[j] = new Color32[n]; values[j] = new float[n];
                minimumMetric[j] = float.PositiveInfinity; maximumMetric[j] = float.NegativeInfinity;
            }
            for (int i = 0; i < n; i++)
            for (int j = 0; j < 5; j++)
            {
                float value = ReadMetric(reader); values[j][i] = value; palettes[j][i] = ColorFor(value);
                if (value >= 0) { minimumMetric[j] = Mathf.Min(minimumMetric[j],value); maximumMetric[j] = Mathf.Max(maximumMetric[j],value); }
            }
            for (int j = 0; j < 5; j++)
            for (int i = 0; i < n; i++)
            {
                float value = values[j][i];
                contrastPalettes[j][i] = ColorFor(value < 0 ? -1 :
                    (maximumMetric[j]-minimumMetric[j] < 1e-8f ? .5f :
                    (value-minimumMetric[j])/(maximumMetric[j]-minimumMetric[j])));
            }
            samples = new Sample[m];
            for (int i = 0; i < m; i++)
            {
                var sample = new Sample { position = ReadVector(reader), normal = ReadVector(reader), metrics = new float[5] };
                if (!Finite(sample.position) || !Finite(sample.normal)) throw new InvalidDataException("Invalid RQF sample.");
                for (int j = 0; j < 5; j++) sample.metrics[j] = ReadMetric(reader);
                sample.observations = reader.ReadSingle(); sample.density = reader.ReadSingle();
                sample.edgeDistance = reader.ReadSingle(); sample.texture = reader.ReadSingle();
                if (!float.IsFinite(sample.observations) || !float.IsFinite(sample.density) ||
                    !float.IsFinite(sample.edgeDistance) || !float.IsFinite(sample.texture))
                    throw new InvalidDataException("Invalid raw RQF metric.");
                samples[i] = sample;
            }
        }
        var data = JsonUtility.FromJson<ProposalFile>(views.text);
        if (data == null || data.version != 1 || data.proposals == null) throw new InvalidDataException("Invalid RQF proposals.");
        proposals = data.proposals;
        float previous = float.PositiveInfinity;
        foreach (var proposal in proposals)
        {
            if (proposal.sample < 0 || proposal.sample >= samples.Length || !Finite(proposal.position) || !Finite(proposal.target) ||
                !float.IsFinite(proposal.priority) || proposal.priority < 0 || proposal.priority > previous + 1e-6f ||
                (proposal.target - samples[proposal.sample].position).sqrMagnitude > 1e-10f ||
                Mathf.Abs(proposal.priority - samples[proposal.sample].metrics[4]) > 1e-5f)
                throw new InvalidDataException("RQF proposal priority/target mismatch.");
            previous = proposal.priority;
        }
        Shader shader = Resources.Load<Shader>("BottleMeshCoverage");
        Shader lines = Resources.Load<Shader>("ColmapCameraLines");
        if (shader == null || lines == null || !shader.isSupported || !lines.isSupported)
            throw new InvalidOperationException("RQF display shaders unavailable.");
        colored = UnityEngine.Object.Instantiate(original); colored.name = "Bottle RQF heatmap";
        material = new Material(shader) { name = "RQF needs-attention heatmap" };
        markerMaterial = new Material(lines) { name = "RQF camera and target" };
        markerRoot = new GameObject("RQF camera and target"); markerRoot.transform.SetParent(frame, false);
        markerMesh = new Mesh { name = "RQF next camera and surface target" };
        markerRoot.AddComponent<MeshFilter>().sharedMesh = markerMesh;
        var mr = markerRoot.AddComponent<MeshRenderer>(); mr.sharedMaterials = new[] { markerMaterial, markerMaterial };
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        targetRoot=new GameObject("RQF surface target");targetRoot.transform.SetParent(frame,false);
        targetMesh=new Mesh {name="RQF filled surface marker",indexFormat=IndexFormat.UInt32};
        targetRoot.AddComponent<MeshFilter>().sharedMesh=targetMesh;
        // Draw the surface overlay just ahead of the mesh without separating adjacent faces.
        targetMaterial=new Material(lines){name="RQF surface target overlay",renderQueue=2010};
        targetMaterial.SetFloat("_DepthBias",-4);
        var targetRenderer=targetRoot.AddComponent<MeshRenderer>();targetRenderer.sharedMaterial=targetMaterial;
        targetRenderer.shadowCastingMode=ShadowCastingMode.Off;targetRenderer.receiveShadows=false;
        UpdateReachable();
    }

    static float ReadMetric(BinaryReader reader)
    {
        float value = reader.ReadSingle();
        if (!float.IsFinite(value) || (value != -1 && (value < 0 || value > 1.00001f)))
            throw new InvalidDataException("Invalid normalized RQF value.");
        return value;
    }
    static Vector3 ReadVector(BinaryReader reader) => new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    static bool Finite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
    public static Color32 ColorFor(float value)
    {
        if (value < 0) return new Color32(135,145,155,255);
        value = Mathf.Clamp01(value);
        return value < .5f ? Color.Lerp(new Color(.1f,.6f,.32f),new Color(1,.9f,.4f),value*2) :
            Color.Lerp(new Color(1,.9f,.4f),new Color(.85f,.18f,.15f),value*2-1);
    }

    public static float Combine(float coverage, float density, float edge, float texture)
    {
        return (1+2*coverage)/3 * ((1+2*density)/3) * ((1+2*edge)/3) * ((1+2*texture)/3);
    }

    void ApplyPalette() { colored.colors32 = contrast ? contrastPalettes[selectedFilter] : palettes[selectedFilter]; }

    void ApplyBackground()
    {
        photoBackground?.Restore();
        filter.sharedMesh = showHeatmap ? colored : original;
        renderer.sharedMaterials = showHeatmap ? new[] { material } : originalMaterials;
        photoBackgroundError = null;
        if (!showHeatmap)
        {
            if (photoBackground == null) photoBackground = new BottlePhotoTextureDisplay();
            photoBackground.TryShow(filter.gameObject, out photoBackgroundError);
        }
    }

    void FindDisplayAxis()
    {
        // Derive the viewing reference from the actual mesh, including unsaved scene transforms.
        Vector3[] vertices=original.vertices;
        int step=Mathf.Max(1,vertices.Length/20000),count=0;
        displayCenter=Vector3.zero;
        for(int i=0;i<vertices.Length;i+=step)
        {displayCenter+=frame.InverseTransformPoint(filter.transform.TransformPoint(vertices[i]));count++;}
        displayCenter/=count;
        float xx=0,xy=0,xz=0,yy=0,yz=0,zz=0;
        for(int i=0;i<vertices.Length;i+=step)
        {
            Vector3 d=frame.InverseTransformPoint(filter.transform.TransformPoint(vertices[i]))-displayCenter;
            xx+=d.x*d.x;xy+=d.x*d.y;xz+=d.x*d.z;yy+=d.y*d.y;yz+=d.y*d.z;zz+=d.z*d.z;
        }
        displayAxis=xx>=yy&&xx>=zz?Vector3.right:yy>=zz?Vector3.up:Vector3.forward;
        for(int i=0;i<100;i++)
            displayAxis=new Vector3(xx*displayAxis.x+xy*displayAxis.y+xz*displayAxis.z,
                xy*displayAxis.x+yy*displayAxis.y+yz*displayAxis.z,
                xz*displayAxis.x+yz*displayAxis.y+zz*displayAxis.z).normalized;
        if(Vector3.Dot(frame.TransformDirection(displayAxis),Vector3.up)<0)displayAxis=-displayAxis;
        displayExtent=original.bounds.size.magnitude;
    }

    Quaternion CameraRotation(Proposal pose)
    {
        Vector3 forward=(pose.target-pose.position).normalized;
        Vector3 up=displayAxis;
        if(Vector3.Cross(up,forward).sqrMagnitude<1e-6f)up=frame.InverseTransformDirection(Vector3.forward);
        if(Vector3.Cross(up,forward).sqrMagnitude<1e-6f)up=frame.InverseTransformDirection(Vector3.right);
        Quaternion rotation=Quaternion.LookRotation(forward,up);
        // Correct screen roll using the projected centerline. Looking at an off-center
        // surface target otherwise makes even an upright object appear tilted by perspective.
        Vector3 center=displayCenter+displayAxis*Vector3.Dot(pose.target-displayCenter,displayAxis);
        Quaternion inverse=Quaternion.Inverse(rotation);
        Vector3 lower=inverse*(center-displayAxis*displayExtent*.15f-pose.position);
        Vector3 upper=inverse*(center+displayAxis*displayExtent*.15f-pose.position);
        if(lower.z>1e-5f&&upper.z>1e-5f)
        {
            Vector2 line=new Vector2(upper.x/upper.z-lower.x/lower.z,upper.y/upper.z-lower.y/lower.z);
            if(line.sqrMagnitude>1e-8f)
                rotation*=Quaternion.AngleAxis(-Mathf.Atan2(line.x,line.y)*Mathf.Rad2Deg,Vector3.forward);
        }
        return rotation;
    }

    void UpdateReachable()
    {
        reachable.Clear();
        foreach (var pose in proposals)
            if (includeBelow || frame.TransformPoint(pose.position).y >= minimumY - .001f) reachable.Add(pose);
        proposalIndex = 0; UpdateMarker();
    }

    void UpdateMarker()
    {
        UpdateSurfaceTarget();
        var v = new List<Vector3>(); var c = new List<Color32>(); var ix = new List<int>();
        var rayTriangles = new List<int>();
        if (Selected != null)
        {
            var pose = Selected;
            Quaternion rotation = CameraRotation(pose);
            float extent = original.bounds.size.magnitude, depth = extent * .11f;
            float h = depth * Mathf.Tan(pose.verticalFov * Mathf.Deg2Rad * .5f);
            Vector3[] corners = new Vector3[4];
            Color gold = new Color(1,.8f,.15f), targetBlue = new Color(.06f,.18f,.58f);
            if (showCamera)
            {
                for(int j=0;j<4;j++)corners[j]=pose.position+rotation*new Vector3((j==0||j==3?-1:1)*h*pose.aspect,(j<2?1:-1)*h,depth);
                for(int j=0;j<4;j++){Line(pose.position,corners[j],gold,v,c,ix);Line(corners[j],corners[(j+1)%4],gold,v,c,ix);}
                Line(pose.position,pose.position+rotation*Vector3.forward*depth*1.8f,gold,v,c,ix);
                // Small housing and top tab make the marker read as a camera from either side.
                var body = new Vector3[8];
                for (int j=0;j<8;j++) body[j]=pose.position+rotation*new Vector3((j%2==0?-1:1)*h*.65f,(j%4<2?-1:1)*h*.5f,j<4?-depth*.45f:0);
                for(int j=0;j<4;j++)
                {Line(body[j],body[j+4],gold,v,c,ix);Line(body[j],body[j^1],gold,v,c,ix);Line(body[j],body[j^2],gold,v,c,ix);Line(body[j+4],body[(j^1)+4],gold,v,c,ix);Line(body[j+4],body[(j^2)+4],gold,v,c,ix);}
                Vector3 top=pose.position+rotation*new Vector3(0,h*1.35f,depth);
                Line(corners[0],top,gold,v,c,ix);Line(top,corners[1],gold,v,c,ix);
            }
            float size = extent * .004f;
            if(showRays && showCamera)
                RayTube(pose.position,pose.target,size*.30f,targetBlue,v,c,rayTriangles);
        }
        markerMesh.Clear();markerMesh.SetVertices(v);markerMesh.SetColors(c);markerMesh.subMeshCount=2;
        markerMesh.SetIndices(ix.ToArray(),MeshTopology.Lines,0);markerMesh.SetTriangles(rayTriangles,1);markerMesh.RecalculateBounds();
    }
    static void Line(Vector3 a,Vector3 b,Color32 color,List<Vector3> v,List<Color32> c,List<int> ix)
    {ix.Add(v.Count);v.Add(a);c.Add(color);ix.Add(v.Count);v.Add(b);c.Add(color);}

    // A narrow tube gives rays visible thickness independently of hardware line rendering.
    static void RayTube(Vector3 a,Vector3 b,float radius,Color32 color,List<Vector3> v,List<Color32> c,List<int> triangles)
    {
        Vector3 direction=(b-a).normalized;
        if(direction.sqrMagnitude<.5f)return;
        Vector3 side=Vector3.Cross(direction,Mathf.Abs(direction.y)<.9f?Vector3.up:Vector3.right).normalized;
        Vector3 up=Vector3.Cross(direction,side);
        int start=v.Count;
        for(int end=0;end<2;end++)for(int j=0;j<6;j++)
        {
            float angle=j*Mathf.PI/3;
            v.Add((end==0?a:b)+radius*(side*Mathf.Cos(angle)+up*Mathf.Sin(angle)));c.Add(color);
        }
        for(int j=0;j<6;j++)
        {
            int next=(j+1)%6;
            triangles.Add(start+j);triangles.Add(start+next);triangles.Add(start+j+6);
            triangles.Add(start+next);triangles.Add(start+next+6);triangles.Add(start+j+6);
        }
    }

    public void Preview(OrbitCamera orbit)
    {
        if (Selected == null || orbit == null) return;
        var pose = Selected;
        Vector3 position=frame.TransformPoint(pose.position);
        orbit.PreviewNextBestView(position, frame.rotation * CameraRotation(pose), pose.verticalFov);
    }

    public void Draw(OrbitCamera orbit)
    {
        if (!shown) return;
        // Looking along the thick ray from inside the proposed camera can cover the target ring.
        // Keep only the surface marker visible in preview; restore the camera and ray in overview.
        bool inPreview = orbit != null && orbit.IsNextBestViewPreview;
        markerRoot.SetActive(!inPreview);
        if (Selected != null && Camera.main != null)
        {
            if(showCamera && !inPreview) LabelAt(Selected.position,"Next camera",105);
            if(showFocus)
            {
                Vector3 world=frame.TransformPoint(Selected.target);
                float radius=original.bounds.size.magnitude*.035f*Mathf.Abs(frame.lossyScale.x);
                float screenRadius=Vector3.Distance(Camera.main.WorldToScreenPoint(world),
                    Camera.main.WorldToScreenPoint(world+Camera.main.transform.up*radius));
                LabelAt(Selected.target,"RQF target",95,screenRadius+32);
            }
        }
        GUILayout.BeginArea(Panel,GUI.skin.box);
        scroll=GUILayout.BeginScrollView(scroll);
        var wrap=new GUIStyle(GUI.skin.label){wordWrap=true};
        var heading=new GUIStyle(GUI.skin.label){fontStyle=FontStyle.Bold};
        GUILayout.Label("Next-best-view · RQF",new GUIStyle(heading){fontSize=18});
        GUILayout.Space(10);
        GUILayout.Label("Surface view",heading);
        GUILayout.BeginHorizontal();
        DrawSurfaceViewButton("Heatmap", true);
        DrawSurfaceViewButton("Photo texture", false);
        GUILayout.EndHorizontal();
        bool highlight = GUILayout.Toggle(showFocus, "Show cyan/blue target highlight");
        if (highlight != showFocus)
        {
            showFocus = highlight;
            targetRoot.SetActive(showFocus);
        }
        if (showHeatmap)
        {
            GUILayout.Space(8);
            GUILayout.Label("Surface filter",heading);
            foreach(int index in FilterOrder)
            {
                bool selected=selectedFilter==index;
                Color previous=GUI.backgroundColor;
                if(selected)GUI.backgroundColor=new Color(.35f,.80f,.86f);
                var filterStyle=new GUIStyle(GUI.skin.button){fontStyle=selected?FontStyle.Bold:FontStyle.Normal};
                if(GUILayout.Button(Names[index],filterStyle,GUILayout.Height(28))&&!selected)
                {selectedFilter=index;ApplyPalette();}
                GUI.backgroundColor=previous;
                GUILayout.Space(3);
            }
            DrawLegend();
        }
        if (!string.IsNullOrEmpty(photoBackgroundError)) GUILayout.Label(photoBackgroundError, wrap);
        GUILayout.Space(12);
        GUILayout.Label("Camera suggestion",heading);
        if(Selected==null)GUILayout.Label("No suggestion. Check Display settings.",wrap);
        else
        {
            GUILayout.Label($"Suggestion {proposalIndex+1} of {reachable.Count}");
            bool previewing=orbit!=null&&orbit.IsNextBestViewPreview;
            if(GUILayout.Button(previewing?"Return to model":"Preview camera",GUILayout.Height(30)))
            {if(previewing)orbit.EndNextBestViewPreview();else Preview(orbit);}
            if(orbit!=null && orbit.IsNextBestViewPreview)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Inspection zoom: {orbit.PreviewZoom:0.0}×");
                if(GUILayout.Button("Reset zoom"))orbit.ResetPreviewZoom();
                GUILayout.EndHorizontal();
                GUILayout.Label("Scroll over model to zoom · Camera pose unchanged",wrap);
            }
            if (orbit == null || !orbit.IsNextBestViewPreview)
            {
                bool enabled=GUI.enabled;
                GUILayout.BeginHorizontal();
                GUI.enabled=enabled&&proposalIndex+1<reachable.Count;
                if(GUILayout.Button("Next suggestion")){proposalIndex++;UpdateMarker();}
                GUI.enabled=enabled&&proposalIndex>0;
                if(GUILayout.Button("Back to best")){proposalIndex=0;UpdateMarker();}
                GUI.enabled=enabled;
                GUILayout.EndHorizontal();
            }
            if(frame.TransformPoint(Selected.position).y<minimumY-.001f)
                GUILayout.Label("Below-object pose: reposition the bottle to capture this view.",wrap);
        }
        GUILayout.Space(10);
        showWhy=Section("Why this view?",showWhy);
        if(showWhy)
        {
            if(Selected==null)GUILayout.Label("Select a camera suggestion first.",wrap);
            else
            {
                GUILayout.Label($"Overall need: {Selected.priority:0.000}",heading);
                GUILayout.Label("Higher bars = greater need at this target.",wrap);
                for(int j=0;j<4;j++)DrawMetric(j,SelectedSample.metrics[j]);
                GUILayout.Label($"{SelectedSample.observations:0} existing cameras can see the target.",wrap);
                GUILayout.Label("Indicators can miss shape errors and reflections.",wrap);
            }
        }
        showAdvanced=Section("Display settings",showAdvanced);
        if(showAdvanced)
        {
            bool rays=GUILayout.Toggle(showRays,"Camera-to-target line");
            if(rays!=showRays){showRays=rays;UpdateMarker();}
            bool display=GUILayout.Toggle(showCamera,"Show camera marker");
            if(display!=showCamera){showCamera=display;UpdateMarker();}
            bool requestedContrast=GUILayout.Toggle(contrast,"Enhance heatmap contrast");
            if(requestedContrast!=contrast){contrast=requestedContrast;ApplyPalette();}
            if (contrast) GUILayout.Label("Contrast changes the colors only; scores and suggestions stay the same.", wrap);
            bool below=GUILayout.Toggle(includeBelow,"Allow below-object poses");
            if(below!=includeBelow){orbit?.EndNextBestViewPreview();includeBelow=below;UpdateReachable();}
            GUILayout.Label("Below-object poses may require moving or turning the bottle.",wrap);
        }
        // Tooltip text changes during repaint; allocate its control on every GUI event.
        // Conditionally adding it only on hover breaks the cached GUILayout control tree.
        GUILayout.Label(GUI.tooltip ?? string.Empty, wrap, GUILayout.MinHeight(36));
        GUILayout.EndScrollView();GUILayout.EndArea();
    }

    void DrawSurfaceViewButton(string label, bool heatmap)
    {
        bool selected = showHeatmap == heatmap;
        Color previous = GUI.backgroundColor;
        if (selected) GUI.backgroundColor = new Color(.35f, .80f, .86f);
        var style = new GUIStyle(GUI.skin.button) { fontStyle = selected ? FontStyle.Bold : FontStyle.Normal };
        if (GUILayout.Button(label, style, GUILayout.Height(28)) && !selected)
        {
            showHeatmap = heatmap;
            ApplyBackground();
        }
        GUI.backgroundColor = previous;
    }

    static bool Section(string title,bool expanded)
    {
        GUILayout.Space(4);
        return GUILayout.Button((expanded?"▾ ":"▸ ")+title)?!expanded:expanded;
    }

    static void Fill(Rect rect,Color color)
    {
        Color previous=GUI.color;
        GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=previous;
    }

    void DrawLegend()
    {
        Rect rect=GUILayoutUtility.GetRect(1,7,GUILayout.ExpandWidth(true));
        for(int i=0;i<64;i++)
            Fill(new Rect(rect.x+rect.width*i/64f,rect.y,rect.width/64f+1,rect.height),ColorFor(i/63f));
        GUILayout.BeginHorizontal();
        GUILayout.Label("Less need");GUILayout.FlexibleSpace();GUILayout.Label("More need");
        GUILayout.EndHorizontal();
    }

    static void DrawMetric(int index,float value)
    {
        GUILayout.Space(5);
        GUILayout.BeginHorizontal();
        GUILayout.Label(new GUIContent(Names[index],FilterHints[index]));GUILayout.FlexibleSpace();GUILayout.Label(value<0?"Unknown":value.ToString("0.000"));
        GUILayout.EndHorizontal();
        Rect rect=GUILayoutUtility.GetRect(1,5,GUILayout.ExpandWidth(true));
        Fill(rect,new Color(.18f,.20f,.22f));
        if(value>=0)Fill(new Rect(rect.x,rect.y,rect.width*Mathf.Clamp01(value),rect.height),ColorFor(value));
    }

    // Clip actual surface triangles to a circular footprint around the target.
    // Its fixed model-space radius is only a marker size, not a predicted improvement area.
    void UpdateSurfaceTarget()
    {
        targetMesh.Clear();
        if(Selected==null)return;
        Vector3 center=Selected.target, normal=samples[Selected.sample].normal.normalized;
        Vector3 right=Vector3.Cross(normal,Mathf.Abs(normal.y)<.9f?Vector3.up:Vector3.right).normalized;
        Vector3 up=Vector3.Cross(normal,right);
        float radius=original.bounds.size.magnitude*.035f;
        targetMaterial.SetFloat("_SurfaceMarker",1);
        targetMaterial.SetVector("_TargetCenter",new Vector4(center.x,center.y,center.z,0));
        targetMaterial.SetVector("_TargetNormal",new Vector4(normal.x,normal.y,normal.z,0));
        targetMaterial.SetFloat("_TargetRadius",radius);
        Vector3[] vertices=original.vertices;
        int[] faces=original.triangles;
        var output=new List<Vector3>();var colors=new List<Color32>();var indices=new List<int>();
        var polygon=new List<Vector3>(40);var clipped=new List<Vector3>(40);
        var planes=new Vector3[128];
        for(int j=0;j<planes.Length;j++)
        {
            float angle=2*Mathf.PI*j/planes.Length;
            planes[j]=right*Mathf.Cos(angle)+up*Mathf.Sin(angle);
        }
        for(int face=0;face<faces.Length;face+=3)
        {
            Vector3 a=frame.InverseTransformPoint(filter.transform.TransformPoint(vertices[faces[face]]));
            Vector3 b=frame.InverseTransformPoint(filter.transform.TransformPoint(vertices[faces[face+1]]));
            Vector3 c=frame.InverseTransformPoint(filter.transform.TransformPoint(vertices[faces[face+2]]));
            var bounds=new Bounds(a,Vector3.zero);bounds.Encapsulate(b);bounds.Encapsulate(c);
            if(bounds.SqrDistance(center)>radius*radius*1.4f)continue;
            // Exclude unrelated surfaces behind the selected patch.
            if(Mathf.Abs(Vector3.Dot((a+b+c)/3-center,normal))>radius*.6f)continue;
            polygon.Clear();polygon.Add(a);polygon.Add(b);polygon.Add(c);
            foreach(Vector3 plane in planes)
            {
                clipped.Clear();
                if(polygon.Count==0)break;
                Vector3 previous=polygon[polygon.Count-1];
                float previousDistance=Vector3.Dot(previous-center,plane)-radius;
                foreach(Vector3 current in polygon)
                {
                    float distance=Vector3.Dot(current-center,plane)-radius;
                    if((distance<=0)!=(previousDistance<=0))
                        clipped.Add(Vector3.Lerp(previous,current,previousDistance/(previousDistance-distance)));
                    if(distance<=0)clipped.Add(current);
                    previous=current;previousDistance=distance;
                }
                var swap=polygon;polygon=clipped;clipped=swap;
            }
            int start=output.Count;
            foreach(Vector3 point in polygon)
            {output.Add(point);colors.Add(new Color32(15,46,148,255));}
            for(int j=1;j+1<polygon.Count;j++)
            {indices.Add(start);indices.Add(start+j);indices.Add(start+j+1);}
        }
        targetMesh.SetVertices(output);targetMesh.SetColors(colors);targetMesh.SetTriangles(indices,0);targetMesh.RecalculateBounds();
    }

    void LabelAt(Vector3 point,string label,float width,float verticalOffset=30)
    {
        Vector3 p=Camera.main.WorldToScreenPoint(frame.TransformPoint(point));
        if(p.z>0)GUI.Box(new Rect(p.x-width*.5f,Screen.height-p.y-verticalOffset,width,24),label);
    }

    public void Restore()
    {
        shown=false;
        photoBackground?.Restore();
        if(filter!=null&&original!=null)filter.sharedMesh=original;
        if(renderer!=null&&originalMaterials!=null)renderer.sharedMaterials=originalMaterials;
        if(markerRoot!=null)markerRoot.SetActive(false);
        if(targetRoot!=null)targetRoot.SetActive(false);
    }
    public void Dispose()
    {
        photoBackground?.Dispose();photoBackground=null;
        Restore();Release(colored);Release(material);Release(markerMesh);Release(markerMaterial);Release(markerRoot);Release(targetMesh);Release(targetRoot);Release(targetMaterial);
        colored=null;material=null;markerMesh=null;markerMaterial=null;markerRoot=null;targetMesh=null;targetRoot=null;targetMaterial=null;
    }
    static void Release(UnityEngine.Object value)
    {if(value==null)return;if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
}
