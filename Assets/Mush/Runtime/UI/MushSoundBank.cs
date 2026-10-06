using UnityEngine;

public sealed class MushSoundBank : ScriptableObject
{
    public AudioClip titleMusic;
    public AudioClip lobbyMusic;
    public AudioClip drivingMusic;
    public AudioClip hover;
    public AudioClip fireplaceAmbience;
    [Range(0f, 1f)] public float fireplaceAmbienceVolume = 0.15f;
    public AudioClip click;
    public AudioClip starFirst;
    public AudioClip starThird;
    public AudioClip[] bonfire;
    public AudioClip[] wind;
    public Material overlayMaterial;
    public static MushSoundBank Load() => Resources.Load<MushSoundBank>("MushSoundBank");
}
