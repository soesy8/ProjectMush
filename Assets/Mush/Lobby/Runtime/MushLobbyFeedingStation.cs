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
        public const int StaminaPerMeal = 40;
        private const int FullBowlPelletCount = 42;
        private const int BowlCount = 2;
        private readonly List<Material> ownedMaterials = new();
        private readonly HashSet<MushLobbyDogRoamer> dogsFedThisRound = new();

        private MushLobbyDogRoamer[] dogs;
        private ParticleSystem fallingFoodParticles;
        private readonly ParticleSystem[] bowlFoodParticles = new ParticleSystem[BowlCount];
        private readonly MushLobbyBowlFoodVisual[] bowlFoodVisuals = new MushLobbyBowlFoodVisual[BowlCount];
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
        private readonly float[] bowlRadius = new float[BowlCount];
        private readonly float[] mealProgress = new float[BowlCount];
        private readonly float[] nextCrunchTime = new float[BowlCount];
        private readonly ParticleSystem.Particle[] storedPellets = new ParticleSystem.Particle[FullBowlPelletCount];
        private readonly AudioSource[] bowlAudio = new AudioSource[BowlCount];
        private MushSoundBank soundBank;
        private float nextImpactTime;

        public float GetBowlFill(int index) => index >= 0 && index < BowlCount ? bowlFill[index] : 0f;
        public bool IsBowlReady(int index) => index >= 0 && index < BowlCount && bowlReady[index];
        public Vector3 GetBowlPosition(int index) => bowlWorld[Mathf.Clamp(index, 0, BowlCount - 1)];
        public MushLobbyDogRoamer GetAssignedDog(int index) => index >= 0 && index < BowlCount ? assignedDogs[index] : null;

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
            station.PrepareArtBowls(lobbyRoot);
            station.CacheExistingFeedingPlace();
            if (station.fallingFoodParticles == null)
                station.BuildFeedingPlace();
            station.PrepareFoodVisualsAndAudio();
            return station;
        }

        public bool TryGetDesktopPointerWorld(Vector2 screenPosition, out Vector3 worldPosition)
        {
            worldPosition = desktopHoldWorld;
            if (desktopCamera == null)
                desktopCamera = Camera.main;
            if (desktopCamera == null)
                return false;

            // 사료통은 급식대에 묶이지 않고 플레이어의 이동과 시점을 따라 손앞에 유지됩니다.
            Ray pointerRay = MushDesktopSeatedLook.GetDesktopPointerRay(desktopCamera, screenPosition);
            worldPosition = pointerRay.GetPoint(0.95f);
            return true;
        }

        public Quaternion GetDesktopCanisterRotation(float tiltDegrees)
        {
            Vector3 forward = desktopCamera != null
                ? Vector3.ProjectOnPlane(desktopCamera.transform.forward, Vector3.up).normalized
                : transform.forward;
            if (forward.sqrMagnitude < 0.0001f)
                forward = transform.forward;
            return Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, 0f, tiltDegrees);
        }

        private void Update()
        {
            for (int index = 0; index < BowlCount; index++)
            {
                MushLobbyDogRoamer dog = assignedDogs[index];
                if (!ReferenceEquals(dog, null) && (dog == null || !dog.isActiveAndEnabled || !dog.IsFeeding))
                    CancelFeeding(index, dog);
                if (bowlReady[index] && assignedDogs[index] == null)
                    TryCallDogToFullBowl(index);
            }
        }

        public void PourFrom(Vector3 source, float deltaTime)
        {
            if (!isActiveAndEnabled || !float.IsFinite(deltaTime) || deltaTime <= 0f ||
                !float.IsFinite(source.x) || !float.IsFinite(source.y) || !float.IsFinite(source.z))
                return;
            // Bound work after a stalled frame: no unbounded emission loop on Quest.
            deltaTime = Mathf.Min(deltaTime, 0.25f);
            int index = FindBowlBelow(source);
            fallingEmitAccumulator += deltaTime;
            while (fallingEmitAccumulator >= 0.025f)
            {
                fallingEmitAccumulator -= 0.025f;
                EmitFallingPellet(source);
            }
            if (index < 0)
                return;
            if (Time.time >= nextImpactTime)
            {
                nextImpactTime = Time.time + 0.18f;
                PlayBowlClip(index, soundBank != null ? soundBank.kibbleBowlImpact : null, 0.18f);
            }
            BeginFeedingRoundIfNeeded();
            if (bowlReady[index] || assignedDogs[index] != null)
                return;
            SetBowlFill(index, Mathf.Clamp01(bowlFill[index] + deltaTime / SecondsToFill));
            if (bowlFill[index] < 1f)
                return;
            bowlReady[index] = true;
            TryCallDogToFullBowl(index);
        }

        public bool OwnsReservation(int index, MushLobbyDogRoamer dog) =>
            isActiveAndEnabled && index >= 0 && index < BowlCount && dog != null &&
            dog.isActiveAndEnabled && assignedDogs[index] == dog;

        public bool UpdateMealProgress(int index, MushLobbyDogRoamer dog, float progress)
        {
            if (!OwnsReservation(index, dog) || !float.IsFinite(progress))
                return false;
            mealProgress[index] = Mathf.Max(mealProgress[index], Mathf.Clamp01(progress));
            SetBowlFill(index, 1f - mealProgress[index]);
            if (mealProgress[index] < 1f && Time.time >= nextCrunchTime[index])
            {
                nextCrunchTime[index] = Time.time + 0.85f;
                AudioClip[] clips = soundBank != null ? soundBank.dogEatCrunches : null;
                if (clips != null && clips.Length > 0)
                    PlayBowlClip(index, clips[Random.Range(0, clips.Length)], 0.20f);
            }
            return true;
        }

        public bool CompleteFeeding(int index, MushLobbyDogRoamer dog)
        {
            if (!OwnsReservation(index, dog) || mealProgress[index] < 1f)
                return false;
            // Release the consumed serving BEFORE the shared save call or callbacks.
            assignedDogs[index] = null;
            bowlReady[index] = false;
            mealProgress[index] = 0f;
            SetBowlFill(index, 0f);
            dogsFedThisRound.Add(dog);
            MushGameSave.RestoreDogStamina(dog.TeamIndex, StaminaPerMeal);
            return true;
        }

        public void CancelFeeding(int index, MushLobbyDogRoamer dog)
        {
            if (index < 0 || index >= BowlCount || !ReferenceEquals(assignedDogs[index], dog))
                return;
            assignedDogs[index] = null;
            mealProgress[index] = 0f;
            // An interrupted meal retains the actual remaining food; top it up before recall.
            bowlReady[index] = bowlFill[index] >= 1f;
            if (bowlAudio[index] != null) bowlAudio[index].Stop();
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
                if (dog == null || !dog.isActiveAndEnabled || dogsFedThisRound.Contains(dog) || dog.IsFetching || dog.IsFeeding || dog.IsInLapRoutine)
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
            {
                assignedDogs[bowlIndex] = nearestDog;
                mealProgress[bowlIndex] = 0f;
                nextCrunchTime[bowlIndex] = 0f;
            }
        }

        private void BeginFeedingRoundIfNeeded()
        {
            for (int bowlIndex = 0; bowlIndex < BowlCount; bowlIndex++)
            {
                if (bowlFill[bowlIndex] > 0f || bowlReady[bowlIndex] || assignedDogs[bowlIndex] != null)
                    return;
            }

            int availableDogCount = 0;
            if (dogs != null)
            {
                foreach (MushLobbyDogRoamer dog in dogs)
                {
                    if (dog != null && dog.isActiveAndEnabled)
                        availableDogCount++;
                }
            }
            if (availableDogCount == 0 || dogsFedThisRound.Count >= availableDogCount)
                dogsFedThisRound.Clear();
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
                string bowlName = bowlIndex == 0 ? "Left Dog Food Bowl" : "Right Dog Food Bowl";
                Transform bowl = FindDescendant(transform, bowlName);
                if (bowl == null)
                {
                    bowl = new GameObject(bowlName).transform;
                    bowl.SetParent(transform, false);
                    bowl.localPosition = new Vector3(side * 0.44f, 0f, 0f);
                    BuildRoundBowl(bowl, bowlMaterial, rimMaterial);
                }
                CacheBowlGeometry(bowlIndex, bowl);
                // 개는 그릇의 방 안쪽에 서고, 그릇과 플레이어가 있는 방향을 함께 바라본다.
                // 이전처럼 플레이어 앞에 등을 보인 채 먹지 않도록 접근 위치와 회전을 반대로 둔다.
                eatingWorld[bowlIndex] = bowl.position + transform.forward * 0.62f;
                eatingRotation[bowlIndex] = Quaternion.LookRotation(-transform.forward, Vector3.up);
                bowlFoodParticles[bowlIndex] = CreateParticleSystem(
                    bowlIndex == 0 ? "Food Stored In Left Bowl" : "Food Stored In Right Bowl",
                    foodMaterial,
                    96);
            }

            // PC에서는 마우스 휠로 기울여 낮아진 주둥이가 각각 왼쪽/오른쪽 그릇을 지나가도록
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

        private void PrepareArtBowls(Transform lobbyRoot)
        {
            if (FindDescendant(transform, "Left Dog Food Bowl") != null)
                return;

            Transform original = FindDescendant(lobbyRoot, "DogFoodBowl_Unity_MaterialSeparated");
            if (original == null || original.IsChildOf(transform))
                return;

            Transform duplicate = Instantiate(original.gameObject, original.parent).transform;
            duplicate.name = "Right Dog Food Bowl";
            float spacing = 0.72f;
            Renderer originalRenderer = original.GetComponentInChildren<Renderer>(true);
            if (originalRenderer != null)
                spacing = Mathf.Max(spacing, originalRenderer.bounds.size.x * 1.25f);
            duplicate.position = original.position + original.right * spacing;
            TintBowl(duplicate, new Color(0.30f, 0.55f, 0.92f));

            original.name = "Left Dog Food Bowl";
            Vector3 midpoint = (original.position + duplicate.position) * 0.5f;
            transform.position = new Vector3(midpoint.x, Mathf.Min(original.position.y, duplicate.position.y), midpoint.z);
            original.SetParent(transform, true);
            duplicate.SetParent(transform, true);
        }

        private void TintBowl(Transform bowl, Color tint)
        {
            foreach (Renderer renderer in bowl.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                if (materials.Length == 0 || materials[0] == null)
                    continue;
                Material copy = new(materials[0]);
                copy.name = materials[0].name + " - Blue Bowl";
                copy.color = Color.Lerp(copy.color, tint, 0.62f);
                ownedMaterials.Add(copy);
                materials[0] = copy;
                renderer.sharedMaterials = materials;
            }
        }

        private void CacheExistingFeedingPlace()
        {
            // Saved scene objects need their runtime station reference restored too.
            foreach (MushLobbyFeedDispenser dispenser in GetComponentsInChildren<MushLobbyFeedDispenser>(true))
            {
                Renderer bodyRenderer = FindDescendant(dispenser.transform, "Canister Body")?.GetComponent<Renderer>();
                dispenser.Configure(this, bodyRenderer);
            }

            if (fallingFoodParticles == null)
                fallingFoodParticles = FindDescendant(transform, "Falling Dog Food")?.GetComponent<ParticleSystem>();

            for (int bowlIndex = 0; bowlIndex < BowlCount; bowlIndex++)
            {
                string bowlName = bowlIndex == 0 ? "Left Dog Food Bowl" : "Right Dog Food Bowl";
                Transform bowl = FindDescendant(transform, bowlName);
                if (bowl == null)
                    continue;

                CacheBowlGeometry(bowlIndex, bowl);
                eatingWorld[bowlIndex] = bowl.position + transform.forward * 0.62f;
                eatingRotation[bowlIndex] = Quaternion.LookRotation(-transform.forward, Vector3.up);
                string foodName = bowlIndex == 0 ? "Food Stored In Left Bowl" : "Food Stored In Right Bowl";
                bowlFoodParticles[bowlIndex] = FindDescendant(transform, foodName)?.GetComponent<ParticleSystem>();
            }

            desktopHoldWorld = transform.TransformPoint(new Vector3(0f, 0.88f, 0.08f));
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

        private void CacheBowlGeometry(int index, Transform bowl)
        {
            Renderer visible = null;
            foreach (Renderer renderer in bowl.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled && renderer.gameObject.activeInHierarchy && renderer is not ParticleSystemRenderer &&
                    renderer.GetComponent<MushLobbyBowlFoodVisual>() == null)
                    visible = renderer;
            if (visible != null)
            {
                Bounds bounds = visible.bounds;
                bowlWorld[index] = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.62f, bounds.center.z);
                bowlRadius[index] = Mathf.Max(0.05f, Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.74f);
            }
            else
            {
                bowlWorld[index] = bowl.position + Vector3.up * 0.13f;
                bowlRadius[index] = 0.23f;
            }
        }

        private int FindBowlBelow(Vector3 source)
        {
            int nearest = -1;
            float nearestDistance = float.PositiveInfinity;
            for (int index = 0; index < BowlCount; index++)
            {
                Vector3 difference = source - bowlWorld[index];
                float distance = difference.x * difference.x + difference.z * difference.z;
                if (difference.y < 0.025f || difference.y > 1.55f || distance > bowlRadius[index] * bowlRadius[index] || distance >= nearestDistance)
                    continue;
                nearestDistance = distance;
                nearest = index;
            }
            return nearest;
        }

        private void EmitFallingPellet(Vector3 source)
        {
            if (fallingFoodParticles == null) return;
            int index = FindBowlBelow(source);
            float landingY = index >= 0 ? bowlWorld[index].y : Mathf.Min(bowlWorld[0].y, bowlWorld[1].y) - 0.12f;
            float height = Mathf.Max(0.02f, source.y - landingY);
            float speed = 0.72f;
            // Gravity-driven lifetime ends at the bowl surface (or the floor for a miss).
            float lifetime = (Mathf.Sqrt(speed * speed + 2f * 9.81f * height) - speed) / 9.81f;
            fallingFoodParticles.Emit(new ParticleSystem.EmitParams {
                position = source + Random.insideUnitSphere * 0.01f,
                velocity = Vector3.down * speed,
                startLifetime = Mathf.Clamp(lifetime, 0.05f, 0.8f),
                startSize = Random.Range(0.027f, 0.040f),
                startColor = new Color(0.55f, 0.29f, 0.10f),
                rotation = Random.Range(0f, 360f)
            }, 1);
        }

        private void SetBowlFill(int index, float fill)
        {
            bowlFill[index] = Mathf.Clamp01(fill);
            if (bowlFoodVisuals[index] != null)
            {
                bowlFoodVisuals[index].SetFill(bowlFill[index]);
                return;
            }
            int count = Mathf.CeilToInt(fill * FullBowlPelletCount);
            if (count == visibleBowlPellets[index]) return;
            visibleBowlPellets[index] = count;
            ParticleSystem particles = bowlFoodParticles[index];
            if (particles == null) return;
            for (int pellet = 0; pellet < count; pellet++)
            {
                float angle = pellet * 2.399963f;
                float radius = Mathf.Sqrt((pellet + 0.5f) / FullBowlPelletCount) * bowlRadius[index] * 0.87f;
                storedPellets[pellet] = new ParticleSystem.Particle {
                    position = bowlWorld[index] + new Vector3(Mathf.Cos(angle) * radius, (pellet % 3) * 0.006f, Mathf.Sin(angle) * radius),
                    velocity = Vector3.zero,
                    startLifetime = float.MaxValue,
                    remainingLifetime = float.MaxValue,
                    startSize = 0.043f + (pellet % 4) * 0.003f,
                    startColor = pellet % 3 == 0 ? new Color(0.68f, 0.39f, 0.13f) : new Color(0.42f, 0.22f, 0.07f),
                    rotation = pellet * 47f,
                    randomSeed = (uint)(pellet + 1)
                };
            }
            if (count > 0 && !particles.isPlaying) particles.Play();
            particles.SetParticles(storedPellets, count);
        }

        private void PrepareFoodVisualsAndAudio()
        {
            PrepareBowlFoodVisuals();
            Mesh kibble = Resources.Load<Mesh>("Feeding/KibblePellet");
            for (int index = -1; index < BowlCount; index++)
            {
                ParticleSystem particles = index < 0 ? fallingFoodParticles : bowlFoodParticles[index];
                if (particles == null) continue;
                if (index >= 0 && bowlFoodVisuals[index] != null)
                {
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    particles.GetComponent<ParticleSystemRenderer>().enabled = false;
                    continue;
                }
                var main = particles.main;
                main.maxParticles = index < 0 ? 64 : FullBowlPelletCount;
                main.gravityModifier = index < 0 ? 1f : 0f;
                main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                var collision = particles.collision;
                collision.enabled = false;
                var renderer = particles.GetComponent<ParticleSystemRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                if (kibble != null) { renderer.renderMode = ParticleSystemRenderMode.Mesh; renderer.mesh = kibble; }
                Material kibbleMaterial = Resources.Load<Material>("Feeding/Kibble");
                if (kibbleMaterial != null) renderer.sharedMaterial = kibbleMaterial;
            }
            soundBank = MushSoundBank.Load();
            for (int index = 0; index < BowlCount; index++)
            {
                if (bowlAudio[index] == null)
                    bowlAudio[index] = CreateCareAudio("Bowl " + index + " SFX", bowlWorld[index]);
            }
        }

        private void PrepareBowlFoodVisuals()
        {
            Mesh foodMesh = Resources.Load<Mesh>("Feeding/BowlFood");
            Material foodMaterial = Resources.Load<Material>("Feeding/BowlFood");
            if (foodMesh == null || foodMaterial == null)
                return;

            for (int index = 0; index < BowlCount; index++)
            {
                if (bowlFoodVisuals[index] == null)
                {
                    Transform bowl = FindDescendant(transform, index == 0 ? "Left Dog Food Bowl" : "Right Dog Food Bowl");
                    if (bowl == null)
                        continue;
                    foreach (MeshFilter model in bowl.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (model.sharedMesh == null || model.sharedMesh.name != "06_DogBowl" ||
                            !model.gameObject.activeInHierarchy || !model.GetComponent<Renderer>().enabled)
                            continue;

                        Transform visualRoot = model.transform.parent;
                        MushLobbyBowlFoodVisual visual = visualRoot.GetComponentInChildren<MushLobbyBowlFoodVisual>(true);
                        if (visual == null)
                        {
                            GameObject food = new("Mush Bowl Food");
                            food.transform.SetParent(visualRoot, false);
                            visual = food.AddComponent<MushLobbyBowlFoodVisual>();
                        }
                        visual.Configure(foodMesh, foodMaterial, model.transform.localPosition);
                        bowlFoodVisuals[index] = visual;
                        break;
                    }
                }
                if (bowlFoodVisuals[index] != null)
                    bowlFoodVisuals[index].SetFill(bowlFill[index]);
            }
        }

        private AudioSource CreateCareAudio(string objectName, Vector3 position)
        {
            GameObject root = new(objectName);
            root.transform.SetParent(transform, false);
            root.transform.position = position;
            AudioSource source = root.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.minDistance = 0.7f;
            source.maxDistance = 8f;
            source.dopplerLevel = 0f;
            root.AddComponent<MushAudioChannel>();
            return source;
        }

        private void PlayBowlClip(int index, AudioClip clip, float volume)
        {
            if (clip == null || bowlAudio[index] == null) return;
            bowlAudio[index].pitch = Random.Range(0.92f, 1.05f);
            bowlAudio[index].PlayOneShot(clip, volume);
        }

        private void OnDisable()
        {
            fallingEmitAccumulator = 0f;
            if (fallingFoodParticles != null) fallingFoodParticles.Clear();
            for (int index = 0; index < BowlCount; index++)
            {
                MushLobbyDogRoamer dog = assignedDogs[index];
                if (dog != null) dog.CancelFeeding();
                assignedDogs[index] = null;
                mealProgress[index] = 0f;
                bowlReady[index] = bowlFill[index] >= 1f;
                if (bowlAudio[index] != null) bowlAudio[index].Stop();
            }
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
            if (!Application.isPlaying)
                return;
            foreach (Material material in ownedMaterials)
            {
                if (material != null)
                    Destroy(material);
            }
        }
    }

}
