using System.Collections.Generic; // 트랙 미리보기 포인트를 임시로 보관할 List<T>를 사용합니다.
using UnityEditor; // CustomEditor, Undo, Handles 같은 Unity 6 에디터 API를 사용합니다.
using UnityEditor.SceneManagement; // 현재 씬의 변경 상태와 씬 열림 이벤트를 처리합니다.
using UnityEngine; // Vector3, Transform, Event 같은 Unity 기본 타입을 사용합니다.
using UnityEngine.Rendering; // 씬 뷰 핸들이 지형 뒤에 가려지지 않도록 CompareFunction을 사용합니다.
using UnityEngine.SceneManagement; // 씬 안에서 트랙 컴포넌트를 찾을 때 Scene 타입을 사용합니다.

/// <summary>
/// Mush 맵의 도로 경로만 직접 편집하는 가벼운 전용 에디터입니다.
/// 지형 포인트 편집은 없으며, 지형 모델이 없을 때만 도로를 기준으로 자동 지형을 갱신합니다.
/// </summary>
[CustomEditor(typeof(MushTrackAuthoring))]
public sealed class MushTrackAuthoringEditor : Editor
{
    private readonly List<Vector3> previewRoute = new(); // 실제 곡선으로 샘플링된 도로 중심선을 씬 뷰에 그릴 때 사용합니다.
    private readonly List<Vector3> previewControlPoints = new(); // 사용자가 직접 움직이는 원본 제어점을 씬 뷰 핸들로 표시할 때 사용합니다.
    private readonly List<Vector3> projectedRoutePreview = new(); // 지형 모델이 있을 때 실제 표면에 투영된 도로 중심선을 임시로 받아 표시합니다.

    private SerializedProperty useEditablePathProperty; // 현재 사용자 제어점을 실제 도로 경로로 사용할지 저장된 값을 연결합니다.
    private SerializedProperty sampleSpacingProperty; // 곡선 도로를 몇 m 간격으로 샘플링할지 Inspector와 연결합니다.
    private SerializedProperty overrideTrackWidthsProperty; // 기본 도로 폭 대신 사용자 폭을 사용할지 Inspector와 연결합니다.
    private SerializedProperty roadHalfWidthProperty; // 도로 중심에서 한쪽 가장자리까지의 폭을 Inspector와 연결합니다.
    private SerializedProperty terrainHalfWidthProperty; // 지형 모델이 없을 때 자동 생성 지형의 폭을 Inspector와 연결합니다.
    private SerializedProperty roadModelProperty; // 경로를 따라 반복 배치할 도로 모델을 Inspector와 연결합니다.
    private SerializedProperty useRoadModelProperty; // 저장된 도로 모델 사용 토글을 Inspector와 연결합니다.
    private SerializedProperty terrainModelProperty; // 도로가 표면 높이를 따라갈 지형 모델을 Inspector와 연결합니다.
    private SerializedProperty roadMaterialOverrideProperty; // 도로 모델이 없을 때 생성 도로에 사용할 머티리얼을 Inspector와 연결합니다.
    private SerializedProperty roadTextureOverrideProperty; // 도로 모델이 없을 때 생성 도로에 사용할 텍스처를 Inspector와 연결합니다.
    private SerializedProperty terrainMaterialOverrideProperty; // 지형 모델이 없을 때 자동 지형에 사용할 머티리얼을 Inspector와 연결합니다.
    private SerializedProperty terrainTextureOverrideProperty; // 지형 모델이 없을 때 자동 지형에 사용할 텍스처를 Inspector와 연결합니다.

    private int selectedPoint = -1; // 현재 씬 뷰에서 선택된 도로 제어점 번호를 저장합니다.
    private bool editingTrack; // 씬 뷰에서 도로 포인트 편집 모드가 켜져 있는지 저장합니다.
    private static MushTrackAuthoring pendingTrackEdit; // 씬 뷰의 빠른 편집 버튼이 선택한 트랙을 실제 CustomEditor에 전달합니다.

    public static void BeginTrackEditing(MushTrackAuthoring authoring) // 씬 뷰에서 바로 도로 편집을 시작할 때 사용합니다.
    {
        if (authoring == null)
            return;

        pendingTrackEdit = authoring; // 다음 Inspector/SceneGUI 호출에서 편집 모드를 켤 대상을 기억합니다.
        Selection.activeGameObject = authoring.gameObject; // 사용자가 Hierarchy에서 따로 찾아 선택하지 않아도 해당 맵을 선택합니다.

        Transform mapRoot = authoring.ResolveMapRoot(); // 지형 모델을 쓰는 경우에는 편집 시작 순간 실제 표면 투영 경로를 준비합니다.
        MushCurvedMapRuntime runtime = mapRoot != null ? mapRoot.GetComponent<MushCurvedMapRuntime>() : null;
        if (runtime != null)
            runtime.RefreshEditorPresentationOnly(); // 저장된 기본 도로/지형 Mesh는 건드리지 않고 모델 프리뷰와 투영 경로만 계산합니다.

        SceneView.RepaintAll();
    }

    private void ConsumeTrackEditRequest(MushTrackAuthoring authoring) // 현재 에디터가 빠른 편집 요청 대상인지 확인합니다.
    {
        if (pendingTrackEdit != authoring)
            return;

        pendingTrackEdit = null;
        editingTrack = true; // 도로 포인트/이동 핸들을 즉시 표시합니다.
        if (authoring.ControlPointCount > 0 && selectedPoint < 0)
            selectedPoint = 0;
    }

    private void OnEnable() // Inspector가 이 컴포넌트를 표시하기 시작할 때 SerializedProperty를 한 번 연결합니다.
    {
        useEditablePathProperty = serializedObject.FindProperty("useEditablePath"); // 저장된 편집 경로 사용 여부를 찾습니다.
        sampleSpacingProperty = serializedObject.FindProperty("sampleSpacing"); // 저장된 샘플 간격 값을 찾습니다.
        overrideTrackWidthsProperty = serializedObject.FindProperty("overrideTrackWidths"); // 저장된 폭 직접 지정 값을 찾습니다.
        roadHalfWidthProperty = serializedObject.FindProperty("roadHalfWidth"); // 저장된 도로 반폭 값을 찾습니다.
        terrainHalfWidthProperty = serializedObject.FindProperty("terrainHalfWidth"); // 자동 생성 지형의 반폭 값을 찾습니다.
        roadModelProperty = serializedObject.FindProperty("deformableRoadModule"); // 기존 직렬화 필드를 도로 모델 슬롯으로 사용합니다.
        useRoadModelProperty = serializedObject.FindProperty("useDeformableRoadModule"); // 기존 씬의 사용 여부를 그대로 보존합니다.
        terrainModelProperty = serializedObject.FindProperty("customTerrainVisual"); // 기존 직렬화 필드를 지형 모델 슬롯으로 사용합니다.
        roadMaterialOverrideProperty = serializedObject.FindProperty("roadMaterialOverride"); // 생성 도로 머티리얼 참조를 찾습니다.
        roadTextureOverrideProperty = serializedObject.FindProperty("roadTextureOverride"); // 생성 도로 텍스처 참조를 찾습니다.
        terrainMaterialOverrideProperty = serializedObject.FindProperty("terrainMaterialOverride"); // 자동 지형 머티리얼 참조를 찾습니다.
        terrainTextureOverrideProperty = serializedObject.FindProperty("terrainTextureOverride"); // 자동 지형 텍스처 참조를 찾습니다.

        // Inspector를 선택하거나 씬을 열었다는 이유만으로 생성 Mesh를 다시 쓰지 않습니다.
        // 실제 도로 포인트/모델/재질 값이 바뀐 순간에만 아래 편집 코드에서 RequestRebuild를 호출합니다.
        ConsumeTrackEditRequest(target as MushTrackAuthoring); // 씬 뷰 빠른 편집 요청이 있었다면 선택 직후 바로 포인트를 표시합니다.
    }

