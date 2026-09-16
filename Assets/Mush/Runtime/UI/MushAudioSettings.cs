using System;
using UnityEngine;

public static class MushAudioSettings
{
    public static event Action Changed;
    public static float Master { get; private set; } = 1f;
    public static float Music { get; private set; } = 1f;
    public static float Effects { get; private set; } = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        Changed = null;
        Master = Read("Master"); Music = Read("Music"); Effects = Read("Effects");
        AudioListener.volume = Master;
        AudioListener.pause = false;
    }

    private static float Read(string channel)
    {
        float value = PlayerPrefs.GetFloat("Mush.Audio." + channel, 1f);
        return float.IsFinite(value) ? Mathf.Clamp01(value) : 1f;
    }

    public static void Set(float master, float music, float effects)
    {
        Master = Mathf.Clamp01(master); Music = Mathf.Clamp01(music); Effects = Mathf.Clamp01(effects);
        AudioListener.volume = Master;
        PlayerPrefs.SetFloat("Mush.Audio.Master", Master);
        PlayerPrefs.SetFloat("Mush.Audio.Music", Music);
        PlayerPrefs.SetFloat("Mush.Audio.Effects", Effects);
        Changed?.Invoke();
    }

    public static void Flush() => PlayerPrefs.Save();
}
