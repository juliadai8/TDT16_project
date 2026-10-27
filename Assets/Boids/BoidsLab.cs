using UnityEngine;
using System.Collections.Generic;

// Attach to an empty GameObject in a fresh Unity 3D scene and press Play.
// Positions and velocities are local to this object so the lab can later be
// placed under an AR image target without changing the flocking algorithm.
public class BoidsLab : MonoBehaviour
{
    const int Count = 200;
    const float Radius = 3.8f;
    const int GroupCount = 2;
    const float GroupSpacing = 12f; // Distance between the two flock centres
    const float Perception = 1.45f;
    const float PersonalSpace = 0.55f;
    const float MaxSpeed = 3.7f;
    const float MaxForce = 2.2f;

    [Header("Terrain Avoidance")]
    public Terrain terrain;
    public float minimumHeight = 3f;
    public float terrainAvoidanceWeight = 5f;

    [Header("Ground Collision")]
    public LayerMask groundLayer;

    Vector3[] positions, velocities, separation, alignment, cohesion;
    readonly Vector3[] groupCentres = new Vector3[GroupCount];
    Transform[] birds;
    LineRenderer[] neighbourLines;
    LineRenderer separationLine, alignmentLine, cohesionLine;
    Material birdMaterial, selectedMaterial, lineMaterial;
    readonly Dictionary<Color, Material> lineColours = new Dictionary<Color, Material>();
    Camera view;
    Transform root;
    Mesh birdMesh;
    float separationWeight = 1.5f, alignmentWeight = 1f, cohesionWeight = 1f;
    float yaw = 25f, pitch = 22f, distance = 21f;
    bool playing = true, inspect = true, stepRequested;
    int selected;
    Vector2 panelScroll;

    void Start()
    {
        root = new GameObject("Flock (local coordinates)").transform;
        root.SetParent(transform, false);
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) { Debug.LogError("An unlit shader is required."); enabled = false; return; }
        birdMaterial = MakeMaterial(shader, new Color(0.2f, 0.85f, 1f));
        selectedMaterial = MakeMaterial(shader, Color.white);
        lineMaterial = MakeMaterial(shader, Color.white);
        birdMesh = CreateBirdMesh();

        positions = new Vector3[Count]; velocities = new Vector3[Count];
        separation = new Vector3[Count]; alignment = new Vector3[Count];
        cohesion = new Vector3[Count]; birds = new Transform[Count];
        neighbourLines = new LineRenderer[Count];
        for (int i = 0; i < Count; i++)
        {
            GameObject bird = new GameObject("Boid " + i);
            bird.transform.SetParent(root, false);
            bird.AddComponent<MeshFilter>().sharedMesh = birdMesh;
            bird.AddComponent<MeshRenderer>().sharedMaterial = birdMaterial;
            birds[i] = bird.transform;
            neighbourLines[i] = MakeLine("Neighbour", new Color(0.3f, 0.4f, 0.5f, 0.5f), 0.009f);
        }
        separationLine = MakeLine("Separation - red", Color.red, 0.035f);
        alignmentLine = MakeLine("Alignment - green", Color.green, 0.035f);
        cohesionLine = MakeLine("Cohesion - yellow", Color.yellow, 0.035f);

