using System.Collections.Generic;
using UnityEngine;

namespace Mush.Lobby
{
    /// <summary>
    /// Represents the currently equipped dog-bed housing slot.
    /// One dog reserves the bed, walks to the approach point, enters the bed, sleeps, and leaves through the same approach point.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MushLobbyDogBedSpot : MonoBehaviour
    {
        private static readonly List<MushLobbyDogBedSpot> ActiveBeds = new(); // 현재 활성화된 개 침대 슬롯만 모아 배회 중인 개가 찾을 수 있게 한다.

        [SerializeField] private float approachDistance = 0.72f; // 침대 본체를 내비메시 장애물로 유지한 채 개가 안전하게 접근할 앞쪽 거리다.
        [SerializeField] private float surfaceInset = 0.06f; // 실제 침대 윗면보다 아주 조금 아래/안쪽으로 눕혀 공중에 떠 보이지 않게 하는 보정값이다.

        private MushLobbyDogRoamer reservedBy; // 두 마리가 같은 침대에 동시에 포개지지 않도록 현재 예약한 개를 기억한다.
        private Bounds bedBounds; // 장착된 실제 침대 모델의 Renderer Bounds를 저장한다.
        private bool hasBounds; // 침대 모델 Bounds를 정상적으로 읽었는지 나타낸다.

        public float SurfaceY => hasBounds ? bedBounds.max.y - surfaceInset : transform.position.y; // 개 비주얼을 침대 윗면까지 올릴 때 사용할 실제 높이다.

        private void OnEnable()
        {
            if (!ActiveBeds.Contains(this))
                ActiveBeds.Add(this); // 장착된 침대가 활성화되는 순간 수면 후보 목록에 등록한다.
            RefreshBounds(); // 하우징 모델이 교체된 뒤 실제 크기 기준으로 접근/수면 위치를 계산할 수 있게 한다.
        }

        private void OnDisable()
        {
            ActiveBeds.Remove(this); // 침대를 제거하거나 다른 하우징으로 바꾸면 더 이상 수면 후보로 사용하지 않는다.
            reservedBy = null; // 비활성화된 침대의 예약도 즉시 해제한다.
        }

        public void RefreshBounds()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true); // 현재 슬롯 안에 실제로 보이는 침대 모델의 렌더러를 모은다.
            bool initialized = false; // 첫 유효 렌더러를 만났는지 추적한다.
            Bounds combined = default; // 여러 파츠로 나뉜 침대도 하나의 월드 Bounds로 합친다.

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue; // 숨겨진 기본 슬롯 모델이나 꺼진 파츠는 실제 침대 크기에 포함하지 않는다.

