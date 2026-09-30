using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace Mush.Art.Test
{
    /// <summary>
    /// 빈 게임 오브젝트에 추가하면 화면 클릭 위치에 VFX를 재생합니다.
    /// 실행 시 입력을 방해하지 않는 오버레이 캔버스와 재사용 가능한 효과 오브젝트를 자동 생성합니다.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Mush/UI/화면 클릭 VFX")]
    public sealed class ScreenClickVfx : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterForScenes()
        {
            SceneManager.sceneLoaded -= InstallInScene;
            SceneManager.sceneLoaded += InstallInScene;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallInFirstScene()
        {
            InstallInScene(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void InstallInScene(Scene scene, LoadSceneMode mode)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<ScreenClickVfx>(true) != null)
                    return;

            GameObject prefab = Resources.Load<GameObject>("ClickVFX");
            if (prefab == null)
                return;
            GameObject instance = Object.Instantiate(prefab);
            instance.name = "ClickVFX";
            SceneManager.MoveGameObjectToScene(instance, scene);
        }

        private const int ShardCount = 4;
        private const int TrailSegmentCount = 3;

        private static readonly Vector2[] ShardStartPositions =
        {
            new(-14f, 3f),
            new(14f, 5f),
            new(-10f, -4f),
            new(11f, -5f),
        };

        private static readonly Vector2[] ShardVelocities =
        {
            new(-105f, 250f),
            new(108f, 240f),
            new(-72f, 205f),
            new(78f, 198f),
        };

        private static readonly float[] ShardSizes = { 34f, 31f, 26f, 33f };
        private static readonly float[] ShardEndRotations = { 110f, -90f, -140f, 125f };

        [Header("사용 이미지")]
        [Tooltip("중심의 부드러운 빛과 순간 섬광에 사용할 PNG입니다.")]
        [SerializeField] private Texture2D glowTexture;

        [Tooltip("바깥으로 퍼지는 원형 충격파에 사용할 PNG입니다.")]
        [SerializeField] private Texture2D ringTexture;

        [Tooltip("입자 뒤를 따라가는 짧은 꼬리에 사용할 PNG입니다.")]
        [SerializeField] private Texture2D trailTexture;

        [Tooltip("둥근 얼음 입자에 사용할 PNG입니다.")]
        [SerializeField] private Texture2D roundParticleTexture;

        [Tooltip("각진 얼음 입자에 사용할 PNG입니다.")]
        [SerializeField] private Texture2D angularParticleTexture;

        [Tooltip("작은 얼음 입자에 사용할 PNG입니다.")]
        [SerializeField] private Texture2D pebbleParticleTexture;

        [Header("캔버스")]
        [Tooltip("다른 Screen Space Overlay Canvas보다 앞에 표시할 정렬 순서입니다.")]
        [SerializeField] private int sortingOrder = 200;

        [Tooltip("효과 전체 크기 배율입니다.")]
        [SerializeField, Range(0.1f, 2f)] private float overallScale = 0.65f;

        [Tooltip("효과 전체 투명도 배율입니다.")]
        [SerializeField, Range(0f, 1f)] private float overallAlpha = 0.65f;

        [Header("재생 및 오브젝트 풀")]
        [Tooltip("클릭 효과 하나가 재생되는 시간입니다.")]
        [SerializeField, Min(0.05f)] private float duration = 0.54f;

        [Tooltip("동시에 재생할 수 있는 효과 개수입니다.")]
        [SerializeField, Range(1, 8)] private int poolSize = 2;

        [Header("중심 원형 효과")]
        [Tooltip("부드러운 원형 빛의 지름입니다.")]
        [SerializeField, Min(1f)] private float auraDiameter = 300f;

        [Tooltip("원형 충격파의 지름입니다.")]
        [SerializeField, Min(1f)] private float ringDiameter = 270f;

        [Tooltip("클릭 순간 섬광의 지름입니다.")]
        [SerializeField, Min(1f)] private float flashDiameter = 120f;

        [Tooltip("부드러운 원형 빛의 최대 알파입니다.")]
        [SerializeField, Range(0f, 1f)] private float auraMaxAlpha = 0.35f;

        [Tooltip("원형 충격파의 최대 알파입니다.")]
        [SerializeField, Range(0f, 1f)] private float ringMaxAlpha = 0.85f;

        [Tooltip("클릭 순간 섬광의 최대 알파입니다.")]
        [SerializeField, Range(0f, 1f)] private float flashMaxAlpha = 0.55f;

        [Header("주변 입자")]
        [Tooltip("입자가 좌우로 퍼지는 범위입니다.")]
        [SerializeField, Range(0.25f, 2f)] private float horizontalSpread = 1f;

        [Tooltip("입자가 처음 위로 솟는 힘입니다.")]
        [SerializeField, Range(0.25f, 2f)] private float launchHeight = 1f;

        [Tooltip("음수일수록 입자가 더 빠르게 아래로 떨어집니다.")]
        [SerializeField, Range(-1200f, -50f)] private float gravity = -620f;

        [Tooltip("얼음 입자의 크기 배율입니다.")]
        [SerializeField, Range(0.25f, 2f)] private float particleSize = 1f;

        [Tooltip("입자 꼬리의 최대 알파입니다.")]
        [SerializeField, Range(0f, 0.5f)] private float trailMaxAlpha = 0.18f;

        [Tooltip("꼬리 조각 사이의 시간 간격입니다. 값이 클수록 꼬리가 길어집니다.")]
        [SerializeField, Range(0.02f, 0.12f)] private float trailTimeStep = 0.055f;

        private RectTransform canvasRect;
        private ClickVfxInstance[] instances;
        private Sprite[] runtimeSprites;
        private int nextInstance;

        private void Awake()
        {
            if (!AreImagesAssigned())
            {
                Debug.LogError(
                    "[화면 클릭 VFX] '사용 이미지' 항목의 PNG 6개를 모두 연결해야 합니다.",
                    this);
                enabled = false;
                return;
            }

            duration = Mathf.Max(0.05f, duration);
            poolSize = Mathf.Clamp(poolSize, 1, 8);

            GameObject canvasObject = new(
                "ClickVFX Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(CanvasGroup));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;

            canvasRect = (RectTransform)canvasObject.transform;
            CanvasGroup canvasGroup = canvasObject.GetComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            Sprite glow = CreateRuntimeSprite(glowTexture);
            Sprite ring = CreateRuntimeSprite(ringTexture);
            Sprite trail = CreateRuntimeSprite(trailTexture);
            Sprite roundParticle = CreateRuntimeSprite(roundParticleTexture);
            Sprite angularParticle = CreateRuntimeSprite(angularParticleTexture);
            Sprite pebbleParticle = CreateRuntimeSprite(pebbleParticleTexture);
            runtimeSprites = new[]
            {
                glow,
                ring,
                trail,
                roundParticle,
                angularParticle,
                pebbleParticle,
            };

            Sprite[] shards =
            {
                roundParticle,
                angularParticle,
                pebbleParticle,
                roundParticle,
            };

            instances = new ClickVfxInstance[poolSize];
            for (int index = 0; index < poolSize; index++)
                instances[index] = new ClickVfxInstance(this, canvasRect, index, glow, ring, trail, shards);
        }

        private void OnDestroy()
        {
            if (runtimeSprites == null)
                return;

            foreach (Sprite runtimeSprite in runtimeSprites)
            {
                if (runtimeSprite != null)
                    Destroy(runtimeSprite);
            }
        }

        private void Update()
        {
            Pointer pointer = Pointer.current;
            if (pointer != null && pointer.press.wasPressedThisFrame &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, pointer.position.ReadValue(), null, out Vector2 localPoint))
            {
                instances[nextInstance].Play(localPoint, Time.unscaledTime);
                nextInstance = (nextInstance + 1) % instances.Length;
            }

            float now = Time.unscaledTime;
            foreach (ClickVfxInstance instance in instances)
                instance.Tick(now);
        }

        private bool AreImagesAssigned()
        {
            return glowTexture != null &&
                   ringTexture != null &&
                   trailTexture != null &&
                   roundParticleTexture != null &&
                   angularParticleTexture != null &&
                   pebbleParticleTexture != null;
        }

        private static Sprite CreateRuntimeSprite(Texture2D texture)
        {
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
        }

        private sealed class ClickVfxInstance
        {
            private readonly ScreenClickVfx settings;
            private readonly RectTransform root;
            private readonly UnityEngine.UI.Image aura;
            private readonly UnityEngine.UI.Image ring;
            private readonly UnityEngine.UI.Image flash;
            private readonly RectTransform[,] trailRects = new RectTransform[ShardCount, TrailSegmentCount];
            private readonly UnityEngine.UI.Image[,] trailImages =
                new UnityEngine.UI.Image[ShardCount, TrailSegmentCount];
            private readonly RectTransform[] shardRects = new RectTransform[ShardCount];
            private readonly UnityEngine.UI.Image[] shardImages = new UnityEngine.UI.Image[ShardCount];
            private float startedAt;
            private bool playing;

            public ClickVfxInstance(
                ScreenClickVfx settings,
                RectTransform parent,
                int index,
                Sprite glowSprite,
                Sprite ringSprite,
                Sprite streakSprite,
                Sprite[] shardSprites)
            {
                this.settings = settings;
                root = CreateRect(parent, $"ClickVFX_{index}", Vector2.zero, new Vector2(320f, 320f));
                root.localScale = Vector3.one * settings.overallScale;
                CanvasGroup group = root.gameObject.AddComponent<CanvasGroup>();
                group.alpha = settings.overallAlpha;
                group.interactable = false;
                group.blocksRaycasts = false;

                aura = CreateImage(
                    root,
                    "AuraSoft",
                    glowSprite,
                    new Vector2(settings.auraDiameter, settings.auraDiameter));
                aura.color = ColorWithAlpha(new Color32(105, 183, 255, 255), 0f);

                ring = CreateImage(
                    root,
                    "ShockRing",
                    ringSprite,
                    new Vector2(settings.ringDiameter, settings.ringDiameter));
                ring.color = ColorWithAlpha(new Color32(203, 234, 255, 255), 0f);

                flash = CreateImage(
                    root,
                    "CoreFlash",
                    glowSprite,
                    new Vector2(settings.flashDiameter, settings.flashDiameter));
                flash.color = ColorWithAlpha(new Color32(234, 247, 255, 255), 0f);

                for (int shardIndex = 0; shardIndex < ShardCount; shardIndex++)
                {
                    for (int segmentIndex = 0; segmentIndex < TrailSegmentCount; segmentIndex++)
                    {
                        float segmentScale = 1f - segmentIndex * 0.18f;
                        RectTransform trailRect = CreateRect(
                            root,
                            $"Trail_{shardIndex + 1:00}_{segmentIndex + 1:00}",
                            ShardStartPositions[shardIndex],
                            new Vector2(6f * segmentScale, 28f * segmentScale));
                        UnityEngine.UI.Image trailImage =
                            trailRect.gameObject.AddComponent<UnityEngine.UI.Image>();
                        ConfigureImage(trailImage, streakSprite);
                        trailImage.color = ColorWithAlpha(new Color32(145, 205, 255, 255), 0f);
                        trailRects[shardIndex, segmentIndex] = trailRect;
                        trailImages[shardIndex, segmentIndex] = trailImage;
                    }
                }

                for (int shardIndex = 0; shardIndex < ShardCount; shardIndex++)
                {
                    float size = ShardSizes[shardIndex];
                    RectTransform shardRect = CreateRect(
                        root,
                        $"Shard_{shardIndex + 1:00}",
                        ShardStartPositions[shardIndex],
                        new Vector2(size * settings.particleSize, size * settings.particleSize));
                    UnityEngine.UI.Image shardImage = shardRect.gameObject.AddComponent<UnityEngine.UI.Image>();
                    ConfigureImage(shardImage, shardSprites[shardIndex]);
                    shardImage.preserveAspect = true;
                    shardImage.color = ColorWithAlpha(Color.white, 0f);
                    shardRects[shardIndex] = shardRect;
                    shardImages[shardIndex] = shardImage;
                }

                root.gameObject.SetActive(false);
            }

            public void Play(Vector2 anchoredPosition, float now)
            {
                root.anchoredPosition = anchoredPosition;
                root.SetAsLastSibling();
                root.gameObject.SetActive(true);
                startedAt = now;
                playing = true;
                Apply(0f);
            }

            public void Tick(float now)
            {
                if (!playing)
                    return;

                float normalizedTime = Mathf.Clamp01((now - startedAt) / settings.duration);
                Apply(normalizedTime);
                if (normalizedTime < 1f)
                    return;

                playing = false;
                root.gameObject.SetActive(false);
            }

            private void Apply(float time)
            {
                float smooth = time * time * (3f - 2f * time);

                aura.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.72f, 1.18f, smooth);
                aura.color = ColorWithAlpha(new Color32(105, 183, 255, 255),
                    settings.auraMaxAlpha * Envelope(time, 0.10f, 0.38f));

                ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.55f, 1.30f, smooth);
                ring.color = ColorWithAlpha(new Color32(203, 234, 255, 255),
                    settings.ringMaxAlpha * Envelope(time, 0.08f, 0.24f));

                float flashAlpha = time < 0.25f
                    ? settings.flashMaxAlpha * (1f - time / 0.25f)
                    : 0f;
                flash.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.55f, 1.10f, Mathf.Min(1f, time / 0.25f));
                flash.color = ColorWithAlpha(new Color32(234, 247, 255, 255), flashAlpha);

                float shardAlpha = Envelope(time, 0.08f, 0.58f);
                float shardScale = time < 0.24f
                    ? Mathf.Lerp(0.40f, 1f, time / 0.24f)
                    : Mathf.Lerp(1f, 0.82f, (time - 0.24f) / 0.76f);

                for (int index = 0; index < shardRects.Length; index++)
                {
                    RectTransform shardRect = shardRects[index];
                    shardRect.anchoredPosition = EvaluateShardPosition(index, time);
                    shardRect.localEulerAngles = new Vector3(0f, 0f, ShardEndRotations[index] * time);
                    shardRect.localScale = Vector3.one * shardScale;
                    shardImages[index].color = ColorWithAlpha(Color.white, shardAlpha);

                    for (int segmentIndex = 0; segmentIndex < TrailSegmentCount; segmentIndex++)
                    {
                        float delay = (segmentIndex + 1) * settings.trailTimeStep;
                        float trailTime = Mathf.Max(0f, time - delay);
                        float visible = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(delay, delay + 0.07f, time));
                        float segmentFade = 1f - segmentIndex * 0.24f;
                        Vector2 velocity = EvaluateShardVelocity(index, trailTime);

                        RectTransform trailRect = trailRects[index, segmentIndex];
                        trailRect.anchoredPosition = EvaluateShardPosition(index, trailTime);
                        trailRect.localEulerAngles = new Vector3(
                            0f,
                            0f,
                            Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg - 90f);
                        trailImages[index, segmentIndex].color = ColorWithAlpha(
                            new Color32(145, 205, 255, 255),
                            settings.trailMaxAlpha * shardAlpha * visible * segmentFade);
                    }
                }
            }

            private Vector2 EvaluateShardPosition(int index, float time)
            {
                Vector2 start = ShardStartPositions[index];
                start.x *= settings.horizontalSpread;
                Vector2 velocity = ScaledShardVelocity(index);
                Vector2 gravityOffset = Vector2.up * (0.5f * settings.gravity * time * time);
                return start + velocity * time + gravityOffset;
            }

            private Vector2 EvaluateShardVelocity(int index, float time)
            {
                return ScaledShardVelocity(index) + Vector2.up * (settings.gravity * time);
            }

            private Vector2 ScaledShardVelocity(int index)
            {
                Vector2 velocity = ShardVelocities[index];
                velocity.x *= settings.horizontalSpread;
                velocity.y *= settings.launchHeight;
                return velocity;
            }

            private static float Envelope(float time, float riseEnd, float fadeStart)
            {
                if (time <= riseEnd)
                    return Mathf.Clamp01(time / Mathf.Max(0.0001f, riseEnd));
                if (time <= fadeStart)
                    return 1f;
                return 1f - Mathf.InverseLerp(fadeStart, 1f, time);
            }

            private static RectTransform CreateRect(
                Transform parent,
                string objectName,
                Vector2 anchoredPosition,
                Vector2 size)
            {
                GameObject child = new(objectName, typeof(RectTransform));
                RectTransform rect = (RectTransform)child.transform;
                rect.SetParent(parent, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = anchoredPosition;
                rect.sizeDelta = size;
                rect.localScale = Vector3.one;
                return rect;
            }

            private static UnityEngine.UI.Image CreateImage(
                Transform parent,
                string objectName,
                Sprite sprite,
                Vector2 size)
            {
                RectTransform rect = CreateRect(parent, objectName, Vector2.zero, size);
                UnityEngine.UI.Image image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
                ConfigureImage(image, sprite);
                return image;
            }

            private static void ConfigureImage(UnityEngine.UI.Image image, Sprite sprite)
            {
                image.sprite = sprite;
                image.type = UnityEngine.UI.Image.Type.Simple;
                image.raycastTarget = false;
                image.maskable = false;
                image.useSpriteMesh = false;
            }

            private static Color ColorWithAlpha(Color color, float alpha)
            {
                color.a = Mathf.Clamp01(alpha);
                return color;
            }
        }
    }
}
