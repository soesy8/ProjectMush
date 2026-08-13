using System.Collections.Generic;
using UnityEngine;

namespace Mush.Lobby
{
    [DisallowMultipleComponent]
    public sealed class MushLobbyFurnitureObstacle : MonoBehaviour
    {
        private static readonly List<MushLobbyFurnitureObstacle> Active = new();

        private Vector2 center;
        private float radius = 0.5f;
        private bool hasBounds;

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
                hasBounds = false;
                return;
            }

            center = new Vector2(bounds.center.x, bounds.center.z);
            radius = Mathf.Max(0.28f, Mathf.Max(bounds.extents.x, bounds.extents.z));
            hasBounds = true;
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
