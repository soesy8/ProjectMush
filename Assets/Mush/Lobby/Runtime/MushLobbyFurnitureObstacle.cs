using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Mush.Lobby
{
    [DisallowMultipleComponent]
    public sealed class MushLobbyFurnitureObstacle : MonoBehaviour
    {
        private static readonly List<MushLobbyFurnitureObstacle> Active = new();

        private Vector2 center;
        private float radius = 0.5f;
        private bool hasBounds;
        private NavMeshObstacle navMeshObstacle; // 개의 실제 NavMesh 경로에서 이 가구 공간을 잘라내는 Unity 내비메시 장애물이다.

        private void OnEnable()
        {
            if (!Active.Contains(this))
                Active.Add(this);
            RefreshBounds();
        }

        private void OnDisable()
        {
            Active.Remove(this);
        }

        public void RefreshBounds()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            bool initialized = false;
            Bounds bounds = default;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                if (!initialized)
                {
                    bounds = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!initialized)
            {
                hasBounds = false; // 현재 장착 모델에 보이는 렌더러가 없으면 기존 원형 회피 정보도 무효화한다.
                if (navMeshObstacle == null)
                    navMeshObstacle = GetComponent<NavMeshObstacle>(); // 이전 장착 모델이 만들어 둔 장애물이 남아 있는지 찾는다.
                if (navMeshObstacle != null)
                    navMeshObstacle.enabled = false; // 모델이 사라진 슬롯의 옛 carving 구멍이 NavMesh에 남지 않게 장애물을 끈다.
                return;
            }

            center = new Vector2(bounds.center.x, bounds.center.z);
            radius = Mathf.Max(0.28f, Mathf.Max(bounds.extents.x, bounds.extents.z));
            hasBounds = true;
            RefreshNavMeshObstacle(bounds); // 기존 원형 회피 정보뿐 아니라 NavMesh에도 실제 가구 크기를 반영해 길 자체가 가구를 통과하지 않게 한다.
        }

        private void RefreshNavMeshObstacle(Bounds worldBounds)
        {
            if (navMeshObstacle == null)
                navMeshObstacle = GetComponent<NavMeshObstacle>(); // 이전 패치에서 이미 만들어진 장애물이 있으면 재사용한다.
            if (navMeshObstacle == null)
                navMeshObstacle = gameObject.AddComponent<NavMeshObstacle>(); // 없으면 같은 가구 루트에 Unity NavMeshObstacle을 추가한다.

            navMeshObstacle.shape = NavMeshObstacleShape.Box; // 의자/탁자/상점/상자처럼 대부분 직사각형인 가구를 보수적으로 감싸는 박스 장애물을 사용한다.
            navMeshObstacle.carving = true; // 단순 회피 힘만 주는 게 아니라 내비메시 경로에서 이 영역을 실제로 잘라낸다.
            navMeshObstacle.carveOnlyStationary = false; // 장착/교체 직후에도 새 가구 크기를 바로 carving에 반영해 잠깐 가구를 가로지르는 프레임이 생기지 않게 한다.

            Vector3 localCenter = transform.InverseTransformPoint(worldBounds.center); // 월드 Bounds 중심을 이 가구 루트의 로컬 좌표로 변환한다.
            Vector3 scale = transform.lossyScale; // 월드 크기를 NavMeshObstacle의 로컬 size로 되돌릴 때 현재 부모 스케일까지 고려한다.
            Vector3 localSize = new(
                worldBounds.size.x / Mathf.Max(0.0001f, Mathf.Abs(scale.x)), // 가로 크기를 로컬 단위로 환산한다.
                worldBounds.size.y / Mathf.Max(0.0001f, Mathf.Abs(scale.y)), // 높이 크기를 로컬 단위로 환산한다.
                worldBounds.size.z / Mathf.Max(0.0001f, Mathf.Abs(scale.z))); // 깊이 크기를 로컬 단위로 환산한다.

            navMeshObstacle.center = localCenter; // 실제 렌더러 중심과 장애물 중심을 일치시킨다.
            navMeshObstacle.size = new Vector3(
                Mathf.Max(0.30f, localSize.x + 0.12f), // 개가 가구에 털이 닿을 듯 바짝 붙지 않게 아주 작은 여유를 더한다.
                Mathf.Max(0.30f, localSize.y), // 낮은 가구도 정상적인 장애물로 남도록 최소 높이를 확보한다.
                Mathf.Max(0.30f, localSize.z + 0.12f)); // 깊이에도 같은 여유를 추가한다.
            navMeshObstacle.enabled = true; // RefreshBounds가 불린 시점의 활성 가구를 즉시 carving 대상으로 만든다.
        }

        public static bool IsBlocked(Vector3 worldPosition, float padding)
        {
            Vector2 point = new(worldPosition.x, worldPosition.z);
            for (int index = Active.Count - 1; index >= 0; index--)
            {
                MushLobbyFurnitureObstacle obstacle = Active[index];
                if (obstacle == null)
                {
                    Active.RemoveAt(index);
                    continue;
                }
                if (!obstacle.isActiveAndEnabled || !obstacle.hasBounds)
                    continue;
                float allowed = obstacle.radius + padding;
                if ((point - obstacle.center).sqrMagnitude < allowed * allowed)
                    return true;
            }
            return false;
        }


        public static bool TryGetEscapeDirection(
            Vector3 worldPosition,
            float padding,
            out Vector3 worldDirection,
            out float penetrationDepth)
        {
            Vector2 point = new(worldPosition.x, worldPosition.z); // 현재 개 위치를 바닥 평면 좌표로 바꿔 가구 원형 Bounds와 비교한다.
            Vector2 bestDirection = Vector2.zero; // 여러 가구가 겹쳐도 가장 깊게 파고든 한 가구에서 빠져나오는 방향을 우선한다.
            float deepest = 0f; // 현재까지 발견한 가장 큰 침범 깊이를 저장한다.

            for (int index = Active.Count - 1; index >= 0; index--)
            {
                MushLobbyFurnitureObstacle obstacle = Active[index]; // 현재 활성화된 하우징/상호작용 가구 하나를 검사한다.
                if (obstacle == null)
                {
                    Active.RemoveAt(index); // 파괴된 오브젝트 참조는 목록에서도 제거해 이후 프레임 비용을 줄인다.
                    continue;
                }
                if (!obstacle.isActiveAndEnabled || !obstacle.hasBounds)
                    continue; // 꺼진 가구나 Bounds를 만들 수 없는 오브젝트는 회피 대상이 아니다.

                float allowed = obstacle.radius + padding; // 개 몸통 여유 거리까지 포함한 실제 금지 반경을 계산한다.
                Vector2 fromCenter = point - obstacle.center; // 가구 중심에서 개 쪽으로 향하는 벡터를 구한다.
                float distance = fromCenter.magnitude; // 현재 개가 가구 중심에서 얼마나 떨어졌는지 계산한다.
                if (distance >= allowed)
                    continue; // 금지 반경 밖이면 이미 안전하므로 다음 가구를 검사한다.

                float depth = allowed - distance; // 금지 반경 안으로 얼마나 들어왔는지 계산한다.
                if (depth <= deepest)
                    continue; // 더 얕은 침범보다 가장 깊은 침범을 먼저 해결해야 진동 없이 빠져나오기 쉽다.

                deepest = depth; // 가장 깊은 침범 값을 갱신한다.
                bestDirection = distance > 0.0001f
                    ? fromCenter / distance // 일반적인 경우 가구 중심의 정확한 반대 방향으로 빠져나간다.
                    : Vector2.up; // 중심과 완전히 겹친 극단적인 경우에는 임의의 안정된 +Z 방향을 사용한다.
            }

            if (deepest <= 0f || bestDirection.sqrMagnitude < 0.0001f)
            {
                worldDirection = Vector3.zero; // 어떤 가구에도 파고들지 않았다면 탈출 방향은 없다.
                penetrationDepth = 0f; // 침범 깊이도 0으로 반환한다.
                return false; // 호출 측에서 평소 경로 이동을 계속하도록 false를 반환한다.
            }

            worldDirection = new Vector3(bestDirection.x, 0f, bestDirection.y).normalized; // 2D 탈출 방향을 Unity 월드 XZ 방향으로 복원한다.
            penetrationDepth = deepest; // 개가 얼마나 깊이 끼었는지 함께 넘겨 탈출 속도 보정에 사용한다.
            return true; // 현재 위치가 가구 금지 반경 안이므로 일반 경로보다 탈출을 우선해야 한다.
        }

        public static Vector3 FindOpenDirection(
            Vector3 worldOrigin,
            Vector3 desiredWorldDirection,
            float lookAhead,
            float padding)
        {
            desiredWorldDirection = Vector3.ProjectOnPlane(desiredWorldDirection, Vector3.up);
            if (desiredWorldDirection.sqrMagnitude < 0.0001f)
                return Vector3.zero;
            desiredWorldDirection.Normalize();

            float[] angles = { 0f, 38f, -38f, 72f, -72f, 108f, -108f };
            foreach (float angle in angles)
            {
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * desiredWorldDirection;
                if (!IsBlocked(worldOrigin + direction * lookAhead, padding))
                    return direction;
            }
            return Vector3.zero;
        }
    }
}
