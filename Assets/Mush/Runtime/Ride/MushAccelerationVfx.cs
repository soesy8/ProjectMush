using Mush.Prototype;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>One reusable, sled-local burst on entry into acceleration.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
[DefaultExecutionOrder(2100)]
public sealed class MushAccelerationVfx : MonoBehaviour
{
    private static readonly int ElapsedId = Shader.PropertyToID("_Elapsed");
    private static readonly int DurationId = Shader.PropertyToID("_Duration");

    [Header("주행 연결")]
    [SerializeField] private MushSledKeyboardController controller;
    [SerializeField] private MushMapRideBootstrap ride;
    [Header("가속 진입 1회 재생")]
    [SerializeField, Min(0.1f)] private float duration = 0.85f;
    [SerializeField] private Material sourceMaterial;

    private MeshRenderer flowRenderer;
    private Material runtimeMaterial;
    private MushSledKeyboardController subscribedController;
    private float elapsed;
    private bool playing;

    public bool IsPlaying => playing;
    public float ElapsedTime => elapsed;
    public float Duration => duration;
    public int PlayCount { get; private set; }

    public void Configure(MushSledKeyboardController motion, MushMapRideBootstrap state,
        Material material, float seconds = 0.85f)
    {
        Unsubscribe();
        controller = motion;
        ride = state;
        sourceMaterial = material;
        duration = Mathf.Max(0.1f, seconds);
        if (Application.isPlaying && isActiveAndEnabled) Subscribe();
    }

    private void Awake()
    {
        flowRenderer = GetComponent<MeshRenderer>();
        if (controller == null) controller = GetComponentInParent<MushSledKeyboardController>();
        if (sourceMaterial == null) sourceMaterial = flowRenderer.sharedMaterial;
        if (sourceMaterial != null)
        {
            runtimeMaterial = new Material(sourceMaterial) { name = sourceMaterial.name + " (Ride Instance)" };
            flowRenderer.sharedMaterial = runtimeMaterial;
            runtimeMaterial.SetFloat(DurationId, duration);
        }
        flowRenderer.shadowCastingMode = ShadowCastingMode.Off;
        flowRenderer.receiveShadows = false;
        ResetEffect();
    }

    private void OnEnable()
    {
        if (Application.isPlaying) Subscribe();
    }

    private void Start()
    {
        // The saved ride root may acquire its controller during bootstrap Start.
        if (controller == null) controller = GetComponentInParent<MushSledKeyboardController>();
        Subscribe();
    }

    private void Subscribe()
    {
        if (subscribedController == controller) return;
        Unsubscribe();
        if (controller == null) return;
        subscribedController = controller;
        subscribedController.AccelerationEntered += Play;
        subscribedController.MotionReset += ResetEffect;
    }

    private void Unsubscribe()
    {
        if (subscribedController == null) return;
        subscribedController.AccelerationEntered -= Play;
        subscribedController.MotionReset -= ResetEffect;
        subscribedController = null;
    }

    public void Play()
    {
        if (!isActiveAndEnabled || playing || runtimeMaterial == null ||
            controller == null || !controller.RideStarted || !controller.enabled ||
            Time.timeScale <= 0f || (ride != null && (ride.IsPaused || ride.HasFinished))) return;
        elapsed = 0f;
        playing = true;
        PlayCount++;
        runtimeMaterial.SetFloat(ElapsedId, 0f);
        flowRenderer.enabled = true;
    }

    private void LateUpdate()
    {
        if (!playing) return;
        if (controller == null || !controller.enabled || !controller.RideStarted ||
            (ride != null && ride.HasFinished))
        {
            ResetEffect();
            return;
        }
        if (Time.timeScale <= 0f || (ride != null && ride.IsPaused)) return;
        elapsed += Time.deltaTime;
        if (elapsed >= duration)
        {
            ResetEffect();
            return;
        }
        runtimeMaterial.SetFloat(ElapsedId, elapsed);
    }

    public void ResetEffect()
    {
        playing = false;
        elapsed = 0f;
        if (flowRenderer == null) flowRenderer = GetComponent<MeshRenderer>();
        if (flowRenderer != null) flowRenderer.enabled = false;
        if (runtimeMaterial != null) runtimeMaterial.SetFloat(ElapsedId, -1f);
    }

    private void OnDisable()
    {
        Unsubscribe();
        ResetEffect();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        if (runtimeMaterial != null) Destroy(runtimeMaterial);
    }
}
