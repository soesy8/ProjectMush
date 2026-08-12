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
                Vector3 hatWorld;
                float fittedWidth;
                if (hasDogFrame)
                {
                    float headUpRadius = ProjectedRadius(headBounds.extents, anatomicalUp);
                    float headForwardRadius = ProjectedRadius(headBounds.extents, anatomicalForward);
                    float headWidth = ProjectedRadius(headBounds.extents, anatomicalRight) * 2f;
                    hatWorld = headBounds.center + anatomicalUp * (headUpRadius * 0.78f) +
                               anatomicalForward * (headForwardRadius * 0.04f);
                    fittedWidth = Mathf.Clamp(headWidth * 1.12f, width * 0.46f, width * 0.78f) / parentScale;
                }
                else
                {
                    hatWorld = new Vector3(dogBounds.center.x, dogBounds.max.y, dogBounds.center.z);
                    fittedWidth = localWidth * 0.68f;
                }

                GameObject hat = CreateFittedModel(
                    catalog.GetPrefab(hatId, malamute),
                    dogRoot,
                    GeneratedPrefix + "Hat",
                    fittedWidth,
                    dogRoot.InverseTransformPoint(hatWorld),
                    true);
                PositionDogAccessory(hat, head, hatWorld, anatomicalForward, anatomicalUp);
            }

            if (!string.IsNullOrEmpty(neckId))
            {
                Vector3 neckWorld = hasDogFrame && neck != null
                    ? GetPartWorldCenter(neck)
                    : hasDogFrame
                        ? Vector3.Lerp(headBounds.center, dogBounds.center, 0.62f)
                        : new Vector3(dogBounds.center.x, dogBounds.min.y + height * 0.67f, dogBounds.center.z);
                GameObject neckAccessory = CreateFittedModel(
                    catalog.GetPrefab(neckId, malamute),
                    dogRoot,
                    GeneratedPrefix + "Neck",
                    Mathf.Max(localWidth * 0.78f, localHeight * 0.32f),
                    dogRoot.InverseTransformPoint(neckWorld));
                PositionDogAccessory(neckAccessory, neck != null ? neck : head, neckWorld, anatomicalForward, anatomicalUp);
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
                return null;

            foreach (Renderer renderer in slotRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.transform.IsChildOf(slotRoot) || renderer.transform == slotRoot)
                    continue;
                renderer.enabled = false;
            }

            RemoveGeneratedChildren(slotRoot, HousingModelName);
            return CreateFittedModel(prefab, slotRoot, HousingModelName, targetLargestSize, Vector3.zero, true);
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

        private static void PositionDogAccessory(
            GameObject accessory,
            Transform trackedPart,
            Vector3 worldPosition,
            Vector3 forward,
            Vector3 up)
        {
            if (accessory == null)
                return;

            if (forward.sqrMagnitude < 0.000001f)
                forward = Vector3.forward;
            if (up.sqrMagnitude < 0.000001f)
                up = Vector3.up;
            accessory.transform.SetPositionAndRotation(worldPosition, Quaternion.LookRotation(forward, up));

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
