using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 지형 포인트 편집은 없으며, 지형 모델이 없을 때만 도로를 기준으로 자동 지형을 갱신합니다.
/// </summary>
[CustomEditor(typeof(MushTrackAuthoring))]
public sealed class MushTrackAuthoringEditor : Editor
{
    private readonly List<Vector3> previewRoute = new();
    private readonly List<Vector3> previewControlPoints = new();
    private readonly List<Vector3> projectedRoutePreview = new();

    private SerializedProperty useEditablePathProperty;
    private SerializedProperty sampleSpacingProperty;
    private SerializedProperty overrideTrackWidthsProperty;
    private SerializedProperty roadHalfWidthProperty;
    private SerializedProperty terrainHalfWidthProperty;
    private SerializedProperty roadModelProperty;
    private SerializedProperty useRoadModelProperty;
    private SerializedProperty terrainModelProperty;
    private SerializedProperty roadMaterialOverrideProperty;
    private SerializedProperty roadTextureOverrideProperty;
    private SerializedProperty terrainMaterialOverrideProperty;
    private SerializedProperty terrainTextureOverrideProperty;

    private int selectedPoint = -1;
    private bool editingTrack;
    private static MushTrackAuthoring pendingTrackEdit;

    public static void BeginTrackEditing(MushTrackAuthoring authoring)
    {
        if (authoring == null)
            return;

        pendingTrackEdit = authoring;
        Selection.activeGameObject = authoring.gameObject;

        Transform mapRoot = authoring.ResolveMapRoot();
        MushCurvedMapRuntime runtime = mapRoot != null ? mapRoot.GetComponent<MushCurvedMapRuntime>() : null;
        if (runtime != null)
            runtime.RefreshEditorPresentationOnly();

        SceneView.RepaintAll();
    }

    private void ConsumeTrackEditRequest(MushTrackAuthoring authoring)
    {
        if (pendingTrackEdit != authoring)
            return;

        pendingTrackEdit = null;
        editingTrack = true;
        if (authoring.ControlPointCount > 0 && selectedPoint < 0)
            selectedPoint = 0;
    }

    private void OnEnable()
    {
        useEditablePathProperty = serializedObject.FindProperty("useEditablePath");
        sampleSpacingProperty = serializedObject.FindProperty("sampleSpacing");
        overrideTrackWidthsProperty = serializedObject.FindProperty("overrideTrackWidths");
        roadHalfWidthProperty = serializedObject.FindProperty("roadHalfWidth");
        terrainHalfWidthProperty = serializedObject.FindProperty("terrainHalfWidth");
        roadModelProperty = serializedObject.FindProperty("deformableRoadModule");
        useRoadModelProperty = serializedObject.FindProperty("useDeformableRoadModule");
        terrainModelProperty = serializedObject.FindProperty("customTerrainVisual");
        roadMaterialOverrideProperty = serializedObject.FindProperty("roadMaterialOverride");
        roadTextureOverrideProperty = serializedObject.FindProperty("roadTextureOverride");
        terrainMaterialOverrideProperty = serializedObject.FindProperty("terrainMaterialOverride");
        terrainTextureOverrideProperty = serializedObject.FindProperty("terrainTextureOverride"); // 자동 지형 텍스처 참조를 찾습니다.
        // 실제 도로 포인트/모델/재질 값이 바뀐 순간에만 아래 편집 코드에서 RequestRebuild를 호출합니다.
        ConsumeTrackEditRequest(target as MushTrackAuthoring);
    }

