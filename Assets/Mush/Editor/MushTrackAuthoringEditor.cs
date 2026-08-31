using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[CustomEditor(typeof(MushTrackAuthoring))]
public sealed class MushTrackAuthoringEditor : Editor
{
    private readonly List<Vector3> previewRoute = new();
    private readonly List<Vector3> previewControlPoints = new();
    private SerializedProperty presetProperty;
    private SerializedProperty targetMapRootNameProperty;
    private SerializedProperty useEditablePathProperty;
    private SerializedProperty sampleSpacingProperty;
    private SerializedProperty overrideTrackWidthsProperty;
    private SerializedProperty roadHalfWidthProperty;
    private SerializedProperty terrainHalfWidthProperty;
    private int selectedPoint = -1;

    private void OnEnable()
    {
        presetProperty = serializedObject.FindProperty("preset");
        targetMapRootNameProperty = serializedObject.FindProperty("targetMapRootName");
        useEditablePathProperty = serializedObject.FindProperty("useEditablePath");
        sampleSpacingProperty = serializedObject.FindProperty("sampleSpacing");
        overrideTrackWidthsProperty = serializedObject.FindProperty("overrideTrackWidths");
        roadHalfWidthProperty = serializedObject.FindProperty("roadHalfWidth");
        terrainHalfWidthProperty = serializedObject.FindProperty("terrainHalfWidth");
    }

    public override void OnInspectorGUI()
    {
        MushTrackAuthoring authoring = (MushTrackAuthoring)target;
        serializedObject.Update();

        EditorGUILayout.HelpBox(
            "도로·지형·나무·산은 미리보기가 아니라 씬에 저장된 실제 게임 오브젝트입니다. 위치·회전·크기와 자식 구성을 직접 편집할 수 있고, 플레이 모드는 이 계층을 다시 만들지 않고 그대로 사용합니다. 새 모델은 'SCENE CONTENT - Add Models Here' 아래에 두면 트랙 재생성 후에도 보존됩니다.",
            MessageType.Info);
        EditorGUILayout.PropertyField(presetProperty, new GUIContent("기본 트랙 종류"));
        EditorGUILayout.PropertyField(targetMapRootNameProperty, new GUIContent("대상 맵 루트 이름"));
        EditorGUILayout.PropertyField(sampleSpacingProperty, new GUIContent("메시 샘플 간격 (m)"));
        EditorGUILayout.PropertyField(overrideTrackWidthsProperty, new GUIContent("트랙 폭 직접 지정"));
        if (overrideTrackWidthsProperty.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(roadHalfWidthProperty, new GUIContent("도로 반폭 (m)"));
            EditorGUILayout.PropertyField(terrainHalfWidthProperty, new GUIContent("지형 반폭 (m)"));
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(8f);
        bool wasEditable = useEditablePathProperty.boolValue;
        EditorGUILayout.LabelField("경로 상태", wasEditable ? $"편집 경로 ({authoring.ControlPointCount}개 포인트)" : "기본 프로토타입 경로");

        if (!wasEditable)
        {
            if (authoring.ControlPointCount >= 2 && GUILayout.Button("보존된 편집 포인트 다시 사용"))
            {
                serializedObject.ApplyModifiedProperties();
                Undo.RecordObject(authoring, "Enable Mush Editable Track");
                authoring.SetEditablePathEnabled(true);
                selectedPoint = 0;
                EditorUtility.SetDirty(authoring);
                MushTrackEditorWorldPreview.RequestRebuild(authoring);
                serializedObject.Update();
            }
            if (GUILayout.Button(authoring.ControlPointCount >= 2
                    ? "기본 트랙으로 편집 포인트 다시 만들기"
                    : "기본 트랙을 편집 포인트로 변환"))
            {
                serializedObject.ApplyModifiedProperties();
                Undo.RecordObject(authoring, "Convert Mush Track To Editable Path");
                authoring.BakeDefaultPath();
                selectedPoint = 0;
                EditorUtility.SetDirty(authoring);
                MushTrackEditorWorldPreview.RequestRebuild(authoring);
                serializedObject.Update();
            }
        }
        else
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("선택 뒤에 포인트 추가"))
                {
                    serializedObject.ApplyModifiedProperties();
                    Undo.RecordObject(authoring, "Add Mush Track Point");
                    selectedPoint = authoring.InsertControlPointAfter(
                        selectedPoint >= 0 ? selectedPoint : authoring.ControlPointCount - 1);
                    EditorUtility.SetDirty(authoring);
                    MushTrackEditorWorldPreview.RequestRebuild(authoring);
                    serializedObject.Update();
                }
                using (new EditorGUI.DisabledScope(authoring.ControlPointCount <= 2 || selectedPoint < 0))
                {
                    if (GUILayout.Button("선택 포인트 삭제"))
                    {
                        serializedObject.ApplyModifiedProperties();
                        Undo.RecordObject(authoring, "Delete Mush Track Point");
                        selectedPoint = authoring.RemoveControlPoint(selectedPoint);
                        EditorUtility.SetDirty(authoring);
                        MushTrackEditorWorldPreview.RequestRebuild(authoring);
                        serializedObject.Update();
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("경로 방향 뒤집기"))
                {
                    serializedObject.ApplyModifiedProperties();
                    Undo.RecordObject(authoring, "Reverse Mush Track");
                    selectedPoint = authoring.ReverseControlPoints(selectedPoint);
                    EditorUtility.SetDirty(authoring);
                    MushTrackEditorWorldPreview.RequestRebuild(authoring);
                    serializedObject.Update();
                }
                if (GUILayout.Button("기본 형태로 다시 만들기"))
                {
                    serializedObject.ApplyModifiedProperties();
                    Undo.RecordObject(authoring, "Reset Mush Track Path");
                    authoring.BakeDefaultPath();
                    selectedPoint = 0;
                    EditorUtility.SetDirty(authoring);
                    MushTrackEditorWorldPreview.RequestRebuild(authoring);
                    serializedObject.Update();
                }
            }

            if (GUILayout.Button("편집 경로 사용 중지 (포인트는 보존)"))
            {
                serializedObject.ApplyModifiedProperties();
                Undo.RecordObject(authoring, "Disable Mush Editable Track");
                authoring.SetEditablePathEnabled(false);
                EditorUtility.SetDirty(authoring);
                MushTrackEditorWorldPreview.RequestRebuild(authoring);
                serializedObject.Update();
            }
        }

