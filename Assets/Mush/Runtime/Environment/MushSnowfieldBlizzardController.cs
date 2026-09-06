using UnityEngine; // Transform, ParticleSystem, Renderer, MaterialPropertyBlock, RenderSettings 등 런타임 기능을 사용한다.

/// <summary>
/// 기본 설원 V2의 진행률 기반 눈보라를 제어한다.
/// 기존 썰매 조작 코드는 수정하지 않고 실제로 앞으로 움직이는 대상 Transform의 Z 위치만 읽는다.
/// 카메라 흔들림이나 강제 회전은 사용하지 않는다.
/// </summary>
[DisallowMultipleComponent] // 같은 맵에 컨트롤러가 중복 부착되는 것을 막는다.
public sealed class MushSnowfieldBlizzardController : MonoBehaviour // 설원 눈보라 전용 컨트롤러다.
{
    [Header("진행 기준")] // 트랙 진행률 계산에 필요한 항목이다.
    [SerializeField] private Transform progressTarget; // 썰매 루트처럼 실제로 트랙을 따라 움직이는 Transform을 연결한다.
    [SerializeField, Min(1f)] private float trackLengthMeters = 900f; // 설원 V2의 Unity Z 진행 길이다.
    [SerializeField] private Transform blizzardBeginMarker; // 선택적으로 TRG_Blizzard_Begin을 연결한다.
    [SerializeField] private Transform blizzardPeakMarker; // 선택적으로 TRG_Blizzard_Peak을 연결한다.
    [SerializeField] private Transform blizzardEndMarker; // 선택적으로 TRG_Blizzard_End를 연결한다.

    [Header("하늘 / 조명")]
    [SerializeField] private Light directionalLight;
    [SerializeField] private Camera skyCamera;
    [SerializeField] private Material skyMaterial;

    [Header("눈보라 파티클")] // Quest 2 시험용으로 과도한 파티클을 제한한다.
    [SerializeField] private ParticleSystem snowParticles; // 눈 입자 시스템이다.
    [SerializeField, Min(0)] private int maxParticles = 420; // 동시에 존재할 수 있는 눈 입자의 상한이다.
    [SerializeField, Min(0f)] private float maxEmissionRate = 110f; // 최고 눈보라 때 초당 방출량이다.
    [SerializeField, Min(0f)] private float calmEmissionRate = 8f; // 평상시에도 아주 약하게 보이는 눈발이다.

    [Header("Fog")] // 눈보라 시야 제한을 카메라가 아니라 Fog로 처리한다.
    [SerializeField] private Color calmFogColor = new Color(0.78f, 0.86f, 0.92f, 1f); // 평상시 푸른 설원 Fog 색이다.
    [SerializeField] private Color stormFogColor = new Color(0.84f, 0.88f, 0.90f, 1f); // 눈보라 최고 강도의 밝은 회백색 Fog다.
    [SerializeField, Min(1f)] private float calmFogEndDistance = 260f; // 평상시 멀리까지 보이는 선형 Fog 끝 거리다.
    [SerializeField, Min(1f)] private float stormFogEndDistance = 34f; // 최고 눈보라에서도 도로와 가까운 경광봉은 읽히게 남기는 거리다.
    [SerializeField, Min(0f)] private float calmFogStartDistance = 55f; // 평상시 Fog가 시작되는 거리다.
    [SerializeField, Min(0f)] private float stormFogStartDistance = 7f; // 눈보라 최고 강도에서 Fog가 가까이 시작되는 거리다.

    [Header("경광봉")] // Blender FBX의 MUSH_MAT_BeaconGlow 재질을 사용하는 Renderer를 등록한다.
    [SerializeField] private Renderer[] beaconRenderers; // 눈보라 구간의 주황 경광봉 렌더러 배열이다.
    [SerializeField, ColorUsage(true, true)] private Color beaconOffColor = new Color(0.18f, 0.055f, 0.01f, 1f); // 평상시에는 아주 약한 주황색이다.
    [SerializeField, ColorUsage(true, true)] private Color beaconOnColor = new Color(4.0f, 1.1f, 0.05f, 1f); // 눈보라 때 URP/Lit Emission에 넣을 HDR 주황색이다.