    public override void OnInspectorGUI()
    {
        MushTrackAuthoring authoring = (MushTrackAuthoring)target;
        ConsumeTrackEditRequest(authoring);
        GameObject previousRoadModel = authoring.RoadModel;
        serializedObject.Update();

        EditorGUILayout.HelpBox(
            "씬에 도로·지형 모델과 오브젝트를 자유롭게 배치할 수 있습니다.\n" +
            "지형 모델이 있으면 도로가 그 모델 표면 높이에 자동으로 붙고, 지형 모델이 없으면 자동 지형이 도로 높이를 따라 생성됩니다.",
            MessageType.Info);

        if (GUILayout.Button("도로 갱신 · 씬에 저장할 형태로 적용"))
            MushTrackEditorWorldPreview.RequestRebuild(authoring);
        EditorGUILayout.LabelField("경로 설정", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(sampleSpacingProperty, new GUIContent("도로 샘플 간격 (m)"));
        EditorGUILayout.PropertyField(overrideTrackWidthsProperty, new GUIContent("도로 폭 직접 지정"));
        if (overrideTrackWidthsProperty.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(roadHalfWidthProperty, new GUIContent("도로 반폭 (m)"));
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("도로 모델", EditorStyles.boldLabel);
        GameObject roadModelBeforeField = roadModelProperty.objectReferenceValue as GameObject;
        EditorGUILayout.PropertyField(roadModelProperty, new GUIContent("도로 모델"));
        GameObject roadModelAfterField = roadModelProperty.objectReferenceValue as GameObject;
        if (roadModelAfterField != roadModelBeforeField)
            useRoadModelProperty.boolValue = roadModelAfterField != null;
        using (new EditorGUI.DisabledScope(roadModelAfterField == null))
            EditorGUILayout.PropertyField(useRoadModelProperty, new GUIContent("도로 모델 사용"));

        if (roadModelProperty.objectReferenceValue == null || !useRoadModelProperty.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(roadMaterialOverrideProperty, new GUIContent("기본 도로 머티리얼"));
            EditorGUILayout.PropertyField(roadTextureOverrideProperty, new GUIContent("기본 도로 텍스처"));
            EditorGUI.indentLevel--;
        }
        else
        {
            EditorGUILayout.HelpBox(
                "모델의 로컬 Z축을 길이로 사용합니다. 곡선 메시를 편집 시 구간별로 만들어 저장하며, 플레이 중에는 다시 계산하지 않습니다. 전체 생성 정점은 240,000개로 제한됩니다.",
                MessageType.None);
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("지형", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(terrainModelProperty, new GUIContent("지형 모델"));

        if (terrainModelProperty.objectReferenceValue != null)
        {
            EditorGUILayout.HelpBox(
                "지형 모델 사용 중: 도로 중심선은 지형 표면을 위에서 아래로 샘플링해 자동으로 높이를 맞춥니다. Collider가 있으면 그대로 사용하고, 없으면 원본 Mesh를 공유하는 씬에 저장되는 MeshCollider를 자동으로 사용합니다.",
                MessageType.None);
        }
        else
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(terrainHalfWidthProperty, new GUIContent("자동 지형 반폭 (m)"));
            EditorGUILayout.PropertyField(terrainMaterialOverrideProperty, new GUIContent("자동 지형 머티리얼"));
            EditorGUILayout.PropertyField(terrainTextureOverrideProperty, new GUIContent("자동 지형 텍스처"));
            EditorGUI.indentLevel--;

            EditorGUILayout.HelpBox(
                "지형 모델 없음: 별도 지형 편집점은 만들지 않고 현재 도로의 높이와 곡선을 기준으로 자동 지형만 가볍게 다시 계산합니다.",
                MessageType.None);
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("도로 포인트 편집", EditorStyles.boldLabel);

        if (GUILayout.Button(editingTrack ? "도로 편집 종료 (Esc)" : "도로 포인트 편집"))
        {
            editingTrack = !editingTrack;
            SceneView.RepaintAll();
        }

        EditorGUILayout.HelpBox(
            editingTrack
                ? "도로 선 위 Shift+클릭: 포인트 추가 / Delete: 선택 포인트 삭제 / 이동 핸들: 위치·높이 변경 / Esc: 편집 종료"
                : "도로 포인트 편집 버튼을 눌렀을 때만 트랙 제어점을 조작합니다.",
            MessageType.None);

        bool wasEditable = useEditablePathProperty.boolValue;
        EditorGUILayout.LabelField(
            "경로 상태",
            wasEditable ? $"편집 경로 ({authoring.ControlPointCount}개 포인트)" : "기본 프로토타입 경로");

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
                    ? "기본 직선으로 편집 포인트 다시 만들기"
                    : "기본 직선을 편집 포인트로 변환"))
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
            EditorGUILayout.HelpBox("편집할 맵 루트를 찾지 못했습니다.", MessageType.Warning);
        }
        else
        {
            authoring.CopyPreviewRoute(previewRoute);
            MushCurvedMapRuntime runtime = mapRoot.GetComponent<MushCurvedMapRuntime>();
            if (authoring.HasCustomTerrainVisual && runtime != null)
            {
                runtime.CopyActiveRoutePreview(projectedRoutePreview);
                if (projectedRoutePreview.Count >= 2)
                {
                    previewRoute.Clear();
                    previewRoute.AddRange(projectedRoutePreview);
                }
            }

            EditorGUILayout.LabelField("예상 트랙 길이", $"{CalculateLength(previewRoute):0.0} m");
        }

        bool propertiesChanged = serializedObject.ApplyModifiedProperties();
        if (propertiesChanged)
        {
            EditorUtility.SetDirty(authoring);

            if (mapRoot != null && previousRoadModel != authoring.RoadModel)
            {
                MushCurvedMapRuntime runtime = mapRoot.GetComponent<MushCurvedMapRuntime>();
                if (runtime != null)
                    runtime.InvalidateRoadModelInstances();
            }

            MushTrackEditorWorldPreview.RequestRebuild(authoring);
        }
    }

    private void OnSceneGUI()
    {
        MushTrackAuthoring authoring = (MushTrackAuthoring)target;
        ConsumeTrackEditRequest(authoring);
        Transform mapRoot = authoring.ResolveMapRoot();
        if (mapRoot == null)
            return;

        CompareFunction previousZTest = Handles.zTest;
        try
        {
            Handles.zTest = CompareFunction.Always;
            DrawSceneEditor(authoring, mapRoot);
        }
        finally
        {
            Handles.zTest = previousZTest;
        }
    }

    private void DrawSceneEditor(MushTrackAuthoring authoring, Transform mapRoot)
    {
        authoring.CopyEditableControlPointPreview(previewControlPoints);
        if (previewControlPoints.Count < 2)
            return;

        authoring.CopyPreviewRoute(previewRoute);
        if (authoring.HasCustomTerrainVisual)
        {
            MushCurvedMapRuntime runtime = mapRoot.GetComponent<MushCurvedMapRuntime>();
            if (runtime != null)
            {
                runtime.CopyActiveRoutePreview(projectedRoutePreview);
                if (projectedRoutePreview.Count >= 2)
                {
                    previewRoute.Clear();
                    previewRoute.AddRange(projectedRoutePreview);
                }
            }
        }

        Vector3[] worldRoute = new Vector3[previewRoute.Count];
        Vector3[] worldLeftRoadEdge = new Vector3[previewRoute.Count];
        Vector3[] worldRightRoadEdge = new Vector3[previewRoute.Count];

        for (int index = 0; index < previewRoute.Count; index++)
        {
            int previous = Mathf.Max(0, index - 1);
            int next = Mathf.Min(previewRoute.Count - 1, index + 1);
            Vector3 tangent = Vector3.ProjectOnPlane(previewRoute[next] - previewRoute[previous], Vector3.up).normalized;
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.back;

            Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
            Vector3 liftedPoint = previewRoute[index] + Vector3.up * 0.16f;
            worldRoute[index] = mapRoot.TransformPoint(liftedPoint);
            worldLeftRoadEdge[index] = mapRoot.TransformPoint(liftedPoint - right * authoring.PreviewRoadHalfWidth);
            worldRightRoadEdge[index] = mapRoot.TransformPoint(liftedPoint + right * authoring.PreviewRoadHalfWidth);
        }

        Handles.color = new Color(0.15f, 0.9f, 1f, 0.75f);
        Handles.DrawAAPolyLine(2.5f, worldRoute);
        Handles.color = new Color(0.15f, 0.9f, 1f, 0.48f);
        Handles.DrawAAPolyLine(2f, worldLeftRoadEdge);
        Handles.DrawAAPolyLine(2f, worldRightRoadEdge);

        if (!editingTrack)
            return;

        Event currentEvent = Event.current;
        int sceneEditControl = GUIUtility.GetControlID("MushTrackOnlySceneEdit".GetHashCode(), FocusType.Passive);
        if (currentEvent.type == EventType.Layout && !currentEvent.alt)
            HandleUtility.AddDefaultControl(sceneEditControl);

        Handles.BeginGUI();
        GUILayout.BeginArea(new Rect(12f, 12f, 510f, 48f), EditorStyles.helpBox);
        GUILayout.Label("도로 편집 중 · Shift+클릭: 추가 · Delete: 삭제 · 이동 핸들: 위치/높이 · Esc: 종료");
        GUILayout.EndArea();
        Handles.EndGUI();

        if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.Escape)
        {
            editingTrack = false;
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
            Repaint();
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
            Vector3 displayLocalPoint = GetControlPointDisplayPosition(authoring, previewControlPoints[index]);
            Vector3 worldPoint = mapRoot.TransformPoint(displayLocalPoint);
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

        Vector3 selectedDisplayLocalPoint = GetControlPointDisplayPosition(authoring, previewControlPoints[selectedPoint]);
        Vector3 selectedWorldPoint = mapRoot.TransformPoint(selectedDisplayLocalPoint);
        Handles.Label(
            selectedWorldPoint + Vector3.up * HandleUtility.GetHandleSize(selectedWorldPoint) * 0.14f,
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

    private Vector3 GetControlPointDisplayPosition(MushTrackAuthoring authoring, Vector3 controlPoint)
    {
        if (!authoring.HasCustomTerrainVisual || previewRoute.Count == 0)
            return controlPoint;

        int nearestIndex = 0;
        float nearestSqrDistance = float.PositiveInfinity;
        Vector2 controlXZ = new(controlPoint.x, controlPoint.z);

        for (int index = 0; index < previewRoute.Count; index++)
        {
            Vector3 routePoint = previewRoute[index];
            Vector2 delta = new(routePoint.x - controlXZ.x, routePoint.z - controlXZ.y);
            float sqrDistance = delta.sqrMagnitude;
            if (sqrDistance >= nearestSqrDistance)
                continue;

            nearestSqrDistance = sqrDistance;
            nearestIndex = index;
        }

        controlPoint.y = previewRoute[nearestIndex].y;
        return controlPoint;
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
            Vector2 start = new(previewControlPoints[index].x, previewControlPoints[index].z);
            Vector2 end = new(previewControlPoints[index + 1].x, previewControlPoints[index + 1].z);
            Vector2 segment = end - start;
            Vector2 localPointXZ = new(localPoint.x, localPoint.z);
            float segmentLengthSqr = segment.sqrMagnitude;
            float t = segmentLengthSqr > 0.0001f
                ? Mathf.Clamp01(Vector2.Dot(localPointXZ - start, segment) / segmentLengthSqr)
                : 0f;
            float distanceSqr = (localPointXZ - (start + segment * t)).sqrMagnitude;
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
}

/// <summary>
/// 지형 모델이 있으면 도로만 지형에 투영하고, 없으면 작은 자동 지형만 갱신하며 GeneratedMaps 베이크는 하지 않습니다.
/// </summary>
[InitializeOnLoad]
public static class MushTrackEditorWorldPreview
{
    private static readonly HashSet<MushTrackAuthoring> PendingTracks = new();
    private static readonly HashSet<Mesh> DirtyGeneratedMeshes = new();
    private static bool rebuildScheduled;
    private static bool rebuilding;

    static MushTrackEditorWorldPreview()
    {
        Undo.undoRedoPerformed += HandleUndoRedo;
        EditorSceneManager.sceneSaved += HandleSceneSaved;
        SceneView.duringSceneGui += DrawQuickTrackEditButton;
    }

    private static void DrawQuickTrackEditButton(SceneView sceneView)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        MushTrackAuthoring found = null;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int rootIndex = 0; rootIndex < roots.Length && found == null; rootIndex++)
            found = roots[rootIndex].GetComponentInChildren<MushTrackAuthoring>(true);
        if (found == null)
            return;

        Handles.BeginGUI();
        GUILayout.BeginArea(new Rect(12f, 70f, 180f, 40f));
        if (GUILayout.Button("도로 포인트 편집", GUILayout.Height(30f)))
            MushTrackAuthoringEditor.BeginTrackEditing(found);
        GUILayout.EndArea();
        Handles.EndGUI();
    }

    private static void MarkCourseMeshesDirty(Transform mapRoot)
    {
        if (mapRoot == null)
            return;
        Transform generatedRoot = mapRoot.Find(MushCurvedMapRuntime.GeneratedWorldRootName);
        if (generatedRoot == null)
            return;

        string[] names = { "VISIBLE Snow Terrain", "VISIBLE Curved Packed-Snow Road", "Left Sled Track", "Right Sled Track" };
        for (int index = 0; index < names.Length; index++)
        {
            Transform child = generatedRoot.Find(names[index]);
            MeshFilter filter = child != null ? child.GetComponent<MeshFilter>() : null;
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || !AssetDatabase.Contains(mesh))
                continue;

            EditorUtility.SetDirty(mesh);
            DirtyGeneratedMeshes.Add(mesh);
        }
    }

    private static void HandleSceneSaved(Scene scene)
    {
        if (DirtyGeneratedMeshes.Count == 0)
            return;

        Mesh[] meshes = new Mesh[DirtyGeneratedMeshes.Count];
        DirtyGeneratedMeshes.CopyTo(meshes);
        DirtyGeneratedMeshes.Clear();
        for (int index = 0; index < meshes.Length; index++)
        {
            Mesh mesh = meshes[index];
            if (mesh != null && AssetDatabase.Contains(mesh))
                AssetDatabase.SaveAssetIfDirty(mesh);
        }
    }

    public static void RequestRebuild(MushTrackAuthoring authoring)
    {
        if (authoring == null || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        PendingTracks.Add(authoring);
        SchedulePendingRebuild();
    }

    private static void HandleUndoRedo()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        RequestAllOpenTracks();
    }


    private static void RequestAllOpenTracks()
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

    public static void FlushPending()
    {
        if (PendingTracks.Count > 0) RebuildPending();
    }

    private static void RebuildPending()
    {
        rebuildScheduled = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode || rebuilding)
            return;

        if (GUIUtility.hotControl != 0)
        {
            SchedulePendingRebuild();
            return;
        }

        MushTrackAuthoring[] tracks = new MushTrackAuthoring[PendingTracks.Count];
        PendingTracks.CopyTo(tracks);
        PendingTracks.Clear();

        rebuilding = true;
        try
        {
            for (int index = 0; index < tracks.Length; index++)
            {
                MushTrackAuthoring authoring = tracks[index];
                if (authoring == null)
                    continue;

                Transform mapRoot = authoring.ResolveMapRoot();
                if (mapRoot == null)
                    continue;

                MushCurvedMapRuntime runtime = mapRoot.GetComponent<MushCurvedMapRuntime>();
                if (runtime == null)
                    runtime = Undo.AddComponent<MushCurvedMapRuntime>(mapRoot.gameObject);

                runtime.RebuildSceneCourseGeometry();
                MarkCourseMeshesDirty(mapRoot);
                EditorUtility.SetDirty(runtime);
                MushSceneAuthoringMigration.Persist(authoring.gameObject.scene);
                EditorSceneManager.MarkSceneDirty(authoring.gameObject.scene); // 바뀐 작은 도로/지형 Mesh는 다음 일반 씬 저장 때만 함께 저장합니다.
                // 포인트/Inspector 값을 실제로 바꾼 코드가 authoring을 이미 Dirty 처리하므로,
            }
        }
        finally
        {
            rebuilding = false;
        }

        SceneView.RepaintAll();
    }