        Transform mapRoot = authoring.ResolveMapRoot();
        if (mapRoot == null)
        {
            EditorGUILayout.HelpBox(
                "대상 맵 루트를 찾지 못했습니다. 씬의 맵 루트 이름과 '대상 맵 루트 이름'을 맞춰 주세요.",
                MessageType.Warning);
        }
        else
        {
            authoring.CopyPreviewRoute(previewRoute);
            EditorGUILayout.LabelField("예상 트랙 길이", $"{CalculateLength(previewRoute):0.0} m");
            EditorGUILayout.HelpBox(
                "씬 뷰의 원형 포인트를 선택해 위치와 높이를 조정합니다. 기본 경로의 연두색 포인트를 처음 움직이면 편집 경로로 자동 변환됩니다.",
                MessageType.None);
        }

        if (serializedObject.ApplyModifiedProperties())
            MushTrackEditorWorldPreview.RequestRebuild(authoring);
    }

    private void OnSceneGUI()
    {
        MushTrackAuthoring authoring = (MushTrackAuthoring)target;
        Transform mapRoot = authoring.ResolveMapRoot();
        if (mapRoot == null)
            return;

        authoring.CopyEditableControlPointPreview(previewControlPoints);
        if (previewControlPoints.Count < 2)
            return;

        Handles.zTest = CompareFunction.LessEqual;
        authoring.CopyPreviewRoute(previewRoute);
        Vector3[] worldRoute = new Vector3[previewRoute.Count];
        for (int index = 0; index < previewRoute.Count; index++)
            worldRoute[index] = mapRoot.TransformPoint(previewRoute[index] + Vector3.up * 0.16f);
        Handles.color = new Color(0.15f, 0.9f, 1f, 0.75f);
        Handles.DrawAAPolyLine(2.5f, worldRoute);

        for (int index = 0; index < previewControlPoints.Count; index++)
        {
            Vector3 worldPoint = mapRoot.TransformPoint(previewControlPoints[index]);
            float size = HandleUtility.GetHandleSize(worldPoint) * 0.055f;
            Handles.color = index == selectedPoint
                ? new Color(1f, 0.72f, 0.12f)
                : authoring.UsesEditablePath ? new Color(0.15f, 0.9f, 1f) : new Color(0.35f, 0.95f, 0.65f);
            if (Handles.Button(worldPoint, Quaternion.identity, size, size * 1.3f, Handles.SphereHandleCap))
            {
                selectedPoint = index;
                Repaint();
            }
        }

        if (selectedPoint < 0 || selectedPoint >= previewControlPoints.Count)
            return;

        Vector3 selectedWorldPoint = mapRoot.TransformPoint(previewControlPoints[selectedPoint]);
        Handles.Label(selectedWorldPoint + Vector3.up * HandleUtility.GetHandleSize(selectedWorldPoint) * 0.14f,
            $"트랙 포인트 {selectedPoint + 1}/{previewControlPoints.Count}");
        EditorGUI.BeginChangeCheck();
        Vector3 movedWorldPoint = Handles.PositionHandle(selectedWorldPoint, Quaternion.identity);
        if (!EditorGUI.EndChangeCheck())
            return;

        Undo.RecordObject(authoring, "Move Mush Track Point");
        if (!authoring.UsesEditablePath)
            authoring.BakeDefaultPath();
        authoring.SetControlPoint(selectedPoint, mapRoot.InverseTransformPoint(movedWorldPoint));
        EditorUtility.SetDirty(authoring);
        MushTrackEditorWorldPreview.RequestRebuild(authoring);
    }

    private static float CalculateLength(IReadOnlyList<Vector3> points)
    {
        float length = 0f;
        for (int index = 1; index < points.Count; index++)
            length += Vector3.Distance(points[index - 1], points[index]);
        return length;
    }
}