    [Header("선택적 바람 소리")] // 없어도 눈보라 기능 전체가 동작한다.
    [SerializeField] private AudioSource windAudio; // 바람 루프 AudioSource다.
    [SerializeField, Range(0f, 1f)] private float maxWindVolume = 0.72f; // 최고 강도에서 사용할 볼륨이다.

    [Header("디버그")] // Inspector에서 현재 상태를 바로 확인할 수 있다.
    [SerializeField, Range(0f, 1f)] private float currentProgress; // 현재 트랙 진행률이다.
    [SerializeField, Range(0f, 1f)] private float currentStormStrength; // 최종 눈보라 강도다.

    private MaterialPropertyBlock propertyBlock; // Unity 네이티브 객체는 MonoBehaviour 필드 초기화 단계에서 만들지 않고 Awake에서 생성한다.
    private int emissionColorId; // URP/Lit Emission 프로퍼티 ID는 Awake에서 계산한다.
    private int baseColorId; // URP/Lit Base Color 프로퍼티 ID도 Awake에서 계산한다.
    private float rideSpeedStrength; // W 가속 중 추가되는 근거리 눈발/바람 강도다.
    private Vector3 lastProgressPosition;
    private float travelledDistance;
    private bool progressInitialized;

    private void Awake() // 첫 프레임 전에 Unity 네이티브 객체와 파티클/Fog/경광봉을 안전한 초기 상태로 만든다.
    {
        propertyBlock = new MaterialPropertyBlock(); // MonoBehaviour 생성자/필드 초기화가 끝난 Awake에서 생성해 CreateImpl 예외를 막는다.
        emissionColorId = Shader.PropertyToID("_EmissionColor"); // URP/Lit Emission 프로퍼티 ID를 Awake에서 한 번만 계산한다.
        baseColorId = Shader.PropertyToID("_BaseColor"); // URP/Lit Base Color 프로퍼티 ID도 Awake에서 한 번만 계산한다.

        AutoFindMarkers(); // Inspector 연결이 비어 있으면 FBX에서 임포트된 TRG 이름으로 자동 탐색한다.
        if (directionalLight == null)
        {
            foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional) { directionalLight = light; break; }
            }
        }
        if (skyCamera == null) skyCamera = Camera.main;

        if (snowParticles != null) // 파티클이 연결된 경우에만 모듈 값을 변경한다.
        {
            ParticleSystem.MainModule main = snowParticles.main; // MainModule은 구조체 래퍼이므로 지역 변수로 받아 설정한다.
            main.maxParticles = maxParticles; // Quest 2 시험에서 파티클 수가 폭증하지 않도록 상한을 건다.
            main.simulationSpace = ParticleSystemSimulationSpace.World; // 썰매가 움직여도 이미 뿌린 눈이 카메라에 붙어 따라오지 않도록 월드 공간으로 시뮬레이션한다.
        }

        RenderSettings.fog = true; // 설원 이벤트에서 항상 사용할 Fog 기능을 켠다.
        RenderSettings.fogMode = FogMode.Linear; // 가시 거리 제어가 직관적인 Linear Fog를 사용한다.

        if (windAudio != null && !windAudio.isPlaying) windAudio.Play(); // 선택적 바람 루프가 연결되어 있으면 시작하되 볼륨은 Update에서 강도에 맞춘다.

        ApplyStorm(0f); // 시작 지점은 평온한 설원 상태로 강제 초기화한다.
    }

    private void Update() // 매 프레임 현재 Z 진행률을 읽어 눈보라 강도를 갱신한다.
    {
        if (progressTarget == null) return; // 진행 대상이 없다면 기존 게임 코드에는 아무 영향도 주지 않고 대기한다.

        if (!progressInitialized)
        {
            lastProgressPosition = progressTarget.position;
            progressInitialized = true;
        }
        float stepDistance = Vector3.Distance(progressTarget.position, lastProgressPosition);
        if (stepDistance < 30f) travelledDistance += stepDistance;
        lastProgressPosition = progressTarget.position;
        currentProgress = Mathf.Clamp01(travelledDistance / trackLengthMeters);
        currentStormStrength = EvaluateStormStrength(currentProgress); // 요구된 32% 시작, 40~58% 최고, 68% 종료 곡선을 계산한다.
        ApplyStorm(currentStormStrength); // Fog, 눈, 경광봉, 바람을 같은 강도로 동기화한다.
    }

    private float EvaluateStormStrength(float progress) // 진행률을 실제 눈보라 강도 0~1로 변환한다.
    {
        if (progress <= 0.32f) return 0f; // 32% 이전에는 평상시다.
        if (progress < 0.40f) return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.32f, 0.40f, progress)); // 32~40%에서 자연스럽게 증가한다.
        if (progress <= 0.58f) return 1f; // 40~58%는 최고 강도를 유지한다.
        if (progress < 0.68f) return Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.58f, 0.68f, progress)); // 58~68%에서 서서히 걷힌다.
        return 0f; // 68% 이후는 완전히 맑아진다.
    }

    private void ApplyStorm(float strength) // 실제 렌더링/오디오 요소에 눈보라 강도를 적용한다.
    {
        if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock(); // 특수 호출 순서에서도 실제 메서드 실행 시점에만 안전하게 생성한다.
        if (emissionColorId == 0) emissionColorId = Shader.PropertyToID("_EmissionColor"); // Awake를 거치지 않은 경우에도 사용 직전에 ID를 보충한다.
        if (baseColorId == 0) baseColorId = Shader.PropertyToID("_BaseColor"); // Base Color ID도 같은 방식으로 보충한다.

        RenderSettings.fogColor = Color.Lerp(calmFogColor, stormFogColor, strength); // Fog 색을 푸른 설원에서 밝은 눈보라 색으로 부드럽게 바꾼다.
        RenderSettings.fogStartDistance = Mathf.Lerp(calmFogStartDistance, stormFogStartDistance, strength); // 강해질수록 Fog 시작점을 플레이어 가까이 이동시킨다.
        RenderSettings.fogEndDistance = Mathf.Lerp(calmFogEndDistance, stormFogEndDistance, strength); // 강해질수록 최대 가시 거리를 줄인다.
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Color.Lerp(
            new Color(0.58f, 0.68f, 0.78f),
            new Color(0.14f, 0.17f, 0.21f),
            strength);

        Color clearSky = new(0.48f, 0.69f, 0.92f);
        Color stormSky = new(0.095f, 0.13f, 0.18f);
        Color currentSky = Color.Lerp(clearSky, stormSky, strength);
        if (directionalLight != null)
        {
            directionalLight.color = Color.Lerp(new Color(1f, 0.96f, 0.86f), new Color(0.48f, 0.55f, 0.65f), strength);
            directionalLight.intensity = Mathf.Lerp(1.16f, 0.24f, strength);
            directionalLight.transform.rotation = Quaternion.Euler(Mathf.Lerp(34f, 18f, strength), -28f, 0f);
        }
        if (skyCamera != null)
        {
            skyCamera.clearFlags = CameraClearFlags.Skybox;
            skyCamera.backgroundColor = currentSky;
        }
        if (skyMaterial != null)
        {
            if (skyMaterial.HasProperty("_SkyTint")) skyMaterial.SetColor("_SkyTint", currentSky);
            if (skyMaterial.HasProperty("_GroundColor")) skyMaterial.SetColor("_GroundColor", Color.Lerp(new Color(0.45f, 0.54f, 0.62f), new Color(0.10f, 0.12f, 0.15f), strength));
            if (skyMaterial.HasProperty("_Exposure")) skyMaterial.SetFloat("_Exposure", Mathf.Lerp(1.25f, 0.56f, strength));
            if (skyMaterial.HasProperty("_AtmosphereThickness")) skyMaterial.SetFloat("_AtmosphereThickness", Mathf.Lerp(0.72f, 1.35f, strength));
        }

        if (snowParticles != null) // 눈 파티클이 실제로 존재할 때만 방출량을 조절한다.
        {
            ParticleSystem.EmissionModule emission = snowParticles.emission; // Emission 모듈을 받아 초당 입자 수를 바꾼다.
            emission.rateOverTime = Mathf.Lerp(calmEmissionRate, maxEmissionRate, strength) + rideSpeedStrength * 52f; // W 가속 시 근거리 눈발을 추가한다.
            ParticleSystem.MainModule main = snowParticles.main;
            main.simulationSpeed = Mathf.Lerp(1f, 1.75f, rideSpeedStrength);
            if (!snowParticles.isPlaying) snowParticles.Play(); // 비활성 상태로 시작했어도 자동으로 재생한다.
        }

        Color beaconColor = Color.Lerp(beaconOffColor, beaconOnColor, strength); // 경광봉의 주황 발광색을 현재 강도로 계산한다.
        if (beaconRenderers != null) // Renderer 배열이 비어 있지 않은 경우 모든 경광봉을 갱신한다.
        {
            foreach (Renderer beacon in beaconRenderers) // 같은 Material Asset을 복제하지 않고 Renderer별 블록만 사용한다.
            {
                if (beacon == null) continue; // 삭제되었거나 비어 있는 슬롯은 안전하게 건너뛴다.
                beacon.GetPropertyBlock(propertyBlock); // 기존 블록 값을 보존하면서 현재 값을 가져온다.
                propertyBlock.SetColor(emissionColorId, beaconColor); // URP/Lit Emission을 주황색 HDR 값으로 설정한다.
                propertyBlock.SetColor(baseColorId, Color.Lerp(new Color(0.18f, 0.055f, 0.01f, 1f), new Color(1f, 0.30f, 0.02f, 1f), strength)); // 발광 외 기본색도 강도에 따라 약간 밝힌다.
                beacon.SetPropertyBlock(propertyBlock); // 최종 값을 Renderer에 적용한다.
            }
        }

        if (windAudio != null) windAudio.volume = Mathf.Max(strength, rideSpeedStrength * 0.42f) * maxWindVolume; // 가속 중에도 약한 바람 소리를 낸다.
    }

    private void AutoFindMarkers() // FBX 이름을 유지한 Transform을 현재 맵 자식에서 자동으로 찾아 Inspector 작업량을 줄인다.
    {
        if (blizzardBeginMarker == null) blizzardBeginMarker = FindDeepChild(transform, "TRG_Blizzard_Begin"); // 눈보라 시작 마커를 찾는다.
        if (blizzardPeakMarker == null) blizzardPeakMarker = FindDeepChild(transform, "TRG_Blizzard_Peak"); // 최고 강도 진입 마커를 찾는다.
        if (blizzardEndMarker == null) blizzardEndMarker = FindDeepChild(transform, "TRG_Blizzard_End"); // 완전 종료 마커를 찾는다.
    }

    private static Transform FindDeepChild(Transform root, string targetName) // Transform.Find가 직접 자식만 찾는 제한을 피하려고 전체 자식을 순회한다.
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) // 비활성 자식까지 포함해 맵 전체 계층을 검사한다.
        {
            if (child.name == targetName) return child; // 이름이 정확히 일치하면 즉시 반환한다.
        }

        return null; // 해당 이름이 없어도 컨트롤러 자체는 진행률 방식으로 계속 동작한다.
    }

    public void SetProgressTarget(Transform target) // 프로토타입 설치기나 기존 게임 코드가 런타임에 썰매 Transform을 지정할 수 있게 한다.
    {
        progressTarget = target; // 기존 썰매 조작 코드는 수정하지 않고 참조만 받는다.
        travelledDistance = 0f;
        progressInitialized = false;
    }

    public void SetRideSpeedStrength(float strength)
    {
        rideSpeedStrength = Mathf.Clamp01(strength);
    }

    public void SetSnowParticles(ParticleSystem particles)
    {
        snowParticles = particles;
        ApplyStorm(currentStormStrength);
    }

    public void ConfigureRuntimeWorld(Light sun, Camera camera, Material runtimeSky, ParticleSystem particles, float courseLength)
    {
        directionalLight = sun;
        skyCamera = camera;
        skyMaterial = runtimeSky;
        if (particles != null) snowParticles = particles;
        trackLengthMeters = Mathf.Max(1f, courseLength);
        ApplyStorm(currentStormStrength);
    }

    public void PreviewProgress(float progress)
    {
        currentProgress = Mathf.Clamp01(progress);
        currentStormStrength = EvaluateStormStrength(currentProgress);
        ApplyStorm(currentStormStrength);
    }
}
