using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Replaces the broken straight V2 map presentation at runtime with a visible,
/// deterministic curved winter course. Existing imported renderers/colliders
/// are disabled, while their scene/controller roots remain intact.
/// </summary>
[DisallowMultipleComponent]
public sealed class MushCurvedMapRuntime : MonoBehaviour
{
    private const float CourseLength = 960f;
    private const float SampleSpacing = 4f;
    private const float RoadHalfWidth = 6.5f;
    private const float TerrainHalfWidth = 105f;
    private const string RebuiltRootName = "Mush Rebuilt Curved World";

    private readonly List<Vector3> routePoints = new();
    private readonly List<CurveEvent> curveEvents = new();
    private readonly List<Material> runtimeMaterials = new();
    private bool built;
    private bool isSnowfield;
    private Transform rebuiltRoot;
    private Mesh pineMesh;
    private Mesh mountainMesh;
    private Renderer roadRenderer;
    private Renderer terrainRenderer;

    public Vector3 StartForward { get; private set; } = Vector3.back;
    public Transform AmbientSnowTransform { get; private set; }
    public float LengthMeters => CourseLength;
    public float RoadHalfWidthMeters => RoadHalfWidth;

    private readonly struct CurveEvent
    {
        public readonly float start;
        public readonly float length;
        public readonly float strength;
        public readonly bool sCurve;

        public CurveEvent(float start, float length, float strength, bool sCurve)
        {
            this.start = start;
            this.length = length;
            this.strength = strength;
            this.sCurve = sCurve;
        }
    }

    public static MushCurvedMapRuntime EnsureBuilt(Transform mapRoot)
    {
        if (mapRoot == null)
            return null;

        MushCurvedMapRuntime runtime = mapRoot.GetComponent<MushCurvedMapRuntime>();
        if (runtime == null)
            runtime = mapRoot.gameObject.AddComponent<MushCurvedMapRuntime>();
        runtime.BuildWorld();
        return runtime;
    }

    public void BuildWorld()
    {
        if (built)
            return;

        isSnowfield = GetComponent<MushSnowfieldBlizzardController>() != null ||
                      name.Contains("Snow", StringComparison.OrdinalIgnoreCase);
        DisableImportedPresentation();

        GameObject rootObject = new(RebuiltRootName);
        rebuiltRoot = rootObject.transform;
        rebuiltRoot.SetParent(transform, false);

        BuildCurveSchedule();
        BuildRoutePoints();
        BuildCourseMeshes();
        BuildScenery();
        BuildSkyAndLighting();
        BuildAmbientSnow();
        PositionRouteMarkers();

        built = true;
        Bounds roadBounds = roadRenderer != null ? roadRenderer.bounds : default;
        Bounds terrainBounds = terrainRenderer != null ? terrainRenderer.bounds : default;
        Debug.Log(
            $"[Mush Map Rebuild] Scene={gameObject.scene.path}, Type={(isSnowfield ? "SNOW" : "TREE")}, " +
            $"Length={CourseLength:0}m, Samples={routePoints.Count}, Curves={curveEvents.Count}, " +
            $"RoadBounds={roadBounds.size}, TerrainBounds={terrainBounds.size}, " +
            $"Renderers={rebuiltRoot.GetComponentsInChildren<Renderer>(true).Length}",
            this);
    }

