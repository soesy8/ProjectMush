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
        private GameObject pausePanel;
        private GameObject optionPanel;
        private TextMesh optionValues;
        [SerializeField] private GameObject authoredPausePanel;
        [SerializeField] private GameObject authoredOptionPanel;
        [SerializeField] private Canvas authoredCanvas;
        private InputAction leftXMenuAction;
        private bool leftXMenuArmed;
        private bool vrCanvasConfigured;

        private void OnEnable()
        {
            leftXMenuAction ??= new InputAction("Lobby Pause (VR X)", InputActionType.Button,
                "<XRController>{LeftHand}/primaryButton");
            leftXMenuAction.Enable();
            leftXMenuArmed = !leftXMenuAction.IsPressed();
        }

        private void OnDisable() => leftXMenuAction?.Disable();

        private void Update()
        {
            TryConfigureVrCanvas();
            if (!MushQuestTrackedInputRig.IsXrActive || leftXMenuAction == null)
                return;
            if (!leftXMenuAction.IsPressed())
                leftXMenuArmed = true;
            if (!leftXMenuArmed || !leftXMenuAction.WasPressedThisFrame())
                return;
            leftXMenuArmed = false;
            Toggle();
        }

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
            pausePanel = authoredPausePanel;
            optionPanel = authoredOptionPanel;
            BindAuthoredButtons();
        }

        private void Awake()
        {
            Active = this;
            viewCamera = authoredCanvas != null && authoredCanvas.worldCamera != null ? authoredCanvas.worldCamera : Camera.main;
            pausePanel = authoredPausePanel;
            optionPanel = authoredOptionPanel;
            BindAuthoredButtons();
        }

        private void Start() => TryConfigureVrCanvas();

        private void TryConfigureVrCanvas()
        {
            if (!vrCanvasConfigured && authoredCanvas != null &&
                MushQuestTrackedInputRig.IsXrActive && viewCamera != null)
            {
                MushVrUiLayout.PlaceFixed(authoredCanvas, viewCamera, 2.35f, 0.00125f, 0.12f);
                vrCanvasConfigured = true;
            }
        }

        private void OnDestroy()
        {
            leftXMenuAction?.Dispose();
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
            if (open && MushQuestTrackedInputRig.IsXrActive)
                MushVrUiLayout.PlaceFixed(authoredCanvas, viewCamera, 2.35f, 0.00125f, 0.12f);
            pausePanel.SetActive(open);
            if (!open && optionPanel != null)
                optionPanel.SetActive(false);
            Time.timeScale = open ? 0f : 1f;
            AudioListener.pause = open;
            MushSounds.SetLobbyMusicPaused(open);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void BindAuthoredButtons()
        {
            if (authoredPausePanel == null)
                return;
            Bind("Button_Resume", () => SetOpen(false));
            Bind("Image_Back", () => SetOpen(false));
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
            MushSceneTransition.Load("Title");
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
            MushSounds.SetLobbyMusicPaused(false);
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
