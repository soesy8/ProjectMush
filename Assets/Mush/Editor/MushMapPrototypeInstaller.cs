#if UNITY_EDITOR // 프로토타입 Scene 자동 구성은 Editor에서만 실행한다.

using System; // 이름 비교에 사용한다.
using System.Collections.Generic; // Renderer 목록을 모으기 위해 사용한다.
using UnityEditor; // MenuItem, Undo, Selection 등 Editor API를 사용한다.
using UnityEngine; // GameObject, ParticleSystem, Light 등 Scene 구성 요소를 사용한다;

/// <summary>
/// 선택한 Mush_Map_Snowfield_V2 또는 Mush_Map_Forest_V2 인스턴스에
/// 필요한 Controller/Particle/Light/Collider를 빠르게 설치한다.
/// 기존 썰매 조작 코드와 로비 코드는 수정하지 않는다.
/// </summary>
public static class MushMapPrototypeInstaller // V2 맵 시험 Scene을 빠르게 만드는 Editor 도구다.
{
    [MenuItem("Mush/Maps/V2/선택한 맵 프로토타입 구성")] // Hierarchy에서 맵 루트를 선택하고 실행하는 메뉴다.
    private static void InstallSelectedMap() // 선택된 맵 이름을 보고 설원/숲 설정을 분기한다.
    {
        GameObject root = Selection.activeGameObject; // 현재 Hierarchy 선택을 가져온다.
        if (root == null) // 맵이 선택되지 않았다면 잘못된 곳에 컴포넌트를 붙이지 않는다.
        {
            EditorUtility.DisplayDialog("Mush Map V2", "Hierarchy에서 Mush_Map_*_V2 맵 루트를 선택해줘.", "확인"); // 필요한 행동만 안내한다.
            return; // 설치를 중단한다.
        }

        bool isSnowfield = root.name.Contains("Snowfield", StringComparison.OrdinalIgnoreCase); // 설원 맵인지 이름으로 판별한다.
        bool isForest = root.name.Contains("Forest", StringComparison.OrdinalIgnoreCase); // 숲 맵인지 이름으로 판별한다.

        if (!isSnowfield && !isForest) // V2 맵이 아닌 다른 오브젝트를 선택한 경우 보호한다.
        {
            EditorUtility.DisplayDialog("Mush Map V2", "선택한 오브젝트가 Snowfield 또는 Forest V2 맵으로 보이지 않아.", "확인"); // 오작동 대신 중단한다.
            return; // 설치하지 않는다.
        }

        MushMapMaterialInstaller.InstallAllV2Materials(); // 먼저 모든 V2 FBX 재질을 URP/Lit 외부 Material에 영구 연결한다.
        AddCollisionComponents(root); // _COL Mesh에 MeshCollider를 자동 부착하고 Renderer는 꺼둔다.

        if (isSnowfield) InstallSnowfield(root); // 설원이면 눈보라 시험 환경을 만든다.
        if (isForest) InstallForest(root); // 숲이면 시간 변화 시험 환경을 만든다.

        EditorUtility.SetDirty(root); // Scene 변경을 저장 대상으로 표시한다.
        Debug.Log($"[Mush Map V2] {root.name} 프로토타입 구성 완료. 기존 썰매/로비 코드는 변경하지 않았습니다.", root); // 작업 결과를 Console에 남긴다.
    }

    private static void AddCollisionComponents(GameObject root) // Blender에서 제공한 _COL 단순 메시를 Unity 물리 충돌로 연결한다.
    {
        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true); // 맵 전체 MeshFilter를 가져온다.