    public static void EnsureEditableMapReady(MushTrackAuthoring authoring, bool saveScene)
    {
        if (authoring == null || EditorApplication.isPlayingOrWillChangePlaymode || rebuilding)
            return;

        Transform mapRoot = authoring.ResolveMapRoot();
        if (mapRoot == null || mapRoot.gameObject.scene != authoring.gameObject.scene)
            return;

        MushCurvedMapRuntime runtime = mapRoot.GetComponent<MushCurvedMapRuntime>();
        if (runtime == null)
            runtime = Undo.AddComponent<MushCurvedMapRuntime>(mapRoot.gameObject);

        if (mapRoot.Find(MushCurvedMapRuntime.GeneratedWorldRootName) == null)
            runtime.RebuildSceneWorld();
        else
            runtime.RebuildSceneCourseGeometry();

        MushSceneAuthoringMigration.Persist(authoring.gameObject.scene);
        EditorUtility.SetDirty(authoring);
        EditorUtility.SetDirty(runtime);
        EditorSceneManager.MarkSceneDirty(authoring.gameObject.scene);

        if (saveScene && !string.IsNullOrEmpty(authoring.gameObject.scene.path))
            EditorSceneManager.SaveScene(authoring.gameObject.scene);

        SceneView.RepaintAll();
    }

