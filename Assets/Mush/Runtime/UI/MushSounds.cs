using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class MushSounds : MonoBehaviour
{
    private static MushSounds instance;
    private AudioSource uiSource;
    private AudioSource musicSource;
    private MushSoundBank bank;
    private int lastClickFrame = -1;
    private int lastHoverFrame = -1;
    private int suppressedClickFrame = -1;

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

        GameObject music = new("Scene BGM");
        music.transform.SetParent(transform, false);
        musicSource = music.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.spatialBlend = 0f;
        music.AddComponent<MushAudioChannel>().SetBus(MushAudioChannel.Bus.Music);
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        if (instance == this) instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => StartCoroutine(BindScene(scene));
    private void OnActiveSceneChanged(Scene previous, Scene next) => UpdateMusic(next);

    private IEnumerator BindScene(Scene scene)
    {
        yield return null;
        if (!scene.IsValid() || !scene.isLoaded) yield break;
        foreach (GameObject root in scene.GetRootGameObjects()) BindButtons(root);
        if (scene == SceneManager.GetActiveScene()) UpdateMusic(scene);
    }

    private void UpdateMusic(Scene scene)
    {
        AudioClip clip = MusicForScene(scene);
        if (musicSource.clip == clip) return;
        musicSource.Stop();
        musicSource.clip = clip;
        if (clip != null) musicSource.Play();
    }

    private AudioClip MusicForScene(Scene scene)
    {
        if (bank == null || !scene.IsValid() || !scene.isLoaded) return null;
        if (scene.name is "Title" or "MushTitle") return bank.titleMusic;
        if (scene.name is "PM_Lobby" or "MushLobby" or "MushStore" or "MushHousing") return bank.lobbyMusic;
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.GetComponentInChildren<MushMapRideBootstrap>(true) != null) return bank.drivingMusic;
        return null;
    }

    public static void BindButtons(GameObject root)
    {
        foreach (Button button in root.GetComponentsInChildren<Button>(true))
        {
            if (!button.TryGetComponent<MushUiButtonSounds>(out _))
                button.gameObject.AddComponent<MushUiButtonSounds>();
            button.onClick.RemoveListener(PlayClick);
            button.onClick.AddListener(PlayClick);
        }
    }

    // Pointer-down already plays alongside the VFX; suppress the matching release callbacks.
    public static void SuppressClickThisFrame()
    {
        if (instance != null) instance.suppressedClickFrame = Time.frameCount;
    }

    public static void PlayClick()
    {
        if (instance == null || instance.lastClickFrame == Time.frameCount ||
            instance.suppressedClickFrame == Time.frameCount) return;
        instance.lastClickFrame = Time.frameCount;
        instance.Play(instance.bank != null ? instance.bank.click : null);
    }

    public static void PlayHover()
    {
        if (instance == null || instance.lastHoverFrame == Time.frameCount) return;
        instance.lastHoverFrame = Time.frameCount;
        instance.Play(instance.bank != null ? instance.bank.hover : null);
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
