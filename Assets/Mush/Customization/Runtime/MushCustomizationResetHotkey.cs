using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Mush.Customization
{
    /// <summary>
    /// Development reset available from every playable scene. R clears only
    /// store ownership/equipment/housing customization and reloads the current
    /// scene so the reset is immediately visible.
    /// </summary>
    internal sealed class MushCustomizationResetHotkey : MonoBehaviour
    {
        private bool resetting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<MushCustomizationResetHotkey>() != null)
                return;

            GameObject resetObject = new("Mush Customization Reset Hotkey");
            DontDestroyOnLoad(resetObject);
            resetObject.AddComponent<MushCustomizationResetHotkey>();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (resetting || keyboard == null || !keyboard.rKey.wasPressedThisFrame)
                return;

            // Ride maps reserve plain R for returning the complete sled team
            // to its last safe course checkpoint. Keep the prototype data
            // reset shortcut available in lobby/customization scenes only.
            if (FindFirstObjectByType<MushMapRideBootstrap>() != null)
                return;

            resetting = true;
            MushCustomizationSave.Reset();
            Debug.Log("[Mush] 커스터마이징과 상점 보유 목록을 기본 상태로 초기화했습니다.", this);

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && !string.IsNullOrEmpty(activeScene.name))
                SceneManager.LoadScene(activeScene.name);
            else
                resetting = false;
        }
    }
}
