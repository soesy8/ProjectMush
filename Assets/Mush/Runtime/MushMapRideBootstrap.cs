using System;
using System.Collections.Generic;
using Mush.Customization;
using Mush.Prototype;
using Mush.Quest;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

/// <summary>
/// Builds a playable desktop sled team at a V2 map's SPAWN_Sled marker.
/// The scene only stores model references; all reins, camera anchors and
/// controller connections are created consistently when play mode starts.
/// </summary>
[DisallowMultipleComponent]
public sealed class MushMapRideBootstrap : MonoBehaviour
{
    [Header("Ride Models")]
    [SerializeField] private GameObject sledPrefab;
    [SerializeField] private GameObject leftDogPrefab;
    [SerializeField] private GameObject rightDogPrefab;
    [SerializeField] private string spawnMarkerName = "SPAWN_Sled";

    [Header("Team Layout")]
    [SerializeField] private float sledScale = 1.18f;
    [SerializeField] private float dogScale = 0.62f;
    [SerializeField, Min(0.5f)] private float targetDogHeight = 1.30f;
    [SerializeField] private Vector3 leftDogPosition = new(-0.72f, 0f, 4.15f);
    [SerializeField] private Vector3 rightDogPosition = new(0.72f, 0f, 4.15f);
    [SerializeField] private Vector3 cameraPosition = new(0f, 1.36f, -1.78f);
    [SerializeField] private Vector3 cameraLookTarget = new(0f, 1.36f, 8f);

    [Header("Presentation")]
    [SerializeField] private bool showKeyboardHelp = true;
    [SerializeField, Range(70f, 100f)] private float normalFieldOfView = 82f;
    [SerializeField, Range(75f, 110f)] private float boostFieldOfView = 90f;
    [SerializeField, Min(1f)] private float vrControlHintSeconds = 5f;

    [Header("Course Completion")]
    [SerializeField] private string lobbySceneName = "MushLobby";
    [SerializeField, Min(1f)] private float finishDistanceTolerance = 11f;

    [Header("Delivery Mission")]
    [SerializeField, Min(10f)] private float deliveryTimeLimitSeconds = 120f;
    [SerializeField, Range(0.1f, 1f)] private float threeStarTimeRatio = 0.80f;
    [SerializeField, Min(0.1f)] private float resultStarFlightSeconds = 0.62f;
    [SerializeField, Min(0f)] private float resultStarIntervalSeconds = 0.42f;

    [Header("Off-course Speed")]
    [SerializeField, Range(0.05f, 1f)] private float offCourseImpactRetainedSpeed = 0.5f;
    [SerializeField, Range(0.1f, 1f)] private float offCourseAccelerationMultiplier = 0.72f;
    [SerializeField, Min(0f)] private float sharpCurveOffCourseTimePenalty = 4f;
    [SerializeField, Min(0f)] private float roadExitMargin = 0.12f;
    [SerializeField, Min(0f)] private float roadReturnInset = 0.45f;

    [Header("Course Recovery")]
    [SerializeField, Min(0.1f)] private float recoveryCheckpointInterval = 0.30f;
    [SerializeField, Min(0.1f)] private float recoveryRoadInset = 1.50f;
    [SerializeField, Min(0.1f)] private float recoveryGroundTolerance = 0.65f;
    [SerializeField, Min(0f)] private float recoveryRollbackMeters = 8f;

    [Header("Quest 2 Reins")]
    [SerializeField, Min(0.05f)] private float questReinPullForFullTurn = 0.24f;
    [SerializeField, Min(0f)] private float questReinDeadZone = 0.035f;
    [SerializeField, Min(0.3f)] private float questRecalibrationHoldSeconds = 1.0f;

    [Header("Quest 2 Haptics")]
    [SerializeField] private bool enableQuestHaptics = true;
    [SerializeField, Range(0f, 1f)] private float roadHapticAmplitude = 0.075f;
    [SerializeField, Range(0f, 1f)] private float offCourseHapticAmplitude = 0.16f;
    [SerializeField, Range(0f, 1f)] private float reinTensionHapticAmplitude = 0.30f;
    [SerializeField, Min(0.03f)] private float drivingHapticInterval = 0.075f;

    private readonly List<DogRuntime> dogs = new();
    private readonly Dictionary<string, Material> runtimeMaterials = new(StringComparer.Ordinal);
    private MushSledKeyboardController rideController;
    private MushCurvedMapRuntime curvedWorld;
    private MushQuestTrackedInputRig questRig;
    private Camera rideCamera;
    private Transform rideSeatAnchor;
    private Transform sledHolder;
    private Vector3 cameraBaseLocalPosition;
    private Quaternion cameraRestLocalRotation;
    private ParticleSystem speedParticles;
    private MushSnowfieldBlizzardController snowController;
    private GUIStyle helpStyle;
    private GUIStyle lobbyButtonStyle;
    private GameObject missionTimerRoot;
    private TextMesh missionTimerText;
    private GameObject vrControlHintRoot;
    private GameObject resultPanel;
    private GameObject resultButtonsRoot;
    private Mesh resultStarMesh;
    private readonly Transform[] resultFilledStars = new Transform[3];
    private readonly ParticleSystem[] resultStarBursts = new ParticleSystem[3];
    private readonly bool[] resultStarLanded = new bool[3];
    private bool built;
    private Transform rideTeam;
    private Transform finishMarker;
    private Vector3 lastRidePosition;
    private float travelledCourseDistance;
    private float courseLengthMeters = 960f;
    private bool ridePositionInitialized;
    private bool returningToLobby;
    private bool offCourse;
    private float sharpOffCoursePenaltyFeedbackUntil;
    private bool ridePaused;
    private bool questReinsCalibrated;
    private float questLeftNeutralZ;
    private float questRightNeutralZ;
    private float questRecalibrationHoldTime;
    private float questRecalibrationFeedbackUntil;
    private bool questRecalibrationArmed;
    private bool questRecalibrationInProgress;
    private bool questBoostHapticHeld;
    private bool sharpDownhillHapticActive;
    private float nextQuestDrivingHapticTime;
    private float questDrivingHapticSuppressedUntil;
    private bool hasRecoveryCheckpoint;
    private float recoveryRouteProgress;
    private float nextRecoveryCheckpointTime;
    private Vector3 recoveryFallbackPosition;
    private Vector3 recoveryFallbackForward;
    private GameObject questPauseMenu;
    private MushCustomizationState customization;
    private bool missionTimerStarted;
    private float missionElapsedSeconds;
    private bool resultVisible;
    private int earnedStars;
    private float resultSequenceElapsed;
    private bool resultButtonsShown;

    private sealed class DogRuntime
    {
        public Transform holder;
        public Transform visual;
        public Vector3 restLocalPosition;
        public Vector3 restHolderLocalPosition;
        public Quaternion restHolderLocalRotation;
        public Quaternion forwardLocalRotation;
        public Transform[] legPivots;
        public Quaternion[] legRestRotations;
        public float gaitPhase;
        public float gaitClock;
    }

    private void Start()
    {
        BuildRideTeam();
    }

    private void BuildRideTeam()
    {
        if (built)
            return;

        Debug.Log(
            $"[Mush] Runtime verification DOG_AXIS_V3: Project={Application.dataPath}, Scene={gameObject.scene.path}",
            this);

        Transform mapRoot = FindMapRoot();
        if (mapRoot == null)
        {
            Debug.LogError("[Mush] V2 map root was not found; ride team was not created.", this);
            return;
        }

        curvedWorld = MushCurvedMapRuntime.EnsureBuilt(mapRoot);

        Transform spawn = FindDeepChild(mapRoot, spawnMarkerName);
        Transform finish = FindDeepChild(mapRoot, "FINISH_Delivery");
        finishMarker = finish;
        if (curvedWorld != null)
            courseLengthMeters = curvedWorld.LengthMeters;
        Vector3 forward = curvedWorld != null ? curvedWorld.StartForward :
            spawn != null ? spawn.forward : mapRoot.forward;
        if (curvedWorld == null && spawn != null && finish != null)
        {
            Vector3 routeDirection = Vector3.ProjectOnPlane(finish.position - spawn.position, Vector3.up);
            if (routeDirection.sqrMagnitude > 1f)
                forward = routeDirection.normalized;
        }
        forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;

        Vector3 spawnPosition = spawn != null ? spawn.position : mapRoot.position;
        spawnPosition = SnapPointToGround(spawnPosition);
        ImproveMapReadability(mapRoot);

        GameObject teamObject = new("Mush Ride Team");
        teamObject.transform.SetPositionAndRotation(
            spawnPosition + Vector3.up * 0.06f,
            Quaternion.LookRotation(forward, Vector3.up));
        rideTeam = teamObject.transform;
        lastRidePosition = rideTeam.position;
        ridePositionInitialized = true;

        customization = MushCustomizationSave.Load();
        // The sled, player camera and both hands must share one slope pivot.
        // Rotating only the sled made the rider appear to hang behind it on a
        // descent even though the movement root itself was correctly grounded.
        rideSeatAnchor = CreateAnchor("Ride Seat Slope Pivot", teamObject.transform, Vector3.zero);
        BuildSled(rideSeatAnchor);
        Vector3 visibleLeftDogPosition = leftDogPosition;
        Vector3 visibleRightDogPosition = rightDogPosition;
        visibleLeftDogPosition.z = Mathf.Min(visibleLeftDogPosition.z, 3.55f);
        visibleRightDogPosition.z = Mathf.Min(visibleRightDogPosition.z, 3.55f);
        DogRuntime leftDog = BuildDog("Left Husky", leftDogPrefab, teamObject.transform,
            visibleLeftDogPosition, false, 0f);
        DogRuntime rightDog = BuildDog("Right Malamute", rightDogPrefab, teamObject.transform,
            visibleRightDogPosition, true, Mathf.PI);
        dogs.Add(leftDog);
        dogs.Add(rightDog);

        Transform leftGrip = CreateAnchor("Left Rein Grip", rideSeatAnchor, new Vector3(-0.48f, 1.08f, -1.06f));
        Transform rightGrip = CreateAnchor("Right Rein Grip", rideSeatAnchor, new Vector3(0.48f, 1.08f, -1.06f));
        leftGrip.localRotation = Quaternion.Euler(18f, -5f, -7f);
        rightGrip.localRotation = Quaternion.Euler(18f, 5f, 7f);
        Transform leftHarness = CreateAnchor("Left Dog Harness", leftDog.holder, new Vector3(0f, 0.58f, -0.35f));
        Transform rightHarness = CreateAnchor("Right Dog Harness", rightDog.holder, new Vector3(0f, 0.58f, -0.35f));

        Transform leftMitten = BuildMitten("Left Winter Mitten", leftGrip, -1);
        Transform rightMitten = BuildMitten("Right Winter Mitten", rightGrip, 1);
        LineRenderer leftRein = BuildRein("Left Rein", teamObject.transform);
        LineRenderer rightRein = BuildRein("Right Rein", teamObject.transform);

        MushReinsVisual reinsVisual = rideSeatAnchor.gameObject.AddComponent<MushReinsVisual>();
        reinsVisual.Configure(leftGrip, rightGrip, leftHarness, rightHarness, leftRein, rightRein);
        reinsVisual.SetHeld(false);

        rideController = teamObject.AddComponent<MushSledKeyboardController>();
        rideController.Configure(reinsVisual, leftMitten, rightMitten, null, null, false);
        rideController.SetCourseSurface(curvedWorld);
        InitializeCourseRecoveryCheckpoint();

        ConfigureRideCamera(rideSeatAnchor);
        ConfigureQuestRide(rideSeatAnchor, leftGrip, rightGrip);
        BuildSpeedParticles();
        EnsureDogTeamVisible();
        ApplyRideDogCustomization();
        ConnectMapEffects(mapRoot, teamObject.transform);
        BuildMissionTimerDisplay();

        // Procedural terrain, team models and UI have all been created now, so
        // remove their shadow passes as well as the scene's serialized ones.
        MushShadowPerformance.DisableForLoadedScenes();

        built = true;
        Debug.Log(
            $"[Mush] Ride ready. Spawn={teamObject.transform.position}, Forward={teamObject.transform.forward}, " +
            $"Finish={(finish != null ? finish.position.ToString() : "missing")}, Camera={rideCamera.transform.position}",
            teamObject);
    }

    private void Update()
    {
        if (!built || returningToLobby)
            return;

        if (resultVisible)
        {
            UpdateResultSequence();
            UpdateDesktopResultSelection();
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.zKey.wasPressedThisFrame)
            SetRidePaused(!ridePaused);
        if (questRig != null && questRig.BButtonPressedThisFrame)
            SetRidePaused(!ridePaused);
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            RecoverToCourse();

        UpdateQuestRideControls();
        UpdateMissionTimer();
    }

