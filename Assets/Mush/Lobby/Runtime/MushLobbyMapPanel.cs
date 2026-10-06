using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Mush.Lobby
{
    // The canvas, labels, stars and buttons are authored in the scene.
    public sealed class MushLobbyMapPanel : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }
        private static readonly string[] Scenes = { "Track_v2", "Tree", "SharpCurve" };
        private static readonly string[] Names = { "기본 설원", "나무 숲", "급커브맵" };
        [SerializeField] private TMP_Text[] records = new TMP_Text[3];
        [SerializeField] private TMP_Text[] labels = new TMP_Text[3];
        [SerializeField] private MushMapStarGraphic[] stars = new MushMapStarGraphic[9];
        [SerializeField] private Button[] courseButtons = new Button[3];
        [SerializeField] private Button closeButton;
        [SerializeField] private MushLobbyController controller;
        [SerializeField] private TMP_Text message;
        [SerializeField] private Canvas canvas;
        private bool buttonsBound;

        public void Configure(MushLobbyController owner, Camera camera, Font koreanFont)
        {
            controller = owner;
            if (canvas == null || camera == null) return;
            transform.SetParent(camera.transform, false);
            transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
            canvas.worldCamera = camera;
            RectTransform rect = canvas.GetComponent<RectTransform>();
            canvas.renderMode = XRSettings.isDeviceActive ? RenderMode.WorldSpace : RenderMode.ScreenSpaceOverlay;
            if (XRSettings.isDeviceActive)
            {
                rect.sizeDelta = new Vector2(1920f, 1080f);
                rect.localScale = Vector3.one * 0.00125f;
                rect.localPosition = new Vector3(0f, 0f, 2.35f);
            }
            else
            {
                rect.localScale = Vector3.one;
                rect.localPosition = Vector3.zero;
            }
            BindButtons();
            RefreshRecords();
        }

        private void OnEnable()
        {
            IsOpen = true;
            if (canvas != null) { BindButtons(); RefreshRecords(); }
        }

        private void OnDisable() => IsOpen = false;

        private void BindButtons()
        {
            if (buttonsBound) return;
            for (int i = 0; i < Scenes.Length; i++)
            {
                int index = i;
                if (courseButtons[i] == null) continue;
                courseButtons[i].onClick.AddListener(() =>
                {
                    MushSounds.PlayClick();
                    controller?.HandleAction(index == 0 ? MushLobbyAction.SelectSnowfield :
                        index == 1 ? MushLobbyAction.SelectForest : MushLobbyAction.SelectSharpCurve);
                    if (!MushGameSave.IsStageUnlocked(Scenes[index]) && message != null)
                        message.text = "이전 스테이지를 완료하면 열립니다";
                });
            }
            if (closeButton != null)
                closeButton.onClick.AddListener(() =>
                {
                    MushSounds.PlayClick();
                    controller?.HandleAction(MushLobbyAction.ClosePanel);
                });
            buttonsBound = true;
        }

        public void RefreshRecords()
        {
            if (message != null) message.text = "지도를 선택하면 바로 출발합니다";
            for (int i = 0; i < Scenes.Length; i++)
            {
                bool unlocked = MushGameSave.IsStageUnlocked(Scenes[i]);
                if (labels[i] != null) labels[i].text = Names[i] + (unlocked ? string.Empty : "\n잠김");

                if (records[i] != null)
                    records[i].text = unlocked ? MushMapRecords.BestTimeLabel(Scenes[i]) : "이전 스테이지 완료 후 입장";
            }
        }
    }
}
