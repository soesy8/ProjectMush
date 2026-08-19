using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Mush.Lobby
{
    [DisallowMultipleComponent]
    public sealed class MushLobbyFeedingStation : MonoBehaviour
    {
        private const float SecondsToFill = 2.35f;
        private const int FullBowlPelletCount = 42;
        private const int BowlCount = 2;
        private readonly List<Material> ownedMaterials = new();

        private MushLobbyDogRoamer[] dogs;
        private ParticleSystem fallingFoodParticles;
        private readonly ParticleSystem[] bowlFoodParticles = new ParticleSystem[BowlCount];
        private readonly MushLobbyDogRoamer[] assignedDogs = new MushLobbyDogRoamer[BowlCount];
        private readonly Vector3[] bowlWorld = new Vector3[BowlCount];
        private readonly Vector3[] eatingWorld = new Vector3[BowlCount];
        private readonly Quaternion[] eatingRotation = new Quaternion[BowlCount];
        private readonly float[] bowlFill = new float[BowlCount];
        private readonly int[] visibleBowlPellets = new int[BowlCount];
        private readonly bool[] bowlReady = new bool[BowlCount];
        private Camera desktopCamera;
        private Vector3 desktopHoldWorld;
        private float fallingEmitAccumulator;

        public Vector3 DesktopHoldWorld => desktopHoldWorld;

        public static MushLobbyFeedingStation Install(Camera lobbyCamera, MushLobbyDogRoamer[] lobbyDogs, Transform lobbyRoot)
        {
            if (lobbyRoot == null)
                return null;

            Transform existingTransform = FindDescendant(lobbyRoot, "Mush Dog Feeding Station");
            MushLobbyFeedingStation station = existingTransform != null
                ? existingTransform.GetComponent<MushLobbyFeedingStation>()
                : null;
            if (station == null)
            {
                GameObject stationObject = new GameObject("Mush Dog Feeding Station");
                stationObject.transform.SetParent(lobbyRoot, false);
                stationObject.transform.localPosition = new Vector3(2.65f, 0f, 0f);
                station = stationObject.AddComponent<MushLobbyFeedingStation>();
            }

            station.dogs = lobbyDogs;
            station.desktopCamera = lobbyCamera != null ? lobbyCamera : Camera.main;
            station.HideLegacyDogBowl(lobbyRoot);
            if (station.fallingFoodParticles == null)
                station.BuildFeedingPlace();
            return station;
        }

        public bool TryGetDesktopPointerWorld(Vector2 screenPosition, out Vector3 worldPosition)
        {
            worldPosition = desktopHoldWorld;
            if (desktopCamera == null)
                desktopCamera = Camera.main;
            if (desktopCamera == null)
                return false;

            // 사료통은 그릇 앞의 세로 평면에서 마우스를 따라간다. 카메라 방향이 조금 바뀌어도
            // 깊이가 요동하지 않아 그릇 위에 정확히 가져다 놓고 기울일 수 있다.
            Plane feedingPlane = new Plane(transform.forward, desktopHoldWorld);
            Ray pointerRay = desktopCamera.ScreenPointToRay(screenPosition);
            if (!feedingPlane.Raycast(pointerRay, out float distance))
                return false;

            Vector3 localPoint = transform.InverseTransformPoint(pointerRay.GetPoint(distance));
            localPoint.x = Mathf.Clamp(localPoint.x, -1.05f, 1.05f);
            localPoint.y = Mathf.Clamp(localPoint.y, 0.46f, 1.30f);
            localPoint.z = 0.08f;
            worldPosition = transform.TransformPoint(localPoint);
            return true;
        }

        public Quaternion GetDesktopCanisterRotation(float tiltDegrees)
        {
            return transform.rotation * Quaternion.Euler(0f, 0f, tiltDegrees);
        }

        private void Update()
        {
            // The bowl can become full while every dog is busy with a ball or
            // the fireplace. Keep the food waiting and call one only when free.
            for (int bowlIndex = 0; bowlIndex < BowlCount; bowlIndex++)
            {
                if (bowlReady[bowlIndex] && assignedDogs[bowlIndex] == null)
                    TryCallDogToFullBowl(bowlIndex);
            }
        }

        public void PourFrom(Vector3 pourWorldPosition, float deltaTime)
        {
            if (deltaTime <= 0f)
                return;

            fallingEmitAccumulator += deltaTime;
            while (fallingEmitAccumulator >= 0.035f)
            {
                fallingEmitAccumulator -= 0.035f;
                EmitFallingPellet(pourWorldPosition);
            }

            int bowlIndex = FindBowlBelow(pourWorldPosition);
            if (bowlIndex < 0 || bowlReady[bowlIndex] || assignedDogs[bowlIndex] != null)
                return; // 그릇 밖에서 기울이면 사료는 보이지만 채움 수치에는 들어가지 않는다.

            bowlFill[bowlIndex] = Mathf.Clamp01(bowlFill[bowlIndex] + deltaTime / SecondsToFill);
            int targetPellets = Mathf.FloorToInt(bowlFill[bowlIndex] * FullBowlPelletCount);
            while (visibleBowlPellets[bowlIndex] < targetPellets)
            {
                EmitBowlPellet(bowlIndex);
                visibleBowlPellets[bowlIndex]++;
            }

            if (bowlFill[bowlIndex] < 1f)
                return;

            bowlReady[bowlIndex] = true;
            TryCallDogToFullBowl(bowlIndex); // 각 그릇을 충분히 채운 순간에만 한 마리씩 출발한다.
        }

        public void CompleteFeeding(int bowlIndex, MushLobbyDogRoamer dog)
        {
            if (bowlIndex < 0 || bowlIndex >= BowlCount || assignedDogs[bowlIndex] != dog)
                return;

            assignedDogs[bowlIndex] = null;
            bowlReady[bowlIndex] = false;
            bowlFill[bowlIndex] = 0f;
            visibleBowlPellets[bowlIndex] = 0;
            fallingEmitAccumulator = 0f;
            bowlFoodParticles[bowlIndex].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void TryCallDogToFullBowl(int bowlIndex)
        {
            if (bowlIndex < 0 || bowlIndex >= BowlCount || !bowlReady[bowlIndex] ||
                assignedDogs[bowlIndex] != null || dogs == null)
                return;

            MushLobbyDogRoamer nearestDog = null;
            float nearestDistance = float.PositiveInfinity;
            foreach (MushLobbyDogRoamer dog in dogs)
            {
                if (dog == null || dog.IsFetching || dog.IsFeeding || dog.IsInLapRoutine)
                    continue;
                float distance = (dog.transform.position - eatingWorld[bowlIndex]).sqrMagnitude;
                if (distance >= nearestDistance)
                    continue;
                nearestDistance = distance;
                nearestDog = dog;
            }

            if (nearestDog != null && nearestDog.TryBeginFeeding(
                    this,
                    bowlIndex,
                    eatingWorld[bowlIndex],
                    eatingRotation[bowlIndex]))
                assignedDogs[bowlIndex] = nearestDog;
        }

        private void BuildFeedingPlace()
        {
            Material bowlMaterial = CreateMaterial("Round Feeding Bowl", new Color(0.30f, 0.13f, 0.055f));
            Material rimMaterial = CreateMaterial("Round Feeding Bowl Rim", new Color(0.78f, 0.55f, 0.25f));
            Material canMaterial = CreateMaterial("Dog Food Canister", new Color(0.48f, 0.25f, 0.085f));
            Material lidMaterial = CreateMaterial("Dog Food Canister Lid", new Color(0.88f, 0.60f, 0.20f));
            Material standMaterial = CreateMaterial("Food Canister Stand", new Color(0.20f, 0.10f, 0.045f));
            Material foodMaterial = CreateParticleMaterial();

            for (int bowlIndex = 0; bowlIndex < BowlCount; bowlIndex++)
            {
                float side = bowlIndex == 0 ? -1f : 1f;
                Transform bowl = new GameObject(bowlIndex == 0 ? "Left Dog Food Bowl" : "Right Dog Food Bowl").transform;
                bowl.SetParent(transform, false);
                bowl.localPosition = new Vector3(side * 0.44f, 0f, 0f);
                BuildRoundBowl(bowl, bowlMaterial, rimMaterial);
                bowlWorld[bowlIndex] = bowl.position + Vector3.up * 0.13f;
                // 개는 그릇의 방 안쪽에 서고, 그릇과 플레이어가 있는 방향을 함께 바라본다.
                // 이전처럼 플레이어 앞에 등을 보인 채 먹지 않도록 접근 위치와 회전을 반대로 둔다.
                eatingWorld[bowlIndex] = bowl.position - transform.forward * 0.62f;
                eatingRotation[bowlIndex] = Quaternion.LookRotation(transform.forward, Vector3.up);
                bowlFoodParticles[bowlIndex] = CreateParticleSystem(
                    bowlIndex == 0 ? "Food Stored In Left Bowl" : "Food Stored In Right Bowl",
                    foodMaterial,
                    96);
            }

            // PC에서는 좌우 방향키에 맞춰 낮아진 주둥이가 각각 왼쪽/오른쪽 그릇을 지나가도록
            // 사료통 중심을 두 그릇 사이 바로 위에 둔다.
            desktopHoldWorld = transform.TransformPoint(new Vector3(0f, 0.88f, 0.08f));

            fallingFoodParticles = CreateParticleSystem("Falling Dog Food", foodMaterial, 180);

            CreateCylinder(
                "Canister Stand",
                transform,
                new Vector3(-1.05f, 0.045f, -0.34f),
                new Vector3(0.56f, 0.045f, 0.56f),
                standMaterial,
                false);

            GameObject canister = new GameObject("Dog Food Canister");
            canister.transform.SetParent(transform, false);
            canister.transform.localPosition = new Vector3(-1.05f, 0.39f, -0.34f);
            Renderer canisterRenderer = CreateCylinder(
                "Canister Body",
                canister.transform,
                Vector3.zero,
                new Vector3(0.43f, 0.32f, 0.43f),
                canMaterial,
                false).GetComponent<Renderer>();
            CreateCylinder(
                "Canister Lid",
                canister.transform,
                new Vector3(0f, 0.36f, 0f),
                new Vector3(0.47f, 0.045f, 0.47f),
                lidMaterial,
                false);
            CreateCube(
                "Canister Pour Lip",
                canister.transform,
                new Vector3(-0.25f, 0.28f, 0f),
                new Vector3(0.18f, 0.14f, 0.22f),
                Quaternion.Euler(0f, 0f, 18f),
                lidMaterial);

            // Three kibble dots provide a readable symbol without another large,
            // possibly mirrored world-space label.
            for (int index = 0; index < 3; index++)
            {
                GameObject pellet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pellet.name = "Canister Food Mark " + (index + 1);
                pellet.transform.SetParent(canister.transform, false);
                pellet.transform.localPosition = new Vector3((index - 1) * 0.12f, 0.08f, -0.22f);
                pellet.transform.localScale = Vector3.one * 0.075f;
                pellet.GetComponent<Renderer>().sharedMaterial = lidMaterial;
                Destroy(pellet.GetComponent<Collider>());
            }

            CapsuleCollider selectionCollider = canister.AddComponent<CapsuleCollider>();
            selectionCollider.center = Vector3.zero;
            selectionCollider.radius = 0.26f;
            selectionCollider.height = 0.82f;
            selectionCollider.direction = 1;
            selectionCollider.isTrigger = true;
            Rigidbody body = canister.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            XRGrabInteractable grab = canister.AddComponent<XRGrabInteractable>();
            grab.selectMode = InteractableSelectMode.Single;
            grab.movementType = XRBaseInteractable.MovementType.Kinematic;
            grab.throwOnDetach = false;
            grab.useDynamicAttach = true;
            MushLobbyFeedDispenser dispenser = canister.AddComponent<MushLobbyFeedDispenser>();
            dispenser.Configure(this, canisterRenderer);
        }

        private void BuildRoundBowl(Transform bowl, Material baseMaterial, Material rimMaterial)
        {
            CreateCylinder(
                "Bowl Base",
                bowl,
                new Vector3(0f, 0.055f, 0f),
                new Vector3(0.66f, 0.055f, 0.66f),
                baseMaterial,
                false);

            const int segmentCount = 12;
            const float radius = 0.32f;
            for (int index = 0; index < segmentCount; index++)
            {
                float angle = index * (360f / segmentCount);
                float radians = angle * Mathf.Deg2Rad;
                CreateCube(
                    "Bowl Rim " + (index + 1),
                    bowl,
                    new Vector3(Mathf.Sin(radians) * radius, 0.14f, Mathf.Cos(radians) * radius),
                    new Vector3(0.19f, 0.15f, 0.075f),
                    Quaternion.Euler(0f, angle, 0f),
                    rimMaterial);
            }
        }

        private int FindBowlBelow(Vector3 source)
        {
            int nearestBowl = -1;
            float nearestHorizontalDistance = float.PositiveInfinity;
            for (int bowlIndex = 0; bowlIndex < BowlCount; bowlIndex++)
            {
                Vector3 difference = source - bowlWorld[bowlIndex];
                if (difference.y < 0.10f || difference.y > 1.35f ||
                    Mathf.Abs(difference.x) > 0.28f || Mathf.Abs(difference.z) > 0.29f)
                    continue;

                float horizontalDistance = difference.x * difference.x + difference.z * difference.z;
                if (horizontalDistance >= nearestHorizontalDistance)
                    continue;
                nearestHorizontalDistance = horizontalDistance;
                nearestBowl = bowlIndex;
            }
            return nearestBowl;
        }

        private void EmitFallingPellet(Vector3 source)
        {
            fallingFoodParticles.Play();
            ParticleSystem.EmitParams pellet = new ParticleSystem.EmitParams
            {
                position = source + Random.insideUnitSphere * 0.018f,
                velocity = Vector3.down * Random.Range(0.72f, 0.95f),
                startLifetime = 0.95f,
                startSize = Random.Range(0.025f, 0.042f),
                startColor = Random.value < 0.35f
                    ? new Color(0.68f, 0.39f, 0.13f)
                    : new Color(0.42f, 0.22f, 0.07f),
            };
            fallingFoodParticles.Emit(pellet, 1);
        }

        private void EmitBowlPellet(int bowlIndex)
        {
            bowlFoodParticles[bowlIndex].Play();
            ParticleSystem.EmitParams pellet = new ParticleSystem.EmitParams
            {
                position = bowlWorld[bowlIndex] + new Vector3(
                    Random.Range(-0.23f, 0.23f),
                    Random.Range(-0.012f, 0.025f),
                    Random.Range(-0.18f, 0.18f)),
                velocity = Vector3.zero,
                startLifetime = 60f,
                startSize = Random.Range(0.027f, 0.044f),
                startColor = Random.value < 0.35f
                    ? new Color(0.68f, 0.39f, 0.13f)
                    : new Color(0.42f, 0.22f, 0.07f),
            };
            bowlFoodParticles[bowlIndex].Emit(pellet, 1);
        }

        private ParticleSystem CreateParticleSystem(string objectName, Material material, int maxParticles)
        {
            GameObject particleObject = new GameObject(objectName);
            particleObject.transform.SetParent(transform, false);
            ParticleSystem particles = particleObject.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.startLifetime = 1f;
            main.startSize = 0.038f;
            main.maxParticles = maxParticles;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = false;
            ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            return particles;
        }

        private static GameObject CreateCylinder(
            string objectName,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            bool keepCollider)
        {
            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = objectName;
            cylinder.transform.SetParent(parent, false);
            cylinder.transform.localPosition = localPosition;
            cylinder.transform.localScale = localScale;
            cylinder.GetComponent<Renderer>().sharedMaterial = material;
            if (!keepCollider)
                Destroy(cylinder.GetComponent<Collider>());
            return cylinder;
        }

        private static GameObject CreateCube(
            string objectName,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation,
            Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = objectName;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localRotation = localRotation;
            cube.transform.localScale = localScale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            Destroy(cube.GetComponent<Collider>());
            return cube;
        }

        private Material CreateMaterial(string materialName, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new Material(shader) { name = "Runtime " + materialName };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.18f);
            ownedMaterials.Add(material);
            return material;
        }

        private Material CreateParticleMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                            Shader.Find("Universal Render Pipeline/Unlit") ??
                            Shader.Find("Particles/Standard Unlit");
            Material material = new Material(shader) { name = "Runtime Dog Food Particles" };
            Color color = new Color(0.56f, 0.30f, 0.08f, 1f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            ownedMaterials.Add(material);
            return material;
        }

        private void HideLegacyDogBowl(Transform lobbyRoot)
        {
            foreach (Transform child in lobbyRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!child.name.StartsWith("INT_DogBowl", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (Renderer renderer in child.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = false;
                foreach (Collider collider in child.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;
            }
        }

        private static Transform FindDescendant(Transform root, string exactName)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == exactName)
                    return child;
            }
            return null;
        }

        private void OnDestroy()
        {
            foreach (Material material in ownedMaterials)
            {
                if (material != null)
                    Destroy(material);
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class MushLobbyFeedDispenser : MonoBehaviour
    {
        private const float PourAngle = 48f;
        private static MushLobbyFeedDispenser activeDesktopDispenser;
        private MushLobbyFeedingStation station;
        private XRGrabInteractable interactable;
        private Renderer highlightRenderer;
        private Color restingColor;
        private Transform originalParent;
        private Vector3 originalLocalPosition;
        private Quaternion originalLocalRotation;
        private bool heldInVr;
        private bool heldOnDesktop;
        private float desktopTilt;

        public static bool IsDesktopCanisterHeld =>
            activeDesktopDispenser != null && activeDesktopDispenser.heldOnDesktop;

        public void Configure(MushLobbyFeedingStation newStation, Renderer newHighlightRenderer)
        {
            station = newStation;
            highlightRenderer = newHighlightRenderer;
            if (highlightRenderer != null)
                restingColor = highlightRenderer.material.color;
        }

        private void Awake()
        {
            originalParent = transform.parent;
            originalLocalPosition = transform.localPosition;
            originalLocalRotation = transform.localRotation;
            interactable = GetComponent<XRGrabInteractable>();
            if (interactable == null)
                return;
            interactable.selectEntered.AddListener(OnSelected);
            interactable.selectExited.AddListener(OnSelectExited);
            interactable.hoverEntered.AddListener(OnHoverEntered);
            interactable.hoverExited.AddListener(OnHoverExited);
        }

        private void Update()
        {
            if (XRSettings.isDeviceActive)
            {
                if (heldInVr && CurrentTiltDegrees() >= PourAngle)
                    station?.PourFrom(GetPourWorldPosition(), Time.deltaTime);
                return;
            }

            if (!heldOnDesktop || station == null)
                return;

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            {
                ReturnToStand(); // PC에서는 어디를 보고 있든 우클릭 한 번으로 왼쪽 거치대에 돌려놓는다.
                return;
            }

            Keyboard keyboard = Keyboard.current;
            float tiltInput = 0f;
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.isPressed) tiltInput += 1f;
                if (keyboard.rightArrowKey.isPressed) tiltInput -= 1f;
            }
            float targetTilt = tiltInput * 68f;
            desktopTilt = Mathf.MoveTowards(desktopTilt, targetTilt, Time.deltaTime * 95f);

            Vector3 pointerWorld = station.DesktopHoldWorld;
            if (mouse != null)
                station.TryGetDesktopPointerWorld(mouse.position.ReadValue(), out pointerWorld);
            transform.SetPositionAndRotation(
                pointerWorld,
                station.GetDesktopCanisterRotation(desktopTilt));
            if (Mathf.Abs(desktopTilt) >= PourAngle)
                station.PourFrom(GetPourWorldPosition(), Time.deltaTime);
        }

        public void Trigger()
        {
            if (XRSettings.isDeviceActive)
                return; // VR은 레이/손의 그립 선택과 실제 컨트롤러 기울기를 사용한다.
            if (heldOnDesktop)
                return; // 집은 뒤의 좌클릭은 무시하고, 내려놓기는 사용자가 지정한 우클릭만 사용한다.

            heldOnDesktop = true;
            activeDesktopDispenser = this;
            desktopTilt = 0f;
        }

        private float CurrentTiltDegrees()
        {
            return Mathf.Acos(Mathf.Clamp(Vector3.Dot(transform.up, Vector3.up), -1f, 1f)) * Mathf.Rad2Deg;
        }

        private Vector3 GetPourWorldPosition()
        {
            Vector3 lowerSide = Vector3.Dot(transform.right, Vector3.up) < 0f
                ? transform.right
                : -transform.right;
            return transform.position + transform.up * 0.30f + lowerSide * 0.24f;
        }

        private void OnSelected(SelectEnterEventArgs args)
        {
            heldInVr = XRSettings.isDeviceActive;
            heldOnDesktop = false;
            if (activeDesktopDispenser == this)
                activeDesktopDispenser = null;
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            heldInVr = false;
            ReturnToStand();
        }

        private void ReturnToStand()
        {
            heldOnDesktop = false;
            if (activeDesktopDispenser == this)
                activeDesktopDispenser = null;
            desktopTilt = 0f;
            transform.SetParent(originalParent, false);
            transform.localPosition = originalLocalPosition;
            transform.localRotation = originalLocalRotation;
            Rigidbody body = GetComponent<Rigidbody>();
            if (body == null)
                return;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        private void OnHoverEntered(HoverEnterEventArgs args) => SetHighlighted(true);
        private void OnHoverExited(HoverExitEventArgs args) => SetHighlighted(false);
        private void OnMouseEnter() => SetHighlighted(true);
        private void OnMouseExit() => SetHighlighted(false);

        private void SetHighlighted(bool highlighted)
        {
            if (highlightRenderer == null)
                return;
            highlightRenderer.material.color = highlighted
                ? new Color(0.74f, 0.43f, 0.13f)
                : restingColor;
        }

        private void OnDestroy()
        {
            if (activeDesktopDispenser == this)
                activeDesktopDispenser = null;
            if (interactable == null)
                return;
            interactable.selectEntered.RemoveListener(OnSelected);
            interactable.selectExited.RemoveListener(OnSelectExited);
            interactable.hoverEntered.RemoveListener(OnHoverEntered);
            interactable.hoverExited.RemoveListener(OnHoverExited);
        }
    }
}
