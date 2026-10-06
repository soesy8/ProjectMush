using System.Collections.Generic;
using UnityEngine;

/// <summary>Receives paw-contact events from ride-only animation overrides.</summary>
[RequireComponent(typeof(Animator))]
[DisallowMultipleComponent]
public sealed class MushDogFootstepAudio : MonoBehaviour
{
    private static readonly string[] PawNames = { "front_toe.L", "front_toe.R", "toe.L", "toe.R" };
    private readonly AudioSource[] sources = new AudioSource[4];
    private MushMapRideBootstrap ride;
    private MushSoundBank bank;
    private Animator animator;
    private RuntimeAnimatorController originalController;
    private AnimatorOverrideController overrideController;
    private int previousClip = -1;

    public void Configure(MushMapRideBootstrap owner)
    {
        ride = owner;
        bank = MushSoundBank.Load();
        animator = GetComponent<Animator>();
        if (bank == null || overrideController != null) return;
        Transform[] bones = GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < PawNames.Length; index++)
        {
            Transform paw = transform;
            foreach (Transform bone in bones)
                if (bone.name == PawNames[index]) { paw = bone; break; }
            GameObject sound = new("Paw Snow " + index);
            sound.transform.SetParent(paw, false);
            AudioSource source = sound.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2f;
            source.maxDistance = 20f;
            sound.AddComponent<MushAudioChannel>();
            sources[index] = source;
        }

        originalController = animator.runtimeAnimatorController;
        if (originalController == null) return;
        overrideController = new AnimatorOverrideController(originalController)
        {
            name = originalController.name + " Paw Contacts"
        };
        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(overrideController.overridesCount);
        overrideController.GetOverrides(overrides);
        for (int index = 0; index < overrides.Count; index++)
        {
            AnimationClip replacement = overrides[index].Key.name switch
            {
                "KAI_Run" => bank.kaiRunFootsteps,
                "LUMI_Run" => bank.lumiRunFootsteps,
                _ => null
            };
            if (replacement != null)
                overrides[index] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[index].Key, replacement);
        }
        overrideController.ApplyOverrides(overrides);
        animator.runtimeAnimatorController = overrideController;
    }

    public void OnPawContact(int pawIndex)
    {
        if (!isActiveAndEnabled || ride == null || !ride.IsMoving || AudioListener.pause ||
            Time.timeScale <= 0f || bank == null || bank.pawSnow == null || bank.pawSnow.Length == 0 ||
            pawIndex < 0 || pawIndex >= sources.Length || sources[pawIndex] == null) return;
        int next = Random.Range(0, bank.pawSnow.Length);
        if (bank.pawSnow.Length > 1 && next == previousClip)
            next = (next + Random.Range(1, bank.pawSnow.Length)) % bank.pawSnow.Length;
        previousClip = next;
        AudioClip clip = bank.pawSnow[next];
        if (clip != null) sources[pawIndex].PlayOneShot(clip);
    }

    private void OnDisable()
    {
        foreach (AudioSource source in sources)
            if (source != null) source.Stop();
    }

    private void OnDestroy()
    {
        if (animator != null && animator.runtimeAnimatorController == overrideController)
            animator.runtimeAnimatorController = originalController;
        if (overrideController != null) Destroy(overrideController);
    }
}
