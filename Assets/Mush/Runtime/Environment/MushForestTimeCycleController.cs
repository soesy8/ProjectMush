using UnityEngine; // Light, RenderSettings, MaterialPropertyBlock, Mathf 등 시간 연출에 필요한 Unity 런타임 API를 사용한다.
using UnityEngine.Rendering; // AmbientMode.Flat을 사용한다.

/// <summary>
/// 나무 숲 V2의 시간대를 실제 시계가 아니라 트랙 진행률로 보간한다.
/// 0% 낮 → 20% 늦은 오후 → 32% 황혼 → 45% 밤/별 → 62% 여명 → 75% 일출 → 90% 낮.
/// </summary>
[DisallowMultipleComponent] // 같은 맵에 두 개가 붙어서 서로 다른 하늘 값을 쓰는 것을 막는다.
public sealed class MushForestTimeCycleController : MonoBehaviour // 숲 시간 변화 전용 런타임 컨트롤러다.
{
    [Header("진행 기준")] // 트랙 진행도를 계산할 대상과 길이다.
    [SerializeField] private Transform progressTarget; // 썰매 루트처럼 실제 이동하는 Transform을 연결한다.
    [SerializeField, Min(1f)] private float trackLengthMeters = 900f; // V2 숲의 Unity Z 진행 길이다.

    [Header("조명 / 하늘")] // 시간대에 따라 바꿀 Scene 조명 요소다.
    [SerializeField] private Light directionalLight; // 태양 역할을 하는 Directional Light다.
    [SerializeField] private Camera skyCamera; // Skybox 재질을 쓰지 않을 때 Background Color를 직접 바꿀 카메라다.
    [SerializeField] private Material skyMaterial; // 선택적 하늘 Material이며 지원하는 Color 프로퍼티가 있으면 자동으로 색을 설정한다.
    [SerializeField] private Renderer starDomeRenderer; // Blender의 단일 결합 메시 FX_StarDome Renderer다.

    [Header("디버그")] // 현재 진행률과 별 강도를 Inspector에서 확인한다.
    [SerializeField, Range(0f, 1f)] private float currentProgress; // 현재 트랙 진행률이다.
    [SerializeField, Range(0f, 1f)] private float currentStarVisibility; // 현재 별의 발광 강도다.

    private MaterialPropertyBlock starBlock; // Unity 네이티브 객체인 MaterialPropertyBlock은 필드 초기화 단계가 아니라 Awake에서 생성한다.
    private int emissionColorId; // URP/Lit Emission 프로퍼티 ID는 Awake에서 계산한다.
    private int baseColorId; // URP/Lit Base Color 프로퍼티 ID도 Awake에서 계산한다.
    private Vector3 lastProgressPosition;
    private float travelledDistance;
    private bool progressInitialized;

    private readonly TimeKey[] keys = // 사용자가 지정한 진행률 시간대를 실제 색/조명 값으로 정의한다.
    {
        new TimeKey(0.00f, new Color(0.56f,0.74f,0.92f), new Color(1.00f,0.96f,0.84f), 1.05f, new Color(0.47f,0.56f,0.62f), new Color(0.70f,0.78f,0.83f), new Vector3(34f,-28f,0f), 0f),
        new TimeKey(0.20f, new Color(0.82f,0.58f,0.38f), new Color(1.00f,0.72f,0.43f), 0.88f, new Color(0.38f,0.39f,0.42f), new Color(0.58f,0.56f,0.54f), new Vector3(20f,-48f,0f), 0f),
        new TimeKey(0.32f, new Color(0.42f,0.22f,0.38f), new Color(1.00f,0.46f,0.24f), 0.62f, new Color(0.22f,0.20f,0.27f), new Color(0.30f,0.28f,0.36f), new Vector3(8f,-70f,0f), 0.20f),
        new TimeKey(0.45f, new Color(0.018f,0.035f,0.115f), new Color(0.25f,0.34f,0.52f), 0.20f, new Color(0.055f,0.075f,0.13f), new Color(0.08f,0.10f,0.16f), new Vector3(-18f,-98f,0f), 1.00f),
        new TimeKey(0.62f, new Color(0.22f,0.27f,0.40f), new Color(0.58f,0.62f,0.76f), 0.34f, new Color(0.15f,0.18f,0.25f), new Color(0.25f,0.28f,0.34f), new Vector3(-5f,20f,0f), 0.25f),
        // At sunrise the procedural skybox sun sits just above the road horizon,
        // so the player sees an actual rising sun instead of only a pink tint.
        new TimeKey(0.75f, new Color(0.92f,0.48f,0.23f), new Color(1.00f,0.52f,0.22f), 0.82f, new Color(0.40f,0.30f,0.26f), new Color(0.52f,0.43f,0.39f), new Vector3(4f,0f,0f), 0f),
        new TimeKey(0.90f, new Color(0.58f,0.76f,0.94f), new Color(1.00f,0.96f,0.84f), 1.05f, new Color(0.48f,0.56f,0.61f), new Color(0.70f,0.78f,0.83f), new Vector3(28f,18f,0f), 0f),
        new TimeKey(1.00f, new Color(0.58f,0.76f,0.94f), new Color(1.00f,0.96f,0.84f), 1.05f, new Color(0.48f,0.56f,0.61f), new Color(0.70f,0.78f,0.83f), new Vector3(38f,32f,0f), 0f),
    };