        // A separate camera avoids depending on the template's camera settings.
        if (Camera.main != null) Camera.main.gameObject.SetActive(false);
        GameObject cameraObject = new GameObject("Boids orbit camera");
        view = cameraObject.AddComponent<Camera>();
        cameraObject.AddComponent<AudioListener>();
        view.clearFlags = CameraClearFlags.SolidColor;
        view.backgroundColor = new Color(0.025f, 0.035f, 0.07f);
        view.nearClipPlane = 0.02f;
        view.farClipPlane = 150f;
        ResetFlock();
    }

    Material MakeMaterial(Shader shader, Color colour)
    {
        Material material = new Material(shader);
        material.color = colour;
        return material;
    }

    Mesh CreateBirdMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "Simple forward-facing boid";
        mesh.vertices = new[] { new Vector3(0, 0, 0.18f), new Vector3(-0.08f, -0.045f, -0.1f),
            new Vector3(0.08f, -0.045f, -0.1f), new Vector3(0, 0.07f, -0.08f) };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 3, 1, 2, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    LineRenderer MakeLine(string label, Color colour, float width)
    {
        GameObject obj = new GameObject(label);
        obj.transform.SetParent(root, false);
        LineRenderer line = obj.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.positionCount = 2;
        // Unlit shaders need material colour; not all read line vertex colours.
        Material colouredMaterial;
        if (!lineColours.TryGetValue(colour, out colouredMaterial))
        {
            colouredMaterial = new Material(lineMaterial);
            colouredMaterial.color = colour;
            lineColours.Add(colour, colouredMaterial);
        }
        line.sharedMaterial = colouredMaterial;
        line.startColor = line.endColor = Color.white;
        line.startWidth = line.endWidth = width;
        return line;
    }

    int GroupOf(int birdIndex)
    {
        // First 35 boids belong to group 0; last 35 belong to group 1.
        return birdIndex < Count / 2 ? 0 : 1;
    }

    void ResetFlock()
    {
        // Keep both groups near independent centres in local coordinates.
        // Place centres above the forest terrain, when a Terrain is assigned.
        for (int group = 0; group < GroupCount; group++)
        {
            float x = (group == 0 ? -0.5f : 0.5f) * GroupSpacing;
            Vector3 centre = new Vector3(x, 5f, 0f);
            if (terrain != null)
            {
                Vector3 worldCentre = root.TransformPoint(centre);
                float terrainY = terrain.SampleHeight(worldCentre) + terrain.transform.position.y;
                worldCentre.y = terrainY + minimumHeight + 3f;
                centre = root.InverseTransformPoint(worldCentre);
            }
            groupCentres[group] = centre;
        }

        Random.State previous = Random.state;
        Random.InitState(16); // Repeatable starting state
        for (int i = 0; i < Count; i++)
        {
            positions[i] = groupCentres[GroupOf(i)] + Random.insideUnitSphere * 1.1f;
            velocities[i] = Random.onUnitSphere * MaxSpeed * 0.7f;
            KeepAboveGround(i);
        }
        Random.state = previous;
    }

    Vector3 Steer(Vector3 desired, Vector3 velocity)
    {
        if (desired.sqrMagnitude < 0.000001f) return Vector3.zero;
        return Vector3.ClampMagnitude(desired.normalized * MaxSpeed - velocity, MaxForce);
    }

    void KeepAboveGround(int i)
{
    Vector3 worldPosition = root.TransformPoint(positions[i]);

        // Start above the bird so we can also detect ground
        // when the bird is already below the surface.
        Vector3 rayOrigin = worldPosition + Vector3.up * 100f;

        if (Physics.Raycast(
            rayOrigin,
            Vector3.down,
            out RaycastHit hit,
            200f,
            groundLayer,
            QueryTriggerInteraction.Ignore))
        {
            float minimumY = hit.point.y + minimumHeight;

            if (worldPosition.y < minimumY)
            {
                worldPosition.y = minimumY;

                positions[i] = root.InverseTransformPoint(worldPosition);

                // Prevent the bird from continuing downward.
                Vector3 worldVelocity =
                    root.TransformDirection(velocities[i]);

                if (worldVelocity.y < 0f)
                {
                    worldVelocity.y = 0f;
                    velocities[i] =
                        root.InverseTransformDirection(worldVelocity);
                }
            }
        }
    }

    Vector3 TerrainAvoidance(Vector3 localPosition, Vector3 localVelocity)
    {
        if (terrain == null)
            return Vector3.zero;

        // Convert local boid position to world coordinates
        Vector3 worldPosition = root.TransformPoint(localPosition);

        // Find the ground height at the boid's position
        float groundHeight =
            terrain.SampleHeight(worldPosition)
            + terrain.transform.position.y;

        float desiredHeight = groundHeight + minimumHeight;

        // No avoidance needed when safely above ground
        if (worldPosition.y >= desiredHeight)
            return Vector3.zero;

        // Steer upward in world space, then convert to local space
        Vector3 worldUp = Vector3.up;
        Vector3 localUp = root.InverseTransformDirection(worldUp);

        return Steer(localUp, localVelocity);
    }

    void CalculateForces()
    {
        // Read a complete snapshot before moving any boid: update order must
        // not affect what neighbours the other boids see this frame.
        for (int i = 0; i < Count; i++)
        {
            Vector3 away = Vector3.zero, heading = Vector3.zero, centre = Vector3.zero;
            int nearby = 0, close = 0;
            for (int j = 0; j < Count; j++)
            {
                if (j == i || GroupOf(i) != GroupOf(j)) continue;
                Vector3 delta = positions[i] - positions[j];
                float squared = delta.sqrMagnitude;
                if (squared > Perception * Perception) continue;
                nearby++;
                heading += velocities[j];
                centre += positions[j];
                if (squared < PersonalSpace * PersonalSpace)
                {
                    away += delta / Mathf.Max(squared, 0.0001f);
                    close++;
                }
            }
            separation[i] = close > 0 ? Steer(away / close, velocities[i]) : Vector3.zero;
            alignment[i] = nearby > 0 ? Steer(heading / nearby, velocities[i]) : Vector3.zero;
            cohesion[i] = nearby > 0 ? Steer(centre / nearby - positions[i], velocities[i]) : Vector3.zero;
        }
    }

    void Update()
    {
        if (positions == null) return;
        CalculateForces();
        if (playing || stepRequested)
        {
            float dt = stepRequested ? 1f / 30f : Mathf.Min(Time.deltaTime, 1f / 30f);
            stepRequested = false;
            for (int i = 0; i < Count; i++)
            {
                Vector3 force = separation[i] * separationWeight
                    + alignment[i] * alignmentWeight
                    + cohesion[i] * cohesionWeight
                    + TerrainAvoidance(positions[i], velocities[i])
                        * terrainAvoidanceWeight;
                // Keep each group near its OWN centre, not one shared centre.
                Vector3 fromGroupCentre = positions[i] - groupCentres[GroupOf(i)];
                float excess = fromGroupCentre.magnitude - Radius;
                if (excess > 0)
                    force -= fromGroupCentre.normalized * excess * 3f;
                velocities[i] = Vector3.ClampMagnitude(velocities[i] + force * dt, MaxSpeed);
                positions[i] += velocities[i] * dt;
                KeepAboveGround(i);
            }
            CalculateForces();
        }
        for (int i = 0; i < Count; i++)
        {
            birds[i].localPosition = positions[i];
            if (velocities[i].sqrMagnitude > 0.000001f)
                birds[i].localRotation = Quaternion.LookRotation(velocities[i]);
            birds[i].GetComponent<MeshRenderer>().sharedMaterial = i == selected ? selectedMaterial : birdMaterial;
            bool neighbour = i != selected && GroupOf(i) == GroupOf(selected)
                && (positions[i] - positions[selected]).sqrMagnitude < Perception * Perception;
            neighbourLines[i].enabled = inspect && neighbour;
            if (inspect && neighbour) SetLine(neighbourLines[i], positions[selected], positions[i]);
        }
        ShowForce(separationLine, separation[selected] * separationWeight);
        ShowForce(alignmentLine, alignment[selected] * alignmentWeight);
        ShowForce(cohesionLine, cohesion[selected] * cohesionWeight);
    }

    void SetLine(LineRenderer line, Vector3 start, Vector3 end)
    { line.SetPosition(0, start); line.SetPosition(1, end); }

    void ShowForce(LineRenderer line, Vector3 force)
    {
        line.enabled = inspect && force.sqrMagnitude > 0.000001f;
        SetLine(line, positions[selected], positions[selected] + force * 0.55f);
    }

    void LateUpdate()
    {
        if (view == null) return;
        Vector3 centre = transform.position;
        view.transform.position = centre + Quaternion.Euler(pitch, yaw, 0) * new Vector3(0, 0, -distance);
        view.transform.LookAt(centre);
        // Leave the left side for the learning controls.
        view.rect = new Rect(Mathf.Min(0.48f, 370f / Screen.width), 0, 1f - Mathf.Min(0.48f, 370f / Screen.width), 1);
    }

    float RuleSlider(string name, string explanation, float value)
    {
        GUILayout.Label(name + ": " + value.ToString("F2"));
        float result = GUILayout.HorizontalSlider(value, 0f, 3f);
        GUILayout.Label(explanation);
        GUILayout.Space(8);
        return result;
    }

    void OnGUI()
    {
        if (positions == null) return;
        float width = Mathf.Min(360f, Screen.width * 0.47f);
        Rect panel = new Rect(8, 8, width, Screen.height - 16);
        GUILayout.BeginArea(panel, GUI.skin.box);
        panelScroll = GUILayout.BeginScrollView(panelScroll);
        GUILayout.Label("BOIDS LEARNING LAB");
        GUILayout.Label("How do local rules produce two flocks?");
        GUILayout.Space(10);
        separationWeight = RuleSlider("Separation (red)", "Avoid crowding close neighbours.", separationWeight);
        alignmentWeight = RuleSlider("Alignment (green)", "Match nearby boids' direction.", alignmentWeight);
        cohesionWeight = RuleSlider("Cohesion (yellow)", "Steer toward the local group's centre.", cohesionWeight);
        GUILayout.Label("Set a rule to 0 to turn it off.");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(playing ? "Pause" : "Play")) playing = !playing;
        if (GUILayout.Button("Step")) { playing = false; stepRequested = true; }
        if (GUILayout.Button("Reset flock")) ResetFlock();
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Restore default rules"))
        { separationWeight = 1.5f; alignmentWeight = cohesionWeight = 1f; }
        inspect = GUILayout.Toggle(inspect, "Inspect selected boid (white)");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Previous")) selected = (selected + Count - 1) % Count;
        GUILayout.Label("Boid " + selected);
        if (GUILayout.Button("Next")) selected = (selected + 1) % Count;
        GUILayout.EndHorizontal();
        GUILayout.Label("Grey lines: neighbours within sensing radius.\nColoured segments: weighted steering vectors.");
        GUILayout.Space(12);
        GUILayout.Label("TRY THIS");
        GUILayout.Label("1. Predict what happens without cohesion.\n2. Set cohesion to 0 and reset the flock.\n3. Restore it and compare the motion.");
        GUILayout.Space(12);
        GUILayout.Label("VIEW: drag in the scene to orbit; scroll to zoom.");
        GUILayout.Label("Two independent groups (35 boids each). Each group has its own boundary and local neighbours. They do not merge.");
        GUILayout.Label("Desktop prototype: AR tracking is not included yet.");
        GUILayout.EndScrollView();
        GUILayout.EndArea();

        // GUI events also work when the project uses the new Input System.
        Event evt = Event.current;
        if (!panel.Contains(evt.mousePosition))
        {
            if (evt.type == EventType.MouseDrag && evt.button == 0)
            { yaw += evt.delta.x * 0.4f; pitch = Mathf.Clamp(pitch + evt.delta.y * 0.4f, -80f, 80f); evt.Use(); }
            if (evt.type == EventType.ScrollWheel)
            { distance = Mathf.Clamp(distance + evt.delta.y * 0.4f, 4f, 25f); evt.Use(); }
        }
    }

    void OnDestroy()
    {
        if (view != null) Destroy(view.gameObject);
        if (root != null) Destroy(root.gameObject);
        if (birdMesh != null) Destroy(birdMesh);
        if (birdMaterial != null) Destroy(birdMaterial);
        if (selectedMaterial != null) Destroy(selectedMaterial);
        if (lineMaterial != null) Destroy(lineMaterial);
        foreach (Material material in lineColours.Values) Destroy(material);
    }
}
