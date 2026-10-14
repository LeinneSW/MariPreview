using UnityEngine;

[DisallowMultipleComponent]
public sealed class MariPreviewCamera : MonoBehaviour
{
    public enum ViewPreset { Front, ThreeQuarter, Side, Back }
    public enum FocusMode { FullBody, Face }

    [SerializeField] private Transform target;
    [SerializeField] private Vector3 targetOffset = new Vector3(0f, 0.72f, 0f);
    [SerializeField, Range(0.45f, 20f)] private float distance = 3.3f;
    [SerializeField, Min(0.45f)] private float nativeFullBodyDistance = 2.2f;
    [SerializeField, Range(-10f, 45f)] private float pitch = 6f;
    [SerializeField] private float yaw = 150f;
    [SerializeField] private FocusMode focusMode;
    [SerializeField, Range(0.1f, 1f)] private float rotateSensitivity = 0.35f;
    [SerializeField, Range(0.1f, 1f)] private float zoomSensitivity = 0.45f;

    private MariPreviewController previewController;
    private Camera cameraComponent;
    private Rect originalViewport;
    private Transform head;
    private Vector3 faceOffsetInHead;
    private bool ownsDrag;
    private Vector2 lastPointer;
    private Vector3 fixedFaceOffset;

    public FocusMode CurrentFocus => focusMode;
    public float CurrentDistance => distance;
    public float CurrentYaw => yaw;
    public Vector3 FocusPoint
    {
        get
        {
            if (focusMode == FocusMode.Face) return target.TransformPoint(fixedFaceOffset);
            return target.position + targetOffset;
        }
    }

    public void Configure(Transform followTarget, MariPreviewController controls)
    {
        target = followTarget;
        if (target != null && target.GetComponent<MariNativeMotion>() != null) targetOffset = new Vector3(0f, 0.6f, 0f);
        previewController = controls;
        CacheFaceFocus();
    }

    public void SetFocus(FocusMode mode)
    {
        focusMode = mode;
        distance = mode == FocusMode.Face ? 0.85f : (target != null && target.GetComponent<MariNativeMotion>() != null ? nativeFullBodyDistance : 3.3f);
    }

    public void SetPreset(ViewPreset preset)
    {
        yaw = preset switch
        {
            ViewPreset.Front => 180f,
            ViewPreset.ThreeQuarter => 140f,
            ViewPreset.Side => 90f,
            ViewPreset.Back => 0f,
            _ => yaw
        };
        pitch = 6f;
    }

    private void Awake()
    {
        if (previewController == null) previewController = FindAnyObjectByType<MariPreviewController>();
        cameraComponent = GetComponent<Camera>();
        if (cameraComponent != null) originalViewport = cameraComponent.rect;
        CacheFaceFocus();
    }

    private void CacheFaceFocus()
    {
        head = null;
        fixedFaceOffset = new Vector3(0f, 0.87f, 0f);
        if (target == null) return;
        foreach (Transform bone in target.GetComponentsInChildren<Transform>(true))
            if (bone.name == "DEF-spine.006" || bone.name == "Bip001 Head") { head = bone; break; }
        if (head == null) return;
        Vector3 center = head.position + target.up * (0.106f * Mathf.Abs(target.lossyScale.y));
        foreach (var renderer in target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer.name != "얼굴피부" && renderer.name != "얼굴" || renderer.sharedMesh == null) continue;
            var mesh = new Mesh { name = "마리 프리뷰 얼굴 초점", hideFlags = HideFlags.HideAndDontSave };
            renderer.BakeMesh(mesh, false);
            center = renderer.transform.TransformPoint(mesh.bounds.center);
            if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
            break;
        }
        // 눈 감기와 관계없이 같은 얼굴 위치를 머리 본에 따라 추적합니다.
        faceOffsetInHead = head.InverseTransformPoint(center);
        if (target.GetComponent<MariNativeMotion>() == null) fixedFaceOffset = target.InverseTransformPoint(center);
    }

    private void LateUpdate()
    {
        if (target == null) return;
        Vector2 guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        bool overControls = previewController != null && previewController.ControlsRect.Contains(guiMouse);
        Vector2 pointer = Input.mousePosition;
        Vector2 delta = pointer - lastPointer;
        lastPointer = pointer;
        ApplyPointerInput(overControls, Input.GetMouseButtonDown(0), Input.GetMouseButton(0), Input.GetMouseButtonUp(0),
            delta.x, delta.y, Input.mouseScrollDelta.y, Application.isFocused);
        if (cameraComponent != null && previewController != null)
        {
            float left = Mathf.Clamp01((previewController.ControlsRect.xMax + 12f) / Mathf.Max(1f, Screen.width));
            cameraComponent.rect = new Rect(left, 0f, Mathf.Max(0.05f, 1f - left), 1f);
        }
        Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 focus = FocusPoint;
        transform.position = focus - orbit * Vector3.forward * distance;
        transform.rotation = Quaternion.LookRotation(focus - transform.position, Vector3.up);
    }

    internal void ApplyPointerInput(bool overControls, bool pressed, bool held, bool released,
        float horizontal, float vertical, float wheel, bool focused = true)
    {
        if (pressed) { ownsDrag = !overControls; return; }
        if (held && ownsDrag && !overControls && focused)
        {
            yaw += Mathf.Clamp(horizontal, -80f, 80f) * rotateSensitivity;
            pitch = Mathf.Clamp(pitch - Mathf.Clamp(vertical, -80f, 80f) * rotateSensitivity, -10f, 45f);
        }
        if (released || !held) ownsDrag = false;
        if (!overControls)
        {
            float minimum = focusMode == FocusMode.Face ? 0.45f : 1.2f;
            float maximum = focusMode == FocusMode.Face ? 1.8f : Mathf.Max(6f, nativeFullBodyDistance * 2f);
            float scale = focusMode == FocusMode.Face ? 0.15f : 0.5f;
            distance = Mathf.Clamp(distance - wheel * zoomSensitivity * scale, minimum, maximum);
        }
    }

    private void OnDisable()
    {
        ownsDrag = false;
        if (cameraComponent != null) cameraComponent.rect = originalViewport;
    }

    private void OnApplicationFocus(bool focused)
    {
        ownsDrag = false;
        lastPointer = Input.mousePosition;
    }
}
