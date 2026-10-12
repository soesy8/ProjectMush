using UnityEngine;

/// <summary>Use SetVolume for envelopes such as wind; channel volume stays independent.</summary>
[RequireComponent(typeof(AudioSource))]
[DisallowMultipleComponent]
public sealed class MushAudioChannel : MonoBehaviour
{
    public enum Bus { Effects, Music }
    [SerializeField] private Bus bus;
    private AudioSource source;
    private float sourceVolume;
    private volatile float transitionVolume = 1f;
    private float audioTransitionVolume = 1f;
    public Bus ChannelBus => bus;
    private void Awake() { source = GetComponent<AudioSource>(); sourceVolume = source.volume; }
    private void OnEnable() { MushAudioSettings.Changed += Apply; Apply(); }
    private void OnDisable()
    {
        MushAudioSettings.Changed -= Apply;
        // Keep a faded source silent when its filter is removed during scene unload.
        if (source != null) source.volume *= transitionVolume;
    }
    public void SetBus(Bus value) { bus = value; Apply(); }
    public void SetVolume(float volume) { sourceVolume = Mathf.Clamp01(volume); Apply(); }
    public void SetTransitionVolume(float volume) => transitionVolume = Mathf.Clamp01(volume);
    private void Apply()
    {
        if (source != null) source.volume = sourceVolume * (bus == Bus.Music ? MushAudioSettings.Music : MushAudioSettings.Effects);
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        float target = transitionVolume;
        if (channels <= 0 || data.Length == 0 || target == 1f && audioTransitionVolume == 1f) return;
        int frames = data.Length / channels;
        if (frames == 0) return;
        float gain = audioTransitionVolume;
        float step = (target - gain) / frames;
        // Interpolate once per audio frame, with identical gain for every channel.
        // This keeps the fade continuous even when rendering or scene loading stalls.
        for (int frame = 0; frame < frames; frame++)
        {
            gain += step;
            int offset = frame * channels;
            for (int channel = 0; channel < channels; channel++)
                data[offset + channel] *= gain;
        }
        audioTransitionVolume = target;
    }
}