    public override void OnInspectorGUI() // MushTrackAuthoring Inspector를 트랙/모델 설정 중심으로 구성합니다.
    {
        MushTrackAuthoring authoring = (MushTrackAuthoring)target; // 현재 편집 중인 실제 트랙 컴포넌트를 가져옵니다.
        ConsumeTrackEditRequest(authoring); // 씬 뷰 빠른 편집 버튼으로 들어온 요청을 즉시 적용합니다.
        GameObject previousRoadModel = authoring.RoadModel; // 도로 모델 교체 여부를 감지하기 위해 변경 전 참조를 기억합니다.
        serializedObject.Update(); // 외부에서 바뀐 직렬화 값을 Inspector에 최신 상태로 동기화합니다.

        EditorGUILayout.HelpBox(
            "지형 포인트 편집은 없습니다. 도로만 포인트로 편집합니다.\n" +
            "지형 모델이 있으면 도로가 그 모델 표면 높이에 자동으로 붙고, 지형 모델이 없으면 자동 지형이 도로 높이를 따라 생성됩니다.",
            MessageType.Info); // 현재 도구의 핵심 동작을 Inspector에서 바로 확인할 수 있게 안내합니다.

        EditorGUILayout.LabelField("경로 설정", EditorStyles.boldLabel); // 도로 형상과 샘플 수를 조절하는 구역을 표시합니다.
        EditorGUILayout.PropertyField(sampleSpacingProperty, new GUIContent("도로 샘플 간격 (m)")); // 곡선의 부드러움과 생성 정점 수를 조절합니다.
        EditorGUILayout.PropertyField(overrideTrackWidthsProperty, new GUIContent("도로 폭 직접 지정")); // 기본 도로 폭 대신 맵별 폭을 사용할지 선택합니다.
        if (overrideTrackWidthsProperty.boolValue) // 직접 지정이 켜졌을 때만 도로 폭 값을 노출합니다.
        {
            EditorGUI.indentLevel++; // 하위 옵션임을 시각적으로 구분합니다.
            EditorGUILayout.PropertyField(roadHalfWidthProperty, new GUIContent("도로 반폭 (m)")); // 도로 중심에서 한쪽 끝까지의 폭을 입력합니다.
            EditorGUI.indentLevel--; // 이후 항목의 들여쓰기를 복원합니다.
        }

        EditorGUILayout.Space(8f); // 경로 설정과 모델 설정 사이를 띄웁니다.
        EditorGUILayout.LabelField("도로 모델", EditorStyles.boldLabel); // 아래 슬롯이 실제 도로 외형을 교체하는 설정임을 표시합니다.
        GameObject roadModelBeforeField = roadModelProperty.objectReferenceValue as GameObject; // 슬롯 교체를 감지하기 위해 현재 모델을 기억합니다.
        EditorGUILayout.PropertyField(roadModelProperty, new GUIContent("도로 모델")); // Prefab/FBX를 지정하면 경로를 따라 공유 인스턴스로 반복 배치합니다.
        GameObject roadModelAfterField = roadModelProperty.objectReferenceValue as GameObject; // 사용자가 방금 고른 새 모델을 읽습니다.
        if (roadModelAfterField != roadModelBeforeField) // 새 모델을 직접 골랐을 때는 별도 토글을 또 켜야 하는 번거로움을 없앱니다.
            useRoadModelProperty.boolValue = roadModelAfterField != null; // 모델을 넣으면 사용, None으로 빼면 기본 도로로 자동 전환합니다.
        using (new EditorGUI.DisabledScope(roadModelAfterField == null))
            EditorGUILayout.PropertyField(useRoadModelProperty, new GUIContent("도로 모델 사용")); // 모델을 보관한 채 기본 도로와 비교하고 싶을 때만 수동으로 끌 수 있습니다.

        if (roadModelProperty.objectReferenceValue == null || !useRoadModelProperty.boolValue) // 별도 도로 모델이 없거나 사용을 껐으면 기본 리본 도로 외형 설정을 보여줍니다.
        {
            EditorGUI.indentLevel++; // 기본 도로 외형 항목을 도로 모델의 하위 설정처럼 표시합니다.
            EditorGUILayout.PropertyField(roadMaterialOverrideProperty, new GUIContent("기본 도로 머티리얼")); // 기본 생성 도로의 머티리얼을 바꿉니다.
            EditorGUILayout.PropertyField(roadTextureOverrideProperty, new GUIContent("기본 도로 텍스처")); // 기본 생성 도로의 텍스처를 바꿉니다.
            EditorGUI.indentLevel--; // 들여쓰기를 원래대로 되돌립니다.
        }
        else // 도로 모델이 지정된 경우에는 배치 규칙을 바로 보여줍니다.
        {
            EditorGUILayout.HelpBox(
                "선택한 도로 모델의 Mesh/Material은 복제하지 않고 공유합니다. 모델의 로컬 Z축을 길이 방향으로 보고 현재 트랙 곡선·오르막·내리막을 따라 구간 인스턴스를 배치합니다.",
                MessageType.None); // 대용량 변형 Mesh를 다시 만들지 않는 구조임을 알려줍니다.
        }

        EditorGUILayout.Space(8f); // 도로 모델과 지형 모델 설정 사이를 띄웁니다.
        EditorGUILayout.LabelField("지형", EditorStyles.boldLabel); // 아래 슬롯이 도로 높이 기준을 결정하는 지형 설정임을 표시합니다.
        EditorGUILayout.PropertyField(terrainModelProperty, new GUIContent("지형 모델")); // 씬 오브젝트 또는 Prefab 지형 모델을 지정할 수 있게 합니다.

        if (terrainModelProperty.objectReferenceValue != null) // 지형 모델이 있으면 도로가 모델을 기준으로 움직입니다.
        {
            EditorGUILayout.HelpBox(
                "지형 모델 사용 중: 도로 중심선은 지형 표면을 위에서 아래로 샘플링해 자동으로 높이를 맞춥니다. Collider가 있으면 그대로 사용하고, 없으면 원본 Mesh를 공유하는 저장되지 않는 MeshCollider 프록시를 자동으로 사용합니다.",
                MessageType.None); // Collider를 따로 넣어야만 동작하는 구조가 아니라는 점을 설명합니다.
        }
        else // 지형 모델이 없으면 도로를 기준으로 자동 지형을 만듭니다.
        {
            EditorGUI.indentLevel++; // 자동 지형 설정을 지형 모델의 대체 옵션처럼 표시합니다.
            EditorGUILayout.PropertyField(terrainHalfWidthProperty, new GUIContent("자동 지형 반폭 (m)")); // 도로 양옆으로 펼쳐질 자동 지형 폭을 조절합니다.
            EditorGUILayout.PropertyField(terrainMaterialOverrideProperty, new GUIContent("자동 지형 머티리얼")); // 자동 지형의 머티리얼을 바꿉니다.
            EditorGUILayout.PropertyField(terrainTextureOverrideProperty, new GUIContent("자동 지형 텍스처")); // 자동 지형의 텍스처를 바꿉니다.
            EditorGUI.indentLevel--; // 들여쓰기를 복원합니다.

            EditorGUILayout.HelpBox(
                "지형 모델 없음: 별도 지형 편집점은 만들지 않고 현재 도로의 높이와 곡선을 기준으로 자동 지형만 가볍게 다시 계산합니다.",
                MessageType.None); // 수동 지형 편집이 다시 생기지 않는다는 점을 분명히 합니다.
        }

        EditorGUILayout.Space(8f); // 모델 설정과 실제 포인트 편집 조작 사이를 분리합니다.
        EditorGUILayout.LabelField("도로 포인트 편집", EditorStyles.boldLabel); // 아래 버튼들이 트랙 포인트용임을 명확히 표시합니다.

        if (GUILayout.Button(editingTrack ? "도로 편집 종료 (Esc)" : "도로 포인트 편집")) // 한 버튼으로 편집 모드를 켜고 끌 수 있게 합니다.
        {
            editingTrack = !editingTrack; // 현재 편집 모드를 반전합니다.
            SceneView.RepaintAll(); // 씬 뷰 상단 안내와 포인트 표시를 즉시 갱신합니다.
        }

        EditorGUILayout.HelpBox(
            editingTrack
                ? "도로 선 위 Shift+클릭: 포인트 추가 / Delete: 선택 포인트 삭제 / 이동 핸들: 위치·높이 변경 / Esc: 편집 종료"
                : "도로 포인트 편집 버튼을 눌렀을 때만 트랙 제어점을 조작합니다.",
            MessageType.None); // 현재 편집 상태에서 가능한 입력만 짧게 안내합니다.

        bool wasEditable = useEditablePathProperty.boolValue; // Inspector 변경을 적용하기 전 현재 경로 사용 상태를 기억합니다.
        EditorGUILayout.LabelField(
            "경로 상태",
            wasEditable ? $"편집 경로 ({authoring.ControlPointCount}개 포인트)" : "기본 프로토타입 경로"); // 현재 실제로 몇 개 제어점이 사용되는지 보여줍니다.

        if (!wasEditable) // 아직 사용자 경로를 사용하지 않는 맵이면 편집 경로를 켜는 선택지만 보여줍니다.
        {
            if (authoring.ControlPointCount >= 2 && GUILayout.Button("보존된 편집 포인트 다시 사용")) // 저장된 제어점이 있으면 그 경로를 다시 활성화합니다.
            {
                serializedObject.ApplyModifiedProperties(); // 버튼 동작 전에 Inspector의 다른 변경값을 실제 객체에 먼저 반영합니다.
                Undo.RecordObject(authoring, "Enable Mush Editable Track"); // Ctrl+Z로 경로 활성화를 되돌릴 수 있게 기록합니다.
                authoring.SetEditablePathEnabled(true); // 저장된 제어점을 실제 도로 경로로 사용하도록 켭니다.
                selectedPoint = 0; // 다시 켠 직후 첫 번째 포인트를 선택 상태로 둡니다.
                EditorUtility.SetDirty(authoring); // 경로 사용 상태가 씬 저장 대상임을 Unity에 알립니다.
                MushTrackEditorWorldPreview.RequestRebuild(authoring); // 도로/필요한 자동 지형만 현재 설정에 맞춰 갱신합니다.
                serializedObject.Update(); // 버튼 처리 후 Inspector 값을 다시 동기화합니다.
            }

            if (GUILayout.Button(authoring.ControlPointCount >= 2
                    ? "기본 직선으로 편집 포인트 다시 만들기"
                    : "기본 직선을 편집 포인트로 변환")) // 저장된 경로가 없거나 초기화하고 싶을 때 기본 직선을 만듭니다.
            {
                serializedObject.ApplyModifiedProperties(); // Inspector의 현재 값부터 실제 객체에 적용합니다.
                Undo.RecordObject(authoring, "Convert Mush Track To Editable Path"); // 기본 경로 변환을 Undo에 기록합니다.
                authoring.BakeDefaultPath(); // 두 개의 기본 제어점으로 직선 경로를 만듭니다.
                selectedPoint = 0; // 새 경로의 첫 포인트를 선택합니다.
                EditorUtility.SetDirty(authoring); // 새 제어점 데이터가 씬 저장 대상임을 표시합니다.
                MushTrackEditorWorldPreview.RequestRebuild(authoring); // 새 기본 경로에 맞춰 화면을 갱신합니다.
                serializedObject.Update(); // Inspector 표시를 새 상태로 갱신합니다.
            }
        }
        else // 사용자 경로를 사용 중이면 포인트 추가/삭제/반전/초기화 조작을 보여줍니다.
        {
            using (new EditorGUILayout.HorizontalScope()) // 추가와 삭제 버튼을 한 줄에 배치합니다.
            {
                if (GUILayout.Button("선택 뒤에 포인트 추가")) // 현재 선택 포인트 뒤에 새 제어점을 삽입합니다.
                {
                    serializedObject.ApplyModifiedProperties(); // Inspector 변경값을 먼저 적용합니다.
                    Undo.RecordObject(authoring, "Add Mush Track Point"); // 포인트 추가를 Undo에 기록합니다.
                    selectedPoint = authoring.InsertControlPointAfter(
                        selectedPoint >= 0 ? selectedPoint : authoring.ControlPointCount - 1); // 선택점이 없으면 마지막 뒤에, 있으면 선택점 뒤에 추가합니다.
                    EditorUtility.SetDirty(authoring); // 제어점 목록 변경을 씬 저장 대상으로 표시합니다.
                    MushTrackEditorWorldPreview.RequestRebuild(authoring); // 도로와 필요한 자동 지형만 다시 만듭니다.
                    serializedObject.Update(); // Inspector 포인트 개수를 즉시 갱신합니다.
                }

                using (new EditorGUI.DisabledScope(authoring.ControlPointCount <= 2 || selectedPoint < 0)) // 최소 두 점은 남겨야 하므로 삭제 불가능한 상태에서는 버튼을 비활성화합니다.
                {
                    if (GUILayout.Button("선택 포인트 삭제")) // 현재 선택된 제어점을 삭제합니다.
                    {
                        serializedObject.ApplyModifiedProperties(); // Inspector 변경값을 먼저 실제 객체에 적용합니다.
                        Undo.RecordObject(authoring, "Delete Mush Track Point"); // 포인트 삭제를 Undo에 기록합니다.
                        selectedPoint = authoring.RemoveControlPoint(selectedPoint); // 선택점을 지우고 남은 유효 인덱스를 다시 받습니다.
                        EditorUtility.SetDirty(authoring); // 제어점 목록 변경을 저장 대상으로 표시합니다.
                        MushTrackEditorWorldPreview.RequestRebuild(authoring); // 도로와 필요한 자동 지형만 다시 갱신합니다.
                        serializedObject.Update(); // Inspector 상태를 다시 동기화합니다.
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope()) // 방향 뒤집기와 기본 형태 초기화를 한 줄에 배치합니다.
            {
                if (GUILayout.Button("경로 방향 뒤집기")) // 시작점과 끝점 방향을 반대로 바꿉니다.
                {
                    serializedObject.ApplyModifiedProperties(); // Inspector 변경값을 먼저 적용합니다.
                    Undo.RecordObject(authoring, "Reverse Mush Track"); // 방향 반전을 Undo에 기록합니다.
                    selectedPoint = authoring.ReverseControlPoints(selectedPoint); // 제어점 순서를 뒤집고 선택 인덱스도 같은 물리적 점을 가리키게 보정합니다.
                    EditorUtility.SetDirty(authoring); // 변경된 경로를 씬 저장 대상으로 표시합니다.
                    MushTrackEditorWorldPreview.RequestRebuild(authoring); // 새 순서에 맞춰 도로와 자동 지형을 갱신합니다.
                    serializedObject.Update(); // Inspector 표시를 새 경로 상태와 맞춥니다.
                }

                if (GUILayout.Button("기본 형태로 다시 만들기")) // 현재 경로를 기본 직선 두 점으로 되돌립니다.
                {
                    serializedObject.ApplyModifiedProperties(); // Inspector 변경값을 먼저 적용합니다.
                    Undo.RecordObject(authoring, "Reset Mush Track Path"); // 경로 초기화를 Undo에 기록합니다.
                    authoring.BakeDefaultPath(); // 기본 직선 제어점을 다시 만듭니다.
                    selectedPoint = 0; // 첫 포인트를 선택합니다.
                    EditorUtility.SetDirty(authoring); // 새 기본 경로가 씬 저장 대상임을 표시합니다.
                    MushTrackEditorWorldPreview.RequestRebuild(authoring); // 기본 직선 기준으로 화면을 갱신합니다.
                    serializedObject.Update(); // Inspector 상태를 다시 읽습니다.
                }
            }

            if (GUILayout.Button("편집 경로 사용 중지 (포인트는 보존)")) // 제어점은 남긴 채 기본 경로 표시로 돌아갑니다.
            {
                serializedObject.ApplyModifiedProperties(); // Inspector 변경값을 먼저 적용합니다.
                Undo.RecordObject(authoring, "Disable Mush Editable Track"); // 사용 중지 동작을 Undo에 기록합니다.
                authoring.SetEditablePathEnabled(false); // 사용자 제어점 사용만 끄고 목록은 그대로 보존합니다.
                EditorUtility.SetDirty(authoring); // 사용 상태 변경을 씬 저장 대상으로 표시합니다.
                MushTrackEditorWorldPreview.RequestRebuild(authoring); // 기본 경로 기준으로 화면을 갱신합니다.
                serializedObject.Update(); // Inspector를 새 상태로 동기화합니다.
            }
        }

        Transform mapRoot = authoring.ResolveMapRoot(); // 도로 포인트가 저장되는 로컬 좌표의 기준 루트를 찾습니다.
        if (mapRoot == null) // 트랙 컴포넌트가 어떤 맵에도 연결되지 않았다면 편집할 수 없습니다.
        {
            EditorGUILayout.HelpBox("편집할 맵 루트를 찾지 못했습니다.", MessageType.Warning); // 연결 문제를 Inspector에서 바로 알립니다.
        }
        else // 정상적인 맵 루트가 있으면 현재 곡선 길이를 계산해 표시합니다.
        {
            authoring.CopyPreviewRoute(previewRoute); // 기본적으로 저장된 제어점 곡선을 가져옵니다.
            MushCurvedMapRuntime runtime = mapRoot.GetComponent<MushCurvedMapRuntime>(); // 실제 지형 투영이 적용된 런타임 경로가 있는지 확인합니다.
            if (authoring.HasCustomTerrainVisual && runtime != null) // 지형 모델이 있을 때는 실제 투영된 경로를 우선 표시합니다.
            {
                runtime.CopyActiveRoutePreview(projectedRoutePreview); // 런타임이 실제 사용하는 지형 맞춤 경로를 재사용 목록에 복사합니다.
                if (projectedRoutePreview.Count >= 2) // 정상 투영 경로가 있으면 길이 계산에도 그 경로를 사용합니다.
                {
                    previewRoute.Clear(); // 기존 비투영 경로를 비웁니다.
                    previewRoute.AddRange(projectedRoutePreview); // 실제 도로와 동일한 높이 경로로 교체합니다.
                }
            }

            EditorGUILayout.LabelField("예상 트랙 길이", $"{CalculateLength(previewRoute):0.0} m"); // 샘플 간 거리 합으로 실제 경로 길이를 표시합니다.
        }

        bool propertiesChanged = serializedObject.ApplyModifiedProperties(); // Inspector에서 바뀐 경로/모델/재질 값을 실제 객체에 최종 반영합니다.
        if (propertiesChanged) // 모델이나 샘플 간격, 폭, 재질 등이 바뀌었다면 필요한 부분만 갱신합니다.
        {
            EditorUtility.SetDirty(authoring); // 변경된 직렬화 값이 씬 저장 대상임을 표시합니다.

            if (mapRoot != null && previousRoadModel != authoring.RoadModel) // 도로 모델 자체가 교체된 경우에는 이전 모델 인스턴스를 정확히 비웁니다.
            {
                MushCurvedMapRuntime runtime = mapRoot.GetComponent<MushCurvedMapRuntime>(); // 현재 맵 런타임을 가져옵니다.
                if (runtime != null) // 런타임이 이미 있으면 이전 도로 모델 인스턴스만 제거합니다.
                    runtime.InvalidateRoadModelInstances(); // 새 모델이 같은 구간 수여도 이전 Mesh가 재사용되지 않게 합니다.
            }

            MushTrackEditorWorldPreview.RequestRebuild(authoring); // 도로와 필요한 자동 지형/모델 위치만 가볍게 다시 계산합니다.
        }
    }

    private void OnSceneGUI() // 씬 뷰에서 도로 선, 포인트 버튼, 이동 핸들을 표시합니다.
    {
        MushTrackAuthoring authoring = (MushTrackAuthoring)target; // 현재 편집 중인 트랙 컴포넌트를 가져옵니다.
        ConsumeTrackEditRequest(authoring); // 선택이 이미 유지된 상태에서도 빠른 편집 요청을 받을 수 있게 합니다.
        Transform mapRoot = authoring.ResolveMapRoot(); // 로컬 제어점을 월드 좌표로 변환할 기준 루트를 찾습니다.
        if (mapRoot == null) // 맵 루트가 없으면 씬 뷰 편집을 진행할 수 없습니다.
            return;

        CompareFunction previousZTest = Handles.zTest; // 다른 에디터 도구의 기존 깊이 테스트 값을 보관합니다.
        try // 도로 핸들을 항상 잘 보이게 그린 뒤 반드시 원래 깊이 테스트를 복원합니다.
        {
            Handles.zTest = CompareFunction.Always; // 도로 포인트가 지형 표면에 묻혀 선택하기 어려워지지 않게 합니다.
            DrawSceneEditor(authoring, mapRoot); // 실제 도로 선과 포인트 입력 처리를 수행합니다.
        }
        finally // 예외가 나더라도 다른 Unity 핸들의 그리기 상태를 망치지 않게 복원합니다.
        {
            Handles.zTest = previousZTest; // 이 에디터가 사용하기 전의 깊이 테스트 값을 되돌립니다.
        }
    }

    private void DrawSceneEditor(MushTrackAuthoring authoring, Transform mapRoot) // 현재 트랙을 씬 뷰에 그리고 입력을 처리합니다.
    {
        authoring.CopyEditableControlPointPreview(previewControlPoints); // 실제 저장된 제어점 또는 기본 두 점을 미리보기 목록으로 가져옵니다.
        if (previewControlPoints.Count < 2) // 도로는 최소 두 점이 있어야 선을 만들 수 있습니다.
            return;

        authoring.CopyPreviewRoute(previewRoute); // 제어점을 부드럽게 샘플링한 기본 도로 중심선을 가져옵니다.
        if (authoring.HasCustomTerrainVisual) // 지형 모델을 사용 중이면 씬 뷰 선도 실제 지형에 붙은 높이를 보여줍니다.
        {
            MushCurvedMapRuntime runtime = mapRoot.GetComponent<MushCurvedMapRuntime>(); // 현재 맵 런타임에서 투영된 경로를 가져올 준비를 합니다.
            if (runtime != null) // 런타임이 이미 생성되어 있을 때만 실제 경로를 요청합니다.
            {
                runtime.CopyActiveRoutePreview(projectedRoutePreview); // 지형 표면 투영이 끝난 실제 샘플을 복사합니다.
                if (projectedRoutePreview.Count >= 2) // 정상 경로가 있을 때만 기존 미리보기를 교체합니다.
                {
                    previewRoute.Clear(); // 비투영 경로를 비웁니다.
                    previewRoute.AddRange(projectedRoutePreview); // 화면에 실제 도로와 동일한 높이의 선을 사용합니다.
                }
            }
        }

        Vector3[] worldRoute = new Vector3[previewRoute.Count]; // 중심선의 월드 좌표 배열을 준비합니다.
        Vector3[] worldLeftRoadEdge = new Vector3[previewRoute.Count]; // 왼쪽 도로 가장자리의 월드 좌표 배열을 준비합니다.
        Vector3[] worldRightRoadEdge = new Vector3[previewRoute.Count]; // 오른쪽 도로 가장자리의 월드 좌표 배열을 준비합니다.

        for (int index = 0; index < previewRoute.Count; index++) // 각 샘플 위치에서 진행 방향과 좌우 방향을 구해 도로 폭을 시각화합니다.
        {
            int previous = Mathf.Max(0, index - 1); // 첫 점에서는 자기 자신을 이전 샘플로 사용합니다.
            int next = Mathf.Min(previewRoute.Count - 1, index + 1); // 마지막 점에서는 자기 자신을 다음 샘플로 사용합니다.
            Vector3 tangent = Vector3.ProjectOnPlane(previewRoute[next] - previewRoute[previous], Vector3.up).normalized; // 높이 변화와 별개로 수평 진행 방향을 계산합니다.
            if (tangent.sqrMagnitude < 0.0001f) // 같은 위치의 점 때문에 진행 방향을 만들 수 없으면 안전한 기본 방향을 사용합니다.
                tangent = Vector3.back;

            Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized; // 진행 방향에 직각인 도로의 오른쪽 방향을 계산합니다.
            Vector3 liftedPoint = previewRoute[index] + Vector3.up * 0.16f; // 씬 지형과 정확히 겹쳐 선이 깜빡이지 않도록 조금 위에서 표시합니다.
            worldRoute[index] = mapRoot.TransformPoint(liftedPoint); // 로컬 중심점을 실제 씬 월드 좌표로 변환합니다.
            worldLeftRoadEdge[index] = mapRoot.TransformPoint(liftedPoint - right * authoring.PreviewRoadHalfWidth); // 현재 폭만큼 왼쪽 가장자리 위치를 계산합니다.
            worldRightRoadEdge[index] = mapRoot.TransformPoint(liftedPoint + right * authoring.PreviewRoadHalfWidth); // 현재 폭만큼 오른쪽 가장자리 위치를 계산합니다.
        }

        Handles.color = new Color(0.15f, 0.9f, 1f, 0.75f); // 중심선은 눈에 잘 띄는 청록색으로 표시합니다.
        Handles.DrawAAPolyLine(2.5f, worldRoute); // 곡선으로 샘플링된 실제 도로 중심선을 그립니다.
        Handles.color = new Color(0.15f, 0.9f, 1f, 0.48f); // 가장자리는 중심선보다 조금 옅게 표시합니다.
        Handles.DrawAAPolyLine(2f, worldLeftRoadEdge); // 왼쪽 도로 경계를 그립니다.
        Handles.DrawAAPolyLine(2f, worldRightRoadEdge); // 오른쪽 도로 경계를 그립니다.

        if (!editingTrack) // 일반 씬 편집 중에는 도로 선만 보여주고 입력을 가로채지 않습니다.
            return;

        Event currentEvent = Event.current; // 현재 씬 뷰의 마우스/키보드 이벤트를 가져옵니다.
        int sceneEditControl = GUIUtility.GetControlID("MushTrackOnlySceneEdit".GetHashCode(), FocusType.Passive); // 이 도구가 씬 입력을 받을 전용 컨트롤 ID를 만듭니다.
        if (currentEvent.type == EventType.Layout && !currentEvent.alt) // Alt 카메라 조작 중이 아닐 때만 빈 공간 클릭을 도로 편집기가 받습니다.
            HandleUtility.AddDefaultControl(sceneEditControl);

        Handles.BeginGUI(); // 씬 뷰 위에 작은 사용법 안내 상자를 그리기 시작합니다.
        GUILayout.BeginArea(new Rect(12f, 12f, 510f, 48f), EditorStyles.helpBox); // 씬 뷰 왼쪽 위에 고정된 안내 영역을 만듭니다.
        GUILayout.Label("도로 편집 중 · Shift+클릭: 추가 · Delete: 삭제 · 이동 핸들: 위치/높이 · Esc: 종료"); // 현재 사용할 수 있는 입력만 표시합니다.
        GUILayout.EndArea(); // 안내 영역을 닫습니다.
        Handles.EndGUI(); // 씬 뷰 GUI 그리기를 마칩니다.

        if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.Escape) // Esc를 누르면 다른 오브젝트를 건드리지 않고 도로 편집만 종료합니다.
        {
            editingTrack = false; // 편집 모드를 끕니다.
            currentEvent.Use(); // Esc 입력이 다른 씬 도구로 중복 전달되지 않게 소비합니다.
            Repaint(); // Inspector 버튼 문구를 즉시 갱신합니다.
            SceneView.RepaintAll(); // 씬 뷰의 포인트 표시를 즉시 제거합니다.
            return;
        }

        if (currentEvent.type == EventType.KeyDown &&
            (currentEvent.keyCode == KeyCode.Delete || currentEvent.keyCode == KeyCode.Backspace) &&
            selectedPoint >= 0 && authoring.ControlPointCount > 2) // 선택된 포인트가 있고 최소 두 점을 남길 수 있을 때만 삭제를 허용합니다.
        {
            Undo.RecordObject(authoring, "Delete Mush Track Point"); // 포인트 삭제를 Undo에 기록합니다.
            selectedPoint = authoring.RemoveControlPoint(selectedPoint); // 선택 포인트를 삭제하고 남은 유효 선택 인덱스를 받습니다.
            EditorUtility.SetDirty(authoring); // 제어점 목록 변경을 씬 저장 대상으로 표시합니다.
            MushTrackEditorWorldPreview.RequestRebuild(authoring); // 지형을 건드리지 않고 도로만 즉시 갱신합니다.
            currentEvent.Use(); // Delete 입력이 다른 선택 오브젝트 삭제로 넘어가지 않게 소비합니다.
            Repaint(); // Inspector의 포인트 개수를 갱신합니다.
            return;
        }

        if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 &&
            currentEvent.shift && !currentEvent.alt &&
            TryGetPointInsertion(mapRoot, currentEvent.mousePosition, out int segmentIndex, out Vector3 localPoint)) // Shift+좌클릭이 실제 도로 선 가까이에서 발생했을 때만 새 점을 삽입합니다.
        {
            Undo.RecordObject(authoring, "Add Mush Track Point"); // 새 점 추가를 Undo에 기록합니다.
            if (!authoring.UsesEditablePath) // 아직 기본 경로를 사용 중이면 먼저 저장 가능한 편집 제어점으로 변환합니다.
                authoring.BakeDefaultPath();

            selectedPoint = authoring.InsertControlPointAfter(segmentIndex); // 클릭한 도로 구간 뒤에 새 제어점을 삽입합니다.
            authoring.SetControlPoint(selectedPoint, localPoint); // 새 제어점을 사용자가 클릭한 곡선 위치로 옮깁니다.
            EditorUtility.SetDirty(authoring); // 새 포인트 데이터를 씬 저장 대상으로 표시합니다.
            MushTrackEditorWorldPreview.RequestRebuild(authoring); // 무거운 지형 재생성 없이 도로만 새 점으로 갱신합니다.
            currentEvent.Use(); // 클릭이 다른 오브젝트 선택으로도 처리되지 않게 소비합니다.
            Repaint(); // Inspector 포인트 개수를 즉시 갱신합니다.
            return;
        }

        for (int index = 0; index < previewControlPoints.Count; index++) // 모든 원본 제어점에 클릭 가능한 원형 핸들을 표시합니다.
        {
            Vector3 displayLocalPoint = GetControlPointDisplayPosition(authoring, previewControlPoints[index]); // 지형 모델이 있으면 포인트 핸들도 실제 표면 높이에 표시합니다.
            Vector3 worldPoint = mapRoot.TransformPoint(displayLocalPoint); // 표시용 로컬 제어점을 씬 월드 좌표로 변환합니다.
            float size = HandleUtility.GetHandleSize(worldPoint) * 0.075f; // 카메라 거리에 관계없이 비슷한 화면 크기로 핸들을 보이게 합니다.
            Handles.color = index == selectedPoint
                ? new Color(1f, 0.72f, 0.12f)
                : authoring.UsesEditablePath ? new Color(0.15f, 0.9f, 1f) : new Color(0.35f, 0.95f, 0.65f); // 선택점, 편집점, 기본점의 색을 구분합니다.

            if (Handles.Button(worldPoint, Quaternion.identity, size, size * 1.3f, Handles.SphereHandleCap)) // 원형 핸들을 클릭하면 해당 제어점을 선택합니다.
            {
                selectedPoint = index; // 클릭한 점 번호를 현재 선택점으로 저장합니다.
                Repaint(); // Inspector 선택 상태를 즉시 갱신합니다.
            }
        }

        if (selectedPoint < 0 || selectedPoint >= previewControlPoints.Count) // 아직 선택점이 없으면 위치 이동 핸들은 그리지 않습니다.
            return;

        Vector3 selectedDisplayLocalPoint = GetControlPointDisplayPosition(authoring, previewControlPoints[selectedPoint]); // 선택 포인트도 지형 모델 표면 높이에 표시합니다.
        Vector3 selectedWorldPoint = mapRoot.TransformPoint(selectedDisplayLocalPoint); // 선택된 제어점의 실제 표시 월드 위치를 구합니다.
        Handles.Label(
            selectedWorldPoint + Vector3.up * HandleUtility.GetHandleSize(selectedWorldPoint) * 0.14f,
            $"트랙 포인트 {selectedPoint + 1}/{previewControlPoints.Count}"); // 선택점 위에 현재 번호와 전체 개수를 표시합니다.

        EditorGUI.BeginChangeCheck(); // PositionHandle이 실제로 움직였는지 감지하기 시작합니다.
        Vector3 movedWorldPoint = Handles.PositionHandle(selectedWorldPoint, Quaternion.identity); // Unity 기본 이동 핸들로 X/Y/Z 위치를 직접 조절합니다.
        if (!EditorGUI.EndChangeCheck()) // 사용자가 핸들을 움직이지 않았다면 아무 메시도 다시 만들지 않습니다.
            return;

        Undo.RecordObject(authoring, "Move Mush Track Point"); // 포인트 이동을 Undo에 기록합니다.
        if (!authoring.UsesEditablePath) // 기본 경로의 점을 처음 움직이는 경우 편집 경로로 자동 변환합니다.
            authoring.BakeDefaultPath();

        authoring.SetControlPoint(selectedPoint, mapRoot.InverseTransformPoint(movedWorldPoint)); // 월드에서 움직인 위치를 맵 로컬 좌표로 변환해 저장합니다.
        EditorUtility.SetDirty(authoring); // 변경된 포인트 위치가 씬 저장 대상임을 표시합니다.
        MushTrackEditorWorldPreview.RequestRebuild(authoring); // 도로 메시만 새 위치에 맞춰 갱신하며 지형은 그대로 둡니다.
    }

