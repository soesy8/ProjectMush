using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mush.Quest
{
    /// <summary>Recovers Unity audio when a headset connects or its output device changes.</summary>
    public sealed class MushVrAudioRecovery : MonoBehaviour
    {
        private static MushVrAudioRecovery instance;
        private bool wasVrActive;
        private bool recoveryPending;
        private float recoverAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (instance != null) return;
            GameObject root = new("Mush VR Audio Recovery");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<MushVrAudioRecovery>();
        }

        private void OnEnable()
        {
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!MushQuestTrackedInputRig.IsXrActive) return;
            EnsureHeadListener();
            if (Time.timeScale > 0f && !MushSceneUI.ModalOpen &&
                !(Mush.Lobby.MushLobbyPauseMenu.Active != null && Mush.Lobby.MushLobbyPauseMenu.Active.IsOpen))
                AudioListener.pause = false;
        }

        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            if (!deviceWasChanged || !MushQuestTrackedInputRig.IsXrActive) return;
            ScheduleRecovery();
        }

        private void ScheduleRecovery()
        {
            recoveryPending = true;
            recoverAt = Time.unscaledTime + 1f;
        }

        private void Update()
        {
            bool vrActive = MushQuestTrackedInputRig.IsXrActive;
            if (vrActive != wasVrActive)
            {
                MushAudioSettings.UseVrProfile(vrActive);
                if (vrActive) ScheduleRecovery();
                else recoveryPending = false;
                wasVrActive = vrActive;
            }
            if (!vrActive || !recoveryPending || Time.unscaledTime < recoverAt || AudioListener.pause) return;
            recoveryPending = false;
            EnsureHeadListener();
            List<(AudioSource source, AudioClip clip, int sample)> playing = new();
            foreach (AudioSource source in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
                if (source.isPlaying && source.clip != null)
                    playing.Add((source, source.clip, source.timeSamples));
            // Preserve the existing configuration; do not change Windows or PC audio settings.
            if (!AudioSettings.Reset(AudioSettings.GetConfiguration()))
            {
                Debug.LogWarning("[Mush VR] 오디오 재연결에 실패했습니다. Meta Horizon Link의 VR 오디오 출력 장치를 확인해 주세요.");
                return;
            }
            foreach (var state in playing)
            {
                if (state.source == null || !state.source.isActiveAndEnabled || state.source.clip != state.clip) continue;
                if (!state.source.isPlaying) state.source.Play();
                if (state.clip.loadState == AudioDataLoadState.Loaded && state.clip.samples > 0)
                    state.source.timeSamples = Mathf.Clamp(state.sample, 0, state.clip.samples - 1);
            }
            MushSounds.ResumeAfterVrAudioRecovery();
        }

        private static void EnsureHeadListener()
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            AudioListener headListener = camera.GetComponent<AudioListener>();
            if (headListener == null) headListener = camera.gameObject.AddComponent<AudioListener>();
            headListener.enabled = true;
            foreach (AudioListener listener in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (listener != headListener) listener.enabled = false;
            AudioListener.volume = MushAudioSettings.Master;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
