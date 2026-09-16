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
    private void Awake() { source = GetComponent<AudioSource>(); sourceVolume = source.volume; }
    private void OnEnable() { MushAudioSettings.Changed += Apply; Apply(); }
    private void OnDisable() { MushAudioSettings.Changed -= Apply; if (source != null) source.volume = sourceVolume; }
    public void SetVolume(float volume) { sourceVolume = Mathf.Clamp01(volume); Apply(); }
    private void Apply()
    {
        if (source != null) source.volume = sourceVolume * (bus == Bus.Music ? MushAudioSettings.Music : MushAudioSettings.Effects);
    }
}
