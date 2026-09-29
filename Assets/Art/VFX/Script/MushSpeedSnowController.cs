using Mush.Prototype;
using UnityEngine;

/// <summary>
/// Track_v2의 P_SpeedSnow를 주행 방향과 속도에 맞춰 제어한다.
/// 이 컴포넌트는 P_SpeedSnow 오브젝트에 직접 부착해서 사용한다.
/// </summary>
[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(ParticleSystem))]
public sealed class MushSpeedSnowRuntimeController : MonoBehaviour
{
    private static readonly AnimationCurve UnitVelocityCurve =
        AnimationCurve.Constant(0f, 1f, 1f);

    [Header("주행 참조")]
    [Tooltip("비워 두면 같은 씬에서 활성화된 썰매 컨트롤러를 자동으로 찾는다.")]
    [SerializeField] private MushSledKeyboardController rideController;
    [Tooltip("비워 두면 같은 씬에서 주행 상태 관리자를 자동으로 찾는다.")]
    [SerializeField] private MushMapRideBootstrap rideState;

    [Header("속도 반응")]
    [SerializeField, Min(0f)] private float minimumWindSpeed = 5f;
    [SerializeField, Min(0f)] private float maximumWindSpeed = 28f;
    [SerializeField, Min(0f)] private float minimumEmissionRate = 8f;
    [SerializeField, Min(0f)] private float maximumEmissionRate = 68f;
    [SerializeField, Min(0.01f)] private float speedResponse = 7f;

    [Header("방향 반응")]
    [SerializeField, Min(0.01f)] private float directionResponse = 10f;
    [SerializeField, Min(0f)] private float movementThreshold = 0.1f;

    private ParticleSystem speedSnow;
    private Vector3 smoothedTravelDirection;
    private float smoothedSpeed01;
    private float nextReferenceSearchTime;

    private void Awake()
    {
        speedSnow = GetComponent<ParticleSystem>();
        FindRideReferences();
        ConfigureParticleSystem();
        smoothedTravelDirection = GetTravelDirection();
        smoothedSpeed01 = 0f;
        SetEmissionRate(0f);
    }

    private void Start()
    {
        // 다른 컴포넌트가 Awake에서 주행 오브젝트를 구성하는 경우를 위해 한 번 더 찾는다.
        if (rideController == null || rideState == null)
            FindRideReferences();

        smoothedTravelDirection = GetTravelDirection();
    }

