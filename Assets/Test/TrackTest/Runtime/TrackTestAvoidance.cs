using Mush.Prototype;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mush.Testing.TrackTest
{
    /// <summary>Test controls and diagnostics for the shared track avoidance.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class TrackTestAvoidance : MushTrackObstacleAvoidance
    {
        public const string ScenePath = "Assets/Test/TrackTest/Track_v2_TrackTest.unity";

        [Header("테스트 도구")]
        [SerializeField] private GameObject demonstrationObstacles;
        [SerializeField] private bool showDiagnostics = true;
        private float nextHud;
        private string hud;

        protected override void Start()
        {
            if (gameObject.scene.path != ScenePath)
            {
                enabled = false;
                return;
            }
            base.Start();
        }

        protected override void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f8Key.wasPressedThisFrame)
                SetAvoidanceEnabled(!AvoidanceEnabled);
            if (keyboard != null && keyboard.f9Key.wasPressedThisFrame && demonstrationObstacles != null)
            {
                demonstrationObstacles.SetActive(!demonstrationObstacles.activeSelf);
                ResetState();
            }
            base.Update();
        }

        private void OnGUI()
        {
            if (!showDiagnostics || Controller == null) return;
            if (hud == null || Time.unscaledTime >= nextHud)
            {
                hud = $"TRACK TEST  |  F8 Avoidance: {(AvoidanceEnabled ? "ON" : "OFF")}  |  F9 Demo props\n" +
                    $"Space: start  A/D: steer  W: boost  P: mouse look  F10: restart\n" +
                    $"Speed {Controller.CurrentSpeed:F1} m/s  |  Extra yaw {AppliedYawRate:F1} deg/s  |  Nearby {CandidateCount}\n" +
                    $"Target: {FocusName}  |  Safety corrections: {SafetyCorrections}\n" +
                    $"Avoidance sample: {AverageMicroseconds:F0} us/call (Editor; see Profiler marker)";
                nextHud = Time.unscaledTime + 0.25f;
            }
            GUI.Box(new Rect(12f, 12f, 590f, 112f), hud);
        }
    }
}
