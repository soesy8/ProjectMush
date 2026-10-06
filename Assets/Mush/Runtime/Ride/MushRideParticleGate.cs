using UnityEngine;

/// <summary>Starts the supplied ride snow/fog only while the sled is actually moving.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(2000)]
public sealed class MushRideParticleGate : MonoBehaviour
{
    [SerializeField] private MushMapRideBootstrap ride;
    private ParticleSystem[] particles;

    public static void Install(MushMapRideBootstrap owner)
    {
        bool hasArtSnow = false;
        foreach (GameObject root in owner.gameObject.scene.GetRootGameObjects())
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name.StartsWith("TrackVFX_Snow", System.StringComparison.Ordinal))
                    hasArtSnow = true;

        foreach (GameObject root in owner.gameObject.scene.GetRootGameObjects())
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                string name = child.name;
                if (hasArtSnow && (name == "Mush Speed Snow" || name == "FX_AmbientSnow_Rebuilt"))
                {
                    foreach (ParticleSystem system in child.GetComponentsInChildren<ParticleSystem>(true))
                        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    child.gameObject.SetActive(false);
                    continue;
                }
                if (!name.StartsWith("TrackVFX_Snow", System.StringComparison.Ordinal) &&
                    !name.StartsWith("VFX_SnowFog", System.StringComparison.Ordinal) &&
                    name != "Mush Speed Snow" && name != "FX_AmbientSnow_Rebuilt") continue;
                MushRideParticleGate gate = child.GetComponent<MushRideParticleGate>();
                if (gate == null) gate = child.gameObject.AddComponent<MushRideParticleGate>();
                gate.Configure(owner);
            }
    }

    public void Configure(MushMapRideBootstrap owner)
    {
        ride = owner;
        particles = GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem system in particles)
        {
            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void Awake()
    {
        particles = GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem system in particles)
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void LateUpdate()
    {
        bool moving = ride != null && ride.IsMoving && Time.timeScale > 0f;
        foreach (ParticleSystem system in particles)
        {
            if (moving)
            {
                if (!system.isPlaying && system.gameObject.activeInHierarchy) system.Play(false);
            }
            else if (system.isPlaying || system.particleCount > 0)
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