    private Vector3 GetControlPointDisplayPosition(MushTrackAuthoring authoring, Vector3 controlPoint) // 지형 모델 사용 시 제어점 핸들을 실제 도로 표면 높이에 맞춥니다.
    {
        if (!authoring.HasCustomTerrainVisual || previewRoute.Count == 0) // 지형 모델이 없거나 아직 투영 경로가 준비되지 않았으면 원래 위치를 사용합니다.
            return controlPoint;

        int nearestIndex = 0; // XZ 기준으로 가장 가까운 실제 도로 샘플 인덱스를 저장합니다.
        float nearestSqrDistance = float.PositiveInfinity; // 현재까지의 최소 XZ 제곱 거리를 저장합니다.
        Vector2 controlXZ = new(controlPoint.x, controlPoint.z); // 높이를 제외한 제어점 평면 좌표를 만듭니다.

        for (int index = 0; index < previewRoute.Count; index++) // 실제 투영 경로의 모든 샘플을 확인합니다.
        {
            Vector3 routePoint = previewRoute[index]; // 현재 실제 도로 샘플을 가져옵니다.
            Vector2 delta = new(routePoint.x - controlXZ.x, routePoint.z - controlXZ.y); // 제어점과 샘플의 XZ 차이를 계산합니다.
            float sqrDistance = delta.sqrMagnitude; // 제곱 거리로 빠르게 비교합니다.
            if (sqrDistance >= nearestSqrDistance) // 기존 후보보다 멀면 건너뜁니다.
                continue;

            nearestSqrDistance = sqrDistance; // 가장 가까운 거리로 갱신합니다.
            nearestIndex = index; // 가장 가까운 샘플 인덱스를 기록합니다.
        }

        controlPoint.y = previewRoute[nearestIndex].y; // XZ는 사용자가 저장한 값을 유지하고 표시 높이만 실제 지형 도로 높이에 맞춥니다.
        return controlPoint; // Scene View 핸들에 사용할 위치를 반환합니다.
    }

