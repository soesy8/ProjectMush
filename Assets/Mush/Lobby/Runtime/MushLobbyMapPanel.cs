using Mush.UI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Mush.Lobby
{
    public sealed class MushLobbyMapPanel : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }
        private static readonly string[] Scenes = { "snow", "Tree", "SharpCurve" };
        private static readonly string[] Names = { "기본 설원", "나무 숲", "급커브맵" };
        private readonly Text[] records = new Text[3];
        private readonly Text[] labels = new Text[3];
        private readonly MushMapStarGraphic[,] stars = new MushMapStarGraphic[3, 3];
        private MushLobbyController controller;
        private Font font;
        private Text message;
        private Canvas canvas;

        public void Configure(MushLobbyController owner, Camera camera, Font koreanFont)
        {
            controller = owner;
            font = koreanFont;
            if (canvas == null)
            {
                foreach (Transform child in transform) child.gameObject.SetActive(false);
                Build(camera);
            }
            transform.SetParent(camera.transform, false);
            transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
            Refresh();
        }

        private void OnEnable() { IsOpen = true; if (canvas != null) Refresh(); }
        private void OnDisable() => IsOpen = false;

        private void Build(Camera camera)
        {
            GameObject root = new("Map Menu Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.worldCamera = camera;
            canvas.sortingOrder = 150;
            canvas.renderMode = XRSettings.isDeviceActive ? RenderMode.WorldSpace : RenderMode.ScreenSpaceOverlay;
            RectTransform rect = root.GetComponent<RectTransform>();
            if (XRSettings.isDeviceActive)
            {
                rect.sizeDelta = new Vector2(1920f, 1080f);
                rect.localScale = Vector3.one * 0.00125f;
                rect.localPosition = new Vector3(0f, 0f, 2.35f);
            }
            else
            {
                CanvasScaler scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }
            root.AddComponent<MushCanvasQuestInput>();
            Image backdrop = Rect(root.transform, "Opaque Backdrop", Vector2.zero, new Vector2(1920f, 1080f)).gameObject.AddComponent<Image>();
            backdrop.color = new Color(0.055f, 0.075f, 0.085f, 1f);
            MushUiPanelSkin.CreateCanvasPanel(root.transform, "Map Board", Vector2.zero, new Vector2(1580f, 880f));
            Transform board = root.transform.Find("Map Board");
            if (board != null)
            {
                foreach (RectTransform leaf in board.GetComponentsInChildren<RectTransform>(true))
                {
                    if (leaf.name != "Image_Leaf" && leaf.name != "Image_Leaf_2") continue;
                    leaf.anchorMin = leaf.anchorMax = Vector2.one;
                    leaf.anchoredPosition = leaf.name == "Image_Leaf" ? new Vector2(-185f, -85f) : new Vector2(-125f, -125f);
                    leaf.sizeDelta = Vector2.one * 140f;
                    leaf.localScale = Vector3.one;
                }
            }
            backdrop.transform.SetAsFirstSibling();
            Label(root.transform, "맵 게시판", new Vector2(0f, 325f), new Vector2(1250f, 100f), 62);
            message = Label(root.transform, "지도를 선택하면 바로 출발합니다", new Vector2(0f, 225f), new Vector2(1400f, 75f), 30);
            for (int i = 0; i < Scenes.Length; i++)
            {
                int index = i;
                float x = (i - 1) * 460f;
                Button button = MakeButton(root.transform, Names[i], new Vector2(x, 40f), new Vector2(405f, 170f));
                labels[i] = button.GetComponentInChildren<Text>(true);
                button.onClick.AddListener(() =>
                {
                    MushSounds.PlayClick();
                    controller.HandleAction(index == 0 ? MushLobbyAction.SelectSnowfield :
                        index == 1 ? MushLobbyAction.SelectForest : MushLobbyAction.SelectSharpCurve);
                    if (!MushGameSave.IsStageUnlocked(Scenes[index])) message.text = "이전 스테이지를 완료하면 열립니다";
                });
                for (int star = 0; star < 3; star++)
                {
                    MushMapStarGraphic icon = Rect(root.transform, Names[i] + " Star " + (star + 1),
                        new Vector2(x + (star - 1) * 76f, -105f), new Vector2(60f, 60f))
                        .gameObject.AddComponent<MushMapStarGraphic>();
                    icon.raycastTarget = false;
                    stars[i, star] = icon;
                }
                records[i] = Label(root.transform, string.Empty, new Vector2(x, -195f), new Vector2(420f, 110f), 28);
            }
            Button close = MakeButton(root.transform, "닫기", new Vector2(0f, -330f), new Vector2(300f, 85f));
            close.onClick.AddListener(() => { MushSounds.PlayClick(); controller.HandleAction(MushLobbyAction.ClosePanel); });
            Material overlay = MushSoundBank.Load()?.overlayMaterial;
            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
                if (overlay != null) graphic.material = overlay;
        }

        private void Refresh()
        {
            message.text = "지도를 선택하면 바로 출발합니다";
            for (int i = 0; i < Scenes.Length; i++)
            {
                bool unlocked = MushGameSave.IsStageUnlocked(Scenes[i]);
                labels[i].text = Names[i] + (unlocked ? string.Empty : "\n잠김");
                int earned = MushMapRecords.GetBestStars(Scenes[i]);
                for (int star = 0; star < 3; star++) stars[i, star].SetEarned(star < earned);
                records[i].text = unlocked ? MushMapRecords.BestTimeLabel(Scenes[i]) : "이전 스테이지 완료 후 입장";
            }
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = Vector2.one * 0.5f;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private Text Label(Transform parent, string content, Vector2 position, Vector2 size, int fontSize)
        {
            Text text = Rect(parent, "Label", position, size).gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = content;
            text.raycastTarget = false;
            return text;
        }

        private Button MakeButton(Transform parent, string label, Vector2 position, Vector2 size)
        {
            RectTransform rect = Rect(parent, label, position, size);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.27f, 0.13f, 0.07f, 1f);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            Label(rect, label, Vector2.zero, size - Vector2.one * 18f, 42);
            return button;
        }
    }
}
