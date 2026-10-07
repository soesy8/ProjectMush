using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    public static void Load(string sceneName)
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
        instance.StartCoroutine(instance.Transition(sceneName));
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

    private IEnumerator Transition(string sceneName)
    {
        canvas.gameObject.SetActive(true);
        group.blocksRaycasts = true;
        yield return Fade(0f, 1f);
        AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
        if (operation != null)
            while (!operation.isDone) yield return null;
        // Let the destination's Start methods and camera setup finish while covered.
        yield return null;
        yield return null;
        yield return Fade(1f, 0f);
        group.blocksRaycasts = false;
        canvas.gameObject.SetActive(false);
        loading = false;
    }

    private IEnumerator Fade(float from, float to)
    {
        float elapsed = 0f;
        while (elapsed < FadeSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = Mathf.SmoothStep(from, to, Mathf.Clamp01(elapsed / FadeSeconds));
            yield return null;
        }
        group.alpha = to;
    }

    private void LateUpdate()
    {
        if (!loading || canvas == null) return;
        Camera camera = Camera.main;
        bool vr = XRSettings.isDeviceActive && camera != null;
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
        if (overlayMaterial != null) Destroy(overlayMaterial);
        if (instance == this) instance = null;
    }
}