    private bool TryGetPointInsertion(
        Transform mapRoot,
        Vector2 mousePosition,
        out int segmentIndex,
        out Vector3 localPoint) // 마우스가 현재 도로 선의 어느 구간에 가까운지 찾아 새 제어점 삽입 위치를 계산합니다.
    {
        const float maximumDistancePixels = 24f; // 도로 선에서 24픽셀보다 멀리 클릭한 Shift+클릭은 포인트 추가로 처리하지 않습니다.
        segmentIndex = -1; // 아직 삽입할 제어점 구간을 찾지 못한 상태로 초기화합니다.
        localPoint = default; // 아직 새 포인트 위치를 찾지 못한 상태로 초기화합니다.
        float nearestDistance = float.PositiveInfinity; // 화면에서 가장 가까운 도로 샘플 구간 거리를 추적합니다.

        for (int index = 0; index < previewRoute.Count - 1; index++) // 실제 곡선을 이루는 모든 샘플 구간을 확인합니다.
        {
            Vector2 start = HandleUtility.WorldToGUIPoint(mapRoot.TransformPoint(previewRoute[index])); // 구간 시작점을 씬 GUI 픽셀 좌표로 바꿉니다.
            Vector2 end = HandleUtility.WorldToGUIPoint(mapRoot.TransformPoint(previewRoute[index + 1])); // 구간 끝점을 씬 GUI 픽셀 좌표로 바꿉니다.
            Vector2 segment = end - start; // 화면상 구간 방향 벡터를 구합니다.
            float segmentLengthSqr = segment.sqrMagnitude; // 투영 계산에서 제곱 길이를 재사용합니다.
            float t = segmentLengthSqr > 0.001f
                ? Mathf.Clamp01(Vector2.Dot(mousePosition - start, segment) / segmentLengthSqr)
                : 0f; // 마우스를 현재 선분 위에 투영한 0~1 비율을 구합니다.
            float distance = Vector2.Distance(mousePosition, start + segment * t); // 마우스와 실제 선분 사이의 화면 픽셀 거리를 계산합니다.
            if (distance >= nearestDistance) // 지금까지 찾은 구간보다 멀면 후보를 바꾸지 않습니다.
                continue;

            nearestDistance = distance; // 가장 가까운 화면 거리로 갱신합니다.
            localPoint = Vector3.Lerp(previewRoute[index], previewRoute[index + 1], t); // 클릭 위치에 대응하는 도로 로컬 좌표를 저장합니다.
        }

        if (nearestDistance > maximumDistancePixels) // 도로와 충분히 가까운 클릭이 아니면 포인트 추가를 거부합니다.
            return false;

        float nearestControlDistanceSqr = float.PositiveInfinity; // 클릭한 곡선 위치가 원본 제어점의 어느 구간에 속하는지 찾기 위한 최소 거리를 준비합니다.
        for (int index = 0; index < previewControlPoints.Count - 1; index++) // 원본 제어점의 모든 구간을 확인합니다.
        {
            Vector2 start = new(previewControlPoints[index].x, previewControlPoints[index].z); // 지형 모델 높이와 무관하게 제어 구간의 XZ 시작점을 사용합니다.
            Vector2 end = new(previewControlPoints[index + 1].x, previewControlPoints[index + 1].z); // 다음 제어점의 XZ 위치를 가져옵니다.
            Vector2 segment = end - start; // 평면상 제어 구간 방향을 계산합니다.
            Vector2 localPointXZ = new(localPoint.x, localPoint.z); // 클릭한 실제 도로 위치도 XZ 평면으로 바꿉니다.
            float segmentLengthSqr = segment.sqrMagnitude; // 평면 투영 계산에 사용할 제곱 길이를 구합니다.
            float t = segmentLengthSqr > 0.0001f
                ? Mathf.Clamp01(Vector2.Dot(localPointXZ - start, segment) / segmentLengthSqr)
                : 0f; // 클릭 위치를 원본 제어 구간의 XZ 선분 위에 투영합니다.
            float distanceSqr = (localPointXZ - (start + segment * t)).sqrMagnitude; // 지형 높이를 무시한 평면 거리로 삽입 구간을 결정합니다.
            if (distanceSqr >= nearestControlDistanceSqr) // 기존 후보보다 멀면 건너뜁니다.
                continue;

            nearestControlDistanceSqr = distanceSqr; // 현재 구간을 가장 가까운 제어 구간으로 기록합니다.
            segmentIndex = index; // 새 포인트를 이 제어점 뒤에 삽입하도록 인덱스를 저장합니다.
        }

        return segmentIndex >= 0; // 실제 삽입 가능한 제어 구간을 찾았을 때만 성공으로 처리합니다.
    }