[InitializeOnLoad]
public static class MushTrackEditorWorldPreview
{
    private static readonly HashSet<MushTrackAuthoring> PendingTracks = new();
    private static bool rebuildScheduled;
    private static bool rebuilding;
    private const string GeneratedAssetFolder = "Assets/Mush/GeneratedMaps";
    private static readonly string[] GameplayScenePaths =
    {
        "Assets/Scenes/snow.unity",
        "Assets/Scenes/Tree.unity",
        "Assets/Scenes/SharpCurve.unity",
    };

    static MushTrackEditorWorldPreview()
    {
        EditorSceneManager.sceneOpened += HandleSceneOpened;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
        EditorApplication.delayCall += RebuildAllOpenScenePreviews;
        EditorApplication.delayCall += BakeMissingProjectMaps;
    }

    public static void RequestRebuild(MushTrackAuthoring authoring)
    {
        if (authoring == null || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        PendingTracks.Add(authoring);
        SchedulePendingRebuild();
    }

    private static void HandleSceneOpened(Scene scene, OpenSceneMode mode)
    {
        EditorApplication.delayCall += RebuildAllOpenScenePreviews;
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += RebuildAllOpenScenePreviews;
    }

    private static void SchedulePendingRebuild()
    {
        if (rebuildScheduled)
            return;
        rebuildScheduled = true;
        EditorApplication.delayCall += RebuildPending;
    }

    private static void RebuildPending()
    {
        rebuildScheduled = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (GUIUtility.hotControl != 0)
        {
            SchedulePendingRebuild();
            return;
        }

        MushTrackAuthoring[] tracks = new MushTrackAuthoring[PendingTracks.Count];
        PendingTracks.CopyTo(tracks);
        PendingTracks.Clear();
        for (int index = 0; index < tracks.Length; index++)
        {
            MushTrackAuthoring authoring = tracks[index];
            if (authoring != null)
                RebuildSceneWorld(authoring, false);
        }
    }

    private static void RebuildAllOpenScenePreviews()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        MushTrackAuthoring[] tracks = Object.FindObjectsByType<MushTrackAuthoring>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int index = 0; index < tracks.Length; index++)
        {
            MushTrackAuthoring authoring = tracks[index];
            Transform mapRoot = authoring != null ? authoring.ResolveMapRoot() : null;
            if (mapRoot != null && mapRoot.Find(MushCurvedMapRuntime.GeneratedWorldRootName) == null)
                RebuildSceneWorld(authoring, true);
        }
    }

    private static void BakeMissingProjectMaps()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || rebuilding)
            return;

        Scene activeScene = SceneManager.GetActiveScene();
        for (int sceneIndex = 0; sceneIndex < GameplayScenePaths.Length; sceneIndex++)
        {
            string scenePath = GameplayScenePaths[sceneIndex];
            Scene scene = EditorSceneManager.GetSceneByPath(scenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            if (!scene.IsValid() || !scene.isLoaded)
                continue;

            MushTrackAuthoring authoring = FindTrackInScene(scene);
            Transform mapRoot = authoring != null ? authoring.ResolveMapRoot() : null;
            if (authoring != null && mapRoot != null &&
                mapRoot.Find(MushCurvedMapRuntime.GeneratedWorldRootName) == null)
            {
                SceneManager.SetActiveScene(scene);
                RebuildSceneWorld(authoring, true);
                if (activeScene.IsValid() && activeScene.isLoaded)
                    SceneManager.SetActiveScene(activeScene);
            }

            if (opened && scene != activeScene)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    [MenuItem("Mush/Maps/Bake All Gameplay Maps Into Scenes")]
    public static void BakeAllGameplayMapsIntoScenes()
    {
        BakeAllGameplayMaps(true);
    }

    [MenuItem("Mush/Maps/Bake Current Map Into This Scene")]
    public static void BakeCurrentMapIntoScene()
    {
        MushTrackAuthoring authoring = FindTrackInScene(SceneManager.GetActiveScene());
        if (authoring == null)
        {
            EditorUtility.DisplayDialog("Mush Map", "현재 신에서 TRACK EDIT 오브젝트를 찾지 못했습니다.", "확인");
            return;
        }

        RebuildSceneWorld(authoring, true);
        AssetDatabase.SaveAssets();
    }

    public static void BakeAllMapsFromCommandLine()
    {
        BakeAllGameplayMaps(false);
    }

    private static void BakeAllGameplayMaps(bool restorePreviousScene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        string previousScenePath = SceneManager.GetActiveScene().path;
        for (int sceneIndex = 0; sceneIndex < GameplayScenePaths.Length; sceneIndex++)
        {
            string scenePath = GameplayScenePaths[sceneIndex];
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            MushTrackAuthoring authoring = FindTrackInScene(scene);
            if (authoring == null)
                throw new MissingReferenceException($"TRACK EDIT authoring object is missing from {scenePath}.");

            RebuildSceneWorld(authoring, true);
            EditorSceneManager.SaveScene(scene);
        }

        if (restorePreviousScene && !string.IsNullOrEmpty(previousScenePath))
            EditorSceneManager.OpenScene(previousScenePath, OpenSceneMode.Single);

        AssetDatabase.SaveAssets();
        Debug.Log("[Mush] Baked all gameplay maps into their scenes. Play mode now uses the saved hierarchy unchanged.");
    }

    private static MushTrackAuthoring FindTrackInScene(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            MushTrackAuthoring authoring = root.GetComponentInChildren<MushTrackAuthoring>(true);
            if (authoring != null)
                return authoring;
        }
        return null;
    }

    private static void RebuildSceneWorld(MushTrackAuthoring authoring, bool saveScene)
    {
        if (rebuilding || authoring == null || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        Transform mapRoot = authoring.ResolveMapRoot();
        if (mapRoot == null)
            return;

        rebuilding = true;
        try
        {
            MushCurvedMapRuntime runtime = mapRoot.GetComponent<MushCurvedMapRuntime>();
            if (runtime == null)
                runtime = Undo.AddComponent<MushCurvedMapRuntime>(mapRoot.gameObject);

            runtime.RebuildSceneWorld();
            Transform generatedRoot = mapRoot.Find(MushCurvedMapRuntime.GeneratedWorldRootName);
            if (generatedRoot == null)
                throw new MissingReferenceException($"Baked world root was not created for {mapRoot.name}.");

            PersistGeneratedResources(runtime, generatedRoot, authoring.gameObject.scene.name);
            EditorUtility.SetDirty(runtime);
            EditorSceneManager.MarkSceneDirty(authoring.gameObject.scene);
            if (saveScene)
                EditorSceneManager.SaveScene(authoring.gameObject.scene);
        }
        finally
        {
            rebuilding = false;
        }
        SceneView.RepaintAll();
    }

    private static void PersistGeneratedResources(
        MushCurvedMapRuntime runtime,
        Transform generatedRoot,
        string sceneName)
    {
        EnsureGeneratedAssetFolder();
        string assetPath = $"{GeneratedAssetFolder}/{sceneName}_BakedMapAssets.asset";
        AssetDatabase.DeleteAsset(assetPath);

        MushBakedMapAssetContainer container = ScriptableObject.CreateInstance<MushBakedMapAssetContainer>();
        container.name = $"{sceneName} Baked Map Assets";
        AssetDatabase.CreateAsset(container, assetPath);

        HashSet<Object> resources = new();
        foreach (MeshFilter filter in generatedRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh != null && !EditorUtility.IsPersistent(filter.sharedMesh))
                resources.Add(filter.sharedMesh);
        }
        foreach (MeshCollider collider in generatedRoot.GetComponentsInChildren<MeshCollider>(true))
        {
            if (collider.sharedMesh != null && !EditorUtility.IsPersistent(collider.sharedMesh))
                resources.Add(collider.sharedMesh);
        }
        foreach (Renderer renderer in generatedRoot.GetComponentsInChildren<Renderer>(true))
        foreach (Material material in renderer.sharedMaterials)
        {
            if (material != null && !EditorUtility.IsPersistent(material))
                resources.Add(material);
        }
        if (RenderSettings.skybox != null && !EditorUtility.IsPersistent(RenderSettings.skybox))
            resources.Add(RenderSettings.skybox);

        int resourceIndex = 0;
        foreach (Object resource in resources)
        {
            resource.name = $"{resourceIndex++:D3}_{resource.name}";
            AssetDatabase.AddObjectToAsset(resource, container);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        runtime.ReleaseBakedResourceOwnership();
    }

    private static void EnsureGeneratedAssetFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Mush"))
            AssetDatabase.CreateFolder("Assets", "Mush");
        if (!AssetDatabase.IsValidFolder(GeneratedAssetFolder))
            AssetDatabase.CreateFolder("Assets/Mush", "GeneratedMaps");
    }
}
