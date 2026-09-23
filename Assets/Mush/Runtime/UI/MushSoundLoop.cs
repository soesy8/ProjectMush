using UnityEngine;

public sealed class MushSoundLoop : MonoBehaviour
{
    private AudioClip[] clips;
    private AudioSource source;
    private MushAudioChannel channel;
    private int previous = -1;

    public static MushSoundLoop Create(Transform parent, bool fireplace)
    {
        MushSoundBank bank = MushSoundBank.Load();
        if (bank == null) return null;
        GameObject root = new(fireplace ? "Bonfire Sound" : "Ride Wind Sound");
        root.transform.SetParent(parent, false);
        AudioSource audio = root.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        audio.spatialBlend = fireplace ? 1f : 0f;
        audio.rolloffMode = AudioRolloffMode.Linear;
        audio.minDistance = 1.5f;
        audio.maxDistance = 14f;
        MushSoundLoop loop = root.AddComponent<MushSoundLoop>();
        loop.source = audio;
        loop.clips = fireplace ? bank.bonfire : bank.wind;
        loop.channel = root.AddComponent<MushAudioChannel>();
        loop.SetVolume(fireplace ? 0.65f : 0f);
        return loop;
    }

    public void SetVolume(float value) => channel?.SetVolume(value);

    // Source.volume is capped at 1; apply the requested gain to the loop samples
    // so both quiet and loud sections double while the option sliders still apply.
    private void OnAudioFilterRead(float[] data, int channels)
    {
        for (int i = 0; i < data.Length; i++) data[i] *= 2f;
    }

    private void Update()
    {
        if (source == null || source.isPlaying || AudioListener.pause || Time.timeScale == 0f ||
            clips == null || clips.Length == 0) return;
        int next = Random.Range(0, clips.Length);
        if (clips.Length > 1 && next == previous) next = (next + 1) % clips.Length;
        previous = next;
        source.clip = clips[next];
        if (source.clip != null) source.Play();
    }
}