    private static float CalculateLength(IReadOnlyList<Vector3> points) // 샘플링된 도로의 전체 3D 길이를 계산합니다.
    {
        float length = 0f; // 누적 길이를 0에서 시작합니다.
        for (int index = 1; index < points.Count; index++) // 두 번째 점부터 이전 점과의 거리를 계속 더합니다.
            length += Vector3.Distance(points[index - 1], points[index]); // 높이 차이까지 포함한 실제 3D 거리로 누적합니다.
        return length; // 계산된 전체 도로 길이를 반환합니다.
    }
}

/// <summary>
/// 씬 뷰에서 도로 포인트가 바뀌었을 때 필요한 것만 다시 계산하는 가벼운 갱신 관리자입니다.
/// 지형 모델이 있으면 도로만 지형에 투영하고, 없으면 작은 자동 지형만 갱신하며 GeneratedMaps 베이크는 하지 않습니다.
/// </summary>
[InitializeOnLoad]
public static class MushTrackEditorWorldPreview
{
    private static readonly HashSet<MushTrackAuthoring> PendingTracks = new(); // 같은 프레임에 같은 트랙이 여러 번 요청되어도 한 번만 갱신하도록 중복을 제거합니다.
    private static readonly HashSet<Mesh> DirtyGeneratedMeshes = new(); // 실제 도로 편집으로 바뀐 작은 생성 Mesh만 Ctrl+S 때 저장합니다.
    private static bool rebuildScheduled; // delayCall에 도로 갱신 함수가 이미 예약되어 있는지 저장합니다.
    private static bool rebuilding; // 도로 갱신 도중 다시 갱신 요청이 겹치는 것을 막습니다.