        foreach (MeshFilter filter in filters) // 각 Mesh 이름을 검사한다.
        {
            if (!filter.name.EndsWith("_COL", StringComparison.OrdinalIgnoreCase)) continue; // _COL 규칙을 가진 오브젝트만 Collision으로 처리한다.

            MeshCollider collider = filter.GetComponent<MeshCollider>(); // 기존 MeshCollider가 있는지 확인한다.
            if (collider == null) collider = Undo.AddComponent<MeshCollider>(filter.gameObject); // 없다면 Undo 가능한 방식으로 추가한다.
            collider.sharedMesh = filter.sharedMesh; // Blender에서 만든 단순 충돌 Mesh를 그대로 연결한다.

            MeshRenderer renderer = filter.GetComponent<MeshRenderer>(); // Collider 오브젝트에 Renderer가 함께 임포트됐는지 확인한다.
            if (renderer != null) renderer.enabled = false; // 충돌용 면은 화면에는 보이지 않게 한다.
        }
    }

    private static void InstallSnowfield(GameObject root) // 설원 눈보라 Controller와 Quest 2용 Particle System을 설치한다.
    {
        MushSnowfieldBlizzardController controller = root.GetComponent<MushSnowfieldBlizzardController>(); // 기존 Controller를 먼저 찾는다.
        if (controller == null) controller = Undo.AddComponent<MushSnowfieldBlizzardController>(root); // 없다면 하나만 추가한다.

        Transform fxRoot = FindOrCreateChild(root.transform, "FX_Blizzard_V2"); // 눈보라 FX를 한 자식 아래 정리한다.
        ParticleSystem particles = fxRoot.GetComponent<ParticleSystem>(); // 기존 Particle System을 재사용할 수 있는지 확인한다.

        if (particles == null) // 처음 설치라면 기본 눈 파티클을 만든다.
        {
            particles = Undo.AddComponent<ParticleSystem>(fxRoot.gameObject); // Unity 기본 Particle System을 추가한다.
            ParticleSystem.MainModule main = particles.main; // 메인 모듈 값을 설정한다.
            main.loop = true; // 눈보라는 지속적으로 뿌려야 하므로 루프한다.
            main.startLifetime = 3.6f; // 멀리 날아간 입자가 오래 쌓이지 않도록 수명을 제한한다.
            main.startSpeed = 10f; // 강한 눈발이 시야를 가로질러 움직이는 기본 속도다.
            main.startSize = 0.06f; // VR에서 지나치게 큰 눈송이가 눈앞을 막지 않게 작게 둔다.
            main.maxParticles = 420; // Quest 2 시험용 상한이다.
            main.simulationSpace = ParticleSystemSimulationSpace.World; // 썰매가 움직여도 입자가 카메라에 고정되지 않게 한다.

            ParticleSystem.EmissionModule emission = particles.emission; // 방출 모듈을 설정한다.
            emission.rateOverTime = 8f; // 실제 강도는 Controller가 런타임에 8~110으로 바꾼다.

            ParticleSystem.ShapeModule shape = particles.shape; // 플레이어 주변에 넓게 눈을 뿌리는 Shape를 만든다.
            shape.shapeType = ParticleSystemShapeType.Box; // 넓은 박스 영역에서 생성한다.
            shape.scale = new Vector3(26f, 12f, 24f); // 도로와 양옆 시야를 덮되 과도하게 넓히지 않는다.
        }

        List<Renderer> beacons = new(); // 이름/재질로 경광봉 Renderer를 모은다.
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true)) // 맵 전체 Renderer를 검사한다.
        {
            foreach (Material material in renderer.sharedMaterials) // Renderer가 가진 모든 Material을 확인한다.
            {
                if (material != null && material.name.StartsWith("MUSH_MAT_BeaconGlow", StringComparison.Ordinal)) // V2 주황 발광 재질이 있으면 경광봉으로 취급한다.
                {
                    beacons.Add(renderer); // Controller에 연결할 목록에 추가한다.
                    break; // 같은 Renderer를 중복 추가하지 않는다.
                }
            }
        }

        SerializedObject serialized = new SerializedObject(controller); // private SerializeField를 안전하게 Editor에서 연결한다.
        serialized.FindProperty("snowParticles").objectReferenceValue = particles; // 자동 생성한 눈 파티클을 연결한다.
        SerializedProperty array = serialized.FindProperty("beaconRenderers"); // 경광봉 Renderer 배열 프로퍼티를 가져온다.
        array.arraySize = beacons.Count; // 찾은 Renderer 수만큼 배열 크기를 맞춘다.
        for (int i = 0; i < beacons.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = beacons[i]; // 각 경광봉을 배열에 연결한다.
        serialized.ApplyModifiedProperties(); // Inspector 직렬화 값에 실제로 반영한다.
    }

    private static void InstallForest(GameObject root) // 숲 시간 Controller와 Directional Light/별돔을 연결한다.
    {
        MushForestTimeCycleController controller = root.GetComponent<MushForestTimeCycleController>(); // 기존 Controller를 찾는다.
        if (controller == null) controller = Undo.AddComponent<MushForestTimeCycleController>(root); // 없다면 하나만 추가한다.

        Light sun = null; // Scene에서 사용할 Directional Light를 찾을 변수다.
        foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) // 현재 Scene의 Light를 모두 확인한다.
        {
            if (light.type == LightType.Directional) { sun = light; break; } // 첫 Directional Light를 태양으로 사용한다.
        }

        if (sun == null) // Directional Light가 아예 없다면 프로토타입용으로 하나 생성한다.
        {
            GameObject sunObject = new GameObject("Mush_Map_Sun_V2"); // 새 태양 GameObject를 만든다.
            Undo.RegisterCreatedObjectUndo(sunObject, "Create Mush Map Sun"); // 생성 자체도 Undo할 수 있게 기록한다.
            sun = sunObject.AddComponent<Light>(); // Light 컴포넌트를 추가한다.
            sun.type = LightType.Directional; // 태양 역할이므로 Directional로 설정한다.
            sun.intensity = 1f; // 시작은 낮 기준 밝기로 둔다.
        }

        Renderer starDome = null; // 단일 결합 별돔 Renderer를 찾는다.
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true)) // 맵 자식을 검색한다.
        {
            if (renderer.name.Contains("StarDome", StringComparison.OrdinalIgnoreCase)) { starDome = renderer; break; } // FX_StarDome 하나만 찾는다.
        }

        SerializedObject serialized = new SerializedObject(controller); // private SerializeField를 Editor에서 자동 연결한다.
        serialized.FindProperty("directionalLight").objectReferenceValue = sun; // 태양 Light를 연결한다.
        serialized.FindProperty("skyCamera").objectReferenceValue = Camera.main; // 현재 Main Camera를 하늘색 fallback으로 연결한다.
        serialized.FindProperty("starDomeRenderer").objectReferenceValue = starDome; // 단일 별돔 Renderer를 연결한다.
        serialized.ApplyModifiedProperties(); // 연결값을 실제 컴포넌트에 저장한다.
    }

    private static Transform FindOrCreateChild(Transform parent, string name) // 동일 이름의 FX 자식이 있으면 재사용하고 없으면 만든다.
    {
        Transform existing = parent.Find(name); // 직접 자식에서 기존 FX 루트를 찾는다.
        if (existing != null) return existing; // 이미 있으면 중복 생성하지 않는다.

        GameObject child = new GameObject(name); // 새 FX 루트를 만든다.
        Undo.RegisterCreatedObjectUndo(child, "Create Mush Map FX Root"); // Undo 지원을 추가한다.
        child.transform.SetParent(parent, false); // 맵 루트의 자식으로 넣고 로컬 Transform을 0/1로 초기화한다.
        return child.transform; // 생성된 Transform을 반환한다.
    }
}

#endif // UNITY_EDITOR
