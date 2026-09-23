using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class MushSounds : MonoBehaviour
{
    private static MushSounds instance;
    private AudioSource uiSource;
    private MushSoundBank bank;
    private int lastClickFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (instance != null) return;
        GameObject root = new("Mush Sounds");
        DontDestroyOnLoad(root);
        instance = root.AddComponent<MushSounds>();
    }

    private void Awake()
    {
        bank = MushSoundBank.Load();
        uiSource = gameObject.AddComponent<AudioSource>();
        uiSource.playOnAwake = false;
        uiSource.ignoreListenerPause = true;
        gameObject.AddComponent<MushAudioChannel>();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => StartCoroutine(BindScene(scene));
    private IEnumerator BindScene(Scene scene)
    {
        yield return null;
        if (!scene.IsValid() || !scene.isLoaded) yield break;
        foreach (GameObject root in scene.GetRootGameObjects()) BindButtons(root);
    }

    public static void BindButtons(GameObject root)
    {
        foreach (Button button in root.GetComponentsInChildren<Button>(true))
        {
            button.onClick.RemoveListener(PlayClick);
            button.onClick.AddListener(PlayClick);
        }
    }

    public static void PlayClick()
    {
        if (instance == null || instance.lastClickFrame == Time.frameCount) return;
        instance.lastClickFrame = Time.frameCount;
        instance.Play(instance.bank != null ? instance.bank.click : null);
    }

    public static void PlayStar(int index)
    {
        if (instance == null || instance.bank == null) return;
        instance.Play(index == 2 ? instance.bank.starThird : instance.bank.starFirst);
    }

    private void Play(AudioClip clip)
    {
        if (clip != null) uiSource.PlayOneShot(clip);
    }
}
