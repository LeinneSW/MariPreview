using UnityEngine;

[DisallowMultipleComponent]
public sealed class MariAnimator : MonoBehaviour
{
    public enum Motion { Idle, Walk, Run, Crouch }

    private static readonly int MotionParameter = Animator.StringToHash("Motion");
    private static readonly int CrouchParameter = Animator.StringToHash("CrouchAmount");
    private static readonly int GroundedParameter = Animator.StringToHash("Grounded");
    private static readonly int JumpParameter = Animator.StringToHash("Jump");
    private static readonly int[] GroundedStates =
    {
        Animator.StringToHash("Base Layer.Grounded.Idle"),
        Animator.StringToHash("Base Layer.Grounded.Walk"),
        Animator.StringToHash("Base Layer.Grounded.Run"),
        Animator.StringToHash("Base Layer.Grounded.Crouch")
    };
    private static readonly string[] NativeGroundedMotions = { "Normal_Idle", "Cafe_Walk", "Move_Ing", "Kneel_Idle" };

    private Animator animator;
    private RuntimeAnimatorController boundController;
    private Motion motion = Motion.Idle;
    private float crouchAmount = 0.5f;
    private bool grounded = true;
    private bool hasMotion, hasCrouch, hasGrounded, hasJump;
    private bool hasRequestedState;

    public Animator Animator => animator;
    public Motion CurrentMotion => motion;
    public bool UsesMotionPreset { get; private set; } = true;

    private void Awake()
    {
        if (!BindAnimator()) return;
        if (hasMotion) animator.SetInteger(MotionParameter, (int)motion);
        if (hasCrouch) animator.SetFloat(CrouchParameter, crouchAmount);
        if (hasGrounded) animator.SetBool(GroundedParameter, grounded);
    }

    private void Start()
    {
        if (GetComponent<MariNativeMotion>() != null) return;
        if (!hasRequestedState && BindAnimator() && animator.HasState(0, GroundedStates[0]))
            SetMotion(Motion.Idle, 0f);
    }

    public bool SetMotion(Motion next, float transitionTime = 0.12f, bool restart = false)
    {
        int index = (int)next;
        if (index < 0 || index >= GroundedStates.Length || !BindAnimator()) return false;
        var native = GetComponent<MariNativeMotion>();
        if (native != null)
        {
            if (!restart && hasRequestedState && UsesMotionPreset && motion == next) return true;
            if (!native.PlayMotion(NativeGroundedMotions[index], transitionTime)) return false;
            native.Loop = true;
            motion = next;
            UsesMotionPreset = true;
            hasRequestedState = true;
            return true;
        }
        if (!animator.HasState(0, GroundedStates[index]))
        {
            Debug.LogWarning($"{nameof(MariAnimator)}: 기본 모션 상태 {next}가 Controller에 없습니다.", this);
            return false;
        }
        if (!restart && hasRequestedState && UsesMotionPreset && motion == next) return true;
        motion = next;
        UsesMotionPreset = true;
        hasRequestedState = true;
        if (hasMotion) animator.SetInteger(MotionParameter, index);
        if (hasJump) animator.ResetTrigger(JumpParameter);
        animator.CrossFadeInFixedTime(GroundedStates[index], Mathf.Max(0f, transitionTime), 0);
        return true;
    }

    public bool PlayState(string statePath, float transitionTime = 0.12f, int layer = 0, float normalizedTime = 0f)
    {
        if (string.IsNullOrEmpty(statePath) || !BindAnimator() || layer < 0 || layer >= animator.layerCount) return false;
        int state = Animator.StringToHash(statePath);
        if (!animator.HasState(layer, state) && GetComponent<MariNativeMotion>() != null)
        {
            string name = statePath.Substring(statePath.LastIndexOf('.') + 1);
            if (statePath.StartsWith("Base Layer.Grounded."))
            {
                int index = System.Array.IndexOf(new[] { "Idle", "Walk", "Run", "Crouch" }, name);
                if (index >= 0) name = NativeGroundedMotions[index];
            }
            state = MariMotionCatalog.ResolveState(animator, name, layer);
        }
        if (!animator.HasState(layer, state))
        {
            Debug.LogWarning($"{nameof(MariAnimator)}: 애니메이션 상태를 찾을 수 없습니다: {statePath}", this);
            return false;
        }
        UsesMotionPreset = false;
        hasRequestedState = true;
        animator.CrossFadeInFixedTime(state, Mathf.Max(0f, transitionTime), layer, normalizedTime);
        return true;
    }

    public void SetCrouchAmount(float amount)
    {
        crouchAmount = Mathf.Clamp01(amount);
        if (BindAnimator() && hasCrouch) animator.SetFloat(CrouchParameter, crouchAmount);
    }

    public void SetGrounded(bool value)
    {
        grounded = value;
        if (BindAnimator() && hasGrounded) animator.SetBool(GroundedParameter, value);
    }

    public bool TriggerJump()
    {
        var native = GetComponent<MariNativeMotion>();
        if (native != null)
        {
            native.Loop = false;
            return native.PlayMotion("Move_Jump");
        }
        if (!BindAnimator() || !hasJump) return false;
        animator.ResetTrigger(JumpParameter);
        animator.SetTrigger(JumpParameter);
        return true;
    }

    private bool BindAnimator()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (animator == null)
        {
            Debug.LogError($"{nameof(MariAnimator)}: Animator를 찾을 수 없습니다.", this);
            return false;
        }
        if (boundController == animator.runtimeAnimatorController) return true;
        boundController = animator.runtimeAnimatorController;
        hasRequestedState = false;
        hasMotion = hasCrouch = hasGrounded = hasJump = false;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == MotionParameter && parameter.type == AnimatorControllerParameterType.Int) hasMotion = true;
            if (parameter.nameHash == CrouchParameter && parameter.type == AnimatorControllerParameterType.Float) hasCrouch = true;
            if (parameter.nameHash == GroundedParameter && parameter.type == AnimatorControllerParameterType.Bool) hasGrounded = true;
            if (parameter.nameHash == JumpParameter && parameter.type == AnimatorControllerParameterType.Trigger) hasJump = true;
        }
        return true;
    }
}