    private Transform FindMapRoot()
    {
        MushSnowfieldBlizzardController snow = FindFirstObjectByType<MushSnowfieldBlizzardController>();
        if (snow != null)
            return snow.transform;

        MushForestTimeCycleController forest = FindFirstObjectByType<MushForestTimeCycleController>();
        if (forest != null)
            return forest.transform;

        foreach (Transform candidate in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (candidate.name.Contains("Mush_Map_", StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }

    private void BuildSled(Transform parent)
    {
        Transform holder = CreateAnchor("Sled", parent, Vector3.zero);
        sledHolder = holder;
        MushCustomizationCatalog catalog = MushCustomizationCatalog.Load();
        GameObject selectedSled = catalog != null && customization != null
            ? catalog.GetPrefab(customization.equippedSledBody)
            : null;
        selectedSled ??= sledPrefab;
        string visualName = customization != null
            ? "Equipped " + customization.equippedSledBody + " Visual"
            : "Natural Sled Visual";
        GameObject model = InstantiateModel(selectedSled, visualName, holder, sledScale);
        if (model == null)
        {
            BuildFallbackSled(holder);
        }
        else
        {
            GroundModel(holder, model.transform);
            ApplySledMaterials(model);
            if (customization?.equippedSledBody == MushCustomizationIds.SledSanta)
            {
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = false;
            }
        }

        GameObject decoration = MushCustomizationVisuals.ApplySledDecoration(holder, customization, 1.25f);
        if (decoration != null && customization?.equippedSledDecoration == MushCustomizationIds.SledLantern)
            BuildVisibleRideLantern(decoration.transform);
        BuildSledCockpit(holder);
    }

    private DogRuntime BuildDog(
        string dogName,
        GameObject prefab,
        Transform parent,
        Vector3 localPosition,
        bool malamute,
        float phase)
    {
        Transform holder = CreateAnchor(dogName, parent, localPosition);
        GameObject model = InstantiateModel(prefab, dogName + " Visual", holder, dogScale);
        if (model == null)
            model = BuildFallbackDog(holder, malamute);

        NormalizeDogModel(holder, model.transform);
        ApplyDogMaterials(model, malamute);
        DisableModelColliders(model);

        DogRuntime dog = new DogRuntime
        {
            holder = holder,
            visual = model.transform,
            restLocalPosition = model.transform.localPosition,
            restHolderLocalPosition = holder.localPosition,
            restHolderLocalRotation = holder.localRotation,
            forwardLocalRotation = model.transform.localRotation,
            gaitPhase = phase,
        };
        BuildDogLegPivots(dog);
        return dog;
    }

    private static void BuildDogLegPivots(DogRuntime dog)
    {
        string[][] legGroups =
        {
            new[] { "_Front_L_Upper", "_Front_L_Lower", "_Front_L_Paw" },
            new[] { "_Front_R_Upper", "_Front_R_Lower", "_Front_R_Paw" },
            new[] { "_Rear_L_Thigh", "_Rear_L_Shin", "_Rear_L_Paw" },
            new[] { "_Rear_R_Thigh", "_Rear_R_Shin", "_Rear_R_Paw" },
        };

        dog.legPivots = new Transform[legGroups.Length];
        dog.legRestRotations = new Quaternion[legGroups.Length];
        for (int index = 0; index < legGroups.Length; index++)
        {
            Transform upper = FindChildContaining(dog.visual, legGroups[index][0]);
            Transform lower = FindChildContaining(dog.visual, legGroups[index][1]);
            Transform paw = FindChildContaining(dog.visual, legGroups[index][2]);
            if (upper == null || lower == null || paw == null)
                continue;

            GameObject pivotObject = new($"Sled Run Leg Pivot {index}");
            Transform pivot = pivotObject.transform;
            pivot.SetParent(dog.visual, true);

            Vector3 upperToLower = lower.position - upper.position;
            pivot.position = upper.position - upperToLower * 0.35f;
            pivot.rotation = Quaternion.LookRotation(dog.holder.forward, dog.holder.up);

            upper.SetParent(pivot, true);
            lower.SetParent(pivot, true);
            paw.SetParent(pivot, true);

            dog.legPivots[index] = pivot;
            dog.legRestRotations[index] = pivot.localRotation;
        }
    }

    private static GameObject InstantiateModel(GameObject prefab, string instanceName, Transform parent, float scale)
    {
        if (prefab == null)
            return null;

        GameObject instance = Instantiate(prefab, parent);
        instance.name = instanceName;
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one * scale;
        return instance;
    }

    private static void GroundModel(Transform relativeTo, Transform model)
    {
        Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

        float minimumY = float.PositiveInfinity;
        foreach (Renderer renderer in renderers)
        {
            Bounds bounds = renderer.bounds;
            for (int x = 0; x <= 1; x++)
            for (int z = 0; z <= 1; z++)
            {
                Vector3 corner = new(
                    x == 0 ? bounds.min.x : bounds.max.x,
                    bounds.min.y,
                    z == 0 ? bounds.min.z : bounds.max.z);
                minimumY = Mathf.Min(minimumY, relativeTo.InverseTransformPoint(corner).y);
            }
        }

        if (!float.IsInfinity(minimumY))
            model.localPosition += Vector3.up * -minimumY;
    }

    private void NormalizeDogModel(Transform holder, Transform model)
    {
        if (!TryGetLocalRendererBounds(holder, model, out Bounds bounds))
            return;

        // Derive the complete 3D basis from semantic FBX parts. The two dog
        // files can arrive with their length axis in Unity's Y direction; a
        // yaw-only nose correction then leaves them standing vertically.
        if (TryGetDogSemanticBasis(holder, model, out Vector3 semanticUp, out Vector3 semanticForward,
                out _, out _))
        {
            Quaternion importedBasis = Quaternion.LookRotation(semanticForward, semanticUp);
            Quaternion axisCorrection = Quaternion.Inverse(importedBasis);
            model.localRotation = axisCorrection * model.localRotation;
        }

        if (TryGetLocalRendererBounds(holder, model, out bounds))
        {
            // These FBXs are authored in centimetres. Depending on importer unit
            // conversion their Unity bounds can be only about 2 cm high. Scale
            // from the measured renderer height instead of trusting import scale.
            if (bounds.size.y > 0.0001f)
            {
                float heightMultiplier = DesiredDogHeight / bounds.size.y;
                model.localScale *= heightMultiplier;
                TryGetLocalRendererBounds(holder, model, out bounds);
            }

            // The FBXs contain an authored root offset. Centering the renderer
            // bounds removes that offset and places both breeds on the snow.
            model.localPosition += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        }
    }

    private static bool TryGetDogSemanticBasis(
        Transform holder,
        Transform model,
        out Vector3 semanticUp,
        out Vector3 semanticForward,
        out Vector3 nosePosition,
        out Vector3 pawPosition)
    {
        semanticUp = Vector3.up;
        semanticForward = Vector3.forward;
        nosePosition = Vector3.zero;
        pawPosition = Vector3.zero;

        Transform nose = FindChildContaining(model, "Nose");
        Transform torso = FindChildContaining(model, "Torso");
        if (nose == null || torso == null ||
            !TryAverageNamedParts(holder, model, "Paw", null, out pawPosition) ||
            !TryAverageNamedParts(holder, model, "Upper", "Thigh", out Vector3 upperLegPosition))
            return false;

        nosePosition = holder.InverseTransformPoint(nose.position);
        Vector3 torsoPosition = holder.InverseTransformPoint(torso.position);
        semanticUp = upperLegPosition - pawPosition;
        if (semanticUp.sqrMagnitude < 0.000001f)
            return false;
        semanticUp.Normalize();

        semanticForward = Vector3.ProjectOnPlane(nosePosition - torsoPosition, semanticUp);
        if (semanticForward.sqrMagnitude < 0.000001f)
            return false;
        semanticForward.Normalize();
        return true;
    }

    private static bool TryAverageNamedParts(
        Transform holder,
        Transform root,
        string firstNamePart,
        string secondNamePart,
        out Vector3 average)
    {
        average = Vector3.zero;
        int count = 0;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            bool matches = child.name.Contains(firstNamePart, StringComparison.OrdinalIgnoreCase) ||
                           (!string.IsNullOrEmpty(secondNamePart) &&
                            child.name.Contains(secondNamePart, StringComparison.OrdinalIgnoreCase));
            if (!matches)
                continue;

            average += holder.InverseTransformPoint(child.position);
            count++;
        }

        if (count == 0)
            return false;

        average /= count;
        return true;
    }

    private static bool TryGetLocalRendererBounds(Transform relativeTo, Transform root, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Bounds worldBounds = renderer.bounds;
            for (int x = 0; x <= 1; x++)
            for (int y = 0; y <= 1; y++)
            for (int z = 0; z <= 1; z++)
            {
                Vector3 worldCorner = new(
                    x == 0 ? worldBounds.min.x : worldBounds.max.x,
                    y == 0 ? worldBounds.min.y : worldBounds.max.y,
                    z == 0 ? worldBounds.min.z : worldBounds.max.z);
                Vector3 localCorner = relativeTo.InverseTransformPoint(worldCorner);
                if (!hasBounds)
                {
                    bounds = new Bounds(localCorner, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(localCorner);
                }
            }
        }

        return hasBounds;
    }

    private void ConfigureRideCamera(Transform team)
    {
        rideCamera = Camera.main;
        if (rideCamera == null)
            rideCamera = FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        if (rideCamera == null)
        {
            GameObject cameraObject = new("Main Camera");
            cameraObject.tag = "MainCamera";
            rideCamera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<UniversalAdditionalCameraData>();
        }

        MushVrRenderPerformance.ConfigureCamera(rideCamera);

        rideCamera.transform.SetParent(team, false);
        bool santaSled = customization?.equippedSledBody == MushCustomizationIds.SledSanta;
        cameraBaseLocalPosition = santaSled
            ? new Vector3(0f, 1.62f, -1.42f)
            : new Vector3(0f, Mathf.Max(cameraPosition.y, 1.46f), Mathf.Max(cameraPosition.z, -1.62f));
        Vector3 effectiveLookTarget = santaSled
            ? new Vector3(0f, 1.12f, 6.2f)
            : new Vector3(cameraLookTarget.x, Mathf.Min(cameraLookTarget.y, 1.28f), cameraLookTarget.z);
        rideCamera.transform.localPosition = cameraBaseLocalPosition;
        Vector3 lookDirection = effectiveLookTarget - cameraBaseLocalPosition;
        rideCamera.transform.localRotation = lookDirection.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(lookDirection.normalized, Vector3.up)
            : Quaternion.identity;
        cameraRestLocalRotation = rideCamera.transform.localRotation;
        rideCamera.nearClipPlane = 0.04f;
        rideCamera.farClipPlane = 1500f;
        rideCamera.fieldOfView = normalFieldOfView;
        rideCamera.clearFlags = CameraClearFlags.Skybox;
        rideCamera.cullingMask = ~0;
        rideCamera.rect = new Rect(0f, 0f, 1f, 1f);
        rideCamera.targetTexture = null;
        rideCamera.targetDisplay = 0;
        rideCamera.useOcclusionCulling = false;
        rideCamera.enabled = true;
        // URP/OpenXR가 양안 렌더링 대상을 직접 관리하므로 Camera.stereoTargetEye를 설정하지 않는다.
        // 이 프로퍼티는 Built-in Render Pipeline 전용이라 URP에서 설정하면 매 씬 시작마다 경고가 발생한다.
    }

    private void ConfigureQuestRide(Transform team, Transform leftGrip, Transform rightGrip)
    {
        if (rideCamera == null || team == null)
            return;

        questRig = team.gameObject.AddComponent<MushQuestTrackedInputRig>();
        questRig.Configure(
            rideCamera,
            team,
            cameraBaseLocalPosition,
            cameraRestLocalRotation,
            leftGrip,
            rightGrip);
    }

    private void UpdateQuestRideControls()
    {
        if (questRig == null || rideController == null || !questRig.IsTracking)
            return;

        if (ridePaused)
        {
            questRecalibrationHoldTime = 0f;
            questRecalibrationInProgress = false;
            questBoostHapticHeld = false;
            rideController.SetBoost(false);
            rideController.SetExternalSteering(0f);
            return;
        }

        if (!rideController.RideStarted && questRig.LeftGripHeld && questRig.RightGripHeld)
        {
            CaptureQuestNeutralPosition();
            questRecalibrationArmed = false; // 처음 출발에 쓴 그립을 계속 누르고 있어도 곧바로 재보정되지 않게 한다.
            rideController.StartRide();
            PulseQuestBothHands(0.34f, 0.12f);
        }

        if (!rideController.RideStarted)
            return;

        if (questRig.XButtonPressedThisFrame)
            rideController.ToggleDogBuff();
        if (questRig.YButtonPressedThisFrame)
            rideController.ToggleDogPenalty();

        bool boostHeld = questRig.LeftTriggerHeld || questRig.RightTriggerHeld;
        if (boostHeld && !questBoostHapticHeld)
            PulseQuestBothHands(0.24f, 0.08f);
        questBoostHapticHeld = boostHeld;
        rideController.SetBoost(boostHeld);
        if (!questReinsCalibrated)
            CaptureQuestNeutralPosition();

        bool bothGripsHeld = questRig.LeftGripHeld && questRig.RightGripHeld;
        if (!bothGripsHeld)
        {
            questRecalibrationHoldTime = 0f;
            questRecalibrationInProgress = false;
            questRecalibrationArmed = true; // 양손을 한 번 놓은 뒤부터 다음 동시 길게 누르기를 받을 수 있다.
        }
        else if (questRecalibrationArmed)
        {
            questRecalibrationInProgress = true;
            questRecalibrationHoldTime += Time.unscaledDeltaTime;
            rideController.SetExternalSteering(0f); // 재보정 자세를 잡는 동안 기존 기준점으로 갑자기 꺾이지 않게 직진 입력으로 둔다.
            if (questRecalibrationHoldTime >= questRecalibrationHoldSeconds)
            {
                CaptureQuestNeutralPosition();
                questRecalibrationHoldTime = 0f;
                questRecalibrationInProgress = false;
                questRecalibrationArmed = false; // 완료 뒤 양손을 놓기 전까지 중복 재보정을 막는다.
                questRecalibrationFeedbackUntil = Time.unscaledTime + 1.0f;
                PulseQuestBothHands(0.48f, 0.14f);
                Debug.Log("[Mush] Quest N Pos recalibrated.", this);
            }
            return;
        }

        float leftPull = Mathf.Max(0f, questLeftNeutralZ - questRig.LeftController.localPosition.z - questReinDeadZone);
        float rightPull = Mathf.Max(0f, questRightNeutralZ - questRig.RightController.localPosition.z - questReinDeadZone);
        float steering = Mathf.Clamp(
            (rightPull - leftPull) / Mathf.Max(0.05f, questReinPullForFullTurn),
            -1f,
            1f);
        rideController.SetExternalSteering(steering);
        UpdateQuestDrivingHaptics(leftPull, rightPull);
    }

    private void UpdateQuestDrivingHaptics(float leftPull, float rightPull)
    {
        if (!enableQuestHaptics || questRig == null || !questRig.IsTracking ||
            !rideController.RideStarted || Time.unscaledTime < nextQuestDrivingHapticTime ||
            Time.unscaledTime < questDrivingHapticSuppressedUntil)
            return;

        float interval = offCourse
            ? Mathf.Max(0.04f, drivingHapticInterval * 0.72f)
            : Mathf.Max(0.04f, drivingHapticInterval);
        nextQuestDrivingHapticTime = Time.unscaledTime + interval;

        float speed01 = Mathf.InverseLerp(0f, rideController.SecondLevelSpeed, rideController.CurrentSpeed);
        float surfaceAmplitude = Mathf.Lerp(0.018f, roadHapticAmplitude, speed01);
        if (offCourse)
            surfaceAmplitude = Mathf.Max(surfaceAmplitude, offCourseHapticAmplitude);

        float fullPull = Mathf.Max(0.05f, questReinPullForFullTurn);
        float leftTension = Mathf.Clamp01(leftPull / fullPull);
        float rightTension = Mathf.Clamp01(rightPull / fullPull);
        float leftAmplitude = Mathf.Clamp01(surfaceAmplitude + leftTension * reinTensionHapticAmplitude);
        float rightAmplitude = Mathf.Clamp01(surfaceAmplitude + rightTension * reinTensionHapticAmplitude);
        float pulseDuration = interval * 0.78f;
        SendQuestHaptic(XRNode.LeftHand, leftAmplitude, pulseDuration);
        SendQuestHaptic(XRNode.RightHand, rightAmplitude, pulseDuration);
    }

    private void PulseQuestBothHands(float amplitude, float duration)
    {
        if (!enableQuestHaptics || questRig == null || !questRig.IsTracking)
            return;

        float safeDuration = Mathf.Max(0.02f, duration);
        questDrivingHapticSuppressedUntil = Mathf.Max(
            questDrivingHapticSuppressedUntil,
            Time.unscaledTime + safeDuration);
        SendQuestHaptic(XRNode.LeftHand, amplitude, safeDuration);
        SendQuestHaptic(XRNode.RightHand, amplitude, safeDuration);
    }

    private static void SendQuestHaptic(XRNode node, float amplitude, float duration)
    {
        UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid ||
            !device.TryGetHapticCapabilities(out UnityEngine.XR.HapticCapabilities capabilities) ||
            !capabilities.supportsImpulse)
            return;

        device.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), Mathf.Max(0.02f, duration));
    }

    private static void StopQuestHaptics()
    {
        UnityEngine.XR.InputDevice left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        UnityEngine.XR.InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (left.isValid)
            left.StopHaptics();
        if (right.isValid)
            right.StopHaptics();
    }

    private void CaptureQuestNeutralPosition()
    {
        if (questRig?.LeftController == null || questRig.RightController == null)
            return;

        questLeftNeutralZ = questRig.LeftController.localPosition.z;
        questRightNeutralZ = questRig.RightController.localPosition.z;
        questReinsCalibrated = true;
    }

    private void SetRidePaused(bool paused)
    {
        if (returningToLobby || ridePaused == paused)
            return;

        ridePaused = paused;
        Time.timeScale = paused ? 0f : 1f;
        rideController?.SetBoost(false);
        rideController?.SetExternalSteering(0f, paused || (questRig != null && questRig.IsTracking));
        if (paused)
            StopQuestHaptics();
        else
            PulseQuestBothHands(0.24f, 0.07f);

        if (paused && XRSettings.isDeviceActive)
        {
            EnsureQuestPauseMenu();
            if (questPauseMenu != null && rideCamera != null)
            {
                questPauseMenu.transform.position = rideCamera.transform.position + rideCamera.transform.forward * 1.75f;
                questPauseMenu.transform.rotation = rideCamera.transform.rotation;
                questPauseMenu.SetActive(true);
            }
        }
        else if (questPauseMenu != null)
        {
            questPauseMenu.SetActive(false);
        }

        questRig?.SetRayEnabled(paused);
    }

    private void EnsureQuestPauseMenu()
    {
        if (questPauseMenu != null)
            return;

        questPauseMenu = new GameObject("Quest Pause Menu");
        Material panelMaterial = GetRuntimeMaterial("QuestPausePanel", new Color(0.035f, 0.055f, 0.085f), 0.18f);
        Material buttonMaterial = GetRuntimeMaterial("QuestPauseButton", new Color(0.14f, 0.27f, 0.43f), 0.24f);
        Material recoveryMaterial = GetRuntimeMaterial("QuestPauseRecoveryButton", new Color(0.11f, 0.48f, 0.36f), 0.24f);
        Material lobbyMaterial = GetRuntimeMaterial("QuestPauseLobbyButton", new Color(0.78f, 0.31f, 0.055f), 0.26f);

        CreatePrimitive("Pause Back", PrimitiveType.Cube, questPauseMenu.transform,
            Vector3.zero, new Vector3(2.55f, 1.62f, 0.06f), panelMaterial);
        CreatePauseText("일시정지", questPauseMenu.transform, new Vector3(0f, 0.52f, -0.07f),
            0.10f, 2.05f, 0.26f);
        CreatePauseButton("코스 복귀", questPauseMenu.transform, new Vector3(0f, 0.10f, -0.08f),
            recoveryMaterial, RecoverToCourse);
        CreatePauseButton("계속하기", questPauseMenu.transform, new Vector3(-0.62f, -0.38f, -0.08f),
            buttonMaterial, () => SetRidePaused(false));
        CreatePauseButton("로비로", questPauseMenu.transform, new Vector3(0.62f, -0.38f, -0.08f),
            lobbyMaterial, ReturnToLobby);
        questPauseMenu.SetActive(false);
    }

    private void CreatePauseButton(
        string label,
        Transform parent,
        Vector3 localPosition,
        Material material,
        Action action)
    {
        GameObject button = CreatePrimitive(label + " 버튼", PrimitiveType.Cube, parent,
            localPosition, new Vector3(0.98f, 0.38f, 0.10f), material);
        BoxCollider collider = button.AddComponent<BoxCollider>();
        collider.size = Vector3.one;
        Renderer renderer = button.GetComponent<Renderer>();
        MushQuestRayAction rayAction = button.AddComponent<MushQuestRayAction>();
        rayAction.Configure(action, renderer, Color.Lerp(renderer.material.color, Color.white, 0.24f));
        CreatePauseText(label, parent, localPosition + new Vector3(0f, 0f, -0.07f),
            0.060f, 0.78f, 0.18f);
    }

    private void CreatePauseText(
        string text,
        Transform parent,
        Vector3 localPosition,
        float characterSize,
        float maxWidth,
        float maxHeight)
    {
        // Use the same path as the result panel so assigning the Korean font
        // also assigns its atlas material. A Font with the default Arial
        // material produced broken or completely missing glyphs in Quest.
        TextMesh textMesh = CreateWorldText(text, parent, localPosition, characterSize, Color.white);
        textMesh.transform.localRotation = Quaternion.identity; // 패널 루트가 이미 카메라 방향이므로 추가 180도 회전을 하면 글자가 뒤집힌다.
        if (textMesh.TryGetComponent(out MeshRenderer renderer))
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 20;

            Bounds textBounds = renderer.localBounds;
            float widthScale = textBounds.size.x > 0.0001f ? maxWidth / textBounds.size.x : 1f;
            float heightScale = textBounds.size.y > 0.0001f ? maxHeight / textBounds.size.y : 1f;
            float fitScale = Mathf.Min(1f, widthScale, heightScale);
            textMesh.transform.localScale = Vector3.one * fitScale; // 지정한 제목/버튼 칸보다 큰 글자는 비율을 유지한 채 자동 축소한다.
        }
    }

    private void BuildMissionTimerDisplay()
    {
        if (rideSeatAnchor == null || missionTimerRoot != null)
            return;

        missionTimerRoot = new GameObject("Delivery Mission Timer");
        // Mount the existing timer on the sled's front centre instead of the
        // camera corner.  The seat pivot is shared by the camera, hands and
        // sled, so turns and steep slopes cannot pull the display away from
        // the marked cockpit position.
        missionTimerRoot.transform.SetParent(rideSeatAnchor, false);
        missionTimerRoot.transform.localPosition = new Vector3(0f, 0.95f, -0.63f);
        missionTimerRoot.transform.localRotation = Quaternion.identity;

        Material back = GetRuntimeMaterial("MissionTimerBack", new Color(0.018f, 0.026f, 0.040f), 0.12f);
        CreatePrimitive("Timer Back", PrimitiveType.Cube, missionTimerRoot.transform,
            Vector3.zero, new Vector3(0.60f, 0.14f, 0.026f), back);
        missionTimerText = CreateWorldText("02:00", missionTimerRoot.transform,
            new Vector3(0f, 0f, -0.030f), 0.022f, Color.white);
        missionTimerText.transform.localRotation = Quaternion.identity;

        vrControlHintRoot = new GameObject("Quest Driving Control Hint");
        vrControlHintRoot.transform.SetParent(missionTimerRoot.transform, false);
        vrControlHintRoot.transform.localPosition = new Vector3(0f, -0.125f, 0f);
        vrControlHintRoot.transform.localRotation = Quaternion.identity;
        Material hintBack = GetRuntimeMaterial(
            "QuestDrivingHintBack",
            new Color(0.025f, 0.040f, 0.060f),
            0.14f);
        CreatePrimitive("Quest Hint Back", PrimitiveType.Cube, vrControlHintRoot.transform,
            Vector3.zero, new Vector3(0.74f, 0.078f, 0.022f), hintBack);
        CreatePauseText(
            "X 버프   Y 페널티   B 일시정지",
            vrControlHintRoot.transform,
            new Vector3(0f, 0f, -0.026f),
            0.009f,
            0.68f,
            0.042f);
        vrControlHintRoot.SetActive(false);
        UpdateMissionTimerText();
    }

    private void UpdateMissionTimer()
    {
        if (rideController == null || resultVisible)
            return;

        if (rideController.RideStarted)
        {
            missionTimerStarted = true;
            missionElapsedSeconds += Time.deltaTime;
        }
        UpdateVrControlHint();
        UpdateMissionTimerText();
    }

    private void UpdateVrControlHint()
    {
        if (vrControlHintRoot == null)
            return;

        bool visible = IsVrRideActive() &&
                       missionTimerStarted &&
                       !ridePaused &&
                       !resultVisible &&
                       missionElapsedSeconds <= vrControlHintSeconds;
        if (vrControlHintRoot.activeSelf != visible)
            vrControlHintRoot.SetActive(visible);
    }

    private bool IsVrRideActive()
    {
        return XRSettings.isDeviceActive || (questRig != null && questRig.IsTracking);
    }

    private void UpdateMissionTimerText()
    {
        if (missionTimerText == null)
            return;

        if (questRecalibrationInProgress)
        {
            float progress = Mathf.Clamp01(questRecalibrationHoldTime / Mathf.Max(0.3f, questRecalibrationHoldSeconds));
            missionTimerText.text = $"N {Mathf.RoundToInt(progress * 100f):00}%";
            missionTimerText.color = new Color(0.32f, 0.88f, 1f);
            return;
        }
        if (Time.unscaledTime < questRecalibrationFeedbackUntil)
        {
            missionTimerText.text = "N POS OK";
            missionTimerText.color = new Color(0.35f, 1f, 0.55f);
            return;
        }
        if (Time.unscaledTime < sharpOffCoursePenaltyFeedbackUntil)
        {
            missionTimerText.text = $"+{Mathf.CeilToInt(sharpCurveOffCourseTimePenalty):00}초";
            missionTimerText.color = new Color(1f, 0.10f, 0.06f);
            return;
        }

        float remaining = missionTimerStarted
            ? Mathf.Max(0f, deliveryTimeLimitSeconds - missionElapsedSeconds)
            : deliveryTimeLimitSeconds;
        missionTimerText.text = FormatRemaining(remaining);
        if (remaining <= 10f)
        {
            // Mission time stops while paused, so using it for the blink phase
            // also freezes the warning instead of flashing behind the pause UI.
            bool brightFrame = Mathf.FloorToInt(missionElapsedSeconds * 4f) % 2 == 0;
            missionTimerText.color = brightFrame
                ? new Color(1f, 0.10f, 0.06f)
                : new Color(0.42f, 0.018f, 0.012f);
        }
        else if (remaining <= 30f)
        {
            missionTimerText.color = new Color(1f, 0.78f, 0.08f);
        }
        else
        {
            missionTimerText.color = Color.white;
        }
    }

    private void EnsureResultPanel()
    {
        if (resultPanel != null)
            return;

        resultPanel = new GameObject("Delivery Result Panel");
        Material panel = GetRuntimeMaterial("DeliveryResultPanel", new Color(0.025f, 0.045f, 0.075f), 0.18f);
        Material trim = GetRuntimeMaterial("DeliveryResultTrim", new Color(0.72f, 0.36f, 0.07f), 0.36f);
        Material button = GetRuntimeMaterial("DeliveryResultButton", new Color(0.10f, 0.28f, 0.48f), 0.28f);
        Material lobbyButton = GetRuntimeMaterial("DeliveryResultLobbyButton", new Color(0.68f, 0.24f, 0.045f), 0.28f);

        CreatePrimitive("Result Back", PrimitiveType.Cube, resultPanel.transform,
            Vector3.zero, new Vector3(3.35f, 2.20f, 0.07f), panel);
        CreatePrimitive("Result Top Trim", PrimitiveType.Cube, resultPanel.transform,
            new Vector3(0f, 1.02f, -0.055f), new Vector3(3.10f, 0.045f, 0.025f), trim);
        CreateWorldText("배달 완료!", resultPanel.transform,
            new Vector3(0f, 0.79f, -0.075f), 0.100f, new Color(1f, 0.86f, 0.48f));
        CreateWorldText($"기록  {FormatElapsed(missionElapsedSeconds)}    제한  {FormatRemaining(deliveryTimeLimitSeconds)}",
            resultPanel.transform, new Vector3(0f, 0.52f, -0.075f), 0.040f, Color.white);

        resultStarMesh = BuildResultStarMesh();
        float[] starX = { -0.76f, 0f, 0.76f };
        for (int index = 0; index < 3; index++)
        {
            Vector3 target = new(starX[index], 0.09f, -0.095f);
            CreateEmptyStarOutline(index, target);
            resultFilledStars[index] = CreateFilledResultStar(index, StarFlightStart(index));
            resultStarBursts[index] = CreateStarImpactBurst(index, target);
        }

        resultButtonsRoot = new GameObject("Result Buttons");
        resultButtonsRoot.transform.SetParent(resultPanel.transform, false);
        CreateResultButton("로비로", resultButtonsRoot.transform, new Vector3(-0.72f, -0.72f, -0.085f),
            lobbyButton, ReturnToLobby);
        CreateResultButton("다시 하기", resultButtonsRoot.transform, new Vector3(0.72f, -0.72f, -0.085f),
            button, RetryCurrentMap);
        resultButtonsRoot.SetActive(false);
        resultPanel.SetActive(false);
    }

    private TextMesh CreateWorldText(
        string value,
        Transform parent,
        Vector3 localPosition,
        float characterSize,
        Color color)
    {
        GameObject textObject = new(value + " Text");
        textObject.transform.SetParent(parent, false);
        textObject.transform.localPosition = localPosition;
        textObject.transform.localRotation = Quaternion.identity;
        TextMesh textMesh = textObject.AddComponent<TextMesh>();
        textMesh.text = value;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.characterSize = characterSize;
        textMesh.fontSize = 64;
        textMesh.color = color;

        MushCustomizationCatalog catalog = MushCustomizationCatalog.Load();
        if (catalog != null && catalog.koreanFont != null)
        {
            textMesh.font = catalog.koreanFont;
            if (textObject.TryGetComponent(out MeshRenderer renderer))
                renderer.sharedMaterial = catalog.koreanFont.material;
        }
        return textMesh;
    }

    private void CreateEmptyStarOutline(int index, Vector3 localPosition)
    {
        GameObject outlineObject = new($"Empty Star {index + 1}");
        outlineObject.transform.SetParent(resultPanel.transform, false);
        outlineObject.transform.localPosition = localPosition;
        LineRenderer outline = outlineObject.AddComponent<LineRenderer>();
        outline.useWorldSpace = false;
        outline.loop = true;
        outline.positionCount = 10;
        outline.startWidth = 0.035f;
        outline.endWidth = 0.035f;
        outline.numCapVertices = 3;
        outline.numCornerVertices = 3;
        outline.shadowCastingMode = ShadowCastingMode.Off;
        outline.receiveShadows = false;
        outline.sharedMaterial = GetUiUnlitMaterial("EmptyStar", new Color(0.48f, 0.54f, 0.62f, 0.92f));
        for (int point = 0; point < 10; point++)
        {
            float radius = (point & 1) == 0 ? 0.34f : 0.15f;
            float angle = (90f - point * 36f) * Mathf.Deg2Rad;
            outline.SetPosition(point, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
        }
    }

    private Transform CreateFilledResultStar(int index, Vector3 startPosition)
    {
        GameObject star = new($"Flying Result Star {index + 1}");
        star.transform.SetParent(resultPanel.transform, false);
        star.transform.localPosition = startPosition;
        star.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        MeshFilter filter = star.AddComponent<MeshFilter>();
        filter.sharedMesh = resultStarMesh;
        MeshRenderer renderer = star.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = GetUiUnlitMaterial("FilledStar", new Color(1f, 0.66f, 0.055f));
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        star.SetActive(false);
        return star.transform;
    }

    private ParticleSystem CreateStarImpactBurst(int index, Vector3 localPosition)
    {
        GameObject effect = new($"Star Impact VFX {index + 1}");
        effect.transform.SetParent(resultPanel.transform, false);
        effect.transform.localPosition = localPosition + new Vector3(0f, 0f, -0.035f);
        ParticleSystem particles = effect.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = 0.45f;
        main.maxParticles = 48;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.55f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 1.25f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.075f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.48f, 0.02f),
            new Color(1f, 0.96f, 0.52f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.055f;
        ParticleSystemRenderer renderer = effect.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sharedMaterial = GetUiUnlitMaterial("StarImpact", new Color(1f, 0.64f, 0.04f));
        return particles;
    }

    private void CreateResultButton(
        string label,
        Transform parent,
        Vector3 localPosition,
        Material material,
        Action action)
    {
        GameObject button = CreatePrimitive(label + " Result Button", PrimitiveType.Cube, parent,
            localPosition, new Vector3(1.18f, 0.36f, 0.10f), material);
        BoxCollider collider = button.AddComponent<BoxCollider>();
        collider.size = Vector3.one;
        Renderer renderer = button.GetComponent<Renderer>();
        MushQuestRayAction rayAction = button.AddComponent<MushQuestRayAction>();
        rayAction.Configure(action, renderer, Color.Lerp(renderer.material.color, Color.white, 0.25f));
        CreateWorldText(label, parent, localPosition + new Vector3(0f, 0f, -0.065f), 0.050f, Color.white);
    }

    private void UpdateResultSequence()
    {
        if (resultPanel == null)
            return;

        resultSequenceElapsed += Time.unscaledDeltaTime;
        float flightDuration = Mathf.Max(0.1f, resultStarFlightSeconds);
        for (int index = 0; index < earnedStars; index++)
        {
            Transform star = resultFilledStars[index];
            if (star == null)
                continue;

            float startTime = 0.30f + index * resultStarIntervalSeconds;
            float rawProgress = (resultSequenceElapsed - startTime) / flightDuration;
            if (rawProgress <= 0f)
                continue;

            if (!star.gameObject.activeSelf)
                star.gameObject.SetActive(true);

            float progress = Mathf.Clamp01(rawProgress);
            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            Vector3 start = StarFlightStart(index);
            Vector3 target = StarTarget(index);
            Vector3 position = Vector3.LerpUnclamped(start, target, eased);
            position.y += Mathf.Sin(progress * Mathf.PI) * 0.36f;
            star.localPosition = position;
            star.localRotation = Quaternion.Euler(0f, 180f, Mathf.Lerp(-300f, 0f, eased));

            if (progress < 1f)
            {
                star.localScale = Vector3.one * Mathf.Lerp(0.52f, 1f, eased);
                continue;
            }

            float impactAge = Mathf.Max(0f, resultSequenceElapsed - startTime - flightDuration);
            float bounce = 1f + Mathf.Exp(-8f * impactAge) * Mathf.Cos(19f * impactAge) * 0.22f;
            star.localScale = Vector3.one * bounce;
            if (!resultStarLanded[index])
            {
                resultStarLanded[index] = true;
                resultStarBursts[index]?.Emit(30);
                PulseQuestBothHands(0.38f, 0.09f);
            }
        }

        float sequenceEnd = 0.30f + (earnedStars - 1) * resultStarIntervalSeconds +
                            Mathf.Max(0.1f, resultStarFlightSeconds) + 0.55f;
        if (!resultButtonsShown && resultSequenceElapsed >= sequenceEnd)
        {
            resultButtonsShown = true;
            resultButtonsRoot?.SetActive(true);
        }
    }

    private void UpdateDesktopResultSelection()
    {
        if (!resultButtonsShown || rideCamera == null || XRSettings.isDeviceActive)
            return;
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            return;

        Ray ray = rideCamera.ScreenPointToRay(mouse.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 8f))
            return;
        hit.collider.GetComponentInParent<MushQuestRayAction>()?.SelectWithQuestRay();
    }

    private Vector3 StarFlightStart(int index)
    {
        return index switch
        {
            0 => new Vector3(-2.05f, 1.30f, -0.13f),
            1 => new Vector3(0f, 1.58f, -0.13f),
            _ => new Vector3(2.05f, 1.30f, -0.13f),
        };
    }

    private static Vector3 StarTarget(int index)
    {
        return new Vector3((index - 1) * 0.76f, 0.09f, -0.13f);
    }

    private Mesh BuildResultStarMesh()
    {
        Mesh mesh = new() { name = "Runtime Delivery Result Star" };
        Vector3[] vertices = new Vector3[11];
        vertices[0] = Vector3.zero;
        for (int point = 0; point < 10; point++)
        {
            float radius = (point & 1) == 0 ? 0.34f : 0.15f;
            float angle = (90f - point * 36f) * Mathf.Deg2Rad;
            vertices[point + 1] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
        }

        int[] triangles = new int[60];
        int write = 0;
        for (int point = 0; point < 10; point++)
        {
            int current = point + 1;
            int next = (point + 1) % 10 + 1;
            triangles[write++] = 0;
            triangles[write++] = current;
            triangles[write++] = next;
            triangles[write++] = 0;
            triangles[write++] = next;
            triangles[write++] = current;
        }
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }

    private Material GetUiUnlitMaterial(string key, Color color)
    {
        string materialKey = "UIUnlit_" + key;
        if (runtimeMaterials.TryGetValue(materialKey, out Material existing) && existing != null)
            return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
        Material material = new(shader) { name = "Runtime " + materialKey };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
        material.enableInstancing = true;
        runtimeMaterials[materialKey] = material;
        return material;
    }

    private static string FormatRemaining(float seconds)
    {
        int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(seconds));
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

    private static string FormatElapsed(float seconds)
    {
        int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

    private void BuildSpeedParticles()
    {
        if (rideCamera == null)
            return;

        GameObject particleObject = new("Mush Speed Snow");
        particleObject.transform.SetParent(rideCamera.transform, false);
        const float effectDistance = 9f;
        particleObject.transform.localPosition = new Vector3(0f, 0f, effectDistance);

        speedParticles = particleObject.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = speedParticles.main;
        main.loop = true;
        main.playOnAwake = true;
        main.maxParticles = 260;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.42f, 0.68f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.045f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.78f, 0.90f, 1f, 0.34f),
            new Color(1f, 1f, 1f, 0.68f));

        ParticleSystem.EmissionModule emission = speedParticles.emission;
        emission.rateOverTime = 0f;
        ParticleSystem.ShapeModule shape = speedParticles.shape;
        shape.shapeType = ParticleSystemShapeType.BoxShell;
        float effectFov = Mathf.Max(normalFieldOfView, boostFieldOfView);
        float effectHalfHeight = Mathf.Tan(effectFov * 0.5f * Mathf.Deg2Rad) * effectDistance;
        float effectAspect = Mathf.Clamp(rideCamera.aspect, 1.45f, 1.90f);
        shape.scale = new Vector3(
            effectHalfHeight * effectAspect * 2.10f,
            effectHalfHeight * 2.10f,
            4.5f); // 카메라 시야보다 조금 큰 직사각형 박스 표면에서 방출해 화면 가장자리까지 속도선이 퍼지게 한다.
        ParticleSystem.VelocityOverLifetimeModule velocity = speedParticles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.z = -16f;

        ParticleSystemRenderer particleRenderer = particleObject.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        particleRenderer.velocityScale = 0.08f;
        particleRenderer.lengthScale = 2.6f;
        particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
        particleRenderer.receiveShadows = false;

        Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                                Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Unlit/Color");
        if (particleShader != null)
        {
            Material particleMaterial = new(particleShader) { name = "Runtime Speed Snow" };
            if (particleMaterial.HasProperty("_BaseColor"))
                particleMaterial.SetColor("_BaseColor", new Color(0.86f, 0.94f, 1f, 0.72f));
            if (particleMaterial.HasProperty("_Color"))
                particleMaterial.SetColor("_Color", new Color(0.86f, 0.94f, 1f, 0.72f));
            particleRenderer.sharedMaterial = particleMaterial;
            runtimeMaterials["SpeedSnowParticles"] = particleMaterial;
        }

        speedParticles.Play();
    }

    private void UpdateRidePresentation(bool running, float boost01)
    {
        float smoothedBoost = Mathf.SmoothStep(0f, 1f, boost01);
        float blend = 1f - Mathf.Exp(-5f * Time.deltaTime);

        if (rideCamera != null)
        {
            float targetFov = Mathf.Lerp(normalFieldOfView, boostFieldOfView, smoothedBoost);
            rideCamera.fieldOfView = Mathf.Lerp(rideCamera.fieldOfView, targetFov, blend);

            if (!XRSettings.isDeviceActive)
            {
                float shake = running ? Mathf.Lerp(0.004f, 0.022f, smoothedBoost) : 0f;
                float shakeTime = Time.time * Mathf.Lerp(8f, 16f, smoothedBoost);
                Vector3 offset = new(
                    (Mathf.PerlinNoise(shakeTime, 0.31f) - 0.5f) * shake,
                    Mathf.Sin(shakeTime * 1.7f) * shake,
                    0f);
                rideCamera.transform.localPosition = Vector3.Lerp(
                    rideCamera.transform.localPosition,
                    cameraBaseLocalPosition + offset,
                    blend);
                Quaternion targetRotation = cameraRestLocalRotation * Quaternion.Euler(
                    Mathf.Sin(shakeTime * 1.3f) * shake * 24f,
                    0f,
                    Mathf.Sin(shakeTime) * shake * 38f);
                rideCamera.transform.localRotation = Quaternion.Slerp(
                    rideCamera.transform.localRotation,
                    targetRotation,
                    blend);
            }
        }

        if (speedParticles != null)
        {
            ParticleSystem.EmissionModule emission = speedParticles.emission;
            emission.rateOverTime = running ? Mathf.Lerp(18f, 125f, smoothedBoost) : 0f;
            ParticleSystem.VelocityOverLifetimeModule velocity = speedParticles.velocityOverLifetime;
            velocity.z = -Mathf.Lerp(13f, 30f, smoothedBoost);
        }

        snowController?.SetRideSpeedStrength(running ? smoothedBoost : 0f);
    }

    private void EnsureDogTeamVisible()
    {
        if (rideCamera == null)
            return;

        for (int dogIndex = 0; dogIndex < dogs.Count; dogIndex++)
        {
            DogRuntime dog = dogs[dogIndex];
            if (dog?.visual == null)
                continue;

            dog.holder.gameObject.SetActive(true);
            dog.visual.gameObject.SetActive(true);
            foreach (Transform child in dog.visual.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.SetActive(true);
                child.gameObject.layer = 0;
            }

            Renderer[] renderers = dog.visual.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                renderer.enabled = true;
                renderer.forceRenderingOff = false;
            }

            if (!TryGetWorldRendererBounds(dog.visual, out Bounds bounds))
            {
                Debug.LogError($"[Mush] {dog.holder.name}: no renderer bounds were found.", dog.holder);
                continue;
            }

            if (bounds.size.y < DesiredDogHeight * 0.8f || bounds.size.y > DesiredDogHeight * 1.2f)
            {
                float emergencyScale = DesiredDogHeight / Mathf.Max(bounds.size.y, 0.0001f);
                dog.visual.localScale *= emergencyScale;
                NormalizeDogModel(dog.holder, dog.visual);
                dog.restLocalPosition = dog.visual.localPosition;
                dog.forwardLocalRotation = dog.visual.localRotation;
                TryGetWorldRendererBounds(dog.visual, out bounds);
            }

            Vector3 viewport = rideCamera.WorldToViewportPoint(bounds.center);
            bool centerOnScreen = viewport.z > rideCamera.nearClipPlane &&
                                  viewport.x > 0.03f && viewport.x < 0.97f &&
                                  viewport.y > 0.03f && viewport.y < 0.97f;
            if (!centerOnScreen)
            {
                dog.holder.localPosition = new Vector3(dogIndex == 0 ? -0.72f : 0.72f, 0f, 3.45f);
                NormalizeDogModel(dog.holder, dog.visual);
                dog.restLocalPosition = dog.visual.localPosition;
                dog.forwardLocalRotation = dog.visual.localRotation;
                TryGetWorldRendererBounds(dog.visual, out bounds);
                viewport = rideCamera.WorldToViewportPoint(bounds.center);
            }

            Vector3 viewportBottom = rideCamera.WorldToViewportPoint(bounds.center - Vector3.up * bounds.extents.y);
            Vector3 viewportTop = rideCamera.WorldToViewportPoint(bounds.center + Vector3.up * bounds.extents.y);
            float screenHeight = Mathf.Abs(viewportTop.y - viewportBottom.y);
            string poseCheck = "semantic-parts-missing";
            if (TryGetDogSemanticBasis(dog.holder, dog.visual,
                    out Vector3 semanticUp, out Vector3 semanticForward,
                    out Vector3 nosePosition, out Vector3 pawPosition))
            {
                poseCheck =
                    $"upDot={Vector3.Dot(semanticUp, Vector3.up):0.000}, " +
                    $"forwardDot={Vector3.Dot(semanticForward, Vector3.forward):0.000}, " +
                    $"noseAbovePaws={(nosePosition.y - pawPosition.y):0.000}";
            }
            Debug.Log(
                $"[Mush] {dog.holder.name} visible-check: renderers={renderers.Length}, " +
                $"boundsCenter={bounds.center}, boundsSize={bounds.size}, viewport={viewport}, " +
                $"screenHeight={screenHeight:0.000}, {poseCheck}",
                dog.holder);
        }
    }

    private void ApplyRideDogCustomization()
    {
        if (customization == null)
            return;
        if (dogs.Count > 0 && dogs[0]?.visual != null)
            MushCustomizationVisuals.ApplyDogLoadout(dogs[0].visual, false, customization, 0);
        if (dogs.Count > 1 && dogs[1]?.visual != null)
            MushCustomizationVisuals.ApplyDogLoadout(dogs[1].visual, true, customization, 1);
    }

    private float DesiredDogHeight => Mathf.Max(targetDogHeight, 1.42f);

    private static bool TryGetWorldRendererBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    private void ConnectMapEffects(Transform mapRoot, Transform team)
    {
        curvedWorld?.SetProgressTarget(team);

        Transform ambientSnow = FindDeepChild(mapRoot, "FX_AmbientSnow_Rebuilt");
        if (ambientSnow != null)
        {
            ambientSnow.SetParent(team, false);
            ambientSnow.localPosition = new Vector3(0f, 4f, 7f);
        }

        snowController = mapRoot.GetComponent<MushSnowfieldBlizzardController>();
        if (snowController != null)
        {
            snowController.SetProgressTarget(team);
        }

        MushForestTimeCycleController forest = mapRoot.GetComponent<MushForestTimeCycleController>();
        if (forest != null)
            forest.SetProgressTarget(team);
    }

    private void LateUpdate()
    {
        if (!built || rideController == null)
            return;

        if (resultVisible)
            return;

        UpdateCourseSpeedMultiplier();
        UpdateOffCourseSpeedLimit();

        bool running = rideController.RideStarted;
        UpdateCourseRecoveryCheckpoint(running);
        if (UpdateCourseCompletion(running))
            return;
        float speed01 = Mathf.InverseLerp(0f, rideController.SecondLevelSpeed, rideController.CurrentSpeed);
        float boost01 = Mathf.InverseLerp(
            rideController.FirstLevelSpeed,
            rideController.SecondLevelSpeed,
            rideController.CurrentSpeed);
        UpdateRidePresentation(running, boost01);
        UpdateSledSurfacePose(running);

        foreach (DogRuntime dog in dogs)
        {
            if (dog?.visual == null)
                continue;

            if (!running && rideCamera != null)
            {
                Vector3 lookDirection = Vector3.ProjectOnPlane(
                    rideCamera.transform.position - dog.holder.position,
                    Vector3.up);
                if (lookDirection.sqrMagnitude > 0.001f)
                {
                    Quaternion target = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
                    float lookBlend = 1f - Mathf.Exp(-5f * Time.deltaTime);
                    // Turn the outer holder only. Rotating the imported visual
                    // directly discards its FBX axis correction and makes the
                    // dog's nose point into the ground.
                    dog.holder.rotation = Quaternion.Slerp(dog.holder.rotation, target, lookBlend);
                    dog.visual.localRotation = Quaternion.Slerp(
                        dog.visual.localRotation,
                        dog.forwardLocalRotation,
                        lookBlend);
                }

                dog.visual.localPosition = Vector3.Lerp(
                    dog.visual.localPosition,
                    dog.restLocalPosition,
                    1f - Mathf.Exp(-8f * Time.deltaTime));
                AnimateDogLegs(dog, 0f, 12f);
                continue;
            }

            float forwardBlend = 1f - Mathf.Exp(-7f * Time.deltaTime);
            UpdateDogSurfacePose(dog, forwardBlend);
            dog.visual.localRotation = Quaternion.Slerp(
                dog.visual.localRotation,
                dog.forwardLocalRotation,
                forwardBlend);
            float cadence = Mathf.Lerp(6.5f, 13.5f, speed01);
            float strideAngle = Mathf.Lerp(10f, 24f, speed01);
            dog.gaitClock += cadence * Time.deltaTime;
            AnimateDogLegs(dog, strideAngle, Mathf.Lerp(12f, 20f, speed01));

            float gait = Mathf.Abs(Mathf.Sin(dog.gaitClock + dog.gaitPhase));
            Vector3 targetPosition = dog.restLocalPosition + Vector3.up * (gait * 0.055f * speed01);
            dog.visual.localPosition = Vector3.Lerp(dog.visual.localPosition, targetPosition, forwardBlend);
        }
    }

    private void UpdateOffCourseSpeedLimit()
    {
        if (curvedWorld == null || rideTeam == null || rideController == null ||
            !curvedWorld.TryGetRoadLateralDistance(rideTeam.position, out float lateralDistance))
            return;

        float roadEdge = curvedWorld.RoadHalfWidthMeters;
        bool nextOffCourse = offCourse
            ? lateralDistance > Mathf.Max(0f, roadEdge - roadReturnInset)
            : lateralDistance > roadEdge + roadExitMargin;
        if (nextOffCourse == offCourse)
            return;

        offCourse = nextOffCourse;
        if (offCourse && curvedWorld.IsSharpCurveMap && missionTimerStarted)
        {
            missionElapsedSeconds += sharpCurveOffCourseTimePenalty;
            sharpOffCoursePenaltyFeedbackUntil = Time.unscaledTime + 0.90f;
        }

        // Every course uses the same racing-style off-road response: momentum
        // is lost on entry, but the sled can rebuild speed instead of being
        // held at a permanent crawl. SharpCurve alone keeps the time penalty.
        rideController.SetTerrainSpeedLimit(false);
        if (offCourse)
        {
            PulseQuestBothHands(0.86f, 0.22f);
            rideController.ApplyOffCourseImpact(
                offCourseImpactRetainedSpeed,
                offCourseAccelerationMultiplier);
        }
        else
        {
            rideController.ClearOffCourseImpactRecovery();
        }
    }

    private void UpdateCourseSpeedMultiplier()
    {
        if (rideController == null)
            return;

        bool downhillActive = curvedWorld != null && curvedWorld.SharpDownhillSpeedBoostActive;
        if (downhillActive != sharpDownhillHapticActive)
        {
            sharpDownhillHapticActive = downhillActive;
            PulseQuestBothHands(downhillActive ? 0.46f : 0.25f, downhillActive ? 0.18f : 0.08f);
        }

        float multiplier = downhillActive
            ? 1.5f
            : 1f;
        rideController.SetCourseSpeedMultiplier(multiplier);
    }

    private void InitializeCourseRecoveryCheckpoint()
    {
        if (rideTeam == null)
            return;

        hasRecoveryCheckpoint = true;
        recoveryRouteProgress = 0f;
        recoveryFallbackPosition = rideTeam.position;
        recoveryFallbackForward = rideTeam.forward;
        nextRecoveryCheckpointTime = Time.unscaledTime + recoveryCheckpointInterval;

        if (curvedWorld == null ||
            !curvedWorld.TryGetRouteProgress(rideTeam.position, out float progress) ||
            !curvedWorld.TryGetRoutePose(progress, out Vector3 surfacePoint, out _, out Vector3 surfaceForward))
            return;

        recoveryRouteProgress = progress;
        recoveryFallbackPosition = surfacePoint + Vector3.up * rideController.RideHeight;
        recoveryFallbackForward = surfaceForward;
    }

    private void UpdateCourseRecoveryCheckpoint(bool running)
    {
        if (!running || ridePaused || curvedWorld == null || rideTeam == null || rideController == null ||
            Time.unscaledTime < nextRecoveryCheckpointTime)
            return;

        nextRecoveryCheckpointTime = Time.unscaledTime + recoveryCheckpointInterval;
        if (!curvedWorld.TryGetCourseSurface(
                rideTeam.position,
                out Vector3 surfacePoint,
                out _,
                out _,
                out float signedLateralDistance))
            return;

        float safeHalfWidth = Mathf.Max(0.50f, curvedWorld.RoadHalfWidthMeters - recoveryRoadInset);
        float expectedHeight = surfacePoint.y + rideController.RideHeight;
        if (Mathf.Abs(signedLateralDistance) > safeHalfWidth ||
            Mathf.Abs(rideTeam.position.y - expectedHeight) > recoveryGroundTolerance ||
            !curvedWorld.TryGetRouteProgress(rideTeam.position, out float routeProgress) ||
            !curvedWorld.TryGetRoutePose(routeProgress, out Vector3 routePoint, out _, out Vector3 routeForward))
            return;

        hasRecoveryCheckpoint = true;
        recoveryRouteProgress = routeProgress;
        recoveryFallbackPosition = routePoint + Vector3.up * rideController.RideHeight;
        recoveryFallbackForward = routeForward;
    }

    private void RecoverToCourse()
    {
        if (returningToLobby || resultVisible || rideTeam == null || rideController == null)
            return;
        if (!hasRecoveryCheckpoint)
            InitializeCourseRecoveryCheckpoint();
        if (!hasRecoveryCheckpoint)
            return;

        Vector3 recoveryPosition = recoveryFallbackPosition;
        Vector3 recoveryForward = recoveryFallbackForward;
        Vector3 recoveryNormal = Vector3.up;
        float restoredRouteProgress = recoveryRouteProgress;
        if (curvedWorld != null)
        {
            float rollbackProgress = recoveryRollbackMeters / Mathf.Max(1f, curvedWorld.LengthMeters);
            float targetProgress = Mathf.Clamp01(recoveryRouteProgress - rollbackProgress);
            if (curvedWorld.TryGetRoutePose(
                    targetProgress,
                    out Vector3 routePoint,
                    out Vector3 routeNormal,
                    out Vector3 routeForward))
            {
                recoveryPosition = routePoint + Vector3.up * rideController.RideHeight;
                recoveryForward = routeForward;
                recoveryNormal = routeNormal;
                restoredRouteProgress = targetProgress;
            }
        }

        Vector3 uprightForward = Vector3.ProjectOnPlane(recoveryForward, Vector3.up).normalized;
        if (uprightForward.sqrMagnitude < 0.0001f)
            uprightForward = Vector3.ProjectOnPlane(rideTeam.forward, Vector3.up).normalized;
        if (uprightForward.sqrMagnitude < 0.0001f)
            uprightForward = Vector3.forward;

        rideTeam.SetPositionAndRotation(
            recoveryPosition,
            Quaternion.LookRotation(uprightForward, Vector3.up));
        rideController.ResetMotionForCourseRecovery();
        offCourse = false;
        rideController.SetTerrainSpeedLimit(false);
        lastRidePosition = recoveryPosition;
        ridePositionInitialized = true;
        nextRecoveryCheckpointTime = Time.unscaledTime + recoveryCheckpointInterval;
        SnapRideVisualsToRecoverySurface(recoveryNormal);

        if (speedParticles != null)
        {
            ParticleSystem.EmissionModule emission = speedParticles.emission;
            emission.rateOverTime = 0f;
            speedParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            speedParticles.Clear(true);
            speedParticles.Play(true);
        }
        snowController?.SetRideSpeedStrength(0f);

        bool resumeFromPause = ridePaused;
        if (resumeFromPause)
            SetRidePaused(false);
        PulseQuestBothHands(0.68f, 0.16f);
        Debug.Log($"[Mush] 코스 복귀: 진행도 {restoredRouteProgress * 100f:0.0}%", this);
    }

    private void SnapRideVisualsToRecoverySurface(Vector3 surfaceNormal)
    {
        if (rideSeatAnchor != null)
        {
            Vector3 slopeForward = Vector3.ProjectOnPlane(rideTeam.forward, surfaceNormal).normalized;
            rideSeatAnchor.localRotation = slopeForward.sqrMagnitude > 0.0001f
                ? Quaternion.Inverse(rideTeam.rotation) * Quaternion.LookRotation(slopeForward, surfaceNormal)
                : Quaternion.identity;
        }
        if (sledHolder != null)
            sledHolder.localRotation = Quaternion.identity;

        foreach (DogRuntime dog in dogs)
        {
            if (dog?.holder != null)
                UpdateDogSurfacePose(dog, 1f);
        }
    }

    private bool UpdateCourseCompletion(bool running)
    {
        if (resultVisible)
            return true;
        if (returningToLobby || rideTeam == null)
            return returningToLobby;

        if (!ridePositionInitialized)
        {
            lastRidePosition = rideTeam.position;
            ridePositionInitialized = true;
        }

        Vector3 previousRidePosition = lastRidePosition;
        float step = Vector3.Distance(rideTeam.position, previousRidePosition);
        lastRidePosition = rideTeam.position;
        if (running && step < 30f)
            travelledCourseDistance += step;

        if (!running || finishMarker == null)
            return false;

        Vector3 finishForward = Vector3.ProjectOnPlane(finishMarker.forward, Vector3.up).normalized;
        if (finishForward.sqrMagnitude < 0.001f)
            return false;

        Vector3 finishRight = Vector3.Cross(Vector3.up, finishForward).normalized;
        float previousAlong = Vector3.Dot(previousRidePosition - finishMarker.position, finishForward);
        float currentAlong = Vector3.Dot(rideTeam.position - finishMarker.position, finishForward);
        float lateralDistance = Mathf.Abs(Vector3.Dot(rideTeam.position - finishMarker.position, finishRight));
        float finishHalfWidth = curvedWorld != null
            ? curvedWorld.RoadHalfWidthMeters + 0.75f
            : finishDistanceTolerance;
        bool crossedVisibleFinishLine = previousAlong <= 0f && currentAlong >= 0f &&
                                        lateralDistance <= finishHalfWidth;
        if (!crossedVisibleFinishLine)
            return false;

        ShowDeliveryResult();
        return true;
    }

    private void ShowDeliveryResult()
    {
        if (resultVisible)
            return;

        resultVisible = true;
        earnedStars = missionElapsedSeconds <= deliveryTimeLimitSeconds * threeStarTimeRatio
            ? 3
            : missionElapsedSeconds <= deliveryTimeLimitSeconds ? 2 : 1;
        resultSequenceElapsed = 0f;
        resultButtonsShown = false;
        Array.Clear(resultStarLanded, 0, resultStarLanded.Length);

        if (ridePaused)
        {
            ridePaused = false;
            Time.timeScale = 1f;
        }
        if (questPauseMenu != null)
            questPauseMenu.SetActive(false);
        if (missionTimerRoot != null)
            missionTimerRoot.SetActive(false);

        rideController.SetBoost(false);
        rideController.SetExternalSteering(0f);
        rideController.enabled = false;
        StopRideEffectsForResult();
        PulseQuestBothHands(0.56f, 0.20f);

        EnsureResultPanel();
        if (resultPanel != null && rideCamera != null)
        {
            resultPanel.transform.position = rideCamera.transform.position + rideCamera.transform.forward * 3.15f;
            resultPanel.transform.rotation = rideCamera.transform.rotation;
            resultPanel.transform.localScale = Vector3.one * 0.62f;
            resultPanel.SetActive(true);
        }
        questRig?.SetRayEnabled(true);

        Debug.Log($"[Mush] 배달 완료: {FormatElapsed(missionElapsedSeconds)}, 별 {earnedStars}개", this);
    }

    private void StopRideEffectsForResult()
    {
        if (speedParticles != null)
        {
            ParticleSystem.EmissionModule emission = speedParticles.emission;
            emission.rateOverTime = 0f;
            speedParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            speedParticles.Clear(true);
            speedParticles.gameObject.SetActive(false);
        }

        snowController?.SetRideSpeedStrength(0f);
        if (rideCamera == null)
            return;

        rideCamera.fieldOfView = normalFieldOfView;
        if (!XRSettings.isDeviceActive)
        {
            rideCamera.transform.localPosition = cameraBaseLocalPosition;
            rideCamera.transform.localRotation = cameraRestLocalRotation;
        }
    }

    private void UpdateSledSurfacePose(bool running)
    {
        if (rideSeatAnchor == null || sledHolder == null)
            return;

        Quaternion targetLocalRotation = Quaternion.identity;
        if (running && curvedWorld != null && curvedWorld.TryGetCourseSurface(
                sledHolder.position,
                out _,
                out Vector3 surfaceNormal,
                out _,
                out _))
        {
            Vector3 slopeForward = Vector3.ProjectOnPlane(rideTeam.forward, surfaceNormal).normalized;
            if (slopeForward.sqrMagnitude > 0.0001f)
            {
                Quaternion targetWorldRotation = Quaternion.LookRotation(slopeForward, surfaceNormal);
                targetLocalRotation = Quaternion.Inverse(rideTeam.rotation) * targetWorldRotation;
            }
        }

        float blend = 1f - Mathf.Exp(-14f * Time.deltaTime);
        rideSeatAnchor.localRotation = Quaternion.Slerp(
            rideSeatAnchor.localRotation,
            targetLocalRotation,
            blend);
        // The holder is now a child of the shared seat pivot.  Keeping an
        // additional pitch here would rotate the sled twice while the rider
        // rotates only once.
        sledHolder.localRotation = Quaternion.identity;
    }

    private void UpdateDogSurfacePose(DogRuntime dog, float rotationBlend)
    {
        Vector3 targetLocalPosition = dog.restHolderLocalPosition;
        Quaternion targetLocalRotation = dog.restHolderLocalRotation;

        Vector3 samplePosition = rideTeam.TransformPoint(new Vector3(
            dog.restHolderLocalPosition.x,
            0f,
            dog.restHolderLocalPosition.z));
        if (curvedWorld != null && curvedWorld.TryGetCourseSurface(
                samplePosition,
                out Vector3 surfacePoint,
                out Vector3 surfaceNormal,
                out _,
                out _))
        {
            // Each dog gets its own ground height instead of inheriting the
            // sled centre height.  A small clearance keeps animated paws from
            // flickering through the road mesh.
            Vector3 localGround = rideTeam.InverseTransformPoint(surfacePoint + Vector3.up * 0.025f);
            targetLocalPosition.y = localGround.y;

            Vector3 slopeForward = Vector3.ProjectOnPlane(rideTeam.forward, surfaceNormal).normalized;
            if (slopeForward.sqrMagnitude > 0.0001f)
            {
                Quaternion targetWorldRotation = Quaternion.LookRotation(slopeForward, surfaceNormal);
                targetLocalRotation = Quaternion.Inverse(rideTeam.rotation) * targetWorldRotation;
            }
        }

        // Height is exact every frame; smoothing it recreates the visible
        // floating problem on the fast downhill.  Only rotation is softened.
        dog.holder.localPosition = targetLocalPosition;
        dog.holder.localRotation = Quaternion.Slerp(
            dog.holder.localRotation,
            targetLocalRotation,
            rotationBlend);
    }

    private void ReturnToLobby()
    {
        if (returningToLobby)
            return;
        returningToLobby = true;
        if (ridePaused)
        {
            ridePaused = false;
            Time.timeScale = 1f;
        }

        if (!Application.CanStreamedLevelBeLoaded(lobbySceneName))
        {
            returningToLobby = false;
            Debug.LogError($"[Mush] 완주했지만 로비 씬 '{lobbySceneName}'을 찾을 수 없습니다.", this);
            return;
        }

        Debug.Log($"[Mush] 완주했습니다. {lobbySceneName} 로비로 돌아갑니다.", this);
        SceneManager.LoadScene(lobbySceneName);
    }

    private void RetryCurrentMap()
    {
        if (returningToLobby)
            return;
        returningToLobby = true;
        Time.timeScale = 1f;

        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || string.IsNullOrEmpty(activeScene.name))
        {
            returningToLobby = false;
            Debug.LogError("[Mush] 다시 시작할 현재 맵 씬을 찾을 수 없습니다.", this);
            return;
        }
        SceneManager.LoadScene(activeScene.name);
    }

    private static void AnimateDogLegs(DogRuntime dog, float strideAngle, float blendSpeed)
    {
        if (dog.legPivots == null || dog.legRestRotations == null)
            return;

        float blend = 1f - Mathf.Exp(-blendSpeed * Time.deltaTime);
        for (int index = 0; index < dog.legPivots.Length; index++)
        {
            Transform pivot = dog.legPivots[index];
            if (pivot == null)
                continue;

            // Front-right and rear-left form the opposite diagonal pair.
            bool oppositeDiagonal = index == 1 || index == 2;
            float phase = dog.gaitPhase + (oppositeDiagonal ? Mathf.PI : 0f);
            float angle = strideAngle > 0f ? Mathf.Sin(dog.gaitClock + phase) * strideAngle : 0f;
            Quaternion target = dog.legRestRotations[index] * Quaternion.Euler(angle, 0f, 0f);
            pivot.localRotation = Quaternion.Slerp(pivot.localRotation, target, blend);
        }
    }

    private void OnGUI()
    {
        if (!built)
            return;

        if (resultVisible)
            return; // 결과는 데스크톱과 VR이 같은 3D 패널을 사용하므로 기존 키보드 도움말을 뒤에 겹쳐 그리지 않는다.

        // Quest uses the small world-space hint beside the sled timer and its
        // own 3D pause panel. IMGUI is desktop-only so keyboard text and the
        // desktop pause box never appear in the headset or XR mirror view.
        if (IsVrRideActive())
            return;

        lobbyButtonStyle ??= CreateLobbyButtonStyle();

        if (ridePaused)
        {
            GUI.Box(new Rect(Screen.width * 0.5f - 220f, Screen.height * 0.5f - 150f, 440f, 300f), "일시정지");
            if (GUI.Button(new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.5f - 55f, 170f, 62f),
                    "계속하기", lobbyButtonStyle))
                SetRidePaused(false);
            if (GUI.Button(new Rect(Screen.width * 0.5f + 10f, Screen.height * 0.5f - 55f, 170f, 62f),
                    "로비로", lobbyButtonStyle))
                ReturnToLobby();
            if (GUI.Button(new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.5f + 30f, 360f, 62f),
                    "코스 복귀", lobbyButtonStyle))
                RecoverToCourse();
            return;
        }

        if (!showKeyboardHelp || rideController == null)
            return;

        helpStyle ??= new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = 18,
            normal = { textColor = Color.white },
            padding = new RectOffset(14, 14, 10, 10),
        };

        string state = rideController.RideStarted
            ? $"RUNNING  SPEED {rideController.SpeedLevel}/2  {rideController.CurrentSpeed:0.0} m/s"
            : "DOGS WAITING - PRESS SPACE TO GRAB THE REINS";
        GUI.Box(new Rect(18f, 78f, 525f, 138f),
            state + $"\nDOG EFFECT: {rideController.ActiveDogEffectLabel}" +
            "\nA/D: STEER    HOLD W: SPEED 2    Q: BUFF    E: PENALTY" +
            "\nR: RETURN TO COURSE", helpStyle);
    }

    private static GUIStyle CreateLobbyButtonStyle()
    {
        MushCustomizationCatalog catalog = MushCustomizationCatalog.Load();
        GUIStyle style = new(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 21,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(14, 14, 8, 8),
        };
        if (catalog != null && catalog.koreanFont != null)
            style.font = catalog.koreanFont;
        return style;
    }

    private Vector3 SnapPointToGround(Vector3 point)
    {
        Vector3 origin = point + Vector3.up * 20f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 50f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        bool foundGround = false;
        RaycastHit bestHit = default;
        float bestHeightDifference = float.PositiveInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit candidate = hits[i];
            if (candidate.normal.y < 0.58f)
                continue;

            float heightDifference = Mathf.Abs(candidate.point.y - point.y);
            if (heightDifference < bestHeightDifference)
            {
                foundGround = true;
                bestHit = candidate;
                bestHeightDifference = heightDifference;
            }
        }

        return foundGround ? bestHit.point : point;
    }

    private void ImproveMapReadability(Transform mapRoot)
    {
        Material snow = GetRuntimeMaterial("ReadableSnow", new Color(0.86f, 0.92f, 0.98f), 0.14f);
        Material road = GetRuntimeMaterial("ReadablePackedSnow", new Color(0.29f, 0.39f, 0.51f), 0.18f);
        Material tracks = GetRuntimeMaterial("ReadableSledTrack", new Color(0.10f, 0.15f, 0.22f), 0.12f);

        foreach (Renderer renderer in mapRoot.GetComponentsInChildren<Renderer>(true))
        {
            Material[] slots = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < slots.Length; i++)
            {
                string materialName = slots[i] != null ? slots[i].name : string.Empty;
                if (materialName.Contains("MUSH_MAT_SledTrack", StringComparison.OrdinalIgnoreCase))
                {
                    slots[i] = tracks;
                    changed = true;
                }
                else if (materialName.Contains("MUSH_MAT_PackedSnow", StringComparison.OrdinalIgnoreCase))
                {
                    slots[i] = road;
                    changed = true;
                }
                else if (materialName.Contains("MUSH_MAT_Snow", StringComparison.OrdinalIgnoreCase))
                {
                    slots[i] = snow;
                    changed = true;
                }
            }

            if (changed)
                renderer.sharedMaterials = slots;
        }
    }

    private LineRenderer BuildRein(string reinName, Transform parent)
    {
        GameObject reinObject = new(reinName);
        reinObject.transform.SetParent(parent, false);
        LineRenderer line = reinObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 3;
        line.startWidth = 0.04f;
        line.endWidth = 0.026f;
        line.startColor = new Color(0.24f, 0.075f, 0.018f, 1f);
        line.endColor = new Color(0.12f, 0.035f, 0.01f, 1f);
        line.numCapVertices = 4;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = GetRuntimeMaterial("ReinLeather", new Color(0.11f, 0.035f, 0.012f), 0.12f);
        return line;
    }

    private Transform BuildMitten(string mittenName, Transform parent, int side)
    {
        GameObject mitten = new(mittenName);
        mitten.transform.SetParent(parent, false);
        mitten.transform.localPosition = Vector3.zero;
        mitten.transform.localRotation = Quaternion.Euler(8f, side * 5f, side * 4f);

        Material glove = GetRuntimeMaterial("WinterMitten", new Color(0.24f, 0.08f, 0.025f), 0.16f);
        Material fur = GetRuntimeMaterial("WinterFur", new Color(0.80f, 0.69f, 0.51f), 0.12f);
        CreateGlovePart("Palm", PrimitiveType.Sphere, mitten.transform,
            new Vector3(0f, 0f, 0.02f), new Vector3(0.22f, 0.15f, 0.29f), Vector3.zero, glove);
        CreateGlovePart("Curled Fingers", PrimitiveType.Sphere, mitten.transform,
            new Vector3(0f, -0.005f, 0.17f), new Vector3(0.23f, 0.145f, 0.20f), Vector3.zero, glove);
        CreateGlovePart("Thumb", PrimitiveType.Capsule, mitten.transform,
            new Vector3(side * 0.13f, -0.02f, 0.055f), new Vector3(0.065f, 0.105f, 0.065f),
            new Vector3(62f, 0f, side * -32f), glove);
        CreateGlovePart("Wrist", PrimitiveType.Cylinder, mitten.transform,
            new Vector3(0f, 0f, -0.17f), new Vector3(0.11f, 0.09f, 0.11f), new Vector3(90f, 0f, 0f), glove);
        CreateGlovePart("Fur Cuff", PrimitiveType.Cylinder, mitten.transform,
            new Vector3(0f, 0f, -0.25f), new Vector3(0.16f, 0.075f, 0.16f), new Vector3(90f, 0f, 0f), fur);
        return mitten.transform;
    }

    private void BuildSledCockpit(Transform parent)
    {
        if (customization?.equippedSledBody == MushCustomizationIds.SledSanta)
        {
            BuildSantaSledCockpit(parent);
            return;
        }

        Color sledColor = EquippedSledColor();
        Material wood = GetRuntimeMaterial("CockpitWood_" + (customization?.equippedSledBody ?? "natural"),
            Color.Lerp(sledColor, Color.black, 0.28f), 0.22f);
        Material lightWood = GetRuntimeMaterial("CockpitLightWood_" + (customization?.equippedSledBody ?? "natural"),
            Color.Lerp(sledColor, Color.white, 0.18f), 0.24f);
        Material runner = GetRuntimeMaterial("CockpitRunner", new Color(0.24f, 0.29f, 0.34f), 0.62f);
        CreatePrimitive("First Person Handle", PrimitiveType.Cube, parent,
            new Vector3(0f, 0.88f, -0.52f), new Vector3(1.25f, 0.065f, 0.075f), wood);
        CreateGlovePart("Left Handle Upright", PrimitiveType.Cube, parent,
            new Vector3(-0.56f, 0.65f, -0.32f), new Vector3(0.07f, 0.50f, 0.07f),
            new Vector3(-15f, 0f, 0f), wood);
        CreateGlovePart("Right Handle Upright", PrimitiveType.Cube, parent,
            new Vector3(0.56f, 0.65f, -0.32f), new Vector3(0.07f, 0.50f, 0.07f),
            new Vector3(-15f, 0f, 0f), wood);
        CreatePrimitive("Front Cross Bar", PrimitiveType.Cube, parent,
            new Vector3(0f, 0.46f, 0.05f), new Vector3(1.12f, 0.055f, 0.07f), lightWood);

        // Guaranteed first-person deck: open slats keep the snow visible while
        // showing more than just the front handle at the bottom of the frame.
        for (int i = -2; i <= 2; i++)
        {
            CreatePrimitive($"Deck Slat {i + 3}", PrimitiveType.Cube, parent,
                new Vector3(i * 0.19f, 0.34f, -0.28f), new Vector3(0.13f, 0.045f, 1.85f), lightWood);
        }
        CreatePrimitive("Left Visible Runner", PrimitiveType.Cube, parent,
            new Vector3(-0.57f, 0.23f, -0.22f), new Vector3(0.07f, 0.07f, 2.25f), runner);
        CreatePrimitive("Right Visible Runner", PrimitiveType.Cube, parent,
            new Vector3(0.57f, 0.23f, -0.22f), new Vector3(0.07f, 0.07f, 2.25f), runner);
    }

    private void BuildSantaSledCockpit(Transform parent)
    {
        Material red = GetRuntimeMaterial("SantaCockpitRed", new Color(0.72f, 0.025f, 0.035f), 0.28f);
        Material deepRed = GetRuntimeMaterial("SantaCockpitDeepRed", new Color(0.34f, 0.012f, 0.018f), 0.22f);
        Material cream = GetRuntimeMaterial("SantaCockpitCream", new Color(0.92f, 0.82f, 0.63f), 0.25f);
        Material gold = GetRuntimeMaterial("SantaCockpitGold", new Color(0.92f, 0.57f, 0.08f), 0.62f);
        Material runner = GetRuntimeMaterial("SantaCockpitRunner", new Color(0.18f, 0.20f, 0.23f), 0.70f);

        // Enclosed sleigh body: unlike the ordinary open slat sled, the Santa
        // model has a deep red tub, raised sides, cream padding and gold trim.
        CreatePrimitive("Santa Solid Floor", PrimitiveType.Cube, parent,
            new Vector3(0f, 0.31f, -0.20f), new Vector3(1.18f, 0.11f, 1.92f), red);
        CreatePrimitive("Santa Left Raised Side", PrimitiveType.Cube, parent,
            new Vector3(-0.61f, 0.60f, -0.16f), new Vector3(0.12f, 0.62f, 1.92f), red);
        CreatePrimitive("Santa Right Raised Side", PrimitiveType.Cube, parent,
            new Vector3(0.61f, 0.60f, -0.16f), new Vector3(0.12f, 0.62f, 1.92f), red);
        CreateGlovePart("Santa Curved Front", PrimitiveType.Cube, parent,
            new Vector3(0f, 0.57f, 0.63f), new Vector3(1.25f, 0.50f, 0.12f),
            new Vector3(-10f, 0f, 0f), deepRed);

        CreatePrimitive("Santa Left Cream Rim", PrimitiveType.Cube, parent,
            new Vector3(-0.61f, 0.94f, -0.13f), new Vector3(0.16f, 0.10f, 1.92f), cream);
        CreatePrimitive("Santa Right Cream Rim", PrimitiveType.Cube, parent,
            new Vector3(0.61f, 0.94f, -0.13f), new Vector3(0.16f, 0.10f, 1.92f), cream);
        CreatePrimitive("Santa Front Cream Rim", PrimitiveType.Cube, parent,
            new Vector3(0f, 0.84f, 0.68f), new Vector3(1.30f, 0.11f, 0.10f), cream);

        CreatePrimitive("Santa Front Gold Trim", PrimitiveType.Cube, parent,
            new Vector3(0f, 0.48f, 0.70f), new Vector3(1.33f, 0.055f, 0.08f), gold);
        CreatePrimitive("Santa Left Gold Rail", PrimitiveType.Cube, parent,
            new Vector3(-0.69f, 0.50f, -0.18f), new Vector3(0.045f, 0.045f, 1.92f), gold);
        CreatePrimitive("Santa Right Gold Rail", PrimitiveType.Cube, parent,
            new Vector3(0.69f, 0.50f, -0.18f), new Vector3(0.045f, 0.045f, 1.92f), gold);

        CreatePrimitive("Santa Left Runner", PrimitiveType.Cube, parent,
            new Vector3(-0.58f, 0.15f, -0.18f), new Vector3(0.08f, 0.08f, 2.25f), runner);
        CreatePrimitive("Santa Right Runner", PrimitiveType.Cube, parent,
            new Vector3(0.58f, 0.15f, -0.18f), new Vector3(0.08f, 0.08f, 2.25f), runner);

        CreatePrimitive("Santa Padded Handle", PrimitiveType.Cube, parent,
            new Vector3(0f, 0.92f, -0.56f), new Vector3(1.32f, 0.085f, 0.095f), cream);
        CreateGlovePart("Santa Left Handle Upright", PrimitiveType.Cube, parent,
            new Vector3(-0.59f, 0.69f, -0.38f), new Vector3(0.075f, 0.48f, 0.075f),
            new Vector3(-14f, 0f, 0f), gold);
        CreateGlovePart("Santa Right Handle Upright", PrimitiveType.Cube, parent,
            new Vector3(0.59f, 0.69f, -0.38f), new Vector3(0.075f, 0.48f, 0.075f),
            new Vector3(-14f, 0f, 0f), gold);
    }

    private void BuildVisibleRideLantern(Transform parent)
    {
        Material frame = GetRuntimeMaterial("EquippedLanternFrame", new Color(0.11f, 0.065f, 0.028f), 0.48f);
        Material glass = GetRuntimeMaterial("EquippedLanternGlass", new Color(1f, 0.38f, 0.035f), 0.58f);
        CreatePrimitive("Visible Lantern Glass", PrimitiveType.Sphere, parent,
            Vector3.zero, new Vector3(0.19f, 0.25f, 0.16f), glass);
        CreatePrimitive("Visible Lantern Top", PrimitiveType.Cube, parent,
            new Vector3(0f, 0.27f, 0f), new Vector3(0.25f, 0.055f, 0.21f), frame);
        CreatePrimitive("Visible Lantern Bottom", PrimitiveType.Cube, parent,
            new Vector3(0f, -0.27f, 0f), new Vector3(0.25f, 0.055f, 0.21f), frame);
        for (int side = -1; side <= 1; side += 2)
        {
            CreatePrimitive("Visible Lantern Side", PrimitiveType.Cube, parent,
                new Vector3(side * 0.13f, 0f, 0f), new Vector3(0.035f, 0.53f, 0.035f), frame);
        }
        CreateGlovePart("Visible Lantern Handle", PrimitiveType.Capsule, parent,
            new Vector3(0f, 0.39f, 0f), new Vector3(0.045f, 0.15f, 0.045f),
            new Vector3(0f, 0f, 90f), frame);
    }

    private Color EquippedSledColor()
    {
        return customization?.equippedSledBody switch
        {
            MushCustomizationIds.SledRed => new Color(0.68f, 0.07f, 0.045f),
            MushCustomizationIds.SledBlue => new Color(0.055f, 0.25f, 0.62f),
            MushCustomizationIds.SledBlack => new Color(0.07f, 0.075f, 0.085f),
            MushCustomizationIds.SledSanta => new Color(0.76f, 0.055f, 0.035f),
            _ => new Color(0.52f, 0.25f, 0.075f),
        };
    }

    private static GameObject CreateGlovePart(
        string objectName,
        PrimitiveType type,
        Transform parent,
        Vector3 localPosition,
        Vector3 localScale,
        Vector3 localEuler,
        Material material)
    {
        GameObject primitive = CreatePrimitive(objectName, type, parent, localPosition, localScale, material);
        primitive.transform.localRotation = Quaternion.Euler(localEuler);
        return primitive;
    }

    private void BuildFallbackSled(Transform parent)
    {
        Material wood = GetRuntimeMaterial("FallbackSledWood", new Color(0.36f, 0.16f, 0.055f), 0.22f);
        Material metal = GetRuntimeMaterial("FallbackSledMetal", new Color(0.34f, 0.39f, 0.44f), 0.65f);
        CreatePrimitive("Sled Deck", PrimitiveType.Cube, parent, new Vector3(0f, 0.24f, -0.2f), new Vector3(1.15f, 0.12f, 1.9f), wood);
        CreatePrimitive("Left Runner", PrimitiveType.Cube, parent, new Vector3(-0.48f, 0.07f, -0.1f), new Vector3(0.08f, 0.08f, 2.15f), metal);
        CreatePrimitive("Right Runner", PrimitiveType.Cube, parent, new Vector3(0.48f, 0.07f, -0.1f), new Vector3(0.08f, 0.08f, 2.15f), metal);
    }

    private GameObject BuildFallbackDog(Transform parent, bool malamute)
    {
        GameObject root = new(malamute ? "Visible Malamute Visual" : "Visible Husky Visual");
        root.transform.SetParent(parent, false);
        Material darkCoat = GetRuntimeMaterial(
            malamute ? "VisibleMalamuteDark" : "VisibleHuskyDark",
            malamute ? new Color(0.20f, 0.12f, 0.075f) : new Color(0.12f, 0.16f, 0.20f),
            0.14f);
        Material cream = GetRuntimeMaterial(
            malamute ? "VisibleMalamuteCream" : "VisibleHuskyCream",
            malamute ? new Color(0.76f, 0.64f, 0.46f) : new Color(0.86f, 0.88f, 0.86f),
            0.12f);
        Material harness = GetRuntimeMaterial(
            malamute ? "VisibleMalamuteHarness" : "VisibleHuskyHarness",
            malamute ? new Color(0.82f, 0.18f, 0.055f) : new Color(0.04f, 0.38f, 0.72f),
            0.22f);
        Material nose = GetRuntimeMaterial("VisibleDogNose", new Color(0.025f, 0.018f, 0.015f), 0.30f);
        Material eye = GetRuntimeMaterial("VisibleDogEye", new Color(0.035f, 0.020f, 0.012f), 0.55f);

        CreateGlovePart("Long Body", PrimitiveType.Capsule, root.transform,
            new Vector3(0f, 0.64f, 0f), new Vector3(0.38f, 0.57f, 0.38f),
            new Vector3(90f, 0f, 0f), darkCoat);
        CreatePrimitive("Cream Chest", PrimitiveType.Sphere, root.transform,
            new Vector3(0f, 0.71f, 0.34f), new Vector3(0.42f, 0.52f, 0.38f), cream);
        CreatePrimitive("Head", PrimitiveType.Sphere, root.transform,
            new Vector3(0f, 0.99f, 0.62f), new Vector3(0.39f, 0.41f, 0.38f), darkCoat);
        CreatePrimitive("Muzzle", PrimitiveType.Sphere, root.transform,
            new Vector3(0f, 0.91f, 0.89f), new Vector3(0.25f, 0.19f, 0.26f), cream);
        CreatePrimitive("Nose", PrimitiveType.Sphere, root.transform,
            new Vector3(0f, 0.94f, 1.08f), new Vector3(0.105f, 0.08f, 0.08f), nose);

        CreateGlovePart("Left Ear", PrimitiveType.Cube, root.transform,
            new Vector3(-0.18f, 1.25f, 0.61f), new Vector3(0.13f, 0.27f, 0.12f),
            new Vector3(8f, 0f, -12f), darkCoat);
        CreateGlovePart("Right Ear", PrimitiveType.Cube, root.transform,
            new Vector3(0.18f, 1.25f, 0.61f), new Vector3(0.13f, 0.27f, 0.12f),
            new Vector3(8f, 0f, 12f), darkCoat);
        CreatePrimitive("Left Eye", PrimitiveType.Sphere, root.transform,
            new Vector3(-0.13f, 1.04f, 0.95f), new Vector3(0.045f, 0.045f, 0.035f), eye);
        CreatePrimitive("Right Eye", PrimitiveType.Sphere, root.transform,
            new Vector3(0.13f, 1.04f, 0.95f), new Vector3(0.045f, 0.045f, 0.035f), eye);

        for (int side = -1; side <= 1; side += 2)
        for (int row = -1; row <= 1; row += 2)
        {
            float x = side * 0.25f;
            float z = row * 0.32f;
            CreatePrimitive($"Leg {side} {row}", PrimitiveType.Capsule, root.transform,
                new Vector3(x, 0.31f, z), new Vector3(0.105f, 0.27f, 0.105f), darkCoat);
            CreatePrimitive($"Paw {side} {row}", PrimitiveType.Sphere, root.transform,
                new Vector3(x, 0.08f, z + 0.055f), new Vector3(0.14f, 0.08f, 0.19f), cream);
        }

        CreateGlovePart("Raised Tail", PrimitiveType.Capsule, root.transform,
            new Vector3(0f, 0.84f, -0.63f), new Vector3(0.13f, 0.36f, 0.13f),
            new Vector3(-42f, 0f, 0f), darkCoat);
        CreatePrimitive("Harness Back", PrimitiveType.Cube, root.transform,
            new Vector3(0f, 0.91f, 0.06f), new Vector3(0.44f, 0.075f, 0.45f), harness);
        CreateGlovePart("Harness Chest", PrimitiveType.Cube, root.transform,
            new Vector3(0f, 0.69f, 0.38f), new Vector3(0.45f, 0.065f, 0.34f),
            new Vector3(28f, 0f, 0f), harness);
        return root;
    }

    private static GameObject CreatePrimitive(
        string objectName,
        PrimitiveType type,
        Transform parent,
        Vector3 localPosition,
        Vector3 localScale,
        Material material)
    {
        GameObject primitive = GameObject.CreatePrimitive(type);
        primitive.name = objectName;
        primitive.transform.SetParent(parent, false);
        primitive.transform.localPosition = localPosition;
        primitive.transform.localScale = localScale;
        primitive.GetComponent<Renderer>().sharedMaterial = material;
        Collider collider = primitive.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);
        return primitive;
    }

    private void ApplySledMaterials(GameObject root)
    {
        string sledKey = customization?.equippedSledBody ?? "natural";
        Color baseColor = EquippedSledColor();
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] slots = renderer.sharedMaterials;
            for (int index = 0; index < slots.Length; index++)
            {
                string source = slots[index] != null ? slots[index].name.ToLowerInvariant() : string.Empty;
                if (sledKey == MushCustomizationIds.SledSanta && source.Contains("santa_gold"))
                    slots[index] = GetRuntimeMaterial("SantaSledGold", new Color(0.92f, 0.57f, 0.08f), 0.62f);
                else if (sledKey == MushCustomizationIds.SledSanta && source.Contains("santa_cream"))
                    slots[index] = GetRuntimeMaterial("SantaSledCream", new Color(0.92f, 0.82f, 0.63f), 0.25f);
                else if (sledKey == MushCustomizationIds.SledSanta && source.Contains("santa_red"))
                    slots[index] = GetRuntimeMaterial("SantaSledRed", new Color(0.72f, 0.025f, 0.035f), 0.28f);
                else if (source.Contains("metal"))
                    slots[index] = GetRuntimeMaterial("SledMetal_" + sledKey, new Color(0.33f, 0.38f, 0.43f), 0.68f);
                else if (source.Contains("woodlight"))
                    slots[index] = GetRuntimeMaterial("SledWoodLight_" + sledKey, Color.Lerp(baseColor, Color.white, 0.22f), 0.22f);
                else
                    slots[index] = GetRuntimeMaterial("SledWood_" + sledKey, baseColor, 0.20f);
            }
            renderer.sharedMaterials = slots;
        }
    }

    private void ApplyDogMaterials(GameObject root, bool malamute)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] slots = renderer.sharedMaterials;
            for (int index = 0; index < slots.Length; index++)
            {
                string source = slots[index] != null ? slots[index].name.ToLowerInvariant() : string.Empty;
                if (source.Contains("tongue"))
                    slots[index] = GetRuntimeMaterial("DogTongue", new Color(0.67f, 0.16f, 0.19f), 0.28f);
                else if (source.Contains("eye") || source.Contains("iris"))
                    slots[index] = GetRuntimeMaterial(malamute ? "BrownIris" : "BlueIris",
                        malamute ? new Color(0.48f, 0.20f, 0.045f) : new Color(0.08f, 0.42f, 0.78f), 0.55f);
                else if (source.Contains("collar"))
                    slots[index] = GetRuntimeMaterial("DogBlueCollar", new Color(0.035f, 0.30f, 0.72f), 0.28f);
                else if (source.Contains("innerear"))
                    slots[index] = GetRuntimeMaterial("DogInnerEar", new Color(0.56f, 0.24f, 0.23f), 0.18f);
                else if (source.Contains("sclera") || source.Contains("lightcoat") ||
                         source.Contains("white") || source.Contains("cream"))
                    slots[index] = GetRuntimeMaterial("DogCream", new Color(0.78f, 0.76f, 0.70f), 0.14f);
                else if (source.Contains("black"))
                    slots[index] = GetRuntimeMaterial("DogBlack", new Color(0.018f, 0.022f, 0.026f), 0.20f);
                else if (source.Contains("darkcoat") || source.Contains("darkdetail") || source.Contains("dark"))
                    slots[index] = GetRuntimeMaterial(malamute ? "MalamuteDark" : "HuskyDark",
                        malamute ? new Color(0.075f, 0.052f, 0.042f) : new Color(0.035f, 0.045f, 0.055f), 0.16f);
                else
                    slots[index] = GetRuntimeMaterial(malamute ? "MalamuteCoat" : "HuskyCoat",
                        malamute ? new Color(0.29f, 0.31f, 0.34f) : new Color(0.25f, 0.30f, 0.35f), 0.16f);
            }
            renderer.sharedMaterials = slots;
        }
    }

    private Material GetRuntimeMaterial(string key, Color color, float smoothness)
    {
        if (runtimeMaterials.TryGetValue(key, out Material material))
            return material;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        material = new Material(shader) { name = "Runtime " + key };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        material.enableInstancing = true;
        runtimeMaterials[key] = material;
        return material;
    }

    private static void DisableModelColliders(GameObject root)
    {
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
    }

    private static Transform CreateAnchor(string anchorName, Transform parent, Vector3 localPosition)
    {
        GameObject anchor = new(anchorName);
        anchor.transform.SetParent(parent, false);
        anchor.transform.localPosition = localPosition;
        return anchor.transform;
    }

    private static Transform FindDeepChild(Transform root, string targetName)
    {
        if (root == null)
            return null;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                return child;
        }
        return null;
    }

    private static Transform FindChildContaining(Transform root, string namePart)
    {
        if (root == null)
            return null;

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name.Contains(namePart, StringComparison.OrdinalIgnoreCase))
                return child;
        }

        return null;
    }

    private void OnDestroy()
    {
        if (ridePaused)
            Time.timeScale = 1f;
        if (resultStarMesh != null)
            Destroy(resultStarMesh);
        foreach (Material material in runtimeMaterials.Values)
        {
            if (material != null)
                Destroy(material);
        }
        runtimeMaterials.Clear();
    }
}
