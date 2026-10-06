using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Mush.Quest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Bakes supplied track art and rider layout into the three playable scenes.</summary>
[InitializeOnLoad]
public static class MushTrackModuleMigration
{
    private const string SourcePath = "Assets/Scenes/Track_v2.unity";
    private const string ArtRootName = "Track_v2 Art Environment V4";
    private const string LayoutMarker = "Ride Art Layout V3";
    private const string VisualMarkerPrefix = "Ride Art Content V4 ";
    private const string AuthoredSledName = "Mush_Sledge";
    private const string ModulePath = "Assets/Art/Prefabs/Track_Module/";
    private static readonly string[] ScenePaths = { SourcePath, "Assets/Scenes/Tree.unity", "Assets/Scenes/SharpCurve.unity" };
    private static readonly string[] ModuleNames =
    {
        "Tree01", "Tree02", "Rock01", "Rock02", "Rock03", "Prop_House",
        "Street_Lantern", "Track_Mountain01", "Track_Mountain02"
    };

    static MushTrackModuleMigration()
    {
        EditorApplication.delayCall += UpgradeScenes;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += UpgradeScenes;
        };
        EditorSceneManager.sceneSaved += scene =>
        {
            if (scene.path == SourcePath) EditorApplication.delayCall += UpgradeScenes;
        };
    }

    private static void UpgradeScenes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        if (EditorApplication.isUpdating) { EditorApplication.delayCall += UpgradeScenes; return; }
        Scene previous = SceneManager.GetActiveScene();
        Scene source = EditorSceneManager.GetSceneByPath(SourcePath);
        bool openedSource = !source.IsValid() || !source.isLoaded;
        try
        {
            if (openedSource) source = EditorSceneManager.OpenScene(SourcePath, OpenSceneMode.Additive);
            MushTrackAuthoring sourceAuthoring = FindInScene<MushTrackAuthoring>(source);
            MushMapRideBootstrap sourceRide = FindInScene<MushMapRideBootstrap>(source);
            if (sourceAuthoring == null || sourceRide == null || sourceRide.RideCamera == null) return;
            // The scene's current artist-authored camera pose is authoritative.
            Vector3 viewPosition = sourceRide.RideCamera.transform.localPosition;
            Quaternion viewRotation = sourceRide.RideCamera.transform.localRotation;
            Dictionary<string, Vector3> scales = ReadAuthoredScales(source);

            foreach (string path in ScenePaths)
            {
                Scene scene = EditorSceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                try
                {
                    if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    MushTrackAuthoring authoring = FindInScene<MushTrackAuthoring>(scene);
                    MushMapRideBootstrap ride = FindInScene<MushMapRideBootstrap>(scene);
                    Transform map = authoring != null ? authoring.ResolveMapRoot() : null;
                    if (map == null || ride == null) continue;
                    bool changed = false;
                    Transform generated = map.Find(MushCurvedMapRuntime.GeneratedWorldRootName);
                    if (path != SourcePath && generated != null && generated.Find(ArtRootName) == null)
                    {
                        Transform previousArt = generated.Find("Track_v2 Art Environment V3");
                        if (previousArt != null) Object.DestroyImmediate(previousArt.gameObject);
                        CopyModuleSettings(sourceAuthoring, authoring);
                        RemovePrototypeScenery(generated);
                        MushTrackEditorWorldPreview.EnsureEditableMapReady(authoring, false);
                        BuildArtEnvironment(generated, map.GetComponent<MushCurvedMapRuntime>(), scales);
                        UpgradeDogModels(map);
                        InstallArtSnow(map, ride);
                        changed = true;
                    }
                    Transform marker = map.Find(LayoutMarker);
                    bool installHands = marker == null;
                    bool layoutChanged = marker == null ||
                        (marker.localPosition - viewPosition).sqrMagnitude > 0.000001f ||
                        Quaternion.Angle(marker.localRotation, viewRotation) > 0.01f;
                    if (layoutChanged)
                    {
                        ride.ApplySceneRideLayout(viewPosition, viewRotation);
                        if (installHands) InstallSceneHands(ride, map);
                        ride.BakeRideTeamIntoScene();
                        if (marker == null)
                        {
                            marker = new GameObject(LayoutMarker).transform;
                            marker.SetParent(map, false);
                        }
                        marker.SetLocalPositionAndRotation(viewPosition, viewRotation);
                        MushRideParticleGate.Install(ride);
                        changed = true;
                    }
                    if (path != SourcePath && SynchronizeRideVisuals(sourceRide, ride, map))
                        changed = true;
                    if (RemoveLegacySceneHands(ride, map))
                        changed = true;
                    if (changed)
                    {
                        MushSceneAuthoringMigration.Persist(scene);
                        EditorSceneManager.MarkSceneDirty(scene);
                        EditorSceneManager.SaveScene(scene);
                        Debug.Log($"[Mush] Saved Track_v2 art, sled, camera, hands, collar reins and ride VFX in {scene.name}.");
                    }
                }
                finally
                {
                    if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
        finally
        {
            if (openedSource && source.IsValid() && source.isLoaded) EditorSceneManager.CloseScene(source, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T component = root.GetComponentInChildren<T>(true);
            if (component != null) return component;
        }
        return null;
    }

    private static Transform ReadRideTransform(SerializedObject settings, string property)
    {
        return settings.FindProperty(property).objectReferenceValue as Transform;
    }

    private static bool RemoveLegacySceneHands(MushMapRideBootstrap ride, Transform map)
    {
        var settings = new SerializedObject(ride);
        Transform leftGrip = ReadRideTransform(settings, "leftGrip");
        Transform rightGrip = ReadRideTransform(settings, "rightGrip");
        Transform leftHand = ReadRideTransform(settings, "leftMitten");
        Transform rightHand = ReadRideTransform(settings, "rightMitten");
        // Older scenes contain glove meshes whose m_Father still points at an empty
        // glove root, even though that root's saved m_Children list is empty.
        // Inspect scene transforms and their parents instead of relying on child enumeration.
        Transform[] sceneTransforms = Object.FindObjectsByType<Transform>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        bool changed = false;
        foreach (Transform root in sceneTransforms)
        {
            if (root == null || root.gameObject.scene != map.gameObject.scene) continue;
            bool legacyLeft = root.name == "Desktop Left Winter Glove" && root.parent == leftGrip;
            bool legacyRight = root.name == "Desktop Right Winter Glove" && root.parent == rightGrip;
            if (!legacyLeft && !legacyRight) continue;
            if (root == leftHand || root == rightHand ||
                (leftHand != null && leftHand.IsChildOf(root)) ||
                (rightHand != null && rightHand.IsChildOf(root))) continue;

            var parts = new List<GameObject>();
            foreach (Transform candidate in sceneTransforms)
            {
                if (candidate == null || candidate == root || candidate.gameObject.scene != map.gameObject.scene)
                    continue;
                for (Transform parent = candidate.parent; parent != null; parent = parent.parent)
                    if (parent == root)
                    {
                        parts.Add(candidate.gameObject);
                        break;
                    }
            }
            foreach (GameObject part in parts)
                if (part != null) Object.DestroyImmediate(part);
            Object.DestroyImmediate(root.gameObject);
            changed = true;
        }
        return changed;
    }

    private static bool SynchronizeRideVisuals(MushMapRideBootstrap source, MushMapRideBootstrap target,
        Transform map)
    {
        var sourceSettings = new SerializedObject(source);
        var targetSettings = new SerializedObject(target);
        Transform sourceSeat = ReadRideTransform(sourceSettings, "rideSeatAnchor");
        Transform targetSeat = ReadRideTransform(targetSettings, "rideSeatAnchor");
        Transform sourceSled = ReadRideTransform(sourceSettings, "sledHolder");
        Transform targetSled = ReadRideTransform(targetSettings, "sledHolder");
        Transform sourceArt = sourceSeat != null ? sourceSeat.Find(AuthoredSledName) : null;
        if (sourceSeat == null || targetSeat == null || sourceSled == null || targetSled == null ||
            source.RideCamera == null || target.RideCamera == null) return false;
        foreach (string side in new[] { "left", "right" })
            if (ReadRideTransform(sourceSettings, side + "Grip") == null ||
                ReadRideTransform(targetSettings, side + "Grip") == null ||
                ReadRideTransform(sourceSettings, side + "Mitten") == null) return false;

        // A camera-pose marker cannot detect FOV, hand edits or an artist-added sled.
        // Stamp the source content, so later edits in a target scene survive reloads and Play.
        string revision = BuildVisualRevision(source, sourceSettings, sourceArt);
        Transform marker = null;
        foreach (Transform child in map)
            if (child.name.StartsWith(VisualMarkerPrefix, System.StringComparison.Ordinal))
            {
                marker = child;
                break;
            }
        if (marker != null && marker.name == VisualMarkerPrefix + revision) return false;

        target.RideCamera.CopyFrom(source.RideCamera);
        target.RideCamera.enabled = source.RideCamera.enabled;
        target.RideCamera.gameObject.SetActive(source.RideCamera.gameObject.activeSelf);
        target.RideCamera.transform.localScale = source.RideCamera.transform.localScale;
        target.ApplySceneRideLayout(source.RideCamera.transform.localPosition,
            source.RideCamera.transform.localRotation);
        targetSettings.FindProperty("cameraPosition").vector3Value = source.RideCamera.transform.localPosition;
        targetSettings.FindProperty("normalFieldOfView").floatValue = source.RideCamera.fieldOfView;
        targetSettings.FindProperty("sledPrefab").objectReferenceValue =
            sourceSettings.FindProperty("sledPrefab").objectReferenceValue;
        targetSettings.FindProperty("sledScale").floatValue = sourceSettings.FindProperty("sledScale").floatValue;
        CopyLocalPose(sourceSled, targetSled);
        targetSled.gameObject.SetActive(sourceSled.gameObject.activeSelf);
        if (sourceArt != null) CopyVisual(sourceArt, targetSeat, targetSeat.Find(AuthoredSledName));

        foreach (string side in new[] { "left", "right" })
        {
            Transform sourceGrip = ReadRideTransform(sourceSettings, side + "Grip");
            Transform targetGrip = ReadRideTransform(targetSettings, side + "Grip");
            CopyLocalPose(sourceGrip, targetGrip);
            Transform hand = CopyVisual(ReadRideTransform(sourceSettings, side + "Mitten"), targetGrip,
                ReadRideTransform(targetSettings, side + "Mitten"));
            targetSettings.FindProperty(side + "Mitten").objectReferenceValue = hand;
        }
        targetSettings.ApplyModifiedPropertiesWithoutUndo();
        target.BakeRideTeamIntoScene(); // Rebind hand and rein references after copying the authored visuals.
        if (marker == null)
        {
            marker = new GameObject().transform;
            marker.SetParent(map, false);
        }
        marker.name = VisualMarkerPrefix + revision;
        return true;
    }

    private static void CopyLocalPose(Transform source, Transform target)
    {
        target.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
        target.localScale = source.localScale;
        if (PrefabUtility.IsPartOfPrefabInstance(target))
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }

    private static Transform CopyVisual(Transform source, Transform parent, Transform existing)
    {
        GameObject prefab = PrefabUtility.GetCorrespondingObjectFromOriginalSource(source.gameObject);
        if (existing == null || prefab != PrefabUtility.GetCorrespondingObjectFromOriginalSource(existing.gameObject))
        {
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
            GameObject instance = prefab != null
                ? (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene)
                : Object.Instantiate(source.gameObject);
            existing = instance.transform;
            existing.SetParent(parent, false);
        }
        CopyVisualHierarchy(source, existing);
        return existing;
    }

    private static void CopyVisualHierarchy(Transform source, Transform target)
    {
        target.name = source.name;
        target.gameObject.SetActive(source.gameObject.activeSelf);
        CopyLocalPose(source, target);
        Renderer sourceRenderer = source.GetComponent<Renderer>();
        Renderer targetRenderer = target.GetComponent<Renderer>();
        if (sourceRenderer != null && targetRenderer != null)
        {
            targetRenderer.enabled = sourceRenderer.enabled;
            targetRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
            targetRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            targetRenderer.receiveShadows = sourceRenderer.receiveShadows;
            if (PrefabUtility.IsPartOfPrefabInstance(targetRenderer))
                PrefabUtility.RecordPrefabInstancePropertyModifications(targetRenderer);
        }
        MeshFilter sourceMesh = source.GetComponent<MeshFilter>();
        MeshFilter targetMesh = target.GetComponent<MeshFilter>();
        if (sourceMesh != null && targetMesh != null)
        {
            targetMesh.sharedMesh = sourceMesh.sharedMesh;
            if (PrefabUtility.IsPartOfPrefabInstance(targetMesh))
                PrefabUtility.RecordPrefabInstancePropertyModifications(targetMesh);
        }
        foreach (Transform child in source)
        {
            Transform targetChild = target.Find(child.name);
            if (targetChild == null)
            {
                GameObject added = Object.Instantiate(child.gameObject, target, false);
                targetChild = added.transform;
            }
            CopyVisualHierarchy(child, targetChild);
        }
        if (PrefabUtility.IsPartOfPrefabInstance(target.gameObject))
            PrefabUtility.RecordPrefabInstancePropertyModifications(target.gameObject);
    }

    private static string BuildVisualRevision(MushMapRideBootstrap source, SerializedObject settings,
        Transform sledArt)
    {
        var signature = new StringBuilder();
        // Scene object instance IDs change when an additive scene is reopened; they are not content.
        signature.Append(Regex.Replace(EditorJsonUtility.ToJson(source.RideCamera),
            @"""instanceID""\s*:\s*-?\d+", "\"instanceID\":0"));
        AppendAssetSignature(signature, source.RideCamera.targetTexture);
        AppendVisualSignature(signature, source.RideCamera.transform, false);
        AppendVisualSignature(signature, ReadRideTransform(settings, "sledHolder"), false);
        AppendVisualSignature(signature, sledArt, true);
        foreach (string side in new[] { "left", "right" })
        {
            AppendVisualSignature(signature, ReadRideTransform(settings, side + "Grip"), false);
            AppendVisualSignature(signature, ReadRideTransform(settings, side + "Mitten"), true);
        }
        using SHA256 hash = SHA256.Create();
        return System.BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(signature.ToString())))
            .Replace("-", string.Empty).ToLowerInvariant();
    }

    private static void AppendVisualSignature(StringBuilder signature, Transform root, bool includeChildren)
    {
        if (root == null) { signature.Append("missing;"); return; }
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        signature.Append(root.name).Append('|').Append(root.gameObject.activeSelf).Append('|')
            .Append(root.localPosition.ToString("R", culture)).Append('|')
            .Append(root.localRotation.ToString("R", culture)).Append('|')
            .Append(root.localScale.ToString("R", culture)).Append(';');
        GameObject prefab = PrefabUtility.GetCorrespondingObjectFromOriginalSource(root.gameObject);
        AppendAssetSignature(signature, prefab);
        if (prefab != null)
            signature.Append(AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(prefab)));
        Renderer renderer = root.GetComponent<Renderer>();
        if (renderer != null)
        {
            signature.Append(renderer.enabled).Append('|').Append(renderer.shadowCastingMode)
                .Append('|').Append(renderer.receiveShadows);
            foreach (Material material in renderer.sharedMaterials) AppendAssetSignature(signature, material);
        }
        MeshFilter mesh = root.GetComponent<MeshFilter>();
        if (mesh != null) AppendAssetSignature(signature, mesh.sharedMesh);
        if (includeChildren)
            foreach (Transform child in root) AppendVisualSignature(signature, child, true);
        signature.Append(';');
    }

    private static void AppendAssetSignature(StringBuilder signature, Object asset)
    {
        if (asset != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long id))
            signature.Append(guid).Append(':').Append(id);
        signature.Append(';');
    }

    private static Dictionary<string, Vector3> ReadAuthoredScales(Scene source)
    {
        var result = new Dictionary<string, Vector3>();
        foreach (GameObject root in source.GetRootGameObjects())
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                foreach (string name in ModuleNames)
                {
                    if (result.ContainsKey(name) || child.name != name) continue;
                    if (PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) != null)
                        result[name] = child.lossyScale;
                }
        return result;
    }

    private static void CopyModuleSettings(MushTrackAuthoring source, MushTrackAuthoring target)
    {
        var settings = new SerializedObject(target);
        settings.FindProperty("deformableRoadModule").objectReferenceValue = source.DeformableRoadModule;
        settings.FindProperty("useDeformableRoadModule").boolValue = true;
        settings.FindProperty("roadMaterialOverride").objectReferenceValue = source.RoadMaterialOverride;
        settings.FindProperty("terrainMaterialOverride").objectReferenceValue = source.TerrainMaterialOverride;
        settings.FindProperty("roadTextureOverride").objectReferenceValue = source.RoadTextureOverride;
        settings.FindProperty("terrainTextureOverride").objectReferenceValue = source.TerrainTextureOverride;
        settings.FindProperty("generateProceduralEnvironment").boolValue = false;
        settings.FindProperty("overrideTrackWidths").boolValue = true;
        settings.FindProperty("roadHalfWidth").floatValue = source.PreviewRoadHalfWidth;
        settings.FindProperty("terrainHalfWidth").floatValue = source.TerrainHalfWidth;
        settings.FindProperty("trackEdgeObjectPrefab").objectReferenceValue = source.TrackEdgeObjectPrefab;
        settings.FindProperty("generateTrackEdgeObjects").boolValue = true;
        settings.FindProperty("trackEdgeObjectSpacing").floatValue = source.TrackEdgeObjectSpacing;
        settings.FindProperty("trackEdgeOutsideOffset").floatValue = source.TrackEdgeOutsideOffset;
        settings.FindProperty("trackEdgeVerticalOffset").floatValue = source.TrackEdgeVerticalOffset;
        settings.FindProperty("trackEdgeRotationOffset").vector3Value = source.TrackEdgeRotationOffset;
        settings.FindProperty("trackEdgeScaleMultiplier").floatValue = source.TrackEdgeScaleMultiplier;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void RemovePrototypeScenery(Transform generated)
    {
        for (int index = generated.childCount - 1; index >= 0; index--)
        {
            Transform child = generated.GetChild(index);
            if (child.name == "Visible Snow Pine" || child.name == "Distant Snow Mountain" ||
                child.name == "Visible Route Beacon") Object.DestroyImmediate(child.gameObject);
        }
    }

    private static void BuildArtEnvironment(Transform generated, MushCurvedMapRuntime runtime,
        Dictionary<string, Vector3> scales)
    {
        Transform art = new GameObject(ArtRootName).transform;
        art.SetParent(generated, false);
        float length = runtime.LengthMeters;
        for (float distance = 12f; distance < length - 12f; distance += 18f)
            for (int side = -1; side <= 1; side += 2)
            {
                if (Mathf.Abs(Mathf.Repeat(distance - 70f + 80f, 160f) - 80f) < 18f) continue;
                int index = Mathf.RoundToInt(distance / 18f);
                PlaceModule(index % 2 == 0 ? "Tree01" : "Tree02", art, runtime, scales,
                    distance, side, 9f + index % 3 * 6f);
            }
        for (float distance = 26f; distance < length - 16f; distance += 44f)
            PlaceModule("Rock0" + (1 + Mathf.RoundToInt(distance / 44f) % 3), art, runtime, scales,
                distance, Mathf.RoundToInt(distance / 44f) % 2 == 0 ? -1 : 1, 6f);
        for (float distance = 20f; distance < length - 10f; distance += 36f)
            for (int side = -1; side <= 1; side += 2)
                PlaceModule("Street_Lantern", art, runtime, scales, distance, side, 4.5f);
        for (float distance = 70f; distance < length - 35f; distance += 160f)
            PlaceModule("Prop_House", art, runtime, scales, distance,
                Mathf.RoundToInt(distance / 160f) % 2 == 0 ? -1 : 1, 20f);
        for (float distance = 90f; distance < length; distance += 180f)
            PlaceModule("Track_Mountain0" + (1 + Mathf.FloorToInt(distance / 180f) % 2), art, runtime,
                scales, distance, Mathf.FloorToInt(distance / 180f) % 2 == 0 ? -1 : 1, 85f);
    }

    private static void PlaceModule(string name, Transform parent, MushCurvedMapRuntime runtime,
        Dictionary<string, Vector3> scales, float distance, int side, float outside)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModulePath + name + ".prefab");
        if (prefab == null || !runtime.TryGetRoutePose(distance / runtime.LengthMeters,
            out Vector3 center, out _, out Vector3 forward)) return;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
        Transform item = instance.transform;
        item.SetParent(parent, false);
        item.localScale = scales.TryGetValue(name, out Vector3 scale) ? scale : prefab.transform.localScale;
        item.SetPositionAndRotation(center + right * side * (runtime.RoadHalfWidthMeters + outside),
            Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, Vector3.up), Vector3.up) * prefab.transform.localRotation);
        Bounds bounds = new();
        bool found = false;
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(false))
        {
            if (renderer is ParticleSystemRenderer || renderer is LineRenderer || !renderer.enabled) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        if (found && runtime.TryGetCourseSurface(item.position, out Vector3 ground, out _, out _, out float lateral))
        {
            float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
            item.position = center + right * side * (runtime.RoadHalfWidthMeters + outside + radius);
            runtime.TryGetCourseSurface(item.position, out ground, out _, out _, out lateral);
            if (Mathf.Abs(lateral) < runtime.RoadHalfWidthMeters + radius + 0.8f)
            {
                Object.DestroyImmediate(instance);
                return;
            }
            item.position += Vector3.up * (ground.y - bounds.min.y);
        }
        foreach (ParticleSystem particles in instance.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = particles.main;
            main.maxParticles = Mathf.Min(main.maxParticles, 64);
        }
    }

    private static void UpgradeDogModels(Transform map)
    {
        foreach (MushRideDog dog in map.GetComponentsInChildren<MushRideDog>(true))
        {
            Transform visual = dog.Visual;
            if (visual == null) continue;
            string name = dog.UseMalamuteAccessories ? "LUMI_Model" : "KAI_Model";
            if (visual.childCount == 1 && visual.GetChild(0).name == name) continue;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Prefabs/Dog/" + name + ".prefab");
            if (prefab == null) continue;
            for (int index = visual.childCount - 1; index >= 0; index--)
                Object.DestroyImmediate(visual.GetChild(index).gameObject);
            visual.SetLocalPositionAndRotation(new Vector3(0f, -0.041688144f, -0.10464156f), Quaternion.identity);
            visual.localScale = Vector3.one * 1.0422033f;
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, visual.gameObject.scene);
            model.transform.SetParent(visual, false);
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        }
    }

    private static void InstallArtSnow(Transform map, MushMapRideBootstrap ride)
    {
        Transform camera = ride.RideCamera != null ? ride.RideCamera.transform : null;
        if (camera == null || camera.Find("TrackVFX_Snow") != null) return;
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Prefabs/VFX/TrackVFX_Snow.prefab");
        if (prefab == null) return;
        GameObject snow = (GameObject)PrefabUtility.InstantiatePrefab(prefab, map.gameObject.scene);
        snow.transform.SetParent(camera, false);
        snow.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    private static void InstallSceneHands(MushMapRideBootstrap ride, Transform map)
    {
        var settings = new SerializedObject(ride);
        foreach (Transform grip in map.GetComponentsInChildren<Transform>(true))
        {
            if (grip == null) continue;
            bool left = grip.name == "Left Rein Grip";
            if (!left && grip.name != "Right Rein Grip") continue;
            string name = left ? "Left Winter Mitten" : "Right Winter Mitten";
            SerializedProperty handSetting = settings.FindProperty(left ? "leftMitten" : "rightMitten");
            Transform hand = handSetting.objectReferenceValue as Transform;
            if (hand == null) hand = grip.Find(name);
            if (hand == null)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Mush/Resources/" + (left ? "MushLeftHand" : "MushRightHand") + ".prefab");
                if (prefab == null) continue;
                GameObject created = (GameObject)PrefabUtility.InstantiatePrefab(prefab, grip.gameObject.scene);
                created.name = name;
                hand = created.transform;
                hand.SetParent(grip, false);
                hand.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                MushPlayerHands.FitRideHand(hand);
            }
            handSetting.objectReferenceValue = hand;
        }
        settings.ApplyModifiedPropertiesWithoutUndo();
    }
}