    /// <summary>
    /// Returns the horizontal distance from a world position to the visible
    /// road centre line. The same sampled route builds the road mesh, so this
    /// remains accurate through every generated curve without using colliders.
    /// </summary>
    public bool TryGetRoadLateralDistance(Vector3 worldPosition, out float lateralDistance)
    {
        if (!built)
            BuildWorld();

        lateralDistance = 0f;
        if (routePoints.Count < 2)
            return false;

        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        Vector2 point = new(localPosition.x, localPosition.z);
        float nearestSqrDistance = float.PositiveInfinity;

        for (int index = 0; index < routePoints.Count - 1; index++)
        {
            Vector2 start = new(routePoints[index].x, routePoints[index].z);
            Vector2 end = new(routePoints[index + 1].x, routePoints[index + 1].z);
            Vector2 segment = end - start;
            float segmentLengthSqr = segment.sqrMagnitude;
            float t = segmentLengthSqr > 0.0001f
                ? Mathf.Clamp01(Vector2.Dot(point - start, segment) / segmentLengthSqr)
                : 0f;
            float sqrDistance = (point - (start + segment * t)).sqrMagnitude;
            if (sqrDistance < nearestSqrDistance)
                nearestSqrDistance = sqrDistance;
        }

        lateralDistance = Mathf.Sqrt(nearestSqrDistance);
        return true;
    }

