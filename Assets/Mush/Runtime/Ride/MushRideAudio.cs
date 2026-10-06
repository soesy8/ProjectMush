using Mush.Prototype;
using UnityEngine;

/// <summary>Uses the same input and terrain state as the ride controller.</summary>
[DefaultExecutionOrder(1800)]
[DisallowMultipleComponent]
public sealed class MushRideAudio : MonoBehaviour
{
    private const float LoopFadeSeconds = 0.12f;
    private MushMapRideBootstrap ride;
    private MushSledKeyboardController controller;
    private Loop snow;
    private Loop ice;
    private Loop reins;
    private AudioSource acceleration;
    private MushAudioChannel accelerationChannel;
    private bool paused;

    private sealed class Loop
    {
        public AudioSource source;
        public MushAudioChannel channel;
        public float volume;
    }

    public void Configure(MushMapRideBootstrap owner, MushSledKeyboardController motion, Transform sled)
    {
        if (controller != null) controller.AccelerationEntered -= PlayAcceleration;
        ride = owner;
        controller = motion;
        if (isActiveAndEnabled && controller != null) controller.AccelerationEntered += PlayAcceleration;
        MushSoundBank bank = MushSoundBank.Load();
        if (bank == null || acceleration != null) return;
        snow = CreateLoop(sled, "Sled Soft Snow", bank.sledSoftSnow);
        ice = CreateLoop(sled, "Sled Icy Grains", bank.sledIcyGrains);
        reins = CreateLoop(sled, "Reins Tension", bank.reinsTension);
        acceleration = CreateSource(sled, "Acceleration Enter", bank.accelerationEnter, false);
        accelerationChannel = acceleration.GetComponent<MushAudioChannel>();
    }

    private static AudioSource CreateSource(Transform parent, string name, AudioClip clip, bool loop)
    {
        GameObject root = new(name);
        root.transform.SetParent(parent, false);
        AudioSource source = root.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;
        source.clip = clip;
        source.volume = 0f;
        root.AddComponent<MushAudioChannel>();
        return source;
    }

    private static Loop CreateLoop(Transform parent, string name, AudioClip clip)
    {
        AudioSource source = CreateSource(parent, name, clip, true);
        return new Loop { source = source, channel = source.GetComponent<MushAudioChannel>() };
    }

    private void OnEnable()
    {
        if (controller != null) controller.AccelerationEntered += PlayAcceleration;
    }

    private void OnDisable()
    {
        if (controller != null) controller.AccelerationEntered -= PlayAcceleration;
        StopAll();
    }

    private void LateUpdate()
    {
        if (ride == null || controller == null || ride.HasFinished || !controller.enabled)
        {
            StopAll();
            return;
        }
        if (ride.IsPaused || AudioListener.pause || Time.timeScale <= 0f)
        {
            if (!paused)
            {
                snow?.source.Pause();
                ice?.source.Pause();
                reins?.source.Pause();
                acceleration?.Pause();
                paused = true;
            }
            return;
        }
        if (paused)
        {
            snow?.source.UnPause();
            ice?.source.UnPause();
            reins?.source.UnPause();
            acceleration?.UnPause();
            paused = false;
        }

        float movingVolume = ride.IsMoving ? Mathf.InverseLerp(0.1f, 2f, controller.CurrentSpeed) : 0f;
        UpdateLoop(snow, ride.IsOffCourse ? 0f : movingVolume);
        UpdateLoop(ice, ride.IsOffCourse ? movingVolume : 0f);
        UpdateLoop(reins, controller.RideStarted && controller.ReinTension > 0.01f ? 1f : 0f);
        if (acceleration != null && acceleration.clip != null && acceleration.isPlaying)
            accelerationChannel.SetVolume(AccelerationVolumeAt(acceleration.time / acceleration.clip.length));
    }

    private static void UpdateLoop(Loop loop, float targetVolume)
    {
        if (loop == null || loop.source == null || loop.source.clip == null) return;
        loop.volume = Mathf.MoveTowards(loop.volume, targetVolume, Time.deltaTime / LoopFadeSeconds);
        loop.channel.SetVolume(loop.volume);
        if (targetVolume > 0f && !loop.source.isPlaying) loop.source.Play();
        if (loop.volume <= 0f && loop.source.isPlaying) loop.source.Stop();
    }

    private void PlayAcceleration()
    {
        if (!isActiveAndEnabled || ride == null || ride.HasFinished || ride.IsPaused ||
            Time.timeScale <= 0f || acceleration == null || acceleration.clip == null) return;
        acceleration.Stop();
        accelerationChannel.SetVolume(0.25f);
        acceleration.Play();
    }

    public static float AccelerationVolumeAt(float progress)
    {
        progress = Mathf.Clamp01(progress);
        return progress <= 0.65f
            ? Mathf.Lerp(0.25f, 1f, Mathf.SmoothStep(0f, 1f, progress / 0.65f))
            : Mathf.Lerp(1f, 0.65f, Mathf.SmoothStep(0f, 1f, (progress - 0.65f) / 0.35f));
    }

    private void StopAll()
    {
        StopLoop(snow);
        StopLoop(ice);
        StopLoop(reins);
        acceleration?.Stop();
        paused = false;
    }

    private static void StopLoop(Loop loop)
    {
        if (loop == null) return;
        loop.source.Stop();
        loop.volume = 0f;
        loop.channel.SetVolume(0f);
    }
}
