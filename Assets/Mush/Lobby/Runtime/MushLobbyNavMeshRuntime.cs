using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Mush.Lobby
{
    /// <summary>
    /// Builds a small dedicated NavMesh for the lobby dogs at runtime.
    /// The source is a single invisible floor box, so furniture does not accidentally become walkable geometry.
    /// Furniture is carved separately by MushLobbyFurnitureObstacle/NavMeshObstacle.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class MushLobbyNavMeshRuntime : MonoBehaviour
    {
        [SerializeField] private Vector3 floorCenter = new Vector3(0f, -0.06f, -1.75f); // 산장 내부 바닥 중심이다. 위쪽 표면이 Y=0이 되도록 절반 두께만 아래에 둔다.
        [SerializeField] private Vector3 floorSize = new Vector3(8.10f, 0.12f, 8.35f); // 벽 안쪽에서 충분히 떨어진 개 전용 보행 면적이다.
        [SerializeField] private float agentRadius = 0.28f; // 허스키/말라뮤트 몸통이 가구 모서리에 너무 붙지 않도록 사용할 내비메시 반지름이다.
        [SerializeField] private float agentHeight = 0.90f; // 개가 통과할 수 있는 천장 높이를 계산할 때 쓰는 값이다.
        [SerializeField] private float agentClimb = 0.16f; // 작은 바닥 높이 차이는 허용하되 가구를 계단처럼 타고 오르지 못하게 제한한다.
        [SerializeField] private float voxelSize = 0.07f; // 좁은 실내 가구 회피가 너무 거칠지 않도록 비교적 세밀하게 굽는다.

        private NavMeshData navMeshData; // 이 로비 전용으로 런타임에 만들어지는 실제 내비메시 데이터다.
        private NavMeshDataInstance navMeshInstance; // NavMesh 시스템에 등록된 인스턴스를 저장해 씬을 나갈 때 정확히 제거한다.

        public static bool IsReady { get; private set; } // 개 스크립트가 내비메시가 준비된 뒤 Agent를 올릴 수 있게 현재 상태를 공개한다.

        public void Configure(Vector3 newFloorCenter, Vector3 newFloorSize)
        {
            floorCenter = newFloorCenter; // SceneBuilder에서 산장 치수와 같은 기준으로 바닥 중심을 주입한다.
            floorSize = newFloorSize; // 방 크기가 바뀌더라도 한 곳의 값만 수정하면 내비메시 범위도 함께 바뀌게 한다.
        }

        private void Awake()
        {
            BuildNavMesh(); // 다른 일반 MonoBehaviour보다 먼저 실행되어 개들의 Awake/Start 전에 가능한 한 내비메시를 준비한다.
        }

        private void OnDestroy()
        {
            if (navMeshInstance.valid)
                navMeshInstance.Remove(); // 로비를 떠날 때 이 씬이 만든 내비메시만 제거해 다른 씬의 내비메시에는 손대지 않는다.

            if (navMeshData != null)
                Destroy(navMeshData); // 런타임 생성 데이터도 함께 정리해 씬 재진입 때 누적되지 않게 한다.

            IsReady = false; // 다음 로비 진입 시 새 내비메시가 준비되기 전까지 Agent가 성급하게 붙지 않게 한다.
        }

        private void BuildNavMesh()
        {
            IsReady = false; // 빌드가 끝나기 전에는 아직 사용할 수 없음을 먼저 표시한다.

            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0); // 프로젝트에 항상 존재하는 기본 Agent Type 설정을 가져온다.
            settings.agentRadius = agentRadius; // 이번 로비 빌드에만 개 크기에 맞는 반지름을 적용한다.
            settings.agentHeight = agentHeight; // 사람 기준 기본 높이 대신 개 높이에 맞춰 실내 경로를 굽는다.
            settings.agentClimb = agentClimb; // 낮은 소품을 계단처럼 넘어가는 것을 줄인다.
            settings.overrideVoxelSize = true; // 아래의 세밀한 voxelSize를 실제 빌드에 사용하도록 명시한다.
            settings.voxelSize = voxelSize; // 개 지름에 비해 충분히 세밀한 내비메시 해상도를 사용한다.
            settings.overrideTileSize = true; // 작은 로비에서 불필요하게 큰 기본 타일을 쓰지 않도록 직접 지정한다.
            settings.tileSize = 128; // 실내 한 장 내비메시에 무리가 없는 작은 타일 크기다.

            Vector3 worldCenter = transform.TransformPoint(floorCenter); // 설정된 로컬 바닥 중심을 실제 월드 좌표로 변환한다.
            Matrix4x4 sourceTransform = Matrix4x4.TRS(worldCenter, transform.rotation, Vector3.one); // NavMeshBuildSource가 사용할 월드 변환 행렬이다.

            List<NavMeshBuildSource> sources = new(1) // 가구를 빌드 원본에 넣지 않고 오직 바닥 한 장만 보행 가능한 원본으로 사용한다.
            {
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box, // 읽기 가능한 Mesh 에셋에 의존하지 않는 단순 박스 소스를 사용한다.
                    transform = sourceTransform, // 위에서 계산한 산장 바닥의 월드 위치/회전을 적용한다.
                    size = floorSize, // 박스의 실제 가로/두께/깊이다.
                    area = 0, // 기본 Walkable 영역으로 굽는다.
                    generateLinks = false, // 로비에는 점프 링크가 필요하지 않으므로 자동 링크를 만들지 않는다.
                }
            };

            navMeshData = new NavMeshData(settings.agentTypeID) // 같은 Agent Type ID를 가진 개 NavMeshAgent가 사용할 빈 데이터 객체를 만든다.
            {
                name = "Mush Lobby Dog NavMesh"
            };

            Bounds buildBounds = new(
                worldCenter + Vector3.up * 1.0f, // 바닥과 개가 움직일 높이를 충분히 포함하도록 빌드 볼륨 중심을 약간 위로 둔다.
                new Vector3(floorSize.x + 1f, 2.5f, floorSize.z + 1f)); // 바닥 가장자리까지 잘리지 않도록 약간 넉넉한 빌드 범위를 사용한다.

            bool built = NavMeshBuilder.UpdateNavMeshData(navMeshData, settings, sources, buildBounds); // Unity 6 런타임 NavMeshBuilder로 실제 내비메시 폴리곤을 생성한다.
            if (!built)
            {
                Debug.LogWarning("[Mush] Lobby dog NavMesh build failed. Dogs will use their fallback movement until a NavMesh is available.", this); // 실패해도 로비 전체가 멈추지 않게 경고만 남긴다.
                return;
            }

            navMeshInstance = NavMesh.AddNavMeshData(navMeshData); // 완성된 데이터를 현재 씬의 내비메시 시스템에 등록한다.
            IsReady = navMeshInstance.valid; // 등록까지 성공했을 때만 개 Agent가 사용할 수 있다고 표시한다.
        }
    }
}
