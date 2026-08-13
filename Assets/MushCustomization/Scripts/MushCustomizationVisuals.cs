using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mush.Customization
{
    public static class MushCustomizationVisuals
    {
        private const string GeneratedPrefix = "Mush Equipped - ";
        private const string HousingModelName = "Mush Housing Model";
        private static readonly Dictionary<string, Material> Materials = new(StringComparer.Ordinal);

        public static GameObject CreateFittedModel(
            GameObject prefab,
            Transform parent,
            string objectName,
            float targetLargestSize,
            Vector3 localPosition,
            bool groundAligned = false)
        {
            if (prefab == null || parent == null)
                return null;

            GameObject holder = new GameObject(objectName);
            holder.transform.SetParent(parent, false);
            holder.transform.localPosition = localPosition;

            GameObject model = UnityEngine.Object.Instantiate(prefab, holder.transform);
            model.name = objectName + " Model";
            // FBX roots in this project carry Blender's Z-up -> Unity Y-up
            // conversion. Resetting the instantiated root rotation to identity
            // was what made every dog and sled stand on its rear end in the
            // preview. Keep the imported basis, then verify it from semantic
            // model parts so old/reimported assets are handled the same way.
            model.transform.localPosition = Vector3.zero;
            if (IsDogBody(prefab.name))
                AlignDogModel(holder.transform, model.transform);
            else if (IsSledBody(prefab.name))
                AlignSledModel(holder.transform, model.transform);
            DisableRuntimeComponents(model);
            ApplyReadableMaterials(model, prefab.name);

            if (!TryCalculateLocalBounds(holder.transform, model, out Bounds bounds))
                return holder;

            float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float scale = largest > 0.0001f ? targetLargestSize / largest : 1f;
            model.transform.localScale *= scale;
            model.transform.localPosition = groundAligned
                ? new Vector3(-bounds.center.x * scale, -bounds.min.y * scale, -bounds.center.z * scale)
                : -bounds.center * scale;
            return holder;
        }

        public static void ApplyDogLoadout(
            Transform dogRoot,
            bool malamute,
            MushCustomizationState state,
            int dogIndex)
        {
            if (dogRoot == null || state == null)
                return;

            RemoveGeneratedChildren(dogRoot);
            if (!TryCalculateWorldBounds(dogRoot.gameObject, out Bounds dogBounds))
                return;

            MushCustomizationCatalog catalog = MushCustomizationCatalog.Load();
            if (catalog == null)
                return;

            string hatId = state.GetDogHat(dogIndex);
            string neckId = state.GetDogNeck(dogIndex);
            float height = Mathf.Max(0.1f, dogBounds.size.y);
            float width = Mathf.Max(0.1f, dogBounds.size.x);
            Vector3 lossyScale = dogRoot.lossyScale;
            float parentScale = Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.y), Mathf.Abs(lossyScale.z)));
            float localHeight = height / parentScale;
            float localWidth = width / parentScale;

            bool hasDogFrame = TryGetDogWorldFrame(
                dogRoot,
                out Transform head,
                out Transform neck,
                out Bounds headBounds,
                out Vector3 anatomicalUp,
                out Vector3 anatomicalForward,
                out Vector3 anatomicalRight);

            if (!string.IsNullOrEmpty(hatId))
            {
                Vector3 hatWorld; // 모자 생성 직후 임시로 둘 머리 중심 기준 월드 위치를 저장한다.
                float fittedWidth; // 품종별 머리 폭에 맞춰 중절모와 산타 모자의 최종 크기를 계산해 저장한다.
                if (hasDogFrame)
                {
                    float headUpRadius = ProjectedRadius(headBounds.extents, anatomicalUp); // 머리 Bounds가 해부학적 위 방향으로 얼마나 뻗어 있는지 계산한다.
                    float headForwardRadius = ProjectedRadius(headBounds.extents, anatomicalForward); // 모자를 지나치게 뒤로 두지 않기 위해 머리 앞뒤 반경도 계산한다.
                    float headWidth = ProjectedRadius(headBounds.extents, anatomicalRight) * 2f; // 머리 좌우 실제 폭을 구해 모자 폭의 기준으로 사용한다.
                    hatWorld = headBounds.center + anatomicalUp * headUpRadius +
                               anatomicalForward * (headForwardRadius * 0.03f); // 우선 머리 꼭대기 근처에 모자를 만든 뒤 회전된 실제 Bounds로 최종 높이를 다시 맞춘다.
                    fittedWidth = Mathf.Clamp(headWidth * 1.12f, width * 0.46f, width * 0.78f) / parentScale; // 모자가 품종별 머리보다 너무 작거나 몸통 폭만큼 커지지 않도록 범위를 제한한다.
                }
                else
                {
                    hatWorld = new Vector3(dogBounds.center.x, dogBounds.max.y, dogBounds.center.z); // 해부학적 프레임을 못 찾은 예외 상황에서는 전체 개 Bounds의 정수리를 사용한다.
                    fittedWidth = localWidth * 0.68f; // 머리 파츠를 못 찾았을 때도 기존 몸 폭 비율로 대략적인 모자 크기를 유지한다.
                }

                GameObject hat = CreateFittedModel(
                    catalog.GetPrefab(hatId, malamute),
                    dogRoot,
                    GeneratedPrefix + "Hat",
                    fittedWidth,
                    dogRoot.InverseTransformPoint(hatWorld),
                    false); // 네 액세서리 FBX가 모두 -90도 X축 가져오기 회전을 가지므로 회전 전에 바닥 정렬을 해버리지 않고 중심 기준으로 만든다.
                Quaternion accessoryAxisFix = Quaternion.Euler(90f, 0f, 0f); // 중절모와 산타 모자 모두 가져오기 -90도를 상쇄해 FBX에서 의도한 위쪽 축이 개 머리의 위쪽과 일치하게 한다.

                if (hasDogFrame)
                    PositionHatAboveHead(hat, head, headBounds, anatomicalForward, anatomicalUp, accessoryAxisFix, hatId == MushCustomizationIds.DogFedora ? 0.10f : 0.08f); // 두 모자 모두 회전이 끝난 실제 최저면을 머리 윗면에 맞춰 눈이나 귀 속으로 파고들지 않게 한다.
                else
                    PositionDogAccessory(hat, head, hatWorld, anatomicalForward, anatomicalUp, accessoryAxisFix); // 머리 Bounds가 없는 예외 상황에서도 최소한 축 방향만은 정상적으로 보정한다.
            }

            if (!string.IsNullOrEmpty(neckId))
            {
                Vector3 neckWorld = hasDogFrame && neck != null
                    ? GetPartWorldCenter(neck) // 목 파츠가 있으면 실제 목 중심을 스카프 기준점으로 사용한다.
                    : hasDogFrame
                        ? Vector3.Lerp(headBounds.center, dogBounds.center, 0.62f) // 목 파츠가 없으면 머리와 몸통 사이를 보간해 목 위치를 추정한다.
                        : new Vector3(dogBounds.center.x, dogBounds.min.y + height * 0.67f, dogBounds.center.z); // 해부학 정보 자체가 없으면 전체 Bounds 높이 비율로 마지막 대체 위치를 만든다.
                Bounds neckBounds = default; // 빨간 반다나와 보라 스카프를 목 윗부분에 정확히 걸기 위한 실제 목 Bounds를 저장한다.
                bool hasNeckBounds = neck != null && TryGetPartWorldBounds(neck, out neckBounds); // 목 Renderer가 있으면 품종별 목 크기를 직접 읽고, 없으면 아래의 안전한 추정값을 사용한다.
                float fittedNeckSize = Mathf.Max(localWidth * 0.52f, localHeight * 0.24f); // 목 장식 전체 크기를 한 단계 더 줄여 굵은 링처럼 목 바깥에 떠 보이지 않게 한다.
                if (hasDogFrame && hasNeckBounds)
                {
                    float neckWidth = ProjectedRadius(neckBounds.extents, anatomicalRight) * 2f; // 실제 목 좌우 폭을 계산한다.
                    fittedNeckSize = Mathf.Clamp(neckWidth * 1.06f, width * 0.36f, width * 0.58f) / parentScale; // 실제 목 폭에 거의 맞는 크기로 줄여 스카프 고리 부분이 목보다 과하게 크게 보이지 않게 한다.
                }

                GameObject neckAccessory = CreateFittedModel(
                    catalog.GetPrefab(neckId, malamute),
                    dogRoot,
                    GeneratedPrefix + "Neck",
                    fittedNeckSize,
                    dogRoot.InverseTransformPoint(neckWorld),
                    false); // 스카프도 회전 전 Bounds 정렬을 하지 않아 -90도 가져오기 축 때문에 위치 오프셋이 앞뒤로 돌아가는 문제를 막는다.
                Quaternion accessoryAxisFix = Quaternion.Euler(90f, 0f, 0f); // 빨간 반다나와 보라 스카프도 모자와 같은 FBX -90도 회전을 상쇄한다.
                if (hasDogFrame)
                    PositionNeckAccessory(neckAccessory, neck != null ? neck : head, neckWorld, hasNeckBounds ? neckBounds : default, hasNeckBounds, anatomicalForward, anatomicalUp, accessoryAxisFix, neckId); // 회전한 뒤 목 아래·앞쪽으로 다시 맞춰 고리보다 스카프/반다나 꼬리가 눈에 들어오게 한다.
                else
                    PositionDogAccessory(neckAccessory, neck != null ? neck : head, neckWorld, anatomicalForward, anatomicalUp, accessoryAxisFix); // 해부학 프레임이 없는 경우에도 축 보정과 추적은 유지한다.
            }
        }

        public static GameObject ApplySledDecoration(
            Transform sledRoot,
            MushCustomizationState state,
            float sledWidth = 1.2f)
        {
            if (sledRoot == null)
                return null;

            RemoveGeneratedChildren(sledRoot, GeneratedPrefix + "Sled Decoration");
            if (state == null || string.IsNullOrEmpty(state.equippedSledDecoration))
                return null;

            MushCustomizationCatalog catalog = MushCustomizationCatalog.Load();
            GameObject prefab = catalog != null ? catalog.GetPrefab(state.equippedSledDecoration) : null;
            GameObject decoration = CreateFittedModel(
                prefab,
                sledRoot,
                GeneratedPrefix + "Sled Decoration",
                Mathf.Max(0.40f, sledWidth * 0.42f),
                new Vector3(sledWidth * 0.39f, 1.02f, 0.60f));
            if (decoration != null && state.equippedSledDecoration == MushCustomizationIds.SledLantern)
            {
                GameObject glowObject = new("Equipped Lantern Glow");
                glowObject.transform.SetParent(decoration.transform, false);
                Light glow = glowObject.AddComponent<Light>();
                glow.type = LightType.Point;
                glow.color = new Color(1f, 0.45f, 0.08f);
                glow.intensity = 1.35f;
                glow.range = 3.2f;
                glow.shadows = LightShadows.None;
            }
            return decoration;
        }

        public static GameObject PrepareHousingSlot(
            Transform slotRoot,
            GameObject prefab,
            float targetLargestSize = 1.05f)
        {
            if (slotRoot == null || prefab == null)
                return null; // 슬롯이나 실제 가구 프리팹이 없으면 장착 처리를 진행하지 않는다.

            RemoveGeneratedChildren(slotRoot, HousingModelName); // 이전에 장착했던 실제 가구 모델을 먼저 제거해 새 모델과 겹치지 않게 한다.

            for (int index = 0; index < slotRoot.childCount; index++)
            {
                Transform child = slotRoot.GetChild(index); // 예전 프로토타입 슬롯에 남아 있는 스툴·화분·사이드테이블 같은 임시 자식을 하나씩 확인한다.
                if (child == null || child.name == HousingModelName)
                    continue; // 지금 막 생성할 실제 가구 모델 루트는 비활성화 대상에서 제외한다.
                child.gameObject.SetActive(false); // 렌더러만 숨기는 대신 임시 슬롯 오브젝트 자체를 꺼서 의자가 받침대 위에 올라간 것처럼 보이는 잔재를 완전히 없앤다.
            }

            return CreateFittedModel(prefab, slotRoot, HousingModelName, targetLargestSize, Vector3.zero, true); // 깨끗해진 고정 슬롯에 선택한 실제 FBX 모델 하나만 바닥 정렬해 장착한다.
        }

        public static bool TryCalculateWorldBounds(GameObject root, out Bounds result)
        {
            result = new Bounds(root != null ? root.transform.position : Vector3.zero, Vector3.zero);
            if (root == null)
                return false;

            bool initialized = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || HasGeneratedParent(renderer.transform, root.transform))
                    continue;
                if (!initialized)
                {
                    result = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    result.Encapsulate(renderer.bounds);
                }
            }
            return initialized;
        }

        private static bool TryCalculateLocalBounds(Transform relativeTo, GameObject root, out Bounds result)
        {
            result = new Bounds(Vector3.zero, Vector3.zero);
            bool initialized = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Bounds world = renderer.bounds;
                Vector3 min = world.min;
                Vector3 max = world.max;
                for (int x = 0; x <= 1; x++)
                for (int y = 0; y <= 1; y++)
                for (int z = 0; z <= 1; z++)
                {
                    Vector3 corner = new(
                        x == 0 ? min.x : max.x,
                        y == 0 ? min.y : max.y,
                        z == 0 ? min.z : max.z);
                    Vector3 local = relativeTo.InverseTransformPoint(corner);
                    if (!initialized)
                    {
                        result = new Bounds(local, Vector3.zero);
                        initialized = true;
                    }
                    else
                    {
                        result.Encapsulate(local);
                    }
                }
            }
            return initialized;
        }

        private static bool IsDogBody(string assetName)
        {
            return !string.IsNullOrEmpty(assetName) &&
                   (assetName.IndexOf("LowPoly_Husky", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    assetName.IndexOf("LowPoly_Malamute", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsSledBody(string assetName)
        {
            return !string.IsNullOrEmpty(assetName) &&
                   assetName.IndexOf("Mush_Sled_", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   assetName.IndexOf("Lantern", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static void AlignDogModel(Transform holder, Transform model)
        {
            if (!TryGetNamedPartCenter(holder, model, "Nose", out Vector3 nose) ||
                !TryGetNamedPartCenter(holder, model, "Torso", out Vector3 torso) ||
                !TryAverageNamedParts(holder, model, "Paw", null, out Vector3 paws) ||
                !TryAverageNamedParts(holder, model, "Upper", "Thigh", out Vector3 upperLegs))
                return;

            Vector3 up = upperLegs - paws;
            if (up.sqrMagnitude < 0.000001f)
                return;
            up.Normalize();

            Vector3 forward = Vector3.ProjectOnPlane(nose - torso, up);
            if (forward.sqrMagnitude < 0.000001f)
                return;
            forward.Normalize();

            Quaternion currentBasis = Quaternion.LookRotation(forward, up);
            model.localRotation = Quaternion.Inverse(currentBasis) * model.localRotation;
        }

        private static void AlignSledModel(Transform holder, Transform model)
        {
            if (!TryGetNamedPartCenter(holder, model, "Socket.DogRope", out Vector3 dogRope) ||
                !TryGetNamedPartCenter(holder, model, "Socket.PlayerGrip", out Vector3 playerGrip) ||
                !TryGetNamedPartCenter(holder, model, "Runner_L", out Vector3 leftRunner) ||
                !TryGetNamedPartCenter(holder, model, "Runner_R", out Vector3 rightRunner))
                return;

            Vector3 forward = dogRope - playerGrip;
            Vector3 right = rightRunner - leftRunner;
            if (forward.sqrMagnitude < 0.000001f || right.sqrMagnitude < 0.000001f)
                return;
            forward.Normalize();
            right = Vector3.ProjectOnPlane(right, forward);
            if (right.sqrMagnitude < 0.000001f)
                return;
            right.Normalize();
            Vector3 up = Vector3.Cross(forward, right).normalized;
            Vector3 runners = (leftRunner + rightRunner) * 0.5f;
            if (Vector3.Dot(up, playerGrip - runners) < 0f)
                up = -up;

            Quaternion currentBasis = Quaternion.LookRotation(forward, up);
            model.localRotation = Quaternion.Inverse(currentBasis) * model.localRotation;
        }

        private static bool TryGetDogWorldFrame(
            Transform dogRoot,
            out Transform head,
            out Transform neck,
            out Bounds headBounds,
            out Vector3 up,
            out Vector3 forward,
            out Vector3 right)
        {
            head = FindNamedPart(dogRoot, "_Head") ?? FindNamedPart(dogRoot, "Head");
            neck = FindNamedPart(dogRoot, "_Neck") ?? FindNamedPart(dogRoot, "Neck");
            headBounds = default;
            up = dogRoot.up;
            forward = dogRoot.forward;
            right = dogRoot.right;
            if (head == null || !TryGetPartWorldBounds(head, out headBounds))
                return false;

            Transform nosePart = FindNamedPart(dogRoot, "_Nose") ?? FindNamedPart(dogRoot, "Nose");
            Transform torsoPart = FindNamedPart(dogRoot, "_Torso") ?? FindNamedPart(dogRoot, "Torso");
            if (nosePart == null || torsoPart == null ||
                !TryAverageNamedPartWorldCenters(dogRoot, "Paw", null, out Vector3 pawCenter) ||
                !TryAverageNamedPartWorldCenters(dogRoot, "Upper", "Thigh", out Vector3 upperCenter))
                return false;

            up = upperCenter - pawCenter;
            if (up.sqrMagnitude < 0.000001f)
                return false;
            up.Normalize();

            forward = Vector3.ProjectOnPlane(GetPartWorldCenter(nosePart) - GetPartWorldCenter(torsoPart), up);
            if (forward.sqrMagnitude < 0.000001f)
                return false;
            forward.Normalize();
            right = Vector3.Cross(up, forward);
            if (right.sqrMagnitude < 0.000001f)
                return false;
            right.Normalize();
            return true;
        }

        private static void PositionHatAboveHead(
            GameObject accessory,
            Transform trackedPart,
            Bounds headBounds,
            Vector3 forward,
            Vector3 up,
            Quaternion localRotationOffset,
            float overlapRatio)
        {
            if (accessory == null)
                return; // 모자 프리팹을 불러오지 못했다면 위치 계산도 진행하지 않는다.

            if (forward.sqrMagnitude < 0.000001f)
                forward = Vector3.forward; // 머리 앞 방향 계산이 실패한 예외 상황에서는 Unity 기본 앞 방향을 사용한다.
            if (up.sqrMagnitude < 0.000001f)
                up = Vector3.up; // 머리 위 방향 계산이 실패한 예외 상황에서는 월드 위 방향을 사용한다.
            forward.Normalize(); // 투영 계산 전에 머리 앞 방향 길이를 1로 맞춘다.
            up.Normalize(); // 머리 윗면과 모자 최저면을 같은 단위로 비교하기 위해 위 방향도 정규화한다.

            Quaternion headAlignedRotation = Quaternion.LookRotation(forward, up); // 개 머리의 해부학적 앞/위 축을 기준으로 기본 모자 방향을 만든다.
            accessory.transform.rotation = headAlignedRotation * localRotationOffset; // 산타 모자 FBX 축 보정 90도를 먼저 적용해 최종 자세에서 Bounds를 다시 계산한다.
            accessory.transform.position = headBounds.center + up * ProjectedRadius(headBounds.extents, up); // 우선 모자 중심을 머리 윗면 근처로 올려 투영 Bounds 계산이 안정적으로 되게 한다.

            float headTop = Vector3.Dot(headBounds.center, up) + ProjectedRadius(headBounds.extents, up); // 개 머리 Bounds에서 위 방향으로 가장 높은 면의 좌표를 계산한다.
            if (TryGetProjectedRange(accessory, up, out float accessoryMin, out _))
            {
                float overlap = ProjectedRadius(headBounds.extents, up) * overlapRatio; // 모자 종류별로 지정한 작은 겹침만 허용해 챙은 머리에 붙고 눈·귀 쪽으로는 내려가지 않게 한다.
                float desiredBottom = headTop - overlap; // 모자 챙의 최저면이 눈 높이까지 내려가지 않고 머리 꼭대기에만 걸리도록 목표 높이를 만든다.
                accessory.transform.position += up * (desiredBottom - accessoryMin); // 회전이 끝난 실제 모자 최저면을 목표 높이에 정확히 맞춘다.
            }

            if (trackedPart != null)
            {
                MushDogAccessoryFollower follower = accessory.AddComponent<MushDogAccessoryFollower>(); // 고개를 돌리거나 갸웃해도 새로 맞춘 모자 위치가 머리를 따라가도록 추적기를 붙인다.
                follower.Configure(trackedPart); // 현재 최종 위치/회전을 머리 로컬 오프셋으로 저장한다.
            }
        }

        private static void PositionNeckAccessory(
            GameObject accessory,
            Transform trackedPart,
            Vector3 neckWorld,
            Bounds neckBounds,
            bool hasNeckBounds,
            Vector3 forward,
            Vector3 up,
            Quaternion localRotationOffset,
            string accessoryId)
        {
            if (accessory == null)
                return; // 목 액세서리 프리팹을 읽지 못했다면 추가 위치 계산을 하지 않는다.

            if (forward.sqrMagnitude < 0.000001f)
                forward = Vector3.forward; // 해부학적 앞 방향이 유효하지 않은 예외 상황에서는 Unity 기본 앞 방향을 사용한다.
            if (up.sqrMagnitude < 0.000001f)
                up = Vector3.up; // 해부학적 위 방향을 못 얻었을 때는 월드 위 방향으로 안전하게 대체한다.
            forward.Normalize(); // LookRotation과 Bounds 투영이 같은 기준을 사용하도록 앞 방향을 정규화한다.
            up.Normalize(); // 스카프 윗면과 목 윗면 높이를 정확히 비교하기 위해 위 방향도 정규화한다.

            Quaternion neckAlignedRotation = Quaternion.LookRotation(forward, up); // 개 목의 앞/위 방향을 기준으로 액세서리 기본 자세를 만든다.
            float yawOffset = accessoryId == MushCustomizationIds.DogPurpleScarf ? -22f : 0f; // 보라 스카프는 작은 꼬리 파츠가 몸 안쪽에 숨지 않도록 목 둘레에서 살짝 돌려 앞옆으로 보이게 한다.
            Quaternion visibleTailRotation = Quaternion.AngleAxis(yawOffset, up) * neckAlignedRotation * localRotationOffset; // FBX 축 보정 뒤에도 스카프 꼬리가 보이는 방향을 유지하도록 최종 회전을 만든다.
            accessory.transform.SetPositionAndRotation(neckWorld, visibleTailRotation); // 먼저 목 중심에 회전을 적용한 뒤 실제 렌더러 Bounds를 이용해 높이와 앞뒤 위치를 다시 맞춘다.

            float forwardOffset = 0f; // 목 Bounds를 못 찾은 경우에는 불필요하게 액세서리를 앞으로 밀지 않는다.
            if (hasNeckBounds)
            {
                float neckForwardRadius = ProjectedRadius(neckBounds.extents, forward); // 목이 앞뒤 방향으로 차지하는 실제 반경을 계산한다.
                forwardOffset = neckForwardRadius * (accessoryId == MushCustomizationIds.DogPurpleScarf ? 0.22f : 0.16f); // 고리 부분은 목에 남기되 천 꼬리가 턱 아래에서 몸통 밖으로 보일 정도만 앞으로 당긴다.
            }

            if (TryGetProjectedRange(accessory, up, out _, out float accessoryTop))
            {
                float desiredTop = Vector3.Dot(neckWorld, up) - 0.01f; // 목 Bounds를 못 찾은 경우에도 액세서리를 목 중심보다 위로 치켜올리지 않는다.
                if (hasNeckBounds)
                {
                    float neckRadius = ProjectedRadius(neckBounds.extents, up); // 목 파츠가 위 방향으로 차지하는 실제 반경을 계산한다.
                    float topRatio = accessoryId == MushCustomizationIds.DogPurpleScarf ? 0.34f : 0.42f; // 보라 스카프는 더 아래에, 반다나는 턱 바로 아래에 걸리도록 종류별 높이를 나눈다.
                    desiredTop = Vector3.Dot(neckBounds.center, up) + neckRadius * topRatio; // 예전 0.72배보다 훨씬 낮춰 목 위의 링처럼 떠 있는 느낌을 없앤다.
                }
                accessory.transform.position += up * (desiredTop - accessoryTop); // 회전된 실제 액세서리 윗면이 목표 목 높이에 오도록 상하 위치를 맞춘다.
            }
            accessory.transform.position += forward * forwardOffset; // 마지막에 목 앞쪽으로 조금 이동시켜 스카프/반다나의 천 부분이 몸통 내부에 묻히지 않게 한다.

            if (trackedPart != null)
            {
                MushDogAccessoryFollower follower = accessory.AddComponent<MushDogAccessoryFollower>(); // 개가 고개와 목을 움직여도 현재 스카프 위치가 함께 따라가도록 추적기를 추가한다.
                follower.Configure(trackedPart); // 회전과 높이 보정이 끝난 최종 상태를 목 파츠 기준 로컬 오프셋으로 저장한다.
            }
        }

        private static bool TryGetProjectedRange(
            GameObject root,
            Vector3 axis,
            out float minimum,
            out float maximum)
        {
            minimum = float.PositiveInfinity; // 아직 어떤 모자 Renderer도 읽지 않았으므로 최소 투영값을 가장 큰 값에서 시작한다.
            maximum = float.NegativeInfinity; // 최대 투영값은 가장 작은 값에서 시작한다.
            bool found = false; // 최소 하나의 활성 Renderer Bounds를 읽었는지 기록한다.

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled)
                    continue; // 꺼진 장식 파츠는 실제 보이는 모자 크기 계산에서 제외한다.

                Bounds bounds = renderer.bounds; // 최종 90도 회전까지 적용된 월드 Bounds를 가져온다.
                Vector3 min = bounds.min; // Bounds 여덟 꼭짓점을 만들기 위한 최소 좌표를 저장한다.
                Vector3 max = bounds.max; // Bounds 여덟 꼭짓점을 만들기 위한 최대 좌표를 저장한다.
                for (int x = 0; x <= 1; x++)
                for (int y = 0; y <= 1; y++)
                for (int z = 0; z <= 1; z++)
                {
                    Vector3 corner = new(
                        x == 0 ? min.x : max.x,
                        y == 0 ? min.y : max.y,
                        z == 0 ? min.z : max.z); // 현재 Bounds의 한 꼭짓점을 만든다.
                    float projected = Vector3.Dot(corner, axis); // 이 꼭짓점이 개 머리의 위 방향 축에서 어느 높이에 있는지 계산한다.
                    minimum = Mathf.Min(minimum, projected); // 모자의 가장 낮은 투영 좌표를 누적한다.
                    maximum = Mathf.Max(maximum, projected); // 모자의 가장 높은 투영 좌표도 함께 누적한다.
                    found = true; // 실제 Bounds 값을 하나 이상 읽었다.
                }
            }

            return found; // Renderer가 하나라도 있었다면 유효한 투영 범위를 반환한다.
        }

        private static void PositionDogAccessory(
            GameObject accessory,
            Transform trackedPart,
            Vector3 worldPosition,
            Vector3 forward,
            Vector3 up,
            Quaternion localRotationOffset)
        {
            if (accessory == null)
                return;

            if (forward.sqrMagnitude < 0.000001f)
                forward = Vector3.forward;
            if (up.sqrMagnitude < 0.000001f)
                up = Vector3.up;
            Quaternion headAlignedRotation = Quaternion.LookRotation(forward, up); // 개 머리의 해부학적 앞/위 방향으로 액세서리 기준 자세를 먼저 만든다.
            accessory.transform.SetPositionAndRotation(worldPosition, headAlignedRotation * localRotationOffset); // 모자별 FBX 축 차이는 기준 자세 뒤의 로컬 오프셋으로만 보정한다.

            if (trackedPart != null)
            {
                MushDogAccessoryFollower follower = accessory.AddComponent<MushDogAccessoryFollower>();
                follower.Configure(trackedPart);
            }
        }

        private static float ProjectedRadius(Vector3 boundsExtents, Vector3 direction)
        {
            direction = new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
            return Vector3.Dot(boundsExtents, direction);
        }

        private static Transform FindNamedPart(Transform root, string nameFragment)
        {
            if (root == null)
                return null;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (HasGeneratedParent(child, root))
                    continue;
                if (child.name.IndexOf(nameFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    return child;
            }
            return null;
        }

        private static bool TryGetNamedPartCenter(
            Transform relativeTo,
            Transform root,
            string nameFragment,
            out Vector3 center)
        {
            Transform part = FindNamedPart(root, nameFragment);
            if (part != null)
            {
                center = relativeTo.InverseTransformPoint(GetPartWorldCenter(part));
                return true;
            }
            center = Vector3.zero;
            return false;
        }

        private static bool TryAverageNamedParts(
            Transform relativeTo,
            Transform root,
            string firstFragment,
            string secondFragment,
            out Vector3 center)
        {
            center = Vector3.zero;
            int count = 0;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                bool matches = child.name.IndexOf(firstFragment, StringComparison.OrdinalIgnoreCase) >= 0 ||
                               (!string.IsNullOrEmpty(secondFragment) &&
                                child.name.IndexOf(secondFragment, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!matches)
                    continue;
                center += relativeTo.InverseTransformPoint(GetPartWorldCenter(child));
                count++;
            }
            if (count == 0)
                return false;
            center /= count;
            return true;
        }

        private static bool TryAverageNamedPartWorldCenters(
            Transform root,
            string firstFragment,
            string secondFragment,
            out Vector3 center)
        {
            center = Vector3.zero;
            int count = 0;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (HasGeneratedParent(child, root))
                    continue;
                bool matches = child.name.IndexOf(firstFragment, StringComparison.OrdinalIgnoreCase) >= 0 ||
                               (!string.IsNullOrEmpty(secondFragment) &&
                                child.name.IndexOf(secondFragment, StringComparison.OrdinalIgnoreCase) >= 0);
                if (!matches)
                    continue;
                center += GetPartWorldCenter(child);
                count++;
            }
            if (count == 0)
                return false;
            center /= count;
            return true;
        }

        private static Vector3 GetPartWorldCenter(Transform part)
        {
            Renderer renderer = part != null ? part.GetComponent<Renderer>() : null;
            if (renderer == null && part != null)
                renderer = part.GetComponentInChildren<Renderer>(true);
            return renderer != null ? renderer.bounds.center : part != null ? part.position : Vector3.zero;
        }

        private static bool TryGetPartWorldBounds(Transform part, out Bounds bounds)
        {
            bounds = default;
            if (part == null)
                return false;
            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer == null)
                renderer = part.GetComponentInChildren<Renderer>(true);
            if (renderer == null)
                return false;
            bounds = renderer.bounds;
            return true;
        }

        private static void DisableRuntimeComponents(GameObject root)
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
                animator.enabled = false;
            foreach (Animation animation in root.GetComponentsInChildren<Animation>(true))
                animation.enabled = false;
        }

        private static void ApplyReadableMaterials(GameObject root, string assetName)
        {
            string asset = (assetName ?? string.Empty).ToLowerInvariant();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;
                for (int index = 0; index < slots.Length; index++)
                {
                    string source = slots[index] != null ? slots[index].name.ToLowerInvariant() : string.Empty;
                    Color color;
                    float smoothness = 0.18f;

                    if (asset.Contains("sled"))
                    {
                        if (asset.Contains("santa") && source.Contains("santa_gold"))
                        {
                            color = new Color(0.92f, 0.57f, 0.08f);
                            smoothness = 0.62f;
                        }
                        else if (asset.Contains("santa") && source.Contains("santa_cream"))
                            color = new Color(0.92f, 0.82f, 0.63f);
                        else if (asset.Contains("santa") && source.Contains("santa_red"))
                            color = new Color(0.72f, 0.025f, 0.035f);
                        else if (source.Contains("metal"))
                        {
                            color = new Color(0.30f, 0.35f, 0.42f);
                            smoothness = 0.62f;
                        }
                        else if (asset.Contains("red") || asset.Contains("santa"))
                            color = new Color(0.70f, 0.055f, 0.035f);
                        else if (asset.Contains("blue"))
                            color = new Color(0.045f, 0.23f, 0.64f);
                        else if (asset.Contains("black"))
                            color = new Color(0.055f, 0.06f, 0.07f);
                        else if (asset.Contains("lantern"))
                        {
                            color = source.Contains("glass") || source.Contains("light")
                                ? new Color(1f, 0.48f, 0.08f)
                                : new Color(0.15f, 0.11f, 0.07f);
                            smoothness = 0.48f;
                        }
                        else
                            color = source.Contains("light")
                                ? new Color(0.65f, 0.34f, 0.11f)
                                : new Color(0.38f, 0.17f, 0.055f);
                    }
                    else if (asset.Contains("fedora"))
                    {
                        color = source.Contains("band")
                            ? new Color(0.08f, 0.045f, 0.025f)
                            : new Color(0.40f, 0.19f, 0.07f);
                    }
                    else if (asset.Contains("purplescarf"))
                        color = new Color(0.42f, 0.12f, 0.62f);
                    else if (asset.Contains("redbandana"))
                        color = new Color(0.72f, 0.035f, 0.025f);
                    else if (asset.Contains("santahat"))
                        color = source.Contains("white") || source.Contains("fur")
                            ? new Color(0.92f, 0.90f, 0.84f)
                            : new Color(0.78f, 0.035f, 0.025f);
                    else if (asset.Contains("husky") || asset.Contains("malamute"))
                    {
                        if (source.Contains("eye") || source.Contains("iris"))
                        {
                            color = asset.Contains("malamute")
                                ? new Color(0.34f, 0.13f, 0.035f)
                                : new Color(0.06f, 0.34f, 0.70f);
                            smoothness = 0.52f;
                        }
                        else if (source.Contains("light") || source.Contains("white") || source.Contains("cream") || source.Contains("sclera"))
                            color = new Color(0.82f, 0.82f, 0.77f);
                        else if (source.Contains("black") || source.Contains("dark"))
                            color = asset.Contains("malamute")
                                ? new Color(0.12f, 0.08f, 0.055f)
                                : new Color(0.055f, 0.07f, 0.085f);
                        else
                            color = asset.Contains("malamute")
                                ? new Color(0.34f, 0.28f, 0.23f)
                                : new Color(0.25f, 0.31f, 0.37f);
                    }
                    else if (asset.Contains("furniture"))
                    {
                        if (asset.Contains("chair"))
                            color = source.Contains("wood")
                                ? new Color(0.34f, 0.15f, 0.055f)
                                : new Color(0.16f, 0.34f, 0.50f);
                        else if (asset.Contains("dogbed"))
                            color = source.Contains("wood")
                                ? new Color(0.34f, 0.15f, 0.055f)
                                : new Color(0.48f, 0.10f, 0.08f);
                        else
                            color = new Color(0.42f, 0.20f, 0.07f);
                    }
                    else
                        color = new Color(0.55f, 0.56f, 0.58f);

                    string key = asset + "|" + source + "|" + color;
                    slots[index] = GetMaterial(key, color, smoothness);
                }
                renderer.sharedMaterials = slots;
            }
        }

        private static Material GetMaterial(string key, Color color, float smoothness)
        {
            if (Materials.TryGetValue(key, out Material material) && material != null)
                return material;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader) { name = "Mush Custom " + key };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            material.enableInstancing = true;
            Materials[key] = material;
            return material;
        }

        private static void RemoveGeneratedChildren(Transform root, string exactName = null)
        {
            for (int index = root.childCount - 1; index >= 0; index--)
            {
                Transform child = root.GetChild(index);
                bool matches = exactName == null
                    ? child.name.StartsWith(GeneratedPrefix, StringComparison.Ordinal)
                    : child.name.Equals(exactName, StringComparison.Ordinal);
                if (!matches)
                    continue;
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        private static bool HasGeneratedParent(Transform candidate, Transform root)
        {
            Transform current = candidate;
            while (current != null && current != root)
            {
                if (current.name.StartsWith(GeneratedPrefix, StringComparison.Ordinal))
                    return true;
                current = current.parent;
            }
            return false;
        }
    }

    /// <summary>
    /// Keeps an equipped item on an animated dog part without inheriting the
    /// mesh object's non-uniform scale. This is important for lobby head tilts.
    /// </summary>
    internal sealed class MushDogAccessoryFollower : MonoBehaviour
    {
        private Transform trackedPart;
        private Vector3 trackedLocalPosition;
        private Quaternion trackedLocalRotation;

        public void Configure(Transform part)
        {
            trackedPart = part;
            trackedLocalPosition = part.InverseTransformPoint(transform.position);
            trackedLocalRotation = Quaternion.Inverse(part.rotation) * transform.rotation;
        }

        private void LateUpdate()
        {
            if (trackedPart == null)
                return;
            transform.SetPositionAndRotation(
                trackedPart.TransformPoint(trackedLocalPosition),
                trackedPart.rotation * trackedLocalRotation);
        }
    }
}
