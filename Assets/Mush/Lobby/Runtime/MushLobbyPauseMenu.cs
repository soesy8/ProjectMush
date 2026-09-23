using System;
using Mush.Quest;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Mush.Lobby
{
    [DisallowMultipleComponent]
    public sealed class MushLobbyPauseMenu : MonoBehaviour
    {
        public static MushLobbyPauseMenu Active { get; private set; }
        public bool IsOpen => (pausePanel != null && pausePanel.activeSelf) ||
                              (optionPanel != null && optionPanel.activeSelf);

        private Camera viewCamera;
        private Font font;
        private GameObject pausePanel;
        private GameObject optionPanel;
        private TextMesh optionValues;
        [SerializeField] private GameObject authoredPausePanel;
        [SerializeField] private GameObject authoredOptionPanel;
        [SerializeField] private Canvas authoredCanvas;

        public void ConfigureAuthored(Camera camera, GameObject pause, GameObject options, Canvas canvas)
        {
            viewCamera = camera;
            authoredPausePanel = pause;
            authoredOptionPanel = options;
            authoredCanvas = canvas;
            pausePanel = pause;
            optionPanel = options;
            BindAuthoredButtons();
        }

        public void Configure(Camera camera, Font koreanFont)
        {
            viewCamera = camera;
            font = koreanFont;
            Build();
        }

        private void Awake()
        {
            Active = this;
            viewCamera = authoredCanvas != null && authoredCanvas.worldCamera != null ? authoredCanvas.worldCamera : Camera.main;
            pausePanel = authoredPausePanel;
            optionPanel = authoredOptionPanel;
            BindAuthoredButtons();
        }

        private void Start()
        {
            if (authoredCanvas != null && XRSettings.isDeviceActive && viewCamera != null)
            {
                authoredCanvas.transform.SetParent(viewCamera.transform, true);
                MushQuestTrackedInputRig.ConfigureWorldCanvas(authoredCanvas, viewCamera, 2.05f);
            }
        }

        private void OnDestroy()
        {
            if (Active == this)
                Active = null;
            RestoreTime();
        }

        public void Toggle()
        {
            if (pausePanel == null)
                return;
            SetOpen(!IsOpen);
        }

        public void SetOpen(bool open)
        {
            pausePanel.SetActive(open);
            if (!open && optionPanel != null)
                optionPanel.SetActive(false);
            Time.timeScale = open ? 0f : 1f;
            AudioListener.pause = open;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Build()
        {
            if (viewCamera == null || pausePanel != null)
                return;

            pausePanel = CreatePanel("Lobby Pause", "일시정지");
            CreateButton(pausePanel.transform, "돌아가기", 0.30f, () => SetOpen(false));
            CreateButton(pausePanel.transform, "타이틀로", -0.05f, GoToTitle);
            CreateButton(pausePanel.transform, "옵션", -0.40f, OpenOptions);
            CreateButton(pausePanel.transform, "나가기", -0.75f, Quit);

            optionPanel = CreatePanel("Lobby Options", "옵션");
            optionValues = CreateText(optionPanel.transform, string.Empty, new Vector3(0f, 0.27f, -0.06f), 0.030f);
            CreateButton(optionPanel.transform, "전체 음량 -", -0.05f, () => AdjustAudio(-0.1f, 0f, 0f), -0.58f);
            CreateButton(optionPanel.transform, "전체 음량 +", -0.05f, () => AdjustAudio(0.1f, 0f, 0f), 0.58f);
            CreateButton(optionPanel.transform, "음악 -", -0.40f, () => AdjustAudio(0f, -0.1f, 0f), -0.58f);
            CreateButton(optionPanel.transform, "음악 +", -0.40f, () => AdjustAudio(0f, 0.1f, 0f), 0.58f);
            CreateButton(optionPanel.transform, "효과음 -", -0.75f, () => AdjustAudio(0f, 0f, -0.1f), -0.58f);
            CreateButton(optionPanel.transform, "효과음 +", -0.75f, () => AdjustAudio(0f, 0f, 0.1f), 0.58f);
            CreateButton(optionPanel.transform, "뒤로", -1.10f, CloseOptions);
            RefreshOptionValues();

            pausePanel.SetActive(false);
            optionPanel.SetActive(false);
        }

        private void BindAuthoredButtons()
        {
            if (authoredPausePanel == null)
                return;
            Bind("Button_Resume", () => SetOpen(false));
            Bind("Button_Title", GoToTitle);
            Bind("Button_Lobby", GoToTitle);
            Bind("Button_Option", OpenOptions);
            Bind("Button_Quit", Quit);
            BindOptionClose(authoredOptionPanel);
            if (authoredOptionPanel != null && !authoredOptionPanel.TryGetComponent<MushVolumeSliders>(out _))
                authoredOptionPanel.AddComponent<MushVolumeSliders>();
            if (authoredCanvas != null && !authoredCanvas.TryGetComponent<MushCanvasQuestInput>(out _))
                authoredCanvas.gameObject.AddComponent<MushCanvasQuestInput>();
        }

        private void Bind(string name, Action callback)
        {
            Transform target = FindChild(authoredPausePanel.transform, name);
            Button button = target != null ? target.GetComponent<Button>() : null;
            if (button == null)
                return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => callback());
        }

        private void BindOptionClose(GameObject root)
        {
            if (root == null)
                return;
            Transform close = FindChild(root.transform, "Button_Close") ?? FindChild(root.transform, "Image_Back");
            Button button = close != null ? close.GetComponent<Button>() : null;
            if (button == null)
                return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(CloseOptions);
        }

        private static Transform FindChild(Transform root, string name)
        {
            if (root == null)
                return null;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name)
                    return child;
            return null;
        }

        private GameObject CreatePanel(string name, string title)
        {
            GameObject panel = new(name);
            panel.transform.SetParent(viewCamera.transform, false);
            panel.transform.localPosition = new Vector3(0f, 0.20f, 2.05f);
            GameObject back = GameObject.CreatePrimitive(PrimitiveType.Cube);
            back.name = "Panel Back";
            back.transform.SetParent(panel.transform, false);
            back.transform.localScale = new Vector3(2.45f, 2.15f, 0.06f);
            back.GetComponent<Renderer>().sharedMaterial = CreateMaterial(new Color(0.045f, 0.06f, 0.085f));
            Destroy(back.GetComponent<Collider>());
            CreateText(panel.transform, title, new Vector3(0f, 0.78f, -0.06f), 0.065f);
            return panel;
        }

        private void CreateButton(Transform parent, string label, float y, Action action, float x = 0f)
        {
            GameObject buttonObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            buttonObject.name = label + " Button";
            buttonObject.transform.SetParent(parent, false);
            buttonObject.transform.localPosition = new Vector3(x, y, -0.07f);
            buttonObject.transform.localScale = new Vector3(x == 0f ? 1.45f : 1.02f, 0.26f, 0.10f);
            Renderer renderer = buttonObject.GetComponent<Renderer>();
            renderer.sharedMaterial = CreateMaterial(new Color(0.20f, 0.31f, 0.43f));
            BoxCollider collider = buttonObject.GetComponent<BoxCollider>();
            collider.isTrigger = true;
            XRSimpleInteractable xr = buttonObject.AddComponent<XRSimpleInteractable>();
            xr.selectMode = InteractableSelectMode.Single;
            MushLobbyPauseButton button = buttonObject.AddComponent<MushLobbyPauseButton>();
            button.Configure(action, renderer);
            CreateText(buttonObject.transform, label, new Vector3(0f, 0f, -0.56f), 0.035f);
        }

        private TextMesh CreateText(Transform parent, string content, Vector3 position, float size)
        {
            GameObject textObject = new(content + " Text");
            textObject.transform.SetParent(parent, false);
            textObject.transform.localPosition = position;
            TextMesh text = textObject.AddComponent<TextMesh>();
            text.text = content;
            text.font = font;
            text.fontSize = 64;
            text.characterSize = size;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            if (font != null)
                text.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return text;
        }

        private void OpenOptions()
        {
            pausePanel.SetActive(false);
            optionPanel.SetActive(true);
            RefreshOptionValues();
        }

        private void CloseOptions()
        {
            MushAudioSettings.Flush();
            optionPanel.SetActive(false);
            pausePanel.SetActive(true);
        }

        private void AdjustAudio(float master, float music, float effects)
        {
            MushAudioSettings.Set(
                MushAudioSettings.Master + master,
                MushAudioSettings.Music + music,
                MushAudioSettings.Effects + effects);
            RefreshOptionValues();
        }

        private void RefreshOptionValues()
        {
            if (optionValues != null)
                optionValues.text = $"전체 {MushAudioSettings.Master * 100f:0}   " +
                                    $"음악 {MushAudioSettings.Music * 100f:0}   " +
                                    $"효과음 {MushAudioSettings.Effects * 100f:0}";
        }

        private void GoToTitle()
        {
            MushGameSave.EnterLobby();
            RestoreTime();
            SceneManager.LoadScene("Title");
        }

        private static void Quit()
        {
            MushGameSave.Save();
            MushAudioSettings.Flush();
            RestoreTime();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static void RestoreTime()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        private static Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            return new Material(shader) { color = color };
        }
    }

    [DisallowMultipleComponent]
    public sealed class MushLobbyPauseButton : MonoBehaviour, IMushQuestRayTarget
    {
        private Action action;
        private Renderer targetRenderer;
        private Color normalColor;
        private XRSimpleInteractable xr;

        public void Configure(Action callback, Renderer renderer)
        {
            action = callback;
            targetRenderer = renderer;
            if (targetRenderer != null)
                normalColor = targetRenderer.material.color;
        }

        private void Awake()
        {
            xr = GetComponent<XRSimpleInteractable>();
            if (xr != null)
                xr.selectEntered.AddListener(OnSelected);
        }

        private void OnDestroy()
        {
            if (xr != null)
                xr.selectEntered.RemoveListener(OnSelected);
        }

        private void OnSelected(SelectEnterEventArgs _) => InvokeAction();
        private void OnMouseUpAsButton() => InvokeAction();
        private void OnMouseEnter() => SetQuestRayHovered(true);
        private void OnMouseExit() => SetQuestRayHovered(false);
        public void SelectWithQuestRay() => InvokeAction();

        private void InvokeAction()
        {
            MushSounds.PlayClick();
            action?.Invoke();
        }

        public void SetQuestRayHovered(bool hovered)
        {
            if (targetRenderer != null)
                targetRenderer.material.color = hovered ? new Color(0.85f, 0.48f, 0.14f) : normalColor;
        }
    }
}
