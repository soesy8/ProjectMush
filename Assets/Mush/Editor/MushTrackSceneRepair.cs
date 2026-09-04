using System; // Mesh 이름 접두사 비교에 StringComparison을 사용합니다.
using UnityEditor; // AssetDatabase, EditorUtility, InitializeOnLoad 같은 Unity 6 에디터 API를 사용합니다.
using UnityEditor.SceneManagement; // 씬 열림 이벤트와 실제 복구 변경의 Dirty 표시를 처리합니다.
using UnityEngine; // GameObject, Transform, Mesh, Renderer 같은 Unity 기본 타입을 사용합니다.
using UnityEngine.SceneManagement; // 현재 열린 Scene과 씬 루트 오브젝트를 다룹니다.

/// <summary>
/// 이전 경량화 패치에서 DontSave Mesh가 씬의 MeshFilter에 연결된 채 저장되면서
/// 도로/지형 Mesh 참조가 사라진 경우에만 원래의 작은 GeneratedMaps Mesh를 다시 연결합니다.
/// 정상 씬에는 아무 변경도 하지 않으므로 씬을 단순히 열어 본 것만으로 Dirty가 생기지 않습니다.
/// </summary>
[InitializeOnLoad]
public static class MushTrackSceneRepair
{
    private const string TerrainMeshPrefix = "000_Non-Folding Winter Terrain"; // 정상 자동 지형 Mesh 이름입니다.
    private const string RoadMeshPrefix = "001_Curved Ribbon"; // 정상 기본 도로 Mesh 이름 접두사입니다.
    private const string LeftTrackMeshPrefix = "002_Curved Ribbon"; // 정상 왼쪽 썰매 자국 Mesh 이름 접두사입니다.
    private const string RightTrackMeshPrefix = "003_Curved Ribbon"; // 정상 오른쪽 썰매 자국 Mesh 이름 접두사입니다.

    static MushTrackSceneRepair() // 스크립트 컴파일 직후와 이후 씬을 열 때 손상 여부만 검사합니다.
    {
        EditorApplication.delayCall += RepairAllLoadedScenesIfNeeded; // 현재 이미 열려 있는 씬도 도메인 리로드 뒤 한 번 검사합니다.
        EditorSceneManager.sceneOpened += HandleSceneOpened; // 이후 다른 맵 씬을 열었을 때도 같은 검사를 수행합니다.
    }

    private static void HandleSceneOpened(Scene scene, OpenSceneMode mode) // 씬 파일 로딩이 끝난 다음 복구 검사를 예약합니다.
    {
        EditorApplication.delayCall += () => RepairSceneIfNeeded(scene); // 로딩 중 오브젝트 직렬화와 겹치지 않게 한 틱 뒤 실행합니다.
    }

