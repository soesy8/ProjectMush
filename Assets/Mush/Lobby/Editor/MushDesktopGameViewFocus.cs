using UnityEditor;
using UnityEngine;
using UnityEngine.XR;

namespace Mush.Lobby.Editor
{
    [InitializeOnLoad]
    internal static class MushDesktopGameViewFocus
    {
        static MushDesktopGameViewFocus()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || XRSettings.isDeviceActive)
                return;

            MushDesktopSeatedLook look = Object.FindAnyObjectByType<MushDesktopSeatedLook>();
            if (look == null || !look.isActiveAndEnabled)
                return;

            // Give desktop gameplay focus at startup so cursor capture needs no first click.
            System.Type gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType == null)
                return;
            EditorWindow.GetWindow(gameViewType, false, "Game", true).Focus();
        }
    }
}
