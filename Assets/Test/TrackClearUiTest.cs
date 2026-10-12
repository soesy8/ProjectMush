using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Mush.Prototype;
using Mush.Quest;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mush.Testing
{
    /// <summary>Runs the real clear UI sequence in the copied scene without keeping test saves.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1100)]
    public sealed class TrackClearUiTest : MonoBehaviour
    {
        public const string ScenePath = "Assets/Test/Track_v2_ClearUITest.unity";

        [SerializeField] private MushMapRideBootstrap ride;
        [SerializeField, Min(0.1f), Tooltip("출발 후 이 거리(m)만큼 주행하면 클리어 UI를 표시합니다.")]
        private float clearDistanceMeters = 5f;

        private MushSledKeyboardController controller;
        private Vector3 previousPosition;
        private float travelledMeters;

        private void Start()
        {
            if (gameObject.scene.path != ScenePath)
            {
                enabled = false;
                return;
            }
            controller = ride != null ? ride.GetComponentInChildren<MushSledKeyboardController>(true) : null;
            if (controller == null || ride.RideCamera == null)
            {
                Debug.LogError("[Clear UI Test] The copied ride has not initialized.", this);
                enabled = false;
                return;
            }
            previousPosition = controller.transform.position;
        }

        private void LateUpdate()
        {
            if (controller == null || ride == null || ride.HasFinished)
                return;

            Vector3 movement = controller.transform.position - previousPosition;
            previousPosition = controller.transform.position;
            if (!ride.HasRideStarted || ride.IsPaused || Time.deltaTime <= 0f)
                return;

            // Sample after the sled follower. Ignore height adjustments and recovery teleports,
            // and keep refreshing the baseline while waiting for the player to depart.
            movement.y = 0f;
            float step = movement.magnitude;
            float plausibleStep = Mathf.Max(3f, controller.CurrentSpeed * Time.deltaTime * 4f + 0.5f);
            if (step > plausibleStep)
                return;
            travelledMeters += step;
            if (travelledMeters < clearDistanceMeters)
                return;

            enabled = false;
            ShowResultWithoutSaving();
        }

        private void ShowResultWithoutSaving()
        {
            MethodInfo complete = typeof(MushMapRideBootstrap).GetMethod(
                "ShowDeliveryResult", BindingFlags.Instance | BindingFlags.NonPublic);
            if (complete == null)
                throw new MissingMethodException(nameof(MushMapRideBootstrap), "ShowDeliveryResult");

            // The normal completion method writes a save and record. Keep its UI behavior,
            // then restore only the files/keys it touches; scene autosave is disabled in this copy.
            string savedState = JsonUtility.ToJson(MushGameSave.Current);
            string savePath = Path.Combine(Application.persistentDataPath, "mush-save.json");
            var savedFiles = new Dictionary<string, byte[]>();
            foreach (string path in new[] { savePath, savePath + ".bak", savePath + ".tmp" })
                savedFiles.Add(path, File.Exists(path) ? File.ReadAllBytes(path) : null);

            string timeKey = "Mush.BestCompletionSeconds." + gameObject.scene.name;
            string starsKey = "Mush.BestStars." + gameObject.scene.name;
            bool hadTime = PlayerPrefs.HasKey(timeKey);
            bool hadStars = PlayerPrefs.HasKey(starsKey);
            float previousTime = PlayerPrefs.GetFloat(timeKey);
            int previousStars = PlayerPrefs.GetInt(starsKey);
            try
            {
                complete.Invoke(ride, null);

                // This scene has no registered stage ID. Preview the source track's
                // reward using the same star-based calculation as real completion.
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                int stars = (int)typeof(MushMapRideBootstrap).GetField("earnedStars", flags).GetValue(ride);
                typeof(MushMapRideBootstrap).GetField("earnedGold", flags)
                    .SetValue(ride, MushGameSave.StageReward("Track_v2", stars));
                typeof(MushMapRideBootstrap).GetMethod("LayoutResultPanel", flags).Invoke(ride, null);
            }
            finally
            {
                JsonUtility.FromJsonOverwrite(savedState, MushGameSave.Current);
                foreach (var savedFile in savedFiles)
                {
                    if (savedFile.Value != null) File.WriteAllBytes(savedFile.Key, savedFile.Value);
                    else if (File.Exists(savedFile.Key)) File.Delete(savedFile.Key);
                }
                if (hadTime) PlayerPrefs.SetFloat(timeKey, previousTime);
                else PlayerPrefs.DeleteKey(timeKey);
                if (hadStars) PlayerPrefs.SetInt(starsKey, previousStars);
                else PlayerPrefs.DeleteKey(starsKey);
                PlayerPrefs.Save();
            }

            // The test scene deliberately stays out of Build Settings. Reload its asset
            // directly in the Editor so the result's retry button can replay the sequence.
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
                foreach (MushTrackResultView view in root.GetComponentsInChildren<MushTrackResultView>(true))
                    foreach (MushQuestRayAction button in view.GetComponentsInChildren<MushQuestRayAction>(true))
                        if (!button.name.Contains("로비"))
                            button.Configure(ReloadTestScene, button.GetComponent<Renderer>(),
                                new Color(1.4f, 1.15f, 0.45f));

            Debug.Log($"[Clear UI Test] Cleared after {travelledMeters:F2} m; original save and records restored.", this);
        }

        private void ReloadTestScene()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(gameObject.scene.name);
#endif
        }
    }
}
