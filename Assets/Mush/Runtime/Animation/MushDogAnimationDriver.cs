using UnityEngine;

namespace Mush.DogAnimation
{
    public enum DogAnimationState { Idle = 0, Walk = 1, Sit = 2, Eat = 3, Bark = 4 }
    public enum DogAnimationRequestResult { Accepted, Queued, IgnoredDuplicate, RejectedBusy, Unavailable, InvalidState }
    public enum DogEatExitPolicy { Immediate, FinishCurrentCycle }

    /// <summary>Input-independent owner of the five-state dog Animator contract.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Animator))]
    public sealed class MushDogAnimationDriver : MonoBehaviour
    {
        [SerializeField] private DogEatExitPolicy eatExitPolicy = DogEatExitPolicy.Immediate;
        private static readonly int StateParameter = Animator.StringToHash("State");
        private static readonly int[] StateHashes = {
            Animator.StringToHash("Base Layer.Idle"), Animator.StringToHash("Base Layer.Walk"),
            Animator.StringToHash("Base Layer.Sit"), Animator.StringToHash("Base Layer.Eat"),
            Animator.StringToHash("Base Layer.Bark")
        };

        private Animator animator;
        private DogAnimationState current;
        private DogAnimationState? pending;
        private float eatExitBoundary = -1f;
        private bool ready;
        private int barkRequestedFrame = -1;

        public DogAnimationState CurrentState => current;
        public DogAnimationState? PendingState => pending;
        public bool IsReady => ready && isActiveAndEnabled && animator != null && animator.isActiveAndEnabled;
        public bool IsLocked => current == DogAnimationState.Bark;
        public DogEatExitPolicy EatExitPolicy { get => eatExitPolicy; set => eatExitPolicy = value; }

        public DogAnimationRequestResult Idle() => Request(DogAnimationState.Idle);
        public DogAnimationRequestResult Walk() => Request(DogAnimationState.Walk);
        public DogAnimationRequestResult Sit() => Request(DogAnimationState.Sit);
        public DogAnimationRequestResult Eat() => Request(DogAnimationState.Eat);
        public DogAnimationRequestResult Bark() => Request(DogAnimationState.Bark);

        private void Awake() { animator = GetComponent<Animator>(); }
        private void OnEnable()
        {
            if (animator == null) animator = GetComponent<Animator>();
            ready = ValidateController();
            if (ready) ResetToIdle();
        }
        private void OnDisable() { pending = null; eatExitBoundary = -1f; }

        private bool ValidateController()
        {
            if (animator == null || animator.runtimeAnimatorController == null) return false;
            bool parameter = false;
            foreach (var p in animator.parameters)
                if (p.nameHash == StateParameter && p.type == AnimatorControllerParameterType.Int) parameter = true;
            foreach (int hash in StateHashes)
                if (!animator.HasState(0, hash)) return false;
            if (!parameter) return false;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return true;
        }

        public DogAnimationRequestResult Request(DogAnimationState state)
        {
            if ((int)state < 0 || (int)state >= StateHashes.Length) return DogAnimationRequestResult.InvalidState;
            if (!IsReady) return DogAnimationRequestResult.Unavailable;
            if (state == current || (pending.HasValue && pending.Value == state))
                return DogAnimationRequestResult.IgnoredDuplicate;
            // Bark completes once, returns Idle, and never queues requests behind it.
            if (current == DogAnimationState.Bark) return DogAnimationRequestResult.RejectedBusy;
            if (current == DogAnimationState.Eat && eatExitPolicy == DogEatExitPolicy.FinishCurrentCycle)
            {
                pending = state; // Latest distinct request wins; duplicate Eat does not cancel it.
                if (eatExitBoundary < 0f)
                {
                    var info = animator.GetCurrentAnimatorStateInfo(0);
                    if (info.fullPathHash == StateHashes[(int)DogAnimationState.Eat])
                        eatExitBoundary = Mathf.Floor(info.normalizedTime) + 1f;
                }
                return DogAnimationRequestResult.Queued;
            }
            Begin(state);
            return DogAnimationRequestResult.Accepted;
        }

        /// <summary>Explicit lifecycle reset, bypasses locks. Also used after pooling/re-enable.</summary>
        public void ResetToIdle()
        {
            if (!ready || animator == null || !animator.isActiveAndEnabled) return;
            pending = null;
            eatExitBoundary = -1f;
            current = DogAnimationState.Idle;
            barkRequestedFrame = -1;
            animator.applyRootMotion = false;
            animator.speed = 1f;
            animator.SetInteger(StateParameter, (int)current);
            animator.Play(StateHashes[(int)current], 0, 0f);
        }

        private void Begin(DogAnimationState state)
        {
            current = state;
            barkRequestedFrame = state == DogAnimationState.Bark ? Time.frameCount : -1;
            pending = null;
            eatExitBoundary = -1f;
            animator.SetInteger(StateParameter, (int)state);
            // A new Bark may be accepted after completion but before the Animator
            // has left the clamped Bark state. There is deliberately no self-transition.
            // Restart only this completed instance, never an in-flight duplicate.
            var info = animator.GetCurrentAnimatorStateInfo(0);
            if (state == DogAnimationState.Bark && info.fullPathHash == StateHashes[(int)state]
                && info.normalizedTime >= 1f)
                animator.Play(StateHashes[(int)state], 0, 0f);
        }

        private void LateUpdate()
        {
            if (!IsReady) return;
            var info = animator.GetCurrentAnimatorStateInfo(0);
            if (info.fullPathHash != StateHashes[(int)current]) return;
            if (current == DogAnimationState.Bark && Time.frameCount == barkRequestedFrame) return;
            if (current == DogAnimationState.Bark && info.normalizedTime >= 1f)
                Begin(DogAnimationState.Idle);
            else if (current == DogAnimationState.Eat && pending.HasValue)
            {
                if (eatExitPolicy == DogEatExitPolicy.Immediate) { Begin(pending.Value); return; }
                if (eatExitBoundary < 0f) eatExitBoundary = Mathf.Floor(info.normalizedTime) + 1f;
                if (info.normalizedTime >= eatExitBoundary) Begin(pending.Value);
            }
        }
    }
}
