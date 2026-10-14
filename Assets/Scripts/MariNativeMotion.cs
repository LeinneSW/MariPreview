using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MariNativeMotion : MonoBehaviour
{
    public const string UpperBodyLayer = "상체 장전";
    public const string UpperBodySpeedParameter = "UpperBodySpeed";
    public const string FullBodyReloadSpeedParameter = "FullBodyReloadSpeed";
    private const float UpperBodyBlendIn = 0.08f;
    private const float UpperBodyBlendOut = 0.1f;

    [Serializable]
    public sealed class ReloadReference
    {
        public AnimationClip clip;
        public AnimationCurve[] pelvisCurves;
        public AnimationCurve[] pelvisRootCurves;
        public AnimationCurve[] spineRootCurves;
        public AnimationCurve[] weaponSpineCurves;

        public static Vector3 ReadPosition(AnimationCurve[] curves, float time) => new Vector3(
            curves[0].Evaluate(time), curves[1].Evaluate(time), curves[2].Evaluate(time));
        public static Quaternion ReadRotation(AnimationCurve[] curves, float time) => Quaternion.Normalize(new Quaternion(
            curves[3].Evaluate(time), curves[4].Evaluate(time), curves[5].Evaluate(time), curves[6].Evaluate(time)));

        public Vector3 Position(float time) => new Vector3(pelvisCurves[0].Evaluate(time),
            pelvisCurves[1].Evaluate(time), pelvisCurves[2].Evaluate(time));

        public Quaternion Rotation(float time) => Quaternion.Normalize(new Quaternion(
            pelvisCurves[3].Evaluate(time), pelvisCurves[4].Evaluate(time),
            pelvisCurves[5].Evaluate(time), pelvisCurves[6].Evaluate(time)));
    }

    [SerializeField] private Animator animator;
    [SerializeField] private AnimationClip[] clips;
    [SerializeField] private Transform reloadPelvis;
    [SerializeField] private Transform reloadWeapon;
    [SerializeField] private Transform reloadSpine;
    [SerializeField] private ReloadReference[] reloadReferences;
    private AnimationClip current;
    private AnimationClip upperBodyClip;
    private ReloadReference upperBodyReference;
    private float upperBodyTargetWeight;
    private bool upperBodyPendingStart;
    private int upperBodyLayer = -1;
    private RuntimeAnimatorController upperBodyController;
    private bool hasPausedWeaponPose;
    private Vector3 pausedWeaponPosition;
    private Quaternion pausedWeaponRotation;
    private Quaternion pausedSpineRotation;
    private Vector3 pausedSpinePosition;
    private string reloadName;
    private float reloadDuration;
    private bool reloadMoving;
    private bool paused;
    private float speed = 1f;
    public AnimationClip[] Clips => clips;
    public AnimationClip CurrentClip => current;
    public bool Paused => paused;
    public bool Loop { get; set; } = true;
    public float Speed => speed;
    public AnimationClip UpperBodyClip => upperBodyClip;
    public bool IsUpperBodyPlaying => upperBodyClip != null && upperBodyTargetWeight > 0f;
    public bool IsReloadPlaying => reloadName != null;
    public bool ReloadUsesUpperBody => reloadName != null && reloadMoving;
    public float ReloadProgress
    {
        get
        {
            if (reloadName == null) return 0f;
            int layer = reloadMoving ? upperBodyLayer : 0;
            var state = animator.IsInTransition(layer) ? animator.GetNextAnimatorStateInfo(layer)
                : animator.GetCurrentAnimatorStateInfo(layer);
            return Mathf.Clamp01(state.normalizedTime);
        }
    }
    public float UpperBodyProgress => upperBodyClip != null && BindUpperBodyLayer()
        ? Mathf.Clamp01(animator.GetCurrentAnimatorStateInfo(upperBodyLayer).normalizedTime) : 0f;
    public float Position => animator != null && current != null
        ? Mathf.Clamp01(animator.GetCurrentAnimatorStateInfo(0).normalizedTime % (Loop ? 1f : 100000f)) * current.length : 0f;

    public void Configure(Animator target, AnimationClip[] available)
    {
        animator = target;
        clips = available;
    }

    public void ConfigureReload(Transform pelvis, Transform weapon, Transform spine, ReloadReference[] references)
    {
        reloadPelvis = pelvis;
        reloadWeapon = weapon;
        reloadSpine = spine;
        reloadReferences = references;
    }

    private void Start()
    {
        if (current == null) PlayMotion("Normal_Idle", 0f);
    }

    private void Update()
    {
        if (animator == null || paused) return;
        UpdateUpperBody(Time.deltaTime);
        if (current == null || !Loop) return;
        var state = animator.GetCurrentAnimatorStateInfo(0);
        if (!state.loop && state.normalizedTime >= 1f && !animator.IsInTransition(0))
            animator.Play(MariMotionCatalog.ResolveState(animator, current.name), 0, state.normalizedTime % 1f);
    }

    public bool PlayUpperBodyMotion(string name, float durationSeconds = 0f, float normalizedTime = 0f)
    {
        if (name == "Kneel_Reload") return PlayReloadMotion(name, durationSeconds, false, null, normalizedTime);
        if (!BindUpperBodyLayer() || reloadReferences == null) return false;
        var reference = Array.Find(reloadReferences, item => item != null && item.clip != null && item.clip.name == name);
        int state = Animator.StringToHash(UpperBodyLayer + "." + name);
        if (reference == null || !animator.HasState(upperBodyLayer, state)) return false;
        upperBodyClip = reference.clip;
        upperBodyReference = reference;
        upperBodyTargetWeight = 1f;
        upperBodyPendingStart = true;
        hasPausedWeaponPose = false;
        animator.SetFloat(UpperBodySpeedParameter, durationSeconds > 0f ? upperBodyClip.length / durationSeconds : 1f);
        animator.Play(state, upperBodyLayer, Mathf.Clamp01(normalizedTime));
        animator.speed = paused ? 0f : speed;
        return true;
    }

    public bool PlayReloadMotion(string name, float durationSeconds = 0f, bool moving = false,
        string locomotion = "Move_Ing", float normalizedTime = -1f)
    {
        if (animator == null || clips == null) return false;
        var clip = Array.Find(clips, item => item != null && item.name == name);
        if (clip == null || (name != "Normal_Reload" && name != "Kneel_Reload")) return false;
        moving = moving && name != "Kneel_Reload";
        float progress = normalizedTime >= 0f ? Mathf.Clamp01(normalizedTime) : ReloadProgress;

        if (moving)
        {
            if (!PlayUpperBodyMotion(name, durationSeconds, progress)) return false;
            if (!PlayMotion(locomotion)) return false;
        }
        else
        {
            int state = MariMotionCatalog.ResolveState(animator, name);
            if (!animator.HasState(0, state)) return false;
            StopUpperBodyMotion(true);
            animator.SetFloat(FullBodyReloadSpeedParameter, durationSeconds > 0f ? clip.length / durationSeconds : 1f);
            current = clip;
            float duration = durationSeconds > 0f ? durationSeconds : clip.length;
            animator.CrossFade(state, 0.08f / duration, 0, progress);
        }
        reloadName = name;
        reloadDuration = durationSeconds;
        reloadMoving = moving;
        return true;
    }

    public bool PlayLocomotion(string name)
    {
        if (reloadName == "Kneel_Reload") CancelReload(true);
        if (reloadName != null)
            return PlayReloadMotion(reloadName, reloadDuration, name != "Normal_Idle" && name != "Cafe_Idle",
                name, ReloadProgress);
        return PlayMotion(name);
    }

    public void CancelReload(bool immediate = false)
    {
        reloadName = null;
        StopUpperBodyMotion(immediate);
    }

    public void StopUpperBodyMotion(bool immediate = false)
    {
        upperBodyTargetWeight = 0f;
        upperBodyPendingStart = false;
        if (!immediate) return;
        if (BindUpperBodyLayer())
        {
            animator.SetLayerWeight(upperBodyLayer, 0f);
            animator.Play(Animator.StringToHash(UpperBodyLayer + ".대기"), upperBodyLayer, 0f);
        }
        upperBodyClip = null;
        upperBodyReference = null;
        hasPausedWeaponPose = false;
    }

    private bool BindUpperBodyLayer()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return false;
        if (upperBodyController != animator.runtimeAnimatorController)
        {
            upperBodyController = animator.runtimeAnimatorController;
            upperBodyLayer = animator.GetLayerIndex(UpperBodyLayer);
        }
        return upperBodyLayer >= 0;
    }

    private void UpdateUpperBody(float deltaTime)
    {
        if (!BindUpperBodyLayer()) return;
        if (upperBodyPendingStart) upperBodyPendingStart = false;
        else if (IsUpperBodyPlaying && Loop)
        {
            var state = animator.GetCurrentAnimatorStateInfo(upperBodyLayer);
            if (!state.loop && state.normalizedTime >= 1f && !animator.IsInTransition(upperBodyLayer))
                animator.Play(Animator.StringToHash(UpperBodyLayer + "." + upperBodyClip.name),
                    upperBodyLayer, state.normalizedTime % 1f);
        }
        float weight = animator.GetLayerWeight(upperBodyLayer);
        float duration = upperBodyTargetWeight > weight ? UpperBodyBlendIn : UpperBodyBlendOut;
        weight = Mathf.MoveTowards(weight, upperBodyTargetWeight, Mathf.Max(0f, deltaTime) / duration);
        animator.SetLayerWeight(upperBodyLayer, weight);
        if (weight <= 0f && upperBodyTargetWeight <= 0f && upperBodyClip != null)
        {
            upperBodyClip = null;
            upperBodyReference = null;
            animator.Play(Animator.StringToHash(UpperBodyLayer + ".대기"), upperBodyLayer, 0f);
        }
    }

    private void LateUpdate()
    {
        if (upperBodyReference == null || reloadPelvis == null || reloadWeapon == null || !BindUpperBodyLayer()) return;
        float weight = animator.GetLayerWeight(upperBodyLayer);
        if (weight <= 0f) return;
        if (paused && hasPausedWeaponPose)
        {
            if (reloadSpine != null) reloadSpine.SetLocalPositionAndRotation(pausedSpinePosition, pausedSpineRotation);
            reloadWeapon.SetLocalPositionAndRotation(pausedWeaponPosition, pausedWeaponRotation);
            return;
        }
        float time = UpperBodyProgress * upperBodyClip.length;
        if (reloadSpine != null && upperBodyReference.spineRootCurves != null)
        {
            Vector3 referencePelvis = ReloadReference.ReadPosition(upperBodyReference.pelvisRootCurves, time);
            Vector3 referenceSpine = ReloadReference.ReadPosition(upperBodyReference.spineRootCurves, time);
            Quaternion referenceRotation = ReloadReference.ReadRotation(upperBodyReference.spineRootCurves, time);
            Vector3 position = reloadPelvis.position + animator.transform.TransformVector(referenceSpine - referencePelvis);
            Quaternion rotation = animator.transform.rotation * referenceRotation;
            reloadSpine.SetPositionAndRotation(Vector3.Lerp(reloadSpine.position, position, weight),
                Quaternion.Slerp(reloadSpine.rotation, rotation, weight));
            Vector3 weaponPosition = reloadSpine.TransformPoint(ReloadReference.ReadPosition(upperBodyReference.weaponSpineCurves, time));
            Quaternion weaponRotation = reloadSpine.rotation * ReloadReference.ReadRotation(upperBodyReference.weaponSpineCurves, time);
            reloadWeapon.SetPositionAndRotation(Vector3.Lerp(reloadWeapon.position, weaponPosition, weight),
                Quaternion.Slerp(reloadWeapon.rotation, weaponRotation, weight));
        }
        if (paused) CapturePausedWeapon();
    }

    private void OnDisable() => CancelReload(true);

    private void CapturePausedWeapon()
    {
        if (reloadWeapon == null || upperBodyReference == null || !BindUpperBodyLayer()
            || animator.GetLayerWeight(upperBodyLayer) <= 0f) return;
        pausedWeaponPosition = reloadWeapon.localPosition;
        pausedWeaponRotation = reloadWeapon.localRotation;
        if (reloadSpine != null)
        {
            pausedSpineRotation = reloadSpine.localRotation;
            pausedSpinePosition = reloadSpine.localPosition;
        }
        hasPausedWeaponPose = true;
    }

    public bool PlayMotion(string name, float transitionTime = 0.12f)
    {
        if (animator == null || clips == null) return false;
        var clip = Array.Find(clips, item => item != null && item.name == name);
        int state = MariMotionCatalog.ResolveState(animator, name);
        if (clip == null || !animator.HasState(0, state)) return false;
        current = clip;
        if (paused) hasPausedWeaponPose = false;
        animator.CrossFadeInFixedTime(state, Mathf.Max(0f, transitionTime), 0, 0f);
        animator.speed = paused ? 0f : speed;
        return true;
    }

    public void SetPaused(bool value)
    {
        if (value && !paused) CapturePausedWeapon();
        if (!value) hasPausedWeaponPose = false;
        paused = value;
        if (animator != null) animator.speed = value ? 0f : speed;
    }

    public void SetSpeed(float value)
    {
        speed = Mathf.Clamp(value, 0.1f, 2f);
        if (animator != null && !paused) animator.speed = speed;
    }

    public void SetPosition(float seconds)
    {
        if (animator == null || current == null || current.length <= 0f) return;
        float time = Mathf.Clamp(seconds / current.length, 0f, 0.999999f);
        animator.Play(MariMotionCatalog.ResolveState(animator, current.name), 0, time);
        animator.Update(0f);
        hasPausedWeaponPose = false;
    }
}
