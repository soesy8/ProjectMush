using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Only binds authored scene UI. All panels and controls are saved in the scene.</summary>
[DefaultExecutionOrder(-1000)]
public sealed class MushSceneUI : MonoBehaviour
{
    [SerializeField] private MushMapRideBootstrap ride;
    [SerializeField] private GameObject titlePanel;
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private GameObject optionPanel;
    [SerializeField] private TMP_Text message;
    [SerializeField] private Slider master;
    [SerializeField] private Slider music;
    [SerializeField] private Slider effects;
    [SerializeField] private TMP_Text masterCount;
    [SerializeField] private TMP_Text musicCount;
    [SerializeField] private TMP_Text effectsCount;
    private Button continueButton;
    private bool ready;
    private bool leaving;
    private float nextSave;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    public static MushSceneUI Active { get; private set; }
    public static bool ModalOpen => Active != null && Active.OptionsOpen;
    private bool OptionsOpen => optionPanel != null && optionPanel.activeSelf;

    private void Awake()
    {
        Active = this;
        if (pausePanel != null) pausePanel.SetActive(false);
        if (optionPanel != null) optionPanel.SetActive(false);
        Bind(titlePanel, "TitleUI_Start", StartGame);
        continueButton = Bind(titlePanel, "TitleUI_Continue", ContinueGame);
        if (continueButton != null) continueButton.interactable = MushGameSave.HasSave;
        Bind(titlePanel, "TitleUI_Option", OpenOptions);
        Bind(titlePanel, "TitleUI_Quit", QuitGame);
        Bind(pausePanel, "Button_Resume", Resume);
        Bind(pausePanel, "Button_Recover", () => ride?.RecoverToCourse());
        Bind(pausePanel, "Button_Option", OpenOptions);
        Bind(pausePanel, "Button_Lobby", ReturnToLobby);
        Bind(pausePanel, "Button_Quit", QuitGame);
        Bind(optionPanel, "Button_Close", CloseOptions);
        if (master != null) master.SetValueWithoutNotify(MushAudioSettings.Master);
        if (music != null) music.SetValueWithoutNotify(MushAudioSettings.Music);
        if (effects != null) effects.SetValueWithoutNotify(MushAudioSettings.Effects);
        if (master != null) master.onValueChanged.AddListener(OnVolumeChanged);
        if (music != null) music.onValueChanged.AddListener(OnVolumeChanged);
        if (effects != null) effects.onValueChanged.AddListener(OnVolumeChanged);
        RefreshVolumeCounts();
        // Explicitly enable the new Input System's default UI action set on the authored module.
        foreach (InputSystemUIInputModule module in GetComponentsInChildren<InputSystemUIInputModule>(true))
            if (module.actionsAsset == null) module.AssignDefaultActions();
        if (titlePanel != null) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    }

    private IEnumerator Start()
    {
        // Ride construction binds its authored sled in Start; restore only after that binding.
        yield return null;
        if (ride != null && MushGameSave.ConsumeRideRestore(gameObject.scene.name)) ride.RestoreSavedRide(MushGameSave.Current);
        else if (gameObject.scene.name == "MushLobby") MushGameSave.EnterLobby();
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            foreach (AudioSource source in root.GetComponentsInChildren<AudioSource>(true))
                if (!source.TryGetComponent<MushAudioChannel>(out _)) source.gameObject.AddComponent<MushAudioChannel>();
        ready = true;
        if (titlePanel != null) SelectFirst(titlePanel);
        if (titlePanel == null) SaveCurrent();
        nextSave = Time.unscaledTime + 15f;
    }

    private void Update()
    {
        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
        {
            if (OptionsOpen) CloseOptions();
            else if (ride != null && !ride.HasFinished) ride.SetRidePaused(!ride.IsPaused);
            else if (titlePanel != null) OpenOptions();
        }
        if (!ready || leaving || titlePanel != null) return;
        if (Time.unscaledTime >= nextSave)
        {
            SaveCurrent();
            nextSave = Time.unscaledTime + 15f;
        }
    }

    public void ShowPause(bool paused)
    {
        if (pausePanel == null) return;
        if (paused)
        {
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SaveCurrent();
        }
        else
        {
            CloseOptions();
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }
        pausePanel.SetActive(paused);
        SelectFirst(paused ? pausePanel : null);
    }

    private void StartGame() { MushGameSave.NewGame(); Load("MushLobby"); }
    private void ContinueGame()
    {
        string scene = MushGameSave.ContinueGame();
        if (scene != null) Load(scene);
        else { if (continueButton != null) continueButton.interactable = false; SetMessage("불러올 수 있는 저장 데이터가 없습니다."); }
    }
    private void Resume() => ride?.SetRidePaused(false);
    public void OpenOptions()
    {
        if (optionPanel == null) return;
        optionPanel.SetActive(true);
        optionPanel.transform.SetAsLastSibling();
        SelectFirst(optionPanel);
    }
    private void CloseOptions()
    {
        if (optionPanel != null) optionPanel.SetActive(false);
        MushAudioSettings.Flush();
        SelectFirst(ride != null && ride.IsPaused ? pausePanel : titlePanel);
    }
    private void OnVolumeChanged(float _)
    {
        MushAudioSettings.Set(master.value, music.value, effects.value);
        RefreshVolumeCounts();
    }
    private void RefreshVolumeCounts()
    {
        if (masterCount != null) masterCount.SetText("{0:0}", MushAudioSettings.Master * 100f);
        if (musicCount != null) musicCount.SetText("{0:0}", MushAudioSettings.Music * 100f);
        if (effectsCount != null) effectsCount.SetText("{0:0}", MushAudioSettings.Effects * 100f);
    }
    public bool SaveCurrent()
    {
        if (!ready || titlePanel != null || leaving) return false;
        if (ride != null) ride.CaptureSavedRide(MushGameSave.Current);
        else { MushGameSave.Current.scene = gameObject.scene.name; MushGameSave.Current.riding = false; }
        return MushGameSave.Save();
    }
    private void SetMessage(string value) { if (message != null) message.text = value; }
    private void ReturnToLobby()
    {
        if (ride != null) { leaving = true; ride.ReturnToLobby(); }
        else { MushGameSave.EnterLobby(); Load("MushLobby"); }
    }
    private void QuitGame()
    {
        if (titlePanel == null && !SaveCurrent()) { SetMessage("Save failed. Please try again."); return; }
        MushAudioSettings.Flush();
        leaving = true;
        Time.timeScale = 1f;
        AudioListener.pause = false;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
    private void Load(string scene)
    {
        if (!Application.CanStreamedLevelBeLoaded(scene)) { SetMessage("Scene unavailable: " + scene); return; }
        leaving = true;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        SceneManager.LoadSceneAsync(scene);
    }
    private void OnApplicationPause(bool paused) { if (paused) { SaveCurrent(); MushAudioSettings.Flush(); } }
    private void OnApplicationQuit() { if (!leaving) SaveCurrent(); MushAudioSettings.Flush(); }
    private void OnDestroy() { if (Active == this) Active = null; }

    private static Button Bind(GameObject root, string name, UnityEngine.Events.UnityAction action)
    {
        if (root == null) return null;
        foreach (Button button in root.GetComponentsInChildren<Button>(true))
            if (button.name == name) { button.onClick.AddListener(action); return button; }
        return null;
    }
    private static void SelectFirst(GameObject root)
    {
        if (EventSystem.current == null) return;
        Selectable selected = root != null ? root.GetComponentInChildren<Selectable>() : null;
        EventSystem.current.SetSelectedGameObject(selected != null ? selected.gameObject : null);
    }
}