    private void DisableImportedPresentation()
    {
        foreach (Transform child in transform.GetComponentsInChildren<Transform>(true))
        {
            if (child == transform || child.name == RebuiltRootName)
                continue;

            if (child.name.Equals("FX_Blizzard_V2", StringComparison.OrdinalIgnoreCase) ||
                child.name.Contains("StarDome", StringComparison.OrdinalIgnoreCase))
                child.name = "OLD_DISABLED_" + child.name;
        }

        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;
        foreach (Collider collider in GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
        foreach (ParticleSystem particles in GetComponentsInChildren<ParticleSystem>(true))
        {
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.gameObject.SetActive(false);
        }
    }

    private void BuildCurveSchedule()
    {
        System.Random random = new(isSnowfield ? 27183 : 91457);
        float cursor = 48f;
        int direction = random.NextDouble() < 0.5 ? -1 : 1;
        while (cursor < CourseLength - 80f)
        {
            cursor += random.Next(28, 66);
            bool sCurve = random.NextDouble() < 0.34;
            float length = sCurve ? random.Next(80, 132) : random.Next(58, 146);
            length = Mathf.Min(length, CourseLength - 45f - cursor);
            if (length < 35f)
                break;

            float strength = Mathf.Lerp(0.18f, sCurve ? 0.48f : 0.36f, (float)random.NextDouble());
            curveEvents.Add(new CurveEvent(cursor, length, strength * direction, sCurve));
            direction *= -1;
            cursor += length;
        }
    }

    private void BuildRoutePoints()
    {
        int sampleCount = Mathf.RoundToInt(CourseLength / SampleSpacing) + 1;
        Vector3 position = Vector3.zero;
        float headingRadians = 0f;
        routePoints.Add(new Vector3(0f, RouteHeight(0f), 0f));

        for (int index = 1; index < sampleCount; index++)
        {
            float distance = index * SampleSpacing;
            float curvatureDegreesPerMeter = EvaluateCurvature(distance);
            headingRadians += curvatureDegreesPerMeter * SampleSpacing * Mathf.Deg2Rad;
            headingRadians = Mathf.Clamp(headingRadians, -52f * Mathf.Deg2Rad, 52f * Mathf.Deg2Rad);

            Vector3 forward = new(Mathf.Sin(headingRadians), 0f, -Mathf.Cos(headingRadians));
            position += forward * SampleSpacing;
            position.y = RouteHeight(distance);
            routePoints.Add(position);
        }

        if (routePoints.Count > 1)
            StartForward = Vector3.ProjectOnPlane(routePoints[1] - routePoints[0], Vector3.up).normalized;
    }

    private float EvaluateCurvature(float distance)
    {
        float curvature = 0f;
        for (int index = 0; index < curveEvents.Count; index++)
        {
            CurveEvent curve = curveEvents[index];
            if (distance < curve.start || distance > curve.start + curve.length)
                continue;

            float t = Mathf.InverseLerp(curve.start, curve.start + curve.length, distance);
            float wave = curve.sCurve ? Mathf.Sin(t * Mathf.PI * 2f) : Mathf.Sin(t * Mathf.PI);
            curvature += curve.strength * wave;
        }
        return curvature;
    }

    private static float RouteHeight(float distance)
    {
        float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 70f, distance));
        return fadeIn * (Mathf.Sin(distance * 0.0105f) * 1.7f + Mathf.Sin(distance * 0.027f) * 0.48f);
    }

    private void BuildCourseMeshes()
    {
        Material snow = CreateLitMaterial(
            "Rebuilt Snow Terrain",
            isSnowfield ? new Color(0.84f, 0.91f, 0.98f) : new Color(0.77f, 0.87f, 0.94f),
            0.12f);
        Material road = CreateLitMaterial(
            "Rebuilt Packed Snow Road",
            isSnowfield ? new Color(0.27f, 0.39f, 0.53f) : new Color(0.23f, 0.34f, 0.43f),
            0.18f);
        Material track = CreateLitMaterial("Rebuilt Dark Sled Tracks", new Color(0.075f, 0.12f, 0.17f), 0.08f);

        GameObject terrainObject = CreateMeshObject("VISIBLE Snow Terrain", rebuiltRoot, BuildTerrainMesh(), snow, true);
        terrainRenderer = terrainObject.GetComponent<Renderer>();

        GameObject roadObject = CreateMeshObject(
            "VISIBLE Curved Packed-Snow Road",
            rebuiltRoot,
            BuildRibbonMesh(RoadHalfWidth, 0f, 0.10f),
            road,
            true);
        roadRenderer = roadObject.GetComponent<Renderer>();

        CreateMeshObject("Left Sled Track", rebuiltRoot, BuildRibbonMesh(0.10f, -1.75f, 0.145f), track, false);
        CreateMeshObject("Right Sled Track", rebuiltRoot, BuildRibbonMesh(0.10f, 1.75f, 0.145f), track, false);
    }

    private Mesh BuildRibbonMesh(float halfWidth, float lateralOffset, float yLift)
    {
        int count = routePoints.Count;
        Vector3[] vertices = new Vector3[count * 2];
        Vector2[] uv = new Vector2[vertices.Length];
        int[] triangles = new int[(count - 1) * 6];

        for (int index = 0; index < count; index++)
        {
            Vector3 right = RouteRight(index);
            Vector3 center = routePoints[index] + right * lateralOffset + Vector3.up * yLift;
            vertices[index * 2] = center - right * halfWidth;
            vertices[index * 2 + 1] = center + right * halfWidth;
            float v = index * SampleSpacing * 0.1f;
            uv[index * 2] = new Vector2(0f, v);
            uv[index * 2 + 1] = new Vector2(1f, v);

            if (index >= count - 1)
                continue;
            int triangle = index * 6;
            int vertex = index * 2;
            triangles[triangle] = vertex;
            triangles[triangle + 1] = vertex + 2;
            triangles[triangle + 2] = vertex + 1;
            triangles[triangle + 3] = vertex + 1;
            triangles[triangle + 4] = vertex + 2;
            triangles[triangle + 5] = vertex + 3;
        }

        Mesh mesh = new() { name = $"Curved Ribbon {halfWidth:0.00}" };
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private Mesh BuildTerrainMesh()
    {
        const int columns = 17;
        int rows = routePoints.Count;
        Vector3[] vertices = new Vector3[rows * columns];
        Vector2[] uv = new Vector2[vertices.Length];
        int[] triangles = new int[(rows - 1) * (columns - 1) * 6];

        for (int row = 0; row < rows; row++)
        {
            float distance = row * SampleSpacing;
            Vector3 right = RouteRight(row);
            for (int column = 0; column < columns; column++)
            {
                float lateral = Mathf.Lerp(-TerrainHalfWidth, TerrainHalfWidth, column / (float)(columns - 1));
                float height = TerrainHeight(distance, lateral, routePoints[row].y);
                int vertex = row * columns + column;
                vertices[vertex] = routePoints[row] + right * lateral;
                vertices[vertex].y = height;
                uv[vertex] = new Vector2(column * 0.32f, row * 0.28f);
            }
        }

        int write = 0;
        for (int row = 0; row < rows - 1; row++)
        for (int column = 0; column < columns - 1; column++)
        {
            int a = row * columns + column;
            int b = a + 1;
            int c = a + columns;
            int d = c + 1;
            triangles[write++] = a;
            triangles[write++] = c;
            triangles[write++] = b;
            triangles[write++] = b;
            triangles[write++] = c;
            triangles[write++] = d;
        }

        Mesh mesh = new() { name = "Curved Winter Terrain" };
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static float TerrainHeight(float distance, float lateral, float routeHeight)
    {
        float outsideRoad = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RoadHalfWidth + 1.5f, 42f, Mathf.Abs(lateral)));
        float rolling = Mathf.Sin(distance * 0.019f + lateral * 0.043f) * 1.4f +
                        Mathf.Sin(distance * 0.007f - lateral * 0.085f) * 0.75f;
        float distantRise = Mathf.Pow(Mathf.InverseLerp(24f, TerrainHalfWidth, Mathf.Abs(lateral)), 1.35f) * 8.5f;
        return routeHeight - 0.18f + outsideRoad * (0.55f + rolling + distantRise);
    }

    private Vector3 RouteTangent(int index)
    {
        int previous = Mathf.Max(0, index - 1);
        int next = Mathf.Min(routePoints.Count - 1, index + 1);
        Vector3 tangent = Vector3.ProjectOnPlane(routePoints[next] - routePoints[previous], Vector3.up);
        return tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector3.back;
    }

    private Vector3 RouteRight(int index)
    {
        Vector3 right = Vector3.Cross(Vector3.up, RouteTangent(index));
        return right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.left;
    }

    private void BuildScenery()
    {
        Material trunk = CreateLitMaterial("Rebuilt Tree Trunks", new Color(0.25f, 0.12f, 0.055f), 0.10f);
        Material foliage = CreateLitMaterial(
            "Rebuilt Pine Needles",
            isSnowfield ? new Color(0.055f, 0.14f, 0.16f) : new Color(0.035f, 0.17f, 0.105f),
            0.08f);
        Material rock = CreateLitMaterial("Rebuilt Snowy Rock", new Color(0.38f, 0.45f, 0.51f), 0.15f);
        Material post = CreateLitMaterial("Rebuilt Route Post", new Color(0.88f, 0.24f, 0.035f), 0.20f, new Color(1.8f, 0.25f, 0.02f));
        Material mountain = CreateLitMaterial("Rebuilt Distant Mountains", new Color(0.36f, 0.48f, 0.61f), 0.05f);

        pineMesh = BuildStackedPineMesh();
        mountainMesh = BuildMountainMesh();
        System.Random random = new(isSnowfield ? 6673 : 44191);
        int treeStep = isSnowfield ? 6 : 3;

        for (int index = 8; index < routePoints.Count - 8; index += treeStep)
        {
            float distance = index * SampleSpacing;
            int perSide = isSnowfield ? 1 : 2;
            for (int side = -1; side <= 1; side += 2)
            for (int layer = 0; layer < perSide; layer++)
            {
                float lateral = side * Mathf.Lerp(
                    RoadHalfWidth + 8f + layer * 11f,
                    TerrainHalfWidth - 14f,
                    (float)random.NextDouble());
                float y = TerrainHeight(distance, lateral, routePoints[index].y);
                Vector3 position = routePoints[index] + RouteRight(index) * lateral;
                position.y = y;
                float scale = Mathf.Lerp(isSnowfield ? 2.4f : 3.0f, isSnowfield ? 5.8f : 7.2f, (float)random.NextDouble());
                BuildPine(position, scale, trunk, foliage, random.Next(0, 360));
            }

            if (index % (treeStep * 4) == 0)
            {
                int side = random.NextDouble() < 0.5 ? -1 : 1;
                float lateral = side * Mathf.Lerp(13f, 32f, (float)random.NextDouble());
                Vector3 position = routePoints[index] + RouteRight(index) * lateral;
                position.y = TerrainHeight(distance, lateral, routePoints[index].y) + 0.4f;
                CreateMeshObject("Snow Rock", rebuiltRoot, mountainMesh, rock, false, position,
                    Quaternion.Euler(0f, random.Next(0, 360), 0f),
                    new Vector3(1.2f, 0.75f, 1.0f) * Mathf.Lerp(0.8f, 1.8f, (float)random.NextDouble()));
            }
        }

        for (int index = 10; index < routePoints.Count - 4; index += 10)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 position = routePoints[index] + RouteRight(index) * (side * (RoadHalfWidth + 1.15f));
                position.y += 0.62f;
                CreateCube("Visible Route Beacon", rebuiltRoot, position, new Vector3(0.12f, 1.25f, 0.12f), post,
                    Quaternion.LookRotation(RouteTangent(index), Vector3.up));
            }
        }

        for (int index = 18; index < routePoints.Count - 8; index += 18)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float lateral = side * 88f;
                Vector3 position = routePoints[index] + RouteRight(index) * lateral;
                position.y = TerrainHeight(index * SampleSpacing, lateral, routePoints[index].y) - 1f;
                float size = Mathf.Lerp(13f, 28f, (float)random.NextDouble());
                CreateMeshObject("Distant Snow Mountain", rebuiltRoot, mountainMesh, mountain, false, position,
                    Quaternion.Euler(0f, random.Next(0, 360), 0f),
                    new Vector3(size, size * Mathf.Lerp(0.8f, 1.35f, (float)random.NextDouble()), size));
            }
        }

        if (isSnowfield)
        {
            Material cabinWall = CreateLitMaterial("Rebuilt Cabin Wall", new Color(0.25f, 0.095f, 0.04f), 0.12f);
            Material cabinRoof = CreateLitMaterial("Rebuilt Cabin Roof Snow", new Color(0.76f, 0.85f, 0.92f), 0.14f);
            for (int index = 35; index < routePoints.Count - 20; index += 55)
            {
                int side = (index / 55) % 2 == 0 ? -1 : 1;
                float lateral = side * 25f;
                Vector3 position = routePoints[index] + RouteRight(index) * lateral;
                position.y = TerrainHeight(index * SampleSpacing, lateral, routePoints[index].y);
                BuildCabin(position, RouteTangent(index), cabinWall, cabinRoof);
            }
        }
    }

    private void BuildPine(Vector3 position, float height, Material trunk, Material foliage, int yaw)
    {
        GameObject rootObject = new("Visible Snow Pine");
        Transform root = rootObject.transform;
        root.SetParent(rebuiltRoot, false);
        root.position = position;
        root.rotation = Quaternion.Euler(0f, yaw, 0f);
        CreateCube("Trunk", root, new Vector3(0f, height * 0.22f, 0f), new Vector3(height * 0.10f, height * 0.44f, height * 0.10f), trunk, Quaternion.identity, true);
        GameObject crown = CreateMeshObject("Pine Crown", root, pineMesh, foliage, false, scale: Vector3.one * height);
        crown.transform.localPosition = new Vector3(0f, height * 0.20f, 0f);
        crown.transform.localRotation = Quaternion.identity;
    }

    private void BuildCabin(Vector3 position, Vector3 forward, Material wall, Material roof)
    {
        GameObject cabinObject = new("Visible Snow Cabin");
        Transform cabin = cabinObject.transform;
        cabin.SetParent(rebuiltRoot, false);
        cabin.position = position;
        cabin.rotation = Quaternion.LookRotation(forward, Vector3.up);
        CreateCube("Cabin Walls", cabin, new Vector3(0f, 1.35f, 0f), new Vector3(4.5f, 2.7f, 3.4f), wall, Quaternion.identity, true);
        CreateCube("Cabin Roof Left", cabin, new Vector3(-1.18f, 3.0f, 0f), new Vector3(2.9f, 0.20f, 4.0f), roof, Quaternion.Euler(0f, 0f, -28f), true);
        CreateCube("Cabin Roof Right", cabin, new Vector3(1.18f, 3.0f, 0f), new Vector3(2.9f, 0.20f, 4.0f), roof, Quaternion.Euler(0f, 0f, 28f), true);
    }

    private void BuildSkyAndLighting()
    {
        Light sun = null;
        foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type == LightType.Directional)
            {
                sun = light;
                break;
            }
        }
        if (sun == null)
        {
            GameObject sunObject = new("Mush Rebuilt Sun");
            sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
        }
        sun.enabled = true;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(35f, -28f, 0f);

        Shader skyShader = Shader.Find("Skybox/Procedural");
        Material sky = skyShader != null ? new Material(skyShader) { name = "Runtime Rebuilt Winter Sky" } : null;
        if (sky != null)
        {
            SetColor(sky, "_SkyTint", isSnowfield ? new Color(0.52f, 0.72f, 0.93f) : new Color(0.56f, 0.74f, 0.92f));
            SetColor(sky, "_GroundColor", new Color(0.46f, 0.54f, 0.62f));
            SetFloat(sky, "_AtmosphereThickness", 0.75f);
            SetFloat(sky, "_Exposure", 1.2f);
            SetFloat(sky, "_SunSize", 0.045f);
            RenderSettings.skybox = sky;
            runtimeMaterials.Add(sky);
        }

        Renderer stars = null;
        if (!isSnowfield)
        {
            Material starMaterial = CreateUnlitMaterial("Rebuilt Stars", new Color(0.82f, 0.91f, 1f));
            GameObject starObject = CreateMeshObject("FX_StarDome_Rebuilt", rebuiltRoot, BuildStarDomeMesh(), starMaterial, false);
            stars = starObject.GetComponent<Renderer>();
            stars.enabled = false;
        }

        Camera sceneCamera = Camera.main ?? FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (sceneCamera != null)
        {
            sceneCamera.clearFlags = CameraClearFlags.Skybox;
            sceneCamera.backgroundColor = isSnowfield ? new Color(0.52f, 0.72f, 0.93f) : new Color(0.56f, 0.74f, 0.92f);
        }

        if (isSnowfield)
        {
            MushSnowfieldBlizzardController controller = GetComponent<MushSnowfieldBlizzardController>();
            controller?.ConfigureRuntimeWorld(sun, sceneCamera, sky, null, CourseLength);
        }
        else
        {
            MushForestTimeCycleController controller = GetComponent<MushForestTimeCycleController>();
            controller?.ConfigureRuntimeWorld(sun, sceneCamera, sky, stars, CourseLength);
        }
    }

    private void BuildAmbientSnow()
    {
        GameObject snowObject = new("FX_AmbientSnow_Rebuilt");
        snowObject.transform.SetParent(rebuiltRoot, false);
        snowObject.transform.localPosition = new Vector3(0f, 4f, -7f);
        AmbientSnowTransform = snowObject.transform;

        ParticleSystem particles = snowObject.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.loop = true;
        main.playOnAwake = true;
        main.maxParticles = isSnowfield ? 620 : 380;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 4.0f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.0f, 5.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.075f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.82f, 0.92f, 1f, 0.55f),
            new Color(1f, 1f, 1f, 0.92f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = isSnowfield ? 22f : 14f;
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(28f, 12f, 30f);
        ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = isSnowfield ? -2.4f : -1.1f;
        velocity.y = -2.2f;
        velocity.z = -1.0f;

        ParticleSystemRenderer renderer = snowObject.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.08f;
        renderer.lengthScale = 1.6f;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sharedMaterial = CreateParticleMaterial();
        particles.Play();

        if (isSnowfield)
        {
            MushSnowfieldBlizzardController controller = GetComponent<MushSnowfieldBlizzardController>();
            controller?.SetSnowParticles(particles);
        }
    }

    private void PositionRouteMarkers()
    {
        Vector3 start = routePoints[0] + Vector3.up * 0.02f;
        Vector3 finish = routePoints[^1] + Vector3.up * 0.02f;
        SetOrCreateMarker("SPAWN_Sled", start, StartForward);
        SetOrCreateMarker("SPAWN_Dog_Left", start + RouteRight(0) * -0.8f + StartForward * 4f, StartForward);
        SetOrCreateMarker("SPAWN_Dog_Right", start + RouteRight(0) * 0.8f + StartForward * 4f, StartForward);
        SetOrCreateMarker("FINISH_Delivery", finish, RouteTangent(routePoints.Count - 1));
    }

    private void SetOrCreateMarker(string markerName, Vector3 position, Vector3 forward)
    {
        Transform found = null;
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (!child.name.Equals(markerName, StringComparison.OrdinalIgnoreCase))
                continue;
            found = child;
            break;
        }
        if (found == null)
        {
            GameObject markerObject = new(markerName);
            found = markerObject.transform;
            found.SetParent(rebuiltRoot, false);
        }
        found.position = position;
        found.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, Vector3.up).normalized, Vector3.up);
    }

    private GameObject CreateMeshObject(
        string objectName,
        Transform parent,
        Mesh mesh,
        Material material,
        bool addCollider,
        Vector3? position = null,
        Quaternion? rotation = null,
        Vector3? scale = null)
    {
        GameObject gameObject = new(objectName);
        gameObject.transform.SetParent(parent, false);
        gameObject.transform.position = position ?? parent.position;
        gameObject.transform.rotation = rotation ?? parent.rotation;
        gameObject.transform.localScale = scale ?? Vector3.one;
        MeshFilter filter = gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        if (addCollider)
        {
            MeshCollider collider = gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
        }
        return gameObject;
    }

    private static GameObject CreateCube(
        string objectName,
        Transform parent,
        Vector3 position,
        Vector3 scale,
        Material material,
        Quaternion rotation,
        bool localSpace = false)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = objectName;
        cube.transform.SetParent(parent, false);
        if (localSpace)
        {
            cube.transform.localPosition = position;
            cube.transform.localRotation = rotation;
        }
        else
        {
            cube.transform.position = position;
            cube.transform.rotation = rotation;
        }
        cube.transform.localScale = scale;
        cube.GetComponent<Renderer>().sharedMaterial = material;
        Collider collider = cube.GetComponent<Collider>();
        if (collider != null)
            collider.enabled = false;
        return cube;
    }

    private Material CreateLitMaterial(string materialName, Color color, float smoothness, Color? emission = null)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material material = new(shader) { name = materialName };
        SetColor(material, "_BaseColor", color);
        SetColor(material, "_Color", color);
        SetFloat(material, "_Smoothness", smoothness);
        if (emission.HasValue)
        {
            material.EnableKeyword("_EMISSION");
            SetColor(material, "_EmissionColor", emission.Value);
        }
        material.enableInstancing = true;
        runtimeMaterials.Add(material);
        return material;
    }

    private Material CreateUnlitMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
        Material material = new(shader) { name = materialName };
        SetColor(material, "_BaseColor", color);
        SetColor(material, "_Color", color);
        runtimeMaterials.Add(material);
        return material;
    }

    private Material CreateParticleMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                        Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Universal Render Pipeline/Unlit");
        Material material = new(shader) { name = "Runtime White Snow Particles" };
        SetColor(material, "_BaseColor", new Color(0.92f, 0.97f, 1f, 0.82f));
        SetColor(material, "_Color", new Color(0.92f, 0.97f, 1f, 0.82f));
        runtimeMaterials.Add(material);
        return material;
    }

    private static void SetColor(Material material, string property, Color value)
    {
        if (material != null && material.HasProperty(property))
            material.SetColor(property, value);
    }

    private static void SetFloat(Material material, string property, float value)
    {
        if (material != null && material.HasProperty(property))
            material.SetFloat(property, value);
    }

    private static Mesh BuildStackedPineMesh()
    {
        List<Vector3> vertices = new();
        List<int> triangles = new();
        const int sides = 8;
        for (int tier = 0; tier < 3; tier++)
        {
            float bottom = 0.18f + tier * 0.19f;
            float top = bottom + 0.42f;
            float radius = 0.28f - tier * 0.045f;
            int tip = vertices.Count;
            vertices.Add(new Vector3(0f, top, 0f));
            int ring = vertices.Count;
            for (int side = 0; side < sides; side++)
            {
                float angle = side / (float)sides * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Cos(angle) * radius, bottom, Mathf.Sin(angle) * radius));
            }
            for (int side = 0; side < sides; side++)
            {
                triangles.Add(tip);
                triangles.Add(ring + (side + 1) % sides);
                triangles.Add(ring + side);
            }
        }
        Mesh mesh = new() { name = "Shared Stacked Pine" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BuildMountainMesh()
    {
        Vector3[] vertices =
        {
            new(-0.5f, 0f, -0.5f), new(0.5f, 0f, -0.5f), new(0.5f, 0f, 0.5f), new(-0.5f, 0f, 0.5f),
            new(0f, 1f, 0f),
        };
        int[] triangles = { 0, 4, 1, 1, 4, 2, 2, 4, 3, 3, 4, 0, 0, 1, 2, 0, 2, 3 };
        Mesh mesh = new() { name = "Shared Snow Mountain" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BuildStarDomeMesh()
    {
        const int starCount = 180;
        const float radius = 185f;
        System.Random random = new(73019);
        Vector3[] vertices = new Vector3[starCount * 4];
        int[] triangles = new int[starCount * 6];
        for (int index = 0; index < starCount; index++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float y = Mathf.Lerp(0.12f, 0.96f, (float)random.NextDouble());
            float horizontal = Mathf.Sqrt(1f - y * y);
            Vector3 direction = new(Mathf.Cos(angle) * horizontal, y, Mathf.Sin(angle) * horizontal);
            Vector3 center = direction * radius;
            Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
            if (right.sqrMagnitude < 0.001f)
                right = Vector3.right;
            Vector3 up = Vector3.Cross(direction, right).normalized;
            float size = Mathf.Lerp(0.18f, 0.62f, (float)random.NextDouble());
            int vertex = index * 4;
            vertices[vertex] = center - right * size - up * size;
            vertices[vertex + 1] = center - right * size + up * size;
            vertices[vertex + 2] = center + right * size + up * size;
            vertices[vertex + 3] = center + right * size - up * size;
            int triangle = index * 6;
            triangles[triangle] = vertex;
            triangles[triangle + 1] = vertex + 1;
            triangles[triangle + 2] = vertex + 2;
            triangles[triangle + 3] = vertex;
            triangles[triangle + 4] = vertex + 2;
            triangles[triangle + 5] = vertex + 3;
        }
        Mesh mesh = new() { name = "Rebuilt Star Dome" };
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void OnDestroy()
    {
        foreach (Material material in runtimeMaterials)
        {
            if (material != null)
            {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
        }
        if (pineMesh != null)
        {
            if (Application.isPlaying) Destroy(pineMesh);
            else DestroyImmediate(pineMesh);
        }
        if (mountainMesh != null)
        {
            if (Application.isPlaying) Destroy(mountainMesh);
            else DestroyImmediate(mountainMesh);
        }
    }
}