    private static void RepairAllLoadedScenesIfNeeded() // 현재 에디터에 열려 있는 모든 씬을 순회합니다.
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) // 플레이 전환 중에는 씬 데이터를 수정하지 않습니다.
            return;

        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++) // Additive로 여러 씬이 열려 있어도 각각 검사합니다.
            RepairSceneIfNeeded(SceneManager.GetSceneAt(sceneIndex)); // 실제 손상이 있는 씬만 복구합니다.
    }

    private static void RepairSceneIfNeeded(Scene scene) // 한 씬의 도로/지형 Mesh 참조가 손상됐는지 검사하고 필요한 부분만 고칩니다.
    {
        if (!scene.IsValid() || !scene.isLoaded || EditorApplication.isPlayingOrWillChangePlaymode) // 닫혔거나 플레이 중인 씬은 건드리지 않습니다.
            return;

        string generatedAssetPath = $"Assets/Mush/GeneratedMaps/{scene.name}_BakedMapAssets.asset"; // 현재 씬 이름에 대응하는 작은 생성 Mesh 컨테이너 경로입니다.
        UnityEngine.Object[] generatedAssets = AssetDatabase.LoadAllAssetsAtPath(generatedAssetPath); // 해당 컨테이너의 Mesh sub-asset들을 읽습니다.
        if (generatedAssets == null || generatedAssets.Length == 0) // 이 맵에 기존 생성 에셋이 없으면 자동 복구 대상이 아닙니다.
            return;

        Mesh terrainMesh = FindMeshByPrefix(generatedAssets, TerrainMeshPrefix); // 원래 자동 지형 Mesh를 찾습니다.
        Mesh roadMesh = FindMeshByPrefix(generatedAssets, RoadMeshPrefix); // 원래 기본 도로 Mesh를 찾습니다.
        Mesh leftTrackMesh = FindMeshByPrefix(generatedAssets, LeftTrackMeshPrefix); // 왼쪽 썰매 자국 Mesh를 찾습니다.
        Mesh rightTrackMesh = FindMeshByPrefix(generatedAssets, RightTrackMeshPrefix); // 오른쪽 썰매 자국 Mesh를 찾습니다.
        if (terrainMesh == null || roadMesh == null || leftTrackMesh == null || rightTrackMesh == null) // 정상 4개 Mesh가 모두 있어야 안전하게 복구합니다.
            return;

        bool changed = false; // 실제 씬 수정이 있었는지 기록해 불필요한 저장 표시를 막습니다.
        GameObject[] roots = scene.GetRootGameObjects(); // 씬의 모든 최상위 오브젝트를 가져옵니다.
        for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++) // 각 루트 아래 Mush 맵을 찾습니다.
        {
            MushTrackAuthoring[] authorings = roots[rootIndex].GetComponentsInChildren<MushTrackAuthoring>(true); // 비활성 맵도 복구할 수 있게 포함합니다.
            for (int authoringIndex = 0; authoringIndex < authorings.Length; authoringIndex++) // 한 씬에 여러 맵 루트가 있어도 각각 확인합니다.
            {
                MushTrackAuthoring authoring = authorings[authoringIndex]; // 현재 트랙 데이터를 가져옵니다.
                Transform mapRoot = authoring != null ? authoring.ResolveMapRoot() : null; // 생성 월드가 속한 실제 맵 루트를 찾습니다.
                Transform generatedRoot = mapRoot != null ? mapRoot.Find(MushCurvedMapRuntime.GeneratedWorldRootName) : null; // 기존 배경/나무를 보존한 생성 월드 루트를 찾습니다.
                if (generatedRoot == null) // 생성 월드가 통째로 없는 씬은 이 작은 참조 복구기로 임의 재생성하지 않습니다.
                    continue;

                bool mapChanged = false; // 이 맵에서 실제로 복구한 것이 있는지 따로 추적합니다.
                mapChanged |= RestoreSurface(generatedRoot, "VISIBLE Snow Terrain", terrainMesh, true); // 지형 MeshFilter와 MeshCollider 참조를 복원합니다.
                mapChanged |= RestoreSurface(generatedRoot, "VISIBLE Curved Packed-Snow Road", roadMesh, true); // 기본 도로 Mesh와 충돌체를 복원합니다.
                mapChanged |= RestoreSurface(generatedRoot, "Left Sled Track", leftTrackMesh, false); // 왼쪽 썰매 자국 Mesh를 복원합니다.
                mapChanged |= RestoreSurface(generatedRoot, "Right Sled Track", rightTrackMesh, false); // 오른쪽 썰매 자국 Mesh를 복원합니다.

                if (mapChanged) // 이 맵에서 실제 Mesh 참조가 복구된 경우에만 표시 상태와 모델 프리뷰를 현재 설정에 맞춥니다.
                {
                    changed = true; // 씬 전체에도 실제 복구가 있었다고 기록합니다.
                    MushCurvedMapRuntime runtime = mapRoot.GetComponent<MushCurvedMapRuntime>(); // 현재 맵 런타임을 가져옵니다.
                    if (runtime != null) // 정상 기존 맵이면 저장 Mesh를 다시 생성하지 않고 모델 표시만 복구합니다.
                        runtime.RefreshEditorPresentationOnly(); // 도로 모델/지형 모델이 지정된 경우의 표시 상태를 현재 설정과 맞춥니다.
                }
            }
        }

        if (!changed) // 정상 씬이면 여기서 끝나므로 단순히 씬을 열어 본 것만으로 Dirty가 되지 않습니다.
            return;

        EditorSceneManager.MarkSceneDirty(scene); // 실제로 사라진 Mesh 참조를 복구했으므로 이번 한 번만 정상적인 저장 대상임을 표시합니다.
        Debug.Log($"[Mush] '{scene.name}'의 사라진 도로/지형 Mesh 참조를 원래 GeneratedMaps Mesh로 복구했습니다. 씬을 한 번 저장하면 이후 자동 복구는 다시 실행되지 않습니다."); // 복구가 실제 발생했을 때만 한 줄 알립니다.
        SceneView.RepaintAll(); // 복구된 도로와 지형이 씬 뷰에 즉시 보이게 갱신합니다.
    }

    private static Mesh FindMeshByPrefix(UnityEngine.Object[] assets, string prefix) // 생성 컨테이너에서 용도별 Mesh를 이름으로 찾습니다.
    {
        for (int index = 0; index < assets.Length; index++) // 컨테이너의 모든 sub-asset을 순회합니다.
        {
            Mesh mesh = assets[index] as Mesh; // Mesh가 아닌 Material/Container는 건너뛰기 위해 형변환합니다.
            if (mesh != null && mesh.name.StartsWith(prefix, StringComparison.Ordinal)) // 폭 숫자가 달라도 001/002/003 용도 접두사로 정확히 구분합니다.
                return mesh; // 첫 정상 Mesh를 즉시 반환합니다.
        }

        return null; // 필요한 Mesh가 없으면 안전하게 복구를 포기합니다.
    }

    private static bool RestoreSurface(Transform generatedRoot, string objectName, Mesh correctMesh, bool restoreCollider) // 한 도로/지형 오브젝트의 손상된 Mesh 참조만 복구합니다.
    {
        Transform target = generatedRoot.Find(objectName); // 기존 씬 계층의 원래 오브젝트를 이름으로 찾습니다.
        if (target == null || correctMesh == null) // 원래 오브젝트 자체가 없으면 새 오브젝트를 억지로 만들지 않습니다.
            return false;

        bool changed = false; // 이 오브젝트에서 실제 수정이 있었는지 기록합니다.
        MeshFilter filter = target.GetComponent<MeshFilter>(); // 표시 Mesh 참조를 가진 MeshFilter를 가져옵니다.
        if (filter != null && filter.sharedMesh != correctMesh) // 이전 DontSave Mesh가 사라져 null이 됐거나 잘못된 Mesh면 원래 참조로 돌립니다.
        {
            filter.sharedMesh = correctMesh; // GeneratedMaps의 작은 정상 Mesh를 다시 연결합니다.
            changed = true; // 실제 씬 참조가 바뀌었음을 기록합니다.
        }

        if (restoreCollider) // 지형과 실제 주행 도로만 충돌체 Mesh도 함께 복구합니다.
        {
            MeshCollider collider = target.GetComponent<MeshCollider>(); // 기존 MeshCollider를 가져옵니다.
            if (collider != null && collider.sharedMesh != correctMesh) // 표시 Mesh와 다른 충돌 Mesh가 남아 있으면 바로잡습니다.
            {
                collider.sharedMesh = null; // Unity Physics가 바뀐 Mesh를 확실히 다시 cook하도록 먼저 비웁니다.
                collider.sharedMesh = correctMesh; // 표시 Mesh와 정확히 같은 정상 Mesh를 연결합니다.
                changed = true; // 실제 씬 참조가 바뀌었음을 기록합니다.
            }
        }

        Renderer renderer = target.GetComponent<Renderer>(); // 이전 패치가 Renderer를 꺼버린 경우도 확인합니다.
        if (renderer != null && !renderer.enabled) // 현재 복구 단계에서는 원래 기본 도로/지형을 우선 보이게 돌립니다.
        {
            renderer.enabled = true; // 모델 슬롯이 활성화된 경우에는 이어지는 Presentation 갱신이 필요한 것만 다시 숨깁니다.
            changed = true; // 표시 상태를 복구했음을 기록합니다.
        }

        return changed; // 실제 변경 여부를 호출자에게 반환합니다.
    }
}
