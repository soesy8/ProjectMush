using System.Collections;
using System.Collections.Generic;
using Mush.Quest;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Black fade shared by map departure, retry and return to the lobby.</summary>
public sealed class MushSceneTransition : MonoBehaviour
{
    private const float FadeSeconds = 0.75f;
    private static MushSceneTransition instance;
    private Canvas canvas;
    private CanvasGroup group;
    private RectTransform rect;
    private Material overlayMaterial;
    private bool loading;
    private readonly List<MushAudioChannel> fadingEffects = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    public static void Load(string sceneName, bool lobbyTrackDeparture = false)
    {
        if (instance != null && instance.loading) return;
        if (!Application.CanStreamedLevelBeLoaded(sceneName)) return;
        if (instance == null)
        {
            GameObject root = new("Mush Scene Transition");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<MushSceneTransition>();
            instance.CreateOverlay();
        }
        instance.loading = true;
        bool fadeLobbyAudio = lobbyTrackDeparture &&
            SceneManager.GetActiveScene().name is "PM_Lobby" or "MushLobby";
        instance.StartCoroutine(instance.Transition(sceneName, fadeLobbyAudio));
    }

    private void CreateOverlay()
    {
        GameObject overlay = new("Black Fade", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasGroup), typeof(GraphicRaycaster));
        overlay.transform.SetParent(transform, false);
        rect = overlay.GetComponent<RectTransform>();
        canvas = overlay.GetComponent<Canvas>();
        canvas.sortingOrder = 32767;
        group = overlay.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        GameObject imageObject = new("Black", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(overlay.transform, false);
        RectTransform imageRect = imageObject.GetComponent<RectTransform>();
        imageRect.anchorMin = Vector2.zero;
        imageRect.anchorMax = Vector2.one;
        imageRect.offsetMin = imageRect.offsetMax = Vector2.zero;
        Image image = imageObject.GetComponent<Image>();
        image.color = Color.black;
        overlayMaterial = new Material(Shader.Find("UI/Default"));
        overlayMaterial.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        image.material = overlayMaterial;
        overlay.SetActive(false);
    }

    private IEnumerator Transition(string sceneName, bool fadeLobbyAudio)
    {
        canvas.gameObject.SetActive(true);
        group.blocksRaycasts = true;
        CaptureEffectsForFade();
        if (fadeLobbyAudio) MushSounds.BeginLobbyTrackDeparture();
        yield return Fade(0f, 1f, fadeLobbyAudio);
        // Give the audio thread time to reach silence before sources are unloaded.
        yield return new WaitForSecondsRealtime(0.05f);
        foreach (MushAudioChannel channel in fadingEffects)
        {
            if (channel == null) continue;
            AudioSource source = channel.GetComponent<AudioSource>();
            if (source != null) source.Stop();
        }
        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
        if (operation != null)
            while (!operation.isDone) yield return null;
        // Let the destination's Start methods and camera setup finish while covered.
        yield return null;
        yield return null;
        yield return Fade(1f, 0f);
        group.blocksRaycasts = false;
        canvas.gameObject.SetActive(false);
        fadingEffects.Clear();
        loading = false;
    }

    private void CaptureEffectsForFade()
    {
        fadingEffects.Clear();
        foreach (AudioSource source in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
        {
            if (!source.isActiveAndEnabled) continue;
            MushAudioChannel channel = source.GetComponent<MushAudioChannel>();
            if (channel == null) channel = source.gameObject.AddComponent<MushAudioChannel>();
            if (channel.ChannelBus == MushAudioChannel.Bus.Effects)
                fadingEffects.Add(channel);
        }
    }

    private void SetEffectsFade(float volume)
    {
        foreach (MushAudioChannel channel in fadingEffects)
            if (channel != null) channel.SetTransitionVolume(volume);
    }

    private IEnumerator Fade(float from, float to, bool fadeLobbyAudio = false)
    {
        float elapsed = 0f;
        while (elapsed < FadeSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / FadeSeconds));
            group.alpha = Mathf.Lerp(from, to, progress);
            SetEffectsFade(1f - group.alpha);
            if (fadeLobbyAudio) MushSounds.SetLobbyDepartureFade(progress);
            yield return null;
        }
        group.alpha = to;
        SetEffectsFade(1f - to);
        if (fadeLobbyAudio) MushSounds.SetLobbyDepartureFade(1f);
    }

    private void LateUpdate()
    {
        if (!loading || canvas == null) return;
        Camera camera = Camera.main;
        bool vr = MushQuestTrackedInputRig.IsXrActive && camera != null;
        canvas.renderMode = vr ? RenderMode.WorldSpace : RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = camera;
        if (!vr) return;
        float distance = Mathf.Max(0.15f, camera.nearClipPlane + 0.05f);
        rect.sizeDelta = new Vector2(2f, 2f);
        rect.SetPositionAndRotation(camera.transform.position + camera.transform.forward * distance,
            camera.transform.rotation);
        rect.localScale = Vector3.one * distance * 8f;
        // Cover the eyes after the latest tracked head pose as well.
    }

    private void OnEnable() => Application.onBeforeRender += LateUpdate;
    private void OnDisable() => Application.onBeforeRender -= LateUpdate;
    private void OnDestroy()
    {
        SetEffectsFade(1f);
        if (overlayMaterial != null) Destroy(overlayMaterial);
        if (instance == this) instance = null;
    }
}
