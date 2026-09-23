using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MushVolumeSliders : MonoBehaviour
{
    private readonly Slider[] sliders = new Slider[3];
    private readonly TMP_Text[] counts = new TMP_Text[3];

    private void Awake()
    {
        string[] names = { "01_MasterVolume", "02_BGM", "03_SE" };
        foreach (Transform group in GetComponentsInChildren<Transform>(true))
        for (int i = 0; i < names.Length; i++)
        {
            if (group.name != names[i]) continue;
            sliders[i] = group.GetComponentInChildren<Slider>(true);
            if (sliders[i] != null)
            {
                sliders[i].minValue = 0f;
                sliders[i].maxValue = 1f;
                sliders[i].wholeNumbers = false;
                sliders[i].onValueChanged.AddListener(OnChanged);
            }
            foreach (TMP_Text text in group.GetComponentsInChildren<TMP_Text>(true))
                if (text.name == "Count") counts[i] = text;
        }
    }

    private void OnEnable() { MushAudioSettings.Changed += Refresh; Refresh(); }
    private void OnDisable() { MushAudioSettings.Changed -= Refresh; MushAudioSettings.Flush(); }
    private void OnChanged(float value)
    {
        MushAudioSettings.Set(sliders[0] != null ? sliders[0].value : MushAudioSettings.Master,
            sliders[1] != null ? sliders[1].value : MushAudioSettings.Music,
            sliders[2] != null ? sliders[2].value : MushAudioSettings.Effects);
    }
    private void Refresh()
    {
        float[] values = { MushAudioSettings.Master, MushAudioSettings.Music, MushAudioSettings.Effects };
        for (int i = 0; i < 3; i++)
        {
            if (sliders[i] != null) sliders[i].SetValueWithoutNotify(values[i]);
            if (counts[i] != null) counts[i].SetText("{0:0}", values[i] * 100f);
        }
    }
}
