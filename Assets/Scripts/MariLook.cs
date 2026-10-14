using System;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(300)]
public sealed class MariLook : MonoBehaviour
{
    [Serializable]
    public sealed class EyeBinding
    {
        public Transform bone;
        public Vector3 centerPosition;
        public Quaternion centerRotation;
        public Vector3 yawPivot;
    }

    [SerializeField] private Transform body;
    [SerializeField] private Transform head;
    [SerializeField] private EyeBinding[] eyes = Array.Empty<EyeBinding>();
    [SerializeField] private Vector3 faceForwardAxis;
    [SerializeField] private Vector3 faceUpAxis;
    [SerializeField, Min(0f)] private float transitionSeconds = 0.06f;
    private float targetEyeYaw, targetFaceYaw, targetBodyYaw;
    private float eyeYaw, faceYaw, bodyYaw;
    private bool faceControlled;
    private Quaternion rawBodyRotation, appliedBodyRotation;
    private Quaternion rawHeadRotation, appliedHeadRotation;
    private bool bodyApplied, headApplied;

    public float EyeYaw => eyeYaw;
    public float FaceYaw => faceYaw;
    public float BodyYaw => bodyYaw;
    public bool ControlsFaceYaw => faceControlled;
    public bool IsConfigured => body != null && head != null && eyes.Length == 2;

    public void Configure(Transform bodyRoot, Transform headBone, Transform leftEye, Transform rightEye,
        Vector3 leftEyePivot, Vector3 rightEyePivot)
    {
        RestoreOffsets();
        body = bodyRoot;
        head = headBone;
        faceForwardAxis = Quaternion.Inverse(head.rotation) * body.forward;
        faceUpAxis = Quaternion.Inverse(head.rotation) * body.up;
        eyes = new[] { BindEye(leftEye, leftEyePivot), BindEye(rightEye, rightEyePivot) };
    }

    private static EyeBinding BindEye(Transform bone, Vector3 pivot) => new EyeBinding
    {
        bone = bone,
        centerPosition = bone.localPosition,
        centerRotation = bone.localRotation,
        yawPivot = pivot
    };

    public void SetEyeYaw(float degrees, bool immediate = false)
    {
        // 얼굴 기준 눈동자 방향이며 양수는 마리의 오른쪽입니다.
        targetEyeYaw = Mathf.Clamp(SafeAngle(degrees), -25f, 25f);
        if (immediate) eyeYaw = targetEyeYaw;
    }

    public void SetFaceYaw(float degrees, bool immediate = false)
    {
        // 몸 기준 얼굴 방향을 지정하고 모션의 고개 끄덕임과 기울기는 유지합니다.
        targetFaceYaw = Mathf.Clamp(SafeAngle(degrees), -70f, 70f);
        faceControlled = true;
        if (immediate) faceYaw = targetFaceYaw;
    }

    public void SetBodyYaw(float degrees, bool immediate = false)
    {
        // 캐릭터 기준 시각 모델의 방향이며 이동·충돌을 담당하는 부모는 유지합니다.
        targetBodyYaw = Mathf.DeltaAngle(0f, SafeAngle(degrees));
        if (immediate) bodyYaw = targetBodyYaw;
    }

    public void SetYaw(float eyesYaw, float faceYaw, float bodyYaw, bool immediate = false)
    {
        SetEyeYaw(eyesYaw, immediate);
        SetFaceYaw(faceYaw, immediate);
        SetBodyYaw(bodyYaw, immediate);
    }

    public void ResetYaw(bool immediate = false)
    {
        targetEyeYaw = targetFaceYaw = targetBodyYaw = 0f;
        if (immediate) eyeYaw = faceYaw = bodyYaw = 0f;
        RestoreOffsets();
        faceControlled = false;
        if (immediate)
            foreach (var eye in eyes)
                if (eye.bone != null) eye.bone.SetLocalPositionAndRotation(eye.centerPosition, eye.centerRotation);
    }

    private static float SafeAngle(float degrees)
        => float.IsNaN(degrees) || float.IsInfinity(degrees) ? 0f : degrees;

    private static bool SameRotation(Quaternion a, Quaternion b)
        => Mathf.Abs(Quaternion.Dot(a, b)) > 0.9999999f;

    private void LateUpdate()
    {
        if (!IsConfigured) return;
        float blend = transitionSeconds <= 0f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / transitionSeconds);
        eyeYaw = Mathf.LerpAngle(eyeYaw, targetEyeYaw, blend);
        faceYaw = Mathf.LerpAngle(faceYaw, targetFaceYaw, blend);
        bodyYaw = Mathf.LerpAngle(bodyYaw, targetBodyYaw, blend);
        ApplyPose();
    }

    private void ApplyPose()
    {
        // 애니메이션이 만든 자세를 기준으로 코드에서 지정한 방향을 적용합니다.
        if (!bodyApplied || !SameRotation(body.localRotation, appliedBodyRotation))
            rawBodyRotation = body.localRotation;
        body.localRotation = rawBodyRotation * Quaternion.Euler(0f, bodyYaw, 0f);
        appliedBodyRotation = body.localRotation;
        bodyApplied = true;

        if (!headApplied || !SameRotation(head.localRotation, appliedHeadRotation))
            rawHeadRotation = head.localRotation;
        head.localRotation = rawHeadRotation;
        if (faceControlled)
        {
            Vector3 forward = body.InverseTransformDirection(head.rotation * faceForwardAxis);
            float animationYaw = forward.x * forward.x + forward.z * forward.z > 0.000001f
                ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg : 0f;
            head.rotation = Quaternion.AngleAxis(Mathf.DeltaAngle(animationYaw, faceYaw), body.up) * head.rotation;
        }
        appliedHeadRotation = head.localRotation;
        headApplied = true;

        Vector3 eyeUp = head.rotation * faceUpAxis;
        foreach (var eye in eyes)
        {
            if (eye.bone == null) continue;
            Vector3 axis = eye.bone.parent.InverseTransformDirection(eyeUp).normalized;
            Quaternion rotation = Quaternion.AngleAxis(eyeYaw, axis);
            eye.bone.localPosition = eye.yawPivot + rotation * (eye.centerPosition - eye.yawPivot);
            eye.bone.localRotation = rotation * eye.centerRotation;
        }
    }

    private void RestoreOffsets()
    {
        if (bodyApplied && body != null && SameRotation(body.localRotation, appliedBodyRotation))
            body.localRotation = rawBodyRotation;
        if (headApplied && head != null && SameRotation(head.localRotation, appliedHeadRotation))
            head.localRotation = rawHeadRotation;
        bodyApplied = headApplied = false;
    }

    private void OnDisable()
    {
        RestoreOffsets();
        foreach (var eye in eyes)
            if (eye.bone != null) eye.bone.SetLocalPositionAndRotation(eye.centerPosition, eye.centerRotation);
    }
}
