using UnityEngine;

public sealed class MushSoundBank : ScriptableObject
{
    public AudioClip click;
    public AudioClip starFirst;
    public AudioClip starThird;
    public AudioClip[] bonfire;
    public AudioClip[] wind;
    public Material overlayMaterial;
    public static MushSoundBank Load() => Resources.Load<MushSoundBank>("MushSoundBank");
}