    static MushTrackEditorWorldPreview() // Unity 에디터가 스크립트를 로드할 때 필요한 이벤트를 한 번 연결합니다.
    {
        Undo.undoRedoPerformed += HandleUndoRedo; // Ctrl+Z / Ctrl+Y는 실제 편집 변경이므로 도로 화면도 함께 갱신합니다.
        EditorSceneManager.sceneSaved += HandleSceneSaved; // 사용자가 Ctrl+S로 씬 저장을 끝낸 뒤 실제로 바뀐 작은 도로/지형 Mesh 에셋만 함께 저장합니다.
        SceneView.duringSceneGui += DrawQuickTrackEditButton; // Hierarchy를 뒤질 필요 없이 씬 뷰에서 바로 도로 편집을 시작할 수 있게 합니다.
    }

    private static void DrawQuickTrackEditButton(SceneView sceneView) // 씬에 트랙이 하나 있으면 상단에서 바로 편집을 시작합니다.
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
            MushTrackAuthoringEditor.BeginTrackEditing(found); // 선택과 편집 모드를 한 번에 켭니다.
        GUILayout.EndArea();
        Handles.EndGUI();
    }

    private static void MarkCourseMeshesDirty(Transform mapRoot) // 실제 포인트/폭 변경 때만 작은 생성 Mesh를 저장 대상으로 표시합니다.
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
            if (mesh == null || !AssetDatabase.Contains(mesh)) // DontSave/런타임 Mesh는 프로젝트 파일로 저장하지 않습니다.
                continue;

            EditorUtility.SetDirty(mesh); // 현재 작은 Mesh sub-asset만 실제 변경 대상으로 표시합니다.
            DirtyGeneratedMeshes.Add(mesh);
        }
    }

    private static void HandleSceneSaved(Scene scene) // 일반 Ctrl+S가 끝난 뒤 도로/지형의 작은 Mesh 변경도 함께 디스크에 기록합니다.
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
                AssetDatabase.SaveAssetIfDirty(mesh); // 전체 AssetDatabase 저장/강제 Import 없이 해당 작은 asset만 저장합니다.
        }
    }

    public static void RequestRebuild(MushTrackAuthoring authoring) // 특정 트랙의 도로 메시만 다시 계산하도록 요청합니다.
    {
        if (authoring == null || EditorApplication.isPlayingOrWillChangePlaymode) // 플레이 전환 중에는 에디터 씬을 수정하지 않습니다.
            return;

        PendingTracks.Add(authoring); // 같은 트랙 요청이 여러 번 와도 HashSet에 한 번만 저장합니다.
        SchedulePendingRebuild(); // 사용자가 핸들을 놓은 뒤 한 번만 처리하도록 delayCall을 예약합니다.
    }

    private static void HandleUndoRedo() // Undo/Redo로 제어점 데이터가 바뀐 뒤 도로 화면을 맞춥니다.
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) // 플레이 전환 중에는 씬 프리뷰를 수정하지 않습니다.
            return;

        RequestAllOpenTracks(); // 현재 열려 있는 모든 트랙을 가볍게 다시 표시합니다.
    }


    private static void RequestAllOpenTracks() // 현재 열린 씬의 모든 MushTrackAuthoring에 도로 갱신을 요청합니다.
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) // 플레이 중이나 전환 중에는 에디터 도로를 수정하지 않습니다.
            return;

        MushTrackAuthoring[] tracks = Object.FindObjectsByType<MushTrackAuthoring>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None); // Unity 6의 현재 탐색 API로 비활성 오브젝트를 포함한 트랙 컴포넌트를 찾습니다.

        for (int index = 0; index < tracks.Length; index++) // 열린 씬의 모든 트랙을 순회합니다.
            RequestRebuild(tracks[index]); // 각 트랙을 중복 제거된 대기열에 넣습니다.
    }

    private static void SchedulePendingRebuild() // 실제 도로 갱신을 한 번만 delayCall에 예약합니다.
    {
        if (rebuildScheduled) // 이미 예약되어 있으면 같은 프레임의 추가 요청은 기존 예약이 처리합니다.
            return;

        rebuildScheduled = true; // 예약 상태를 켭니다.
        EditorApplication.delayCall += RebuildPending; // 에디터의 현재 GUI 작업이 끝난 뒤 도로를 갱신합니다.
    }

    private static void RebuildPending() // 대기 중인 트랙의 도로 메시만 실제로 다시 계산합니다.
    {
        rebuildScheduled = false; // 이번 예약이 실행되었으므로 다음 요청을 받을 수 있게 상태를 풉니다.
        if (EditorApplication.isPlayingOrWillChangePlaymode || rebuilding) // 플레이 전환 중이거나 이미 갱신 중이면 겹쳐 실행하지 않습니다.
            return;

        if (GUIUtility.hotControl != 0) // 사용자가 이동 핸들을 아직 잡고 드래그 중이면 메시를 매 마우스 이동마다 만들지 않습니다.
        {
            SchedulePendingRebuild(); // 핸들을 놓은 뒤 다시 확인하도록 한 번 더 예약합니다.
            return;
        }

        MushTrackAuthoring[] tracks = new MushTrackAuthoring[PendingTracks.Count]; // 현재 대기 중인 트랙 수만큼 고정 배열을 만듭니다.
        PendingTracks.CopyTo(tracks); // HashSet 내용을 복사해 순회 중 새 요청이 와도 안전하게 처리합니다.
        PendingTracks.Clear(); // 이번에 처리할 요청은 대기열에서 비웁니다.

        rebuilding = true; // 재귀적인 도로 갱신 호출을 막습니다.
        try // 모든 트랙을 처리한 뒤 반드시 rebuilding 상태를 되돌립니다.
        {
            for (int index = 0; index < tracks.Length; index++) // 요청된 각 트랙을 한 번씩 처리합니다.
            {
                MushTrackAuthoring authoring = tracks[index]; // 현재 처리할 트랙 컴포넌트를 가져옵니다.
                if (authoring == null) // 사용자가 그 사이 오브젝트를 삭제했다면 건너뜁니다.
                    continue;

                Transform mapRoot = authoring.ResolveMapRoot(); // 이 트랙이 실제로 적용되는 맵 루트를 찾습니다.
                if (mapRoot == null) // 유효한 맵 루트가 없으면 메시를 갱신할 수 없습니다.
                    continue;

                MushCurvedMapRuntime runtime = mapRoot.GetComponent<MushCurvedMapRuntime>(); // 기존 맵 런타임 컴포넌트를 가져옵니다.
                if (runtime == null) // 정상 기존 맵에는 있어야 하지만 빠진 경우에만 Unity Undo를 지원하며 추가합니다.
                    runtime = Undo.AddComponent<MushCurvedMapRuntime>(mapRoot.gameObject);

                runtime.RebuildSceneCourseGeometry(); // 실제 편집이 발생한 뒤에만 도로/자동 지형 미리보기를 갱신합니다.
                MarkCourseMeshesDirty(mapRoot); // 바뀐 작은 도로/지형 Mesh는 다음 일반 씬 저장 때만 함께 저장합니다.
                // 여기서는 SetDirty/MarkSceneDirty를 호출하지 않습니다.
                // 포인트/Inspector 값을 실제로 바꾼 코드가 authoring을 이미 Dirty 처리하므로,
                // 단순 미리보기 갱신이 별도의 저장 요구를 만드는 일을 막습니다.
            }
        }
        finally // 중간에 예외가 나더라도 다음 편집을 막지 않도록 상태를 항상 복원합니다.
        {
            rebuilding = false; // 다음 도로 갱신 요청을 받을 수 있게 합니다.
        }

        SceneView.RepaintAll(); // 새 도로 메시와 포인트 위치를 모든 씬 뷰에 즉시 다시 그립니다.
    }

    public static void EnsureEditableMapReady(MushTrackAuthoring authoring, bool saveScene) // 새 맵 생성 메뉴가 처음 한 번 기본 월드를 만들 때 사용하는 호환 함수입니다.
    {
        if (authoring == null || EditorApplication.isPlayingOrWillChangePlaymode || rebuilding) // 유효하지 않은 상황에서는 새 월드를 만들지 않습니다.
            return;

        Transform mapRoot = authoring.ResolveMapRoot(); // 생성 대상 맵 루트를 찾습니다.
        if (mapRoot == null || mapRoot.gameObject.scene != authoring.gameObject.scene) // 다른 씬의 루트를 잘못 건드리지 않도록 검사합니다.
            return;

        MushCurvedMapRuntime runtime = mapRoot.GetComponent<MushCurvedMapRuntime>(); // 맵 생성 런타임 컴포넌트를 가져옵니다.
        if (runtime == null) // 새 오브젝트라 아직 없다면 Undo 가능한 방식으로 추가합니다.
            runtime = Undo.AddComponent<MushCurvedMapRuntime>(mapRoot.gameObject);

        if (mapRoot.Find(MushCurvedMapRuntime.GeneratedWorldRootName) == null) // 기존 지형/월드가 없는 완전히 새 맵에서만 전체 월드를 한 번 생성합니다.
            runtime.RebuildSceneWorld(); // 이미 지형이 있는 기존 맵에서는 이 경로가 절대 실행되지 않습니다.
        else // 기존 맵이라면 지형을 유지하고 도로만 현재 제어점에 맞춥니다.
            runtime.RebuildSceneCourseGeometry();

        EditorUtility.SetDirty(authoring); // 새 맵 제어점 데이터를 저장 대상으로 표시합니다.
        EditorUtility.SetDirty(runtime); // 새 런타임 상태를 저장 대상으로 표시합니다.
        EditorSceneManager.MarkSceneDirty(authoring.gameObject.scene); // 새로 만든 오브젝트가 씬에 저장되게 변경 상태로 표시합니다.

        if (saveScene && !string.IsNullOrEmpty(authoring.gameObject.scene.path)) // 호출자가 명시적으로 저장을 요청한 경우에만 씬 파일을 저장합니다.
            EditorSceneManager.SaveScene(authoring.gameObject.scene); // 일반 씬 저장만 수행하며 GeneratedMaps 강제 재임포트는 하지 않습니다.

        SceneView.RepaintAll(); // 새 맵 또는 새 도로를 씬 뷰에 즉시 표시합니다.
    }

    public static void RebuildFullWorld(MushTrackAuthoring authoring) // 기존 외부 호출 호환을 위해 남기되 기존 지형이 있으면 도로만 갱신합니다.
    {
        EnsureEditableMapReady(authoring, false); // 기존 씬의 지형을 삭제하거나 재생성하지 않는 안전한 경로만 사용합니다.
    }

    public static void BakeAllMapsFromCommandLine() // 기존 자동화 진입점과의 이름 호환을 유지합니다.
    {
        RequestAllOpenTracks(); // 열린 맵의 도로만 가볍게 갱신하며 지형 베이크는 수행하지 않습니다.
    }
}