    private void Awake() // 시작 시 Unity 네이티브 객체를 만든 뒤 별돔과 Directional Light를 자동으로 보충한다.
    {
        starBlock = new MaterialPropertyBlock(); // MonoBehaviour 생성자/필드 초기화 이후 Awake에서 생성하여 CreateImpl 예외를 막는다.
        emissionColorId = Shader.PropertyToID("_EmissionColor"); // URP/Lit Emission 프로퍼티 ID를 Awake에서 한 번만 캐시한다.
        baseColorId = Shader.PropertyToID("_BaseColor"); // URP/Lit Base Color 프로퍼티 ID도 Awake에서 한 번만 캐시한다.
        if (directionalLight == null) // Inspector가 비어 있을 때 Point/Spot Light를 태양으로 잘못 잡지 않도록 Directional만 찾는다.
        {
            foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) // 현재 로드된 Light를 한 번만 검색한다.
            {
                if (light.type == LightType.Directional) { directionalLight = light; break; } // 실제 Directional Light만 태양으로 채택한다.
            }
        }
        if (skyCamera == null) skyCamera = Camera.main; // 메인 카메라가 있으면 하늘색 fallback에 사용한다.
        if (starDomeRenderer == null) // 별돔을 직접 연결하지 않았다면 이름으로 찾는다.
        {
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true)) // 맵 자식 전체 Renderer를 확인한다.
            {
                if (renderer.name.Contains("StarDome")) { starDomeRenderer = renderer; break; } // 결합 별돔 하나만 연결한다.
            }
        }

        RenderSettings.fog = true; // 숲의 시간대 Fog 색을 사용하기 위해 켠다.
        RenderSettings.fogMode = FogMode.Linear; // 거리 기반 Fog를 사용한다.
        RenderSettings.fogStartDistance = 35f; // 가까운 개와 도로는 항상 또렷하게 보이도록 시작 거리를 둔다.
        RenderSettings.fogEndDistance = 240f; // 숲의 원경이 자연스럽게 사라지는 기본 거리다.
        RenderSettings.ambientMode = AmbientMode.Flat; // 스크립트에서 ambientLight 한 값으로 안정적으로 보간하기 위해 Flat 모드를 쓴다.
    }

    private void Update() // 매 프레임 진행률에 맞는 두 키를 찾아 연속 보간한다.
    {
        if (progressTarget == null) return; // 썰매 참조가 없으면 기존 시스템을 건드리지 않고 대기한다.

        if (!progressInitialized)
        {
            lastProgressPosition = progressTarget.position;
            progressInitialized = true;
        }
        float stepDistance = Vector3.Distance(progressTarget.position, lastProgressPosition);
        if (stepDistance < 30f) travelledDistance += stepDistance;
        lastProgressPosition = progressTarget.position;
        currentProgress = Mathf.Clamp01(travelledDistance / trackLengthMeters);
        ApplyTimeProgress(currentProgress);
    }

    private void ApplyTimeProgress(float progress)
    {
        EvaluateKeys(progress, out TimeKey a, out TimeKey b, out float t); // 현재 진행률 양옆의 시간 키와 보간값을 구한다.

        Color sky = Color.Lerp(a.skyColor, b.skyColor, t); // 하늘색을 갑자기 바꾸지 않고 연속 보간한다.
        Color sun = Color.Lerp(a.sunColor, b.sunColor, t); // 태양광 색도 시간대에 맞춰 보간한다.
        float intensity = Mathf.Lerp(a.sunIntensity, b.sunIntensity, t); // 태양 밝기를 밤에 낮췄다가 일출에서 다시 올린다.
        Color ambient = Color.Lerp(a.ambientColor, b.ambientColor, t); // Ambient Light 색을 보간한다.
        Color fog = Color.Lerp(a.fogColor, b.fogColor, t); // Fog 색을 하늘과 맞춰 보간한다.
        Vector3 euler = Vector3.Lerp(a.sunEuler, b.sunEuler, t); // 태양 방향을 부드럽게 회전시킨다.
        currentStarVisibility = Mathf.Lerp(a.starVisibility, b.starVisibility, t); // 별은 밤에만 1에 가까워지고 여명 때 사라진다.

        if (directionalLight != null) // 태양 Light가 연결된 경우 시간대 값을 적용한다.
        {
            directionalLight.color = sun; // 시간대별 태양색을 적용한다.
            directionalLight.intensity = intensity; // 시간대별 태양 밝기를 적용한다.
            directionalLight.transform.rotation = Quaternion.Euler(euler); // 강제 카메라 회전이 아니라 빛의 각도만 바꾼다.
        }

        RenderSettings.ambientLight = ambient; // Scene Ambient를 현재 시간대 색으로 설정한다.
        RenderSettings.fogColor = fog; // Scene Fog를 현재 시간대 색으로 설정한다.

        ApplySkyColor(sky); // Skybox 또는 Camera Background에 현재 하늘색을 적용한다.
        ApplyStars(currentStarVisibility); // 결합 별돔의 발광만 조절한다.
    }

    private void EvaluateKeys(float progress, out TimeKey a, out TimeKey b, out float t) // 현재 진행률을 감싸는 시간 키 두 개를 찾는다.
    {
        for (int i = 0; i < keys.Length - 1; i++) // 앞에서부터 인접한 키 구간을 확인한다.
        {
            if (progress <= keys[i + 1].progress) // 현재 진행률이 다음 키보다 작거나 같으면 이 구간이다.
            {
                a = keys[i]; // 앞 키를 저장한다.
                b = keys[i + 1]; // 뒤 키를 저장한다.
                t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a.progress, b.progress, progress)); // 구간 내부를 부드러운 S 곡선으로 보간한다.
                return; // 알맞은 구간을 찾았으므로 끝낸다.
            }
        }

        a = keys[^2]; // 100%를 넘는 특수 상황에서는 마지막 두 키를 사용한다.
        b = keys[^1]; // 마지막 키를 목표값으로 사용한다.
        t = 1f; // 완전한 마지막 시간대 값을 적용한다.
    }

    private void ApplySkyColor(Color color) // 여러 종류의 하늘 Material에서도 가능한 색 프로퍼티를 찾아 적용한다.
    {
        if (skyMaterial != null) // 별도로 지정한 하늘 Material이 있을 경우 우선 사용한다.
        {
            if (skyMaterial.HasColor("_SkyTint")) skyMaterial.SetColor("_SkyTint", color); // Procedural Skybox 계열의 색 프로퍼티를 지원하면 설정한다.
            else if (skyMaterial.HasColor("_Tint")) skyMaterial.SetColor("_Tint", color); // 다른 Skybox가 Tint를 쓰면 설정한다.
            else if (skyMaterial.HasColor("_BaseColor")) skyMaterial.SetColor("_BaseColor", color); // URP 계열 BaseColor를 쓰는 하늘 Mesh에도 대응한다.
            else if (skyMaterial.HasColor("_Color")) skyMaterial.SetColor("_Color", color); // 일반 Color 프로퍼티만 있는 재질도 대응한다.
        }

        if (skyMaterial != null)
        {
            if (skyMaterial.HasColor("_GroundColor")) skyMaterial.SetColor("_GroundColor", color * 0.48f);
            if (skyMaterial.HasFloat("_Exposure")) skyMaterial.SetFloat("_Exposure", Mathf.Lerp(0.38f, 1.28f, Mathf.Clamp01(color.maxColorComponent)));
            if (skyMaterial.HasFloat("_AtmosphereThickness")) skyMaterial.SetFloat("_AtmosphereThickness", Mathf.Lerp(1.45f, 0.72f, Mathf.Clamp01(color.maxColorComponent)));
        }

        if (skyCamera != null)
        {
            skyCamera.clearFlags = CameraClearFlags.Skybox;
            skyCamera.backgroundColor = color;
        }
    }

    private void ApplyStars(float visibility) // 256개 별을 가진 단일 Renderer 하나만 조절한다.
    {
        if (starDomeRenderer == null) return; // 별돔이 없는 테스트 Scene에서도 오류 없이 동작한다.
        if (skyCamera != null) starDomeRenderer.transform.position = skyCamera.transform.position;
        if (starBlock == null) starBlock = new MaterialPropertyBlock(); // 특수 에디터 호출에서도 실제 메서드 실행 시점에만 안전하게 생성한다.
        if (emissionColorId == 0) emissionColorId = Shader.PropertyToID("_EmissionColor"); // Emission ID가 아직 없으면 사용 직전에 보충한다.
        if (baseColorId == 0) baseColorId = Shader.PropertyToID("_BaseColor"); // Base Color ID도 같은 방식으로 보충한다.

        starDomeRenderer.enabled = visibility > 0.01f; // 거의 보이지 않는 낮에는 Renderer 자체를 꺼서 불필요한 드로우를 줄인다.
        if (!starDomeRenderer.enabled) return; // 꺼진 상태에서는 프로퍼티 갱신도 생략한다.

        Color emission = new Color(0.75f, 0.88f, 1f, 1f) * Mathf.Lerp(0.2f, 3.4f, visibility); // 밤이 깊을수록 청백색 별 발광을 키운다.
        starDomeRenderer.GetPropertyBlock(starBlock); // 기존 Renderer 프로퍼티를 가져온다.
        starBlock.SetColor(emissionColorId, emission); // URP/Lit Emission에 현재 별 밝기를 적용한다.
        starBlock.SetColor(baseColorId, new Color(0.75f, 0.88f, 1f, 1f)); // 별의 기본색은 항상 청백색으로 유지한다.
        starDomeRenderer.SetPropertyBlock(starBlock); // Material 인스턴스를 만들지 않고 Renderer에 적용한다.
    }

    public void SetProgressTarget(Transform target) // 기존 썰매 시스템이 생성된 뒤 참조만 전달할 수 있는 함수다.
    {
        progressTarget = target; // 기존 이동 로직 자체는 수정하지 않는다.
        travelledDistance = 0f;
        progressInitialized = false;
    }

    public void ConfigureRuntimeWorld(Light sun, Camera camera, Material runtimeSky, Renderer stars, float courseLength)
    {
        directionalLight = sun;
        skyCamera = camera;
        skyMaterial = runtimeSky;
        starDomeRenderer = stars;
        trackLengthMeters = Mathf.Max(1f, courseLength);
    }

    public void PreviewProgress(float progress)
    {
        currentProgress = Mathf.Clamp01(progress);
        ApplyTimeProgress(currentProgress);
    }

    private readonly struct TimeKey // 시간대 한 지점의 모든 환경 값을 한 번에 보관한다.
    {
        public readonly float progress; // 트랙 진행률 키다.
        public readonly Color skyColor; // 하늘색이다.
        public readonly Color sunColor; // Directional Light 색이다.
        public readonly float sunIntensity; // Directional Light 세기다.
        public readonly Color ambientColor; // Ambient Light 색이다.
        public readonly Color fogColor; // Fog 색이다.
        public readonly Vector3 sunEuler; // Directional Light 회전값이다.
        public readonly float starVisibility; // 별돔 발광 가중치다.

        public TimeKey(float progress, Color skyColor, Color sunColor, float sunIntensity, Color ambientColor, Color fogColor, Vector3 sunEuler, float starVisibility) // 모든 키 값을 생성 시 한 번 확정한다.
        {
            this.progress = progress; // 진행률을 저장한다.
            this.skyColor = skyColor; // 하늘색을 저장한다.
            this.sunColor = sunColor; // 태양색을 저장한다.
            this.sunIntensity = sunIntensity; // 태양 밝기를 저장한다.
            this.ambientColor = ambientColor; // Ambient 색을 저장한다.
            this.fogColor = fogColor; // Fog 색을 저장한다.
            this.sunEuler = sunEuler; // 태양 회전을 저장한다.
            this.starVisibility = starVisibility; // 별 표시량을 저장한다.
        }
    }
}
