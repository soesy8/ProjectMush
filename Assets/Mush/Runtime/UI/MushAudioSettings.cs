using System;
using UnityEngine;
using Mush.Quest;

public static class MushAudioSettings
{
    public static event Action Changed;
    public static float Master { get; private set; } = 1f;
    public static float Music { get; private set; } = 1f;
    public static float Effects { get; private set; } = 1f;
    private static bool vrProfile;
    private static string KeyPrefix => vrProfile ? "Mush.Audio.VR." : "Mush.Audio.";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        Changed = null;
        vrProfile = MushQuestTrackedInputRig.IsXrActive;
        Master = Read("Master"); Music = Read("Music"); Effects = Read("Effects");
        AudioListener.volume = Master;
        AudioListener.pause = false;
    }

    private static float Read(string channel)
    {
        float value = PlayerPrefs.GetFloat(KeyPrefix + channel, 1f);
        return float.IsFinite(value) ? Mathf.Clamp01(value) : 1f;
    }

    public static void Set(float master, float music, float effects)
    {
        Master = Mathf.Clamp01(master); Music = Mathf.Clamp01(music); Effects = Mathf.Clamp01(effects);
        AudioListener.volume = Master;
        PlayerPrefs.SetFloat(KeyPrefix + "Master", Master);
        PlayerPrefs.SetFloat(KeyPrefix + "Music", Music);
        PlayerPrefs.SetFloat(KeyPrefix + "Effects", Effects);
        Changed?.Invoke();
    }

    public static void UseVrProfile(bool enabled)
    {
        if (vrProfile == enabled) return;
        vrProfile = enabled;
        Master = Read("Master"); Music = Read("Music"); Effects = Read("Effects");
        AudioListener.volume = Master;
        Changed?.Invoke();
    }

    public static void Flush() => PlayerPrefs.Save();
}