                if (!initialized)
                {
                    combined = renderer.bounds; // 첫 파츠의 Bounds를 시작값으로 사용한다.
                    initialized = true; // 이후 파츠부터는 Encapsulate로 합치도록 표시한다.
                }
                else
                {
                    combined.Encapsulate(renderer.bounds); // 모든 활성 파츠를 감싸는 하나의 침대 Bounds를 만든다.
                }
            }

            bedBounds = combined; // 계산된 실제 침대 크기를 저장한다.
            hasBounds = initialized; // 유효 파츠가 하나라도 있었을 때만 이 침대를 사용 가능하게 한다.
        }

        public static bool TryReserveNearest(MushLobbyDogRoamer dog, out MushLobbyDogBedSpot bed, out Vector3 approachWorld, out Vector3 sleepWorld, out Quaternion sleepRotation)
        {
            bed = null; // 아직 선택된 침대가 없으므로 기본값으로 시작한다.
            approachWorld = Vector3.zero; // 예약 실패 시 사용할 안전한 기본 반환값이다.
            sleepWorld = Vector3.zero; // 예약 실패 시 사용할 안전한 기본 반환값이다.
            sleepRotation = Quaternion.identity; // 예약 실패 시 사용할 안전한 기본 반환값이다.
            if (dog == null)
                return false; // 호출한 개가 없으면 침대를 예약할 수 없다.

            float bestDistance = float.PositiveInfinity; // 여러 침대가 생길 미래 확장까지 고려해 가장 가까운 빈 침대를 고른다.
            MushLobbyDogBedSpot best = null; // 현재까지 가장 적합한 침대를 저장한다.

            for (int index = ActiveBeds.Count - 1; index >= 0; index--)
            {
                MushLobbyDogBedSpot candidate = ActiveBeds[index]; // 현재 활성 침대 후보 하나를 가져온다.
                if (candidate == null)
                {
                    ActiveBeds.RemoveAt(index); // 파괴된 참조는 정적 목록에서도 제거한다.
                    continue;
                }
                if (!candidate.isActiveAndEnabled || !candidate.hasBounds || candidate.reservedBy != null)
                    continue; // 꺼졌거나 모델이 없거나 다른 개가 자고 있는 침대는 건너뛴다.

                float distance = (candidate.bedBounds.center - dog.transform.position).sqrMagnitude; // 개와 침대 중심 사이 거리를 비교한다.
                if (distance >= bestDistance)
                    continue; // 더 먼 침대는 현재 최적 후보를 바꾸지 않는다.

                bestDistance = distance; // 새로 찾은 더 가까운 거리로 갱신한다.
                best = candidate; // 가장 가까운 빈 침대를 새 최적 후보로 저장한다.
            }

            if (best == null)
                return false; // 현재 장착된 빈 개 침대가 없으면 이번 수면 행동을 건너뛴다.

            best.reservedBy = dog; // 다른 개가 같은 침대를 동시에 선택하지 못하도록 즉시 예약한다.
            best.GetPoints(out approachWorld, out sleepWorld, out sleepRotation); // 현재 모델 Bounds와 슬롯 방향으로 실제 접근/수면 위치를 계산한다.
            bed = best; // 호출한 개가 나중에 예약을 해제할 수 있도록 선택된 침대 컴포넌트를 돌려준다.
            return true; // 침대 예약과 위치 계산이 정상적으로 끝났다.
        }

        public void Release(MushLobbyDogRoamer dog)
        {
            if (reservedBy == dog)
                reservedBy = null; // 예약한 당사자만 자기 예약을 해제할 수 있게 해 다른 개의 수면을 끊지 않는다.
        }

        private void GetPoints(out Vector3 approachWorld, out Vector3 sleepWorld, out Quaternion sleepRotation)
        {
            Vector3 bedForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized; // 하우징 슬롯이 바라보는 방향을 바닥 평면의 침대 입구 방향으로 사용한다.
            if (bedForward.sqrMagnitude < 0.0001f)
                bedForward = Vector3.forward; // 비정상 회전일 때도 계산이 깨지지 않도록 기본 정면을 사용한다.

            float forwardHalf = Mathf.Max(0.32f, Vector3.Dot(bedBounds.extents, new Vector3(Mathf.Abs(bedForward.x), 0f, Mathf.Abs(bedForward.z)))); // 침대 중심에서 앞쪽 가장자리까지의 대략적인 반경을 구한다.
            Vector3 flatCenter = new(bedBounds.center.x, transform.position.y, bedBounds.center.z); // 내비메시 접근용 위치는 바닥 높이를 유지한다.
            approachWorld = flatCenter + bedForward * (forwardHalf + approachDistance); // 침대 장애물 바깥쪽에 Agent가 정상적으로 도착할 지점을 만든다.
            sleepWorld = flatCenter; // 마지막 짧은 진입에서 개 루트를 침대 중심 XZ로 이동시킨다.
            sleepRotation = Quaternion.LookRotation(bedForward, Vector3.up); // 누웠을 때 머리가 침대 입구와 방 중앙/플레이어 쪽을 향하도록 정렬한다.
        }
    }
}