    public static void RebuildFullWorld(MushTrackAuthoring authoring)
    {
        EnsureEditableMapReady(authoring, false);
    }

    public static void BakeAllMapsFromCommandLine()
    {
        RequestAllOpenTracks();
    }
}

/// <summary>
/// 만들어진 뒤에는 도로 포인트만 직접 편집하며 지형은 모델 지정 또는 자동 생성 방식만 사용합니다.
/// </summary>
public static class MushEditableMapCreationMenu
{
    [MenuItem("Mush/Maps/Create Track Editor In Current Scene", false, 1)]
    [MenuItem("GameObject/Mush/Track Editor", false, 10)]
    private static void CreateEditableMap(MenuCommand command)
    {
        GameObject mapObject = new("Mush Map Editor");
        Undo.RegisterCreatedObjectUndo(mapObject, "Create Editable Mush Map");

        Undo.AddComponent<MushCurvedMapRuntime>(mapObject);
        Undo.AddComponent<MushMapRideBootstrap>(mapObject);
        MushTrackAuthoring authoring = Undo.AddComponent<MushTrackAuthoring>(mapObject);
        authoring.ConfigureNewMapDefaults();
        EditorUtility.SetDirty(authoring);
        Selection.activeGameObject = mapObject;

        MushTrackEditorWorldPreview.EnsureEditableMapReady(authoring, false);
        mapObject.GetComponent<MushMapRideBootstrap>().BakeRideTeamIntoScene();
        MushSceneAuthoringMigration.Persist(mapObject.scene);
        EditorGUIUtility.PingObject(mapObject);
    }
}