/// <summary>
/// 새 씬에 Mush 맵 편집 오브젝트를 만드는 메뉴입니다.
/// 만들어진 뒤에는 도로 포인트만 직접 편집하며 지형은 모델 지정 또는 자동 생성 방식만 사용합니다.
/// </summary>
public static class MushEditableMapCreationMenu
{
    [MenuItem("Mush/Maps/Create Track Editor In Current Scene", false, 1)] // Mush 메뉴에서 도로 전용 편집기를 만들 수 있게 합니다.
    [MenuItem("GameObject/Mush/Track Editor", false, 10)] // Hierarchy의 GameObject 메뉴에서도 같은 도로 전용 편집기를 만들 수 있게 합니다.
    private static void CreateEditableMap(MenuCommand command) // 현재 씬에 새 Mush 맵 루트와 필요한 컴포넌트를 생성합니다.
    {
        GameObject mapObject = new("Mush Map Editor"); // 새 맵 루트 오브젝트를 만듭니다.
        Undo.RegisterCreatedObjectUndo(mapObject, "Create Editable Mush Map"); // 새 맵 생성 자체를 Ctrl+Z로 되돌릴 수 있게 합니다.

        Undo.AddComponent<MushCurvedMapRuntime>(mapObject); // 도로 메시와 플레이용 경로 정보를 담당하는 런타임 컴포넌트를 추가합니다.
        Undo.AddComponent<MushMapRideBootstrap>(mapObject); // 기존 개썰매 플레이 세팅과의 호환을 위해 부트스트랩을 추가합니다.
        MushTrackAuthoring authoring = Undo.AddComponent<MushTrackAuthoring>(mapObject); // 실제 도로 제어점을 저장할 편집 컴포넌트를 추가합니다.
        authoring.ConfigureNewMapDefaults(); // 새 맵의 기본 직선 도로 데이터를 준비합니다.
        EditorUtility.SetDirty(authoring); // 새 기본 데이터가 씬 저장 대상임을 표시합니다.
        Selection.activeGameObject = mapObject; // 생성 직후 새 맵 오브젝트를 선택해 Inspector가 바로 열리게 합니다.

        MushTrackEditorWorldPreview.EnsureEditableMapReady(authoring, false); // 새 씬이라 지형이 없을 때만 최초 기본 월드를 한 번 만듭니다.
        EditorGUIUtility.PingObject(mapObject); // Project/Hierarchy에서 생성된 오브젝트 위치를 눈에 띄게 표시합니다.
    }
}
