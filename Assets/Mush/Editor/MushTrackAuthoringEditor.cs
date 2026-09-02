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
    private SerializedProperty deformableRoadModuleProperty;
    private SerializedProperty useDeformableRoadModuleProperty;
    private SerializedProperty customRoadVisualProperty;
    private SerializedProperty customTerrainVisualProperty;
    private SerializedProperty roadMaterialOverrideProperty;
    private SerializedProperty terrainMaterialOverrideProperty;
    private int selectedPoint = -1;
    private bool editMode;

    private void OnEnable()
    {
        presetProperty = serializedObject.FindProperty("preset");
        targetMapRootNameProperty = serializedObject.FindProperty("targetMapRootName");
        useEditablePathProperty = serializedObject.FindProperty("useEditablePath");
        sampleSpacingProperty = serializedObject.FindProperty("sampleSpacing");
        overrideTrackWidthsProperty = serializedObject.FindProperty("overrideTrackWidths");
        roadHalfWidthProperty = serializedObject.FindProperty("roadHalfWidth");
        terrainHalfWidthProperty = serializedObject.FindProperty("terrainHalfWidth");
        deformableRoadModuleProperty = serializedObject.FindProperty("deformableRoadModule");
        useDeformableRoadModuleProperty = serializedObject.FindProperty("useDeformableRoadModule");
        customRoadVisualProperty = serializedObject.FindProperty("customRoadVisual");
        customTerrainVisualProperty = serializedObject.FindProperty("customTerrainVisual");
        roadMaterialOverrideProperty = serializedObject.FindProperty("roadMaterialOverride");
        terrainMaterialOverrideProperty = serializedObject.FindProperty("terrainMaterialOverride");
        editMode = true;
    }

    public override void OnInspectorGUI()
    {
        MushTrackAuthoring authoring = (MushTrackAuthoring)target;
        serializedObject.Update();

        EditorGUILayout.HelpBox(
            "포인트 편집 중에는 도로·지형만 갱신되고 나무·바위 같은 주변 오브젝트는 움직이지 않습니다. 주변 오브젝트를 현재 경로에 다시 맞추려면 아래의 별도 버튼을 사용해 주세요. 새 모델은 'SCENE CONTENT - Add Models Here' 아래에 두면 항상 보존됩니다.",
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
        EditorGUILayout.LabelField("최종 도로·지형 모델", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(
            useDeformableRoadModuleProperty,
            new GUIContent("경로 변형 도로 모델 사용"));
        EditorGUILayout.PropertyField(
            deformableRoadModuleProperty,
            new GUIContent("경로 변형 도로 모듈 (FBX/Prefab)"));
        if (!useDeformableRoadModuleProperty.boolValue)
        {
            EditorGUILayout.HelpBox(
                "현재는 원래의 매끈한 스크립트 생성 도로가 기본으로 표시됩니다. 위 옵션을 켰을 때만 연결된 FBX/Prefab 도로가 경로를 따라 변형되어 표시됩니다.",
                MessageType.None);
        }

        // 슬롯을 바꾸기 직전의 씬 오브젝트를 기억합니다.
        // SerializedProperty는 Inspector에서 값을 바꾸는 즉시 새 참조를 갖기 때문에, 이전 참조를 먼저 보관해야 None/교체 시 옛 모델을 숨길 수 있습니다.
        GameObject previousRoadVisual = customRoadVisualProperty.objectReferenceValue as GameObject;
        GameObject previousTerrainVisual = customTerrainVisualProperty.objectReferenceValue as GameObject;

        EditorGUILayout.PropertyField(
            customRoadVisualProperty,
            new GUIContent("도로 모델 오브젝트 (씬)"));
        EditorGUILayout.PropertyField(
            customTerrainVisualProperty,
            new GUIContent("지형 모델 오브젝트 (씬)"));

        // Inspector에 현재 표시된 새 참조를 읽습니다. 이 값은 아직 ApplyModifiedProperties 전이어도 SerializedProperty 안에는 반영되어 있습니다.
        GameObject currentRoadVisual = customRoadVisualProperty.objectReferenceValue as GameObject;
        GameObject currentTerrainVisual = customTerrainVisualProperty.objectReferenceValue as GameObject;
        bool sceneVisualAssignmentChanged =
            previousRoadVisual != currentRoadVisual || previousTerrainVisual != currentTerrainVisual;
        EditorGUILayout.PropertyField(
            roadMaterialOverrideProperty,
            new GUIContent("임시 도로 재질 교체"));
        EditorGUILayout.PropertyField(
            terrainMaterialOverrideProperty,
            new GUIContent("임시 지형 재질 교체"));
        EditorGUILayout.HelpBox(
            "10m 도로 모듈은 '경로 변형 도로 모듈'에 프로젝트의 FBX/Prefab 원본을 연결하면 트랙을 따라 자동으로 휘어집니다. 별도로 완성한 고정형 모델은 씬의 'SCENE CONTENT - Add Models Here' 아래에 배치하고 도로/지형 모델 오브젝트 슬롯에 연결합니다. 임시 메시의 Collider와 트랙 경로는 게임 판정을 위해 유지됩니다.",
            MessageType.None);
        DrawSceneVisualWarning(customRoadVisualProperty, "도로");
        DrawSceneVisualWarning(customTerrainVisualProperty, "지형");

        EditorGUILayout.Space(8f);
        if (GUILayout.Button(editMode ? "경로 편집 종료 (Esc)" : "경로 편집 시작"))
        {
            editMode = !editMode;
            SceneView.RepaintAll();
        }
        if (editMode)
        {
            EditorGUILayout.HelpBox(
                "편집 중에는 메시를 클릭해도 선택이 풀리지 않습니다. 청록색 선 위에서 Shift+클릭하면 포인트가 추가되고, 선택한 포인트는 Delete 키로 삭제됩니다.",
                MessageType.None);
        }

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
            if (GUILayout.Button("주변 오브젝트를 현재 경로에 다시 배치"))
            {
                serializedObject.ApplyModifiedProperties();
                MushTrackEditorWorldPreview.RebuildFullWorld(authoring);
                serializedObject.Update();
            }
        }

        bool propertiesChanged = serializedObject.ApplyModifiedProperties();

        // 도로/지형 씬 모델 슬롯을 다른 오브젝트로 바꾸거나 None으로 되돌렸다면,
        // 이전 슬롯 모델은 즉시 숨기고 새 슬롯 모델은 즉시 보이게 합니다.
        // 이렇게 해야 None이 단순히 참조만 끊는 것이 아니라 실제 화면도 기본 생성 도로/지형으로 돌아옵니다.
        if (sceneVisualAssignmentChanged)
        {
            ApplySceneVisualAssignmentChange(
                previousRoadVisual,
                previousTerrainVisual,
                currentRoadVisual,
                currentTerrainVisual);
        }

        // 슬롯 변경을 포함한 Inspector 값 변경이 있으면 생성 도로/지형의 표시 여부까지 다시 계산합니다.
        if (propertiesChanged)
            MushTrackEditorWorldPreview.RequestRebuild(authoring);
    }


    private static void ApplySceneVisualAssignmentChange(
        GameObject previousRoadVisual,
        GameObject previousTerrainVisual,
        GameObject currentRoadVisual,
        GameObject currentTerrainVisual)
    {
        // 예전에 도로 슬롯에 있던 오브젝트가 이제 어느 슬롯에서도 사용되지 않는다면 화면에서 숨깁니다.
        if (previousRoadVisual != null &&
            previousRoadVisual != currentRoadVisual &&
            previousRoadVisual != currentTerrainVisual)
        {
            SetSceneVisualRenderersEnabled(previousRoadVisual, false);
        }

        // 예전에 지형 슬롯에 있던 오브젝트도 새 도로/지형 슬롯에서 재사용되지 않을 때만 숨깁니다.
        if (previousTerrainVisual != null &&
            previousTerrainVisual != previousRoadVisual &&
            previousTerrainVisual != currentRoadVisual &&
            previousTerrainVisual != currentTerrainVisual)
        {
            SetSceneVisualRenderersEnabled(previousTerrainVisual, false);
        }

        // 새로 지정한 도로 모델은 즉시 보이게 해서 모델을 교체하며 비교할 수 있게 합니다.
        if (currentRoadVisual != null)
            SetSceneVisualRenderersEnabled(currentRoadVisual, true);

        // 새로 지정한 지형 모델도 즉시 보이게 합니다.
        if (currentTerrainVisual != null && currentTerrainVisual != currentRoadVisual)
            SetSceneVisualRenderersEnabled(currentTerrainVisual, true);
    }

    private static void SetSceneVisualRenderersEnabled(GameObject root, bool enabled)
    {
        if (root == null || !root.scene.IsValid())
            return;

        // 자식까지 포함한 모든 Renderer를 바꿔 FBX/Prefab을 여러 Mesh로 구성해도 한 번에 숨기거나 복구합니다.
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            Undo.RecordObjects(renderers, enabled ? "Show Mush Scene Visual" : "Hide Mush Scene Visual");
            for (int index = 0; index < renderers.Length; index++)
            {
                renderers[index].enabled = enabled;
                EditorUtility.SetDirty(renderers[index]);
            }
        }

        // Unity Terrain을 슬롯에 넣은 경우도 같은 방식으로 표시 상태를 맞춥니다.
        Terrain[] terrains = root.GetComponentsInChildren<Terrain>(true);
        if (terrains.Length > 0)
        {
            Undo.RecordObjects(terrains, enabled ? "Show Mush Scene Terrain" : "Hide Mush Scene Terrain");
            for (int index = 0; index < terrains.Length; index++)
            {
                terrains[index].enabled = enabled;
                EditorUtility.SetDirty(terrains[index]);
            }
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        SceneView.RepaintAll();
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

        Handles.zTest = CompareFunction.Always;
        authoring.CopyPreviewRoute(previewRoute);
        Vector3[] worldRoute = new Vector3[previewRoute.Count];
        Vector3[] worldLeftRoadEdge = new Vector3[previewRoute.Count];
        Vector3[] worldRightRoadEdge = new Vector3[previewRoute.Count];
        for (int index = 0; index < previewRoute.Count; index++)
        {
            int previous = Mathf.Max(0, index - 1);
            int next = Mathf.Min(previewRoute.Count - 1, index + 1);
            Vector3 tangent = Vector3.ProjectOnPlane(
                previewRoute[next] - previewRoute[previous],
                Vector3.up).normalized;
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.back;
            Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
            Vector3 liftedPoint = previewRoute[index] + Vector3.up * 0.16f;
            worldRoute[index] = mapRoot.TransformPoint(previewRoute[index] + Vector3.up * 0.16f);
            worldLeftRoadEdge[index] = mapRoot.TransformPoint(
                liftedPoint - right * authoring.PreviewRoadHalfWidth);
            worldRightRoadEdge[index] = mapRoot.TransformPoint(
                liftedPoint + right * authoring.PreviewRoadHalfWidth);
        }
        Handles.color = new Color(0.15f, 0.9f, 1f, 0.75f);
        Handles.DrawAAPolyLine(2.5f, worldRoute);
        Handles.color = new Color(0.15f, 0.9f, 1f, 0.48f);
        Handles.DrawAAPolyLine(2f, worldLeftRoadEdge);
        Handles.DrawAAPolyLine(2f, worldRightRoadEdge);

        if (!editMode)
            return;

        Event currentEvent = Event.current;
        int sceneEditControl = GUIUtility.GetControlID(
            "MushTrackAuthoringSceneEdit".GetHashCode(),
            FocusType.Passive);
        if (currentEvent.type == EventType.Layout && !currentEvent.alt)
            HandleUtility.AddDefaultControl(sceneEditControl);

        Handles.BeginGUI();
        GUI.Label(
            new Rect(12f, 12f, 430f, 42f),
            "트랙 편집 중 · Shift+클릭: 추가 · Delete: 삭제 · 놓으면 도로 반영 · Esc: 종료",
            EditorStyles.helpBox);
        Handles.EndGUI();

        if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.Escape)
        {
            editMode = false;
            currentEvent.Use();
            Repaint();
            SceneView.RepaintAll();
            return;
        }

        if (currentEvent.type == EventType.KeyDown &&
            (currentEvent.keyCode == KeyCode.Delete || currentEvent.keyCode == KeyCode.Backspace) &&
            selectedPoint >= 0 && authoring.ControlPointCount > 2)
        {
            Undo.RecordObject(authoring, "Delete Mush Track Point");
            selectedPoint = authoring.RemoveControlPoint(selectedPoint);
            EditorUtility.SetDirty(authoring);
            MushTrackEditorWorldPreview.RequestRebuild(authoring);
            currentEvent.Use();
            return;
        }

        if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 &&
            currentEvent.shift && !currentEvent.alt &&
            TryGetPointInsertion(mapRoot, currentEvent.mousePosition, out int segmentIndex, out Vector3 localPoint))
        {
            Undo.RecordObject(authoring, "Add Mush Track Point");
            if (!authoring.UsesEditablePath)
                authoring.BakeDefaultPath();
            selectedPoint = authoring.InsertControlPointAfter(segmentIndex);
            authoring.SetControlPoint(selectedPoint, localPoint);
            EditorUtility.SetDirty(authoring);
            MushTrackEditorWorldPreview.RequestRebuild(authoring);
            currentEvent.Use();
            Repaint();
            return;
        }

        for (int index = 0; index < previewControlPoints.Count; index++)
        {
            Vector3 worldPoint = mapRoot.TransformPoint(previewControlPoints[index]);
            float size = HandleUtility.GetHandleSize(worldPoint) * 0.075f;
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

    private bool TryGetPointInsertion(
        Transform mapRoot,
        Vector2 mousePosition,
        out int segmentIndex,
        out Vector3 localPoint)
    {
        const float maximumDistancePixels = 24f;
        segmentIndex = -1;
        localPoint = default;
        float nearestDistance = float.PositiveInfinity;

        for (int index = 0; index < previewRoute.Count - 1; index++)
        {
            Vector2 start = HandleUtility.WorldToGUIPoint(mapRoot.TransformPoint(previewRoute[index]));
            Vector2 end = HandleUtility.WorldToGUIPoint(mapRoot.TransformPoint(previewRoute[index + 1]));
            Vector2 segment = end - start;
            float segmentLengthSqr = segment.sqrMagnitude;
            float t = segmentLengthSqr > 0.001f
                ? Mathf.Clamp01(Vector2.Dot(mousePosition - start, segment) / segmentLengthSqr)
                : 0f;
            float distance = Vector2.Distance(mousePosition, start + segment * t);
            if (distance >= nearestDistance)
                continue;

            nearestDistance = distance;
            localPoint = Vector3.Lerp(previewRoute[index], previewRoute[index + 1], t);
        }

        if (nearestDistance > maximumDistancePixels)
            return false;

        float nearestControlDistanceSqr = float.PositiveInfinity;
        for (int index = 0; index < previewControlPoints.Count - 1; index++)
        {
            Vector3 start = previewControlPoints[index];
            Vector3 segment = previewControlPoints[index + 1] - start;
            float segmentLengthSqr = segment.sqrMagnitude;
            float t = segmentLengthSqr > 0.0001f
                ? Mathf.Clamp01(Vector3.Dot(localPoint - start, segment) / segmentLengthSqr)
                : 0f;
            float distanceSqr = (localPoint - (start + segment * t)).sqrMagnitude;
            if (distanceSqr >= nearestControlDistanceSqr)
                continue;

            nearestControlDistanceSqr = distanceSqr;
            segmentIndex = index;
        }

        return segmentIndex >= 0;
    }

    private static float CalculateLength(IReadOnlyList<Vector3> points)
    {
        float length = 0f;
        for (int index = 1; index < points.Count; index++)
            length += Vector3.Distance(points[index - 1], points[index]);
        return length;
    }

    private static void DrawSceneVisualWarning(SerializedProperty property, string label)
    {
        GameObject assignedObject = property.objectReferenceValue as GameObject;
        if (assignedObject != null && !assignedObject.scene.IsValid())
        {
            EditorGUILayout.HelpBox(
                $"{label} 슬롯에는 프로젝트의 Prefab/FBX 원본이 아니라 씬에 배치한 인스턴스를 연결해 주세요.",
                MessageType.Warning);
        }
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
        Undo.undoRedoPerformed += HandleUndoRedo;
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

    private static void HandleUndoRedo()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        MushTrackAuthoring[] tracks = Object.FindObjectsByType<MushTrackAuthoring>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int index = 0; index < tracks.Length; index++)
            RequestRebuild(tracks[index]);
    }

    private static void RefreshAllOpenSceneCourses()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        MushTrackAuthoring[] tracks = Object.FindObjectsByType<MushTrackAuthoring>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int index = 0; index < tracks.Length; index++)
            RequestRebuild(tracks[index]);
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
                RebuildSceneCourse(authoring, false);
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
            if (mapRoot != null && GeneratedWorldNeedsBake(mapRoot))
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
            if (authoring != null && mapRoot != null && GeneratedWorldNeedsBake(mapRoot))
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

    public static void RebuildFullWorld(MushTrackAuthoring authoring)
    {
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

    private static bool GeneratedWorldNeedsBake(Transform mapRoot)
    {
        Transform generatedRoot = mapRoot.Find(MushCurvedMapRuntime.GeneratedWorldRootName);
        if (generatedRoot == null)
            return true;

        MushCurvedMapRuntime runtime = mapRoot.GetComponent<MushCurvedMapRuntime>();
        if (runtime == null || !runtime.HasCurrentBakedWorldVersion)
            return true;

        foreach (MeshFilter filter in generatedRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
                return true;
        }

        foreach (MeshCollider collider in generatedRoot.GetComponentsInChildren<MeshCollider>(true))
        {
            if (collider.sharedMesh == null)
                return true;
        }

        foreach (Renderer renderer in generatedRoot.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            if (materials.Length == 0)
                return true;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                if (materials[materialIndex] == null)
                    return true;
            }
        }

        return false;
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

    private static void RebuildSceneCourse(MushTrackAuthoring authoring, bool saveScene)
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

            runtime.RebuildSceneCourseGeometry();
            Transform generatedRoot = mapRoot.Find(MushCurvedMapRuntime.GeneratedWorldRootName);
            if (generatedRoot == null)
                throw new MissingReferenceException($"Baked world root was not found for {mapRoot.name}.");

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
        MushBakedMapAssetContainer container =
            AssetDatabase.LoadAssetAtPath<MushBakedMapAssetContainer>(assetPath);
        if (container == null)
        {
            container = ScriptableObject.CreateInstance<MushBakedMapAssetContainer>();
            container.name = $"{sceneName} Baked Map Assets";
            AssetDatabase.CreateAsset(container, assetPath);
        }

        Object[] existingResources = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        List<Object> resources = new();
        HashSet<Object> uniqueResources = new();
        foreach (MeshFilter filter in generatedRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            AddGeneratedResource(filter.sharedMesh, resources, uniqueResources);
        }
        foreach (MeshCollider collider in generatedRoot.GetComponentsInChildren<MeshCollider>(true))
        {
            AddGeneratedResource(collider.sharedMesh, resources, uniqueResources);
        }
        foreach (Renderer renderer in generatedRoot.GetComponentsInChildren<Renderer>(true))
        foreach (Material material in renderer.sharedMaterials)
        {
            AddGeneratedResource(material, resources, uniqueResources);
        }
        AddGeneratedResource(RenderSettings.skybox, resources, uniqueResources);

        for (int resourceIndex = 0; resourceIndex < resources.Count; resourceIndex++)
        {
            Object generatedResource = resources[resourceIndex];
            string stablePrefix = $"{resourceIndex:D3}_";
            string stableName = stablePrefix + generatedResource.name;
            Object reusableResource = FindReusableResource(
                existingResources,
                stablePrefix,
                generatedResource.GetType());

            if (reusableResource != null)
            {
                if (generatedResource is Mesh generatedMesh && reusableResource is Mesh reusableMesh)
                    CopyGeneratedMesh(generatedMesh, reusableMesh);
                else
                    EditorUtility.CopySerialized(generatedResource, reusableResource);
                reusableResource.name = stableName;
                ReplaceGeneratedResourceReferences(generatedRoot, generatedResource, reusableResource);
                EditorUtility.SetDirty(reusableResource);
                Object.DestroyImmediate(generatedResource);
                continue;
            }

            generatedResource.name = stableName;
            AssetDatabase.AddObjectToAsset(generatedResource, container);
            EditorUtility.SetDirty(generatedResource);
        }

        EditorUtility.SetDirty(container);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        runtime.ReleaseBakedResourceOwnership();
    }

    private static void CopyGeneratedMesh(Mesh source, Mesh destination)
    {
        destination.Clear(false);
        destination.indexFormat = source.indexFormat;
        destination.vertices = source.vertices;
        destination.normals = source.normals;
        destination.tangents = source.tangents;
        destination.colors = source.colors;

        List<Vector4> uvChannel = new();
        for (int channel = 0; channel < 8; channel++)
        {
            uvChannel.Clear();
            source.GetUVs(channel, uvChannel);
            destination.SetUVs(channel, uvChannel);
        }

        destination.subMeshCount = source.subMeshCount;
        for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
        {
            destination.SetIndices(
                source.GetIndices(subMesh, true),
                source.GetTopology(subMesh),
                subMesh,
                false,
                0);
        }

        destination.bounds = source.bounds;
        destination.UploadMeshData(false);
    }

    private static void AddGeneratedResource(
        Object resource,
        List<Object> resources,
        HashSet<Object> uniqueResources)
    {
        if (resource != null && !EditorUtility.IsPersistent(resource) && uniqueResources.Add(resource))
            resources.Add(resource);
    }

    private static Object FindReusableResource(
        Object[] existingResources,
        string stablePrefix,
        System.Type resourceType)
    {
        for (int index = 0; index < existingResources.Length; index++)
        {
            Object candidate = existingResources[index];
            if (candidate != null && candidate.GetType() == resourceType &&
                candidate.name.StartsWith(stablePrefix))
                return candidate;
        }
        return null;
    }

    private static void ReplaceGeneratedResourceReferences(
        Transform generatedRoot,
        Object source,
        Object replacement)
    {
        if (source is Mesh sourceMesh && replacement is Mesh replacementMesh)
        {
            foreach (MeshFilter filter in generatedRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == sourceMesh)
                    filter.sharedMesh = replacementMesh;
            }
            foreach (MeshCollider collider in generatedRoot.GetComponentsInChildren<MeshCollider>(true))
            {
                if (collider.sharedMesh == sourceMesh)
                    collider.sharedMesh = replacementMesh;
            }
            return;
        }

        if (source is not Material sourceMaterial || replacement is not Material replacementMaterial)
            return;

        foreach (Renderer renderer in generatedRoot.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            bool replaced = false;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                if (materials[materialIndex] != sourceMaterial)
                    continue;
                materials[materialIndex] = replacementMaterial;
                replaced = true;
            }
            if (replaced)
                renderer.sharedMaterials = materials;
        }

        if (RenderSettings.skybox == sourceMaterial)
            RenderSettings.skybox = replacementMaterial;
    }

    private static void EnsureGeneratedAssetFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Mush"))
            AssetDatabase.CreateFolder("Assets", "Mush");
        if (!AssetDatabase.IsValidFolder(GeneratedAssetFolder))
            AssetDatabase.CreateFolder("Assets/Mush", "GeneratedMaps");
    }
}