    private void FindRideReferences()
    {
        if (rideController == null)
        {
            MushSledKeyboardController[] controllers =
                Object.FindObjectsByType<MushSledKeyboardController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            MushSledKeyboardController inactiveFallback = null;
            for (int index = 0; index < controllers.Length; index++)
            {
                MushSledKeyboardController candidate = controllers[index];
                if (candidate.gameObject.scene != gameObject.scene)
                    continue;

                if (candidate.gameObject.activeInHierarchy)
                {
                    rideController = candidate;
                    break;
                }

                inactiveFallback ??= candidate;
            }

            rideController ??= inactiveFallback;
        }

        if (rideState != null)
            return;

        MushMapRideBootstrap[] bootstraps = Object.FindObjectsByType<MushMapRideBootstrap>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int index = 0; index < bootstraps.Length; index++)
        {
            if (bootstraps[index].gameObject.scene != gameObject.scene)
                continue;

            rideState = bootstraps[index];
            break;
        }
    }

    private void ConfigureParticleSystem()
    {
        if (speedSnow == null)
            return;

        ParticleSystem.MainModule main = speedSnow.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSpeed = 0f;

        ParticleSystem.VelocityOverLifetimeModule velocity = speedSnow.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;

        // Y축의 기존 Curve 모드와 일치시켜 축별 모드 불일치 오류를 방지한다.
        // 이후에는 multiplier만 바꿔 곡선 모드를 유지한 채 수평 풍속을 갱신한다.
        velocity.x = new ParticleSystem.MinMaxCurve(1f, UnitVelocityCurve);
        velocity.z = new ParticleSystem.MinMaxCurve(1f, UnitVelocityCurve);
    }

    private void LateUpdate()
    {
        if (speedSnow == null)
            return;

        if (rideController == null && Time.unscaledTime >= nextReferenceSearchTime)
        {
            // 주행 오브젝트가 늦게 생성되는 씬에서도 연결될 수 있도록 낮은 빈도로 재탐색한다.
            nextReferenceSearchTime = Time.unscaledTime + 1f;
            FindRideReferences();
        }

        if (rideController == null)
        {
            SetEmissionRate(0f);
            return;
        }

        bool moving = Time.timeScale > 0f &&
                      rideController.RideStarted &&
                      rideController.CurrentSpeed > movementThreshold &&
                      (rideState == null || (!rideState.IsPaused && !rideState.HasFinished));

        float maximumRideSpeed = Mathf.Max(0.01f, rideController.SecondLevelSpeed);
        float targetSpeed01 = moving
            ? Mathf.Clamp01(rideController.CurrentSpeed / maximumRideSpeed)
            : 0f;
        targetSpeed01 = Mathf.SmoothStep(0f, 1f, targetSpeed01);

        float deltaTime = Time.unscaledDeltaTime;
        float speedBlend = 1f - Mathf.Exp(-speedResponse * deltaTime);
        smoothedSpeed01 = Mathf.Lerp(smoothedSpeed01, targetSpeed01, speedBlend);

        Vector3 targetDirection = GetTravelDirection();
        if (smoothedTravelDirection.sqrMagnitude < 0.0001f)
            smoothedTravelDirection = targetDirection;
        float directionBlend = 1f - Mathf.Exp(-directionResponse * deltaTime);
        smoothedTravelDirection = Vector3.Slerp(
            smoothedTravelDirection,
            targetDirection,
            directionBlend).normalized;

        // 방출 영역은 로컬 +Z 방향 10m 앞에 있으므로 이 자식만 주행 방향으로 회전한다.
        // 프리팹 루트와 P_AmbientSnow는 회전시키지 않는다.
        transform.rotation = Quaternion.LookRotation(smoothedTravelDirection, Vector3.up);

        float windSpeed = Mathf.Lerp(minimumWindSpeed, maximumWindSpeed, smoothedSpeed01);
        Vector3 windVelocity = -smoothedTravelDirection * windSpeed;
        ParticleSystem.VelocityOverLifetimeModule velocity = speedSnow.velocityOverLifetime;
        velocity.xMultiplier = windVelocity.x;
        velocity.zMultiplier = windVelocity.z;

        float emissionRate = moving
            ? Mathf.Lerp(minimumEmissionRate, maximumEmissionRate, smoothedSpeed01)
            : 0f;
        SetEmissionRate(emissionRate);

        if (moving && !speedSnow.isPlaying)
            speedSnow.Play();
    }

    private Vector3 GetTravelDirection()
    {
        if (rideController == null)
            return Vector3.forward;

        // 경사면에서도 눈발이 과도하게 위아래로 기울지 않도록 수평 주행 방향만 사용한다.
        // 썰매 좌석의 시각적 피치는 기존 주행 코드가 별도로 처리한다.
        Vector3 direction = Vector3.ProjectOnPlane(
            rideController.transform.forward,
            Vector3.up);
        return direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Vector3.forward;
    }

    private void SetEmissionRate(float rate)
    {
        if (speedSnow == null)
            return;

        ParticleSystem.EmissionModule emission = speedSnow.emission;
        emission.rateOverTime = Mathf.Max(0f, rate);
    }

    private void OnValidate()
    {
        maximumWindSpeed = Mathf.Max(minimumWindSpeed, maximumWindSpeed);
        maximumEmissionRate = Mathf.Max(minimumEmissionRate, maximumEmissionRate);
        speedResponse = Mathf.Max(0.01f, speedResponse);
        directionResponse = Mathf.Max(0.01f, directionResponse);
        movementThreshold = Mathf.Max(0f, movementThreshold);
    }
}
