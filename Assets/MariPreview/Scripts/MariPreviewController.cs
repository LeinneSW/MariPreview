using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MariPreviewController : MonoBehaviour
{
    private enum Panel { Expressions, Motion }
    private static readonly string[] PanelLabels = { "표정", "모션·귀" };
    private static readonly string[] MotionLabels = { "대기 (1)", "걷기 (2)", "달리기 (3)", "숙이기 (4)" };
    private static readonly string[] EarLabels = { "기본 (Q)", "쫑긋 (W)", "풀이 죽음 (E)", "뒤로 (R)", "호기심 (T)", "앞으로 (Y)" };
    private static readonly MariRig.EarPose[] EarPoses =
    {
        MariRig.EarPose.Neutral, MariRig.EarPose.Alert, MariRig.EarPose.Drooped,
        MariRig.EarPose.Back, MariRig.EarPose.Curious, MariRig.EarPose.Forward
    };
    private static readonly KeyCode[] EarKeys = { KeyCode.Q, KeyCode.W, KeyCode.E, KeyCode.R, KeyCode.T, KeyCode.Y };
    private static readonly string[] ViewLabels = { "앞", "사선", "옆", "뒤" };

    [SerializeField] private Transform characterRoot;
    [SerializeField] private MariPreviewCamera previewCamera;
    [SerializeField] private MariFace facePreview;
    [SerializeField] private Panel selectedPanel = Panel.Expressions;

    private MariAnimator mariAnimator;
    private MariRig mariRig;
    private MariNativeMotion nativeMotion;
    private MariLook look;
    private float eyeYaw, faceYaw, bodyYaw;
    private float jumpGroundedAt = -1f;
    private float expressionStrength = 100f;
    private Vector2 scroll;
    private GUIStyle wrappedLabel;
    private GUIStyle sectionLabel;
    private AnimationClip[][] actionClips = System.Array.Empty<AnimationClip[]>();
    private Font uiFont;
    private bool fontChecked;

    public Rect ControlsRect => new Rect(12f, 12f,
        Mathf.Min(420f, Mathf.Max(280f, Screen.width * 0.32f), Mathf.Max(1f, Screen.width - 24f)),
        Mathf.Max(1f, Screen.height - 24f));
    public MariFace FacePreview => facePreview;

    public void Configure(Transform character, MariPreviewCamera cameraRig)
    {
        characterRoot = character;
        previewCamera = cameraRig;
        CacheCharacter();
    }

    private void CacheCharacter()
    {
        mariAnimator = characterRoot != null ? characterRoot.GetComponent<MariAnimator>() : null;
        nativeMotion = characterRoot != null ? characterRoot.GetComponent<MariNativeMotion>() : null;
        look = characterRoot != null ? characterRoot.GetComponent<MariLook>() : null;
        mariRig = characterRoot != null ? characterRoot.GetComponent<MariRig>() : null;
        facePreview = characterRoot != null ? characterRoot.GetComponent<MariFace>() : null;
        facePreview?.Configure(characterRoot);
        actionClips = nativeMotion == null || nativeMotion.Clips == null ? System.Array.Empty<AnimationClip[]>()
            : MariMotionCatalog.ActionSections.Select(section => nativeMotion.Clips
                .Where(clip => clip != null && MariMotionCatalog.ActionSection(clip.name) == section)
                .OrderBy(clip => MariMotionCatalog.ActionOrder(clip.name))
                .ThenBy(clip => clip.name, System.StringComparer.Ordinal).ToArray()).ToArray();
    }

    private void Awake()
    {
        if (characterRoot == null)
        {
            var character = FindAnyObjectByType<MariAnimator>();
            if (character != null) characterRoot = character.transform;
        }
        CacheCharacter();
        if (previewCamera == null) previewCamera = FindAnyObjectByType<MariPreviewCamera>();
    }

    private void Start()
    {
        if (nativeMotion != null)
        {
            nativeMotion.PlayMotion("Normal_Idle", 0f);
            previewCamera?.SetFocus(MariPreviewCamera.FocusMode.FullBody);
            previewCamera?.SetPreset(MariPreviewCamera.ViewPreset.Front);
            return;
        }
        mariAnimator?.SetGrounded(true);
        mariAnimator?.SetMotion(MariAnimator.Motion.Idle);
        mariRig?.SetEarPose(MariRig.EarPose.Neutral);
        facePreview?.SelectExpression(MariFace.Expression.Neutral, 100f, true);
        previewCamera?.SetFocus(MariPreviewCamera.FocusMode.Face);
        previewCamera?.SetPreset(MariPreviewCamera.ViewPreset.Front);
    }

    private void Update()
    {
        if (nativeMotion != null)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) nativeMotion.PlayLocomotion("Normal_Idle");
            if (Input.GetKeyDown(KeyCode.Alpha2)) nativeMotion.PlayLocomotion("Cafe_Walk");
            if (Input.GetKeyDown(KeyCode.Alpha3)) nativeMotion.PlayLocomotion("Move_Ing");
            if (Input.GetKeyDown(KeyCode.Alpha4)) { nativeMotion.CancelReload(true); nativeMotion.PlayMotion("Kneel_Idle"); }
            if (Input.GetKeyDown(KeyCode.Space)) { nativeMotion.CancelReload(true); nativeMotion.PlayMotion("Move_Jump"); }
            return;
        }
        if (Input.GetKeyDown(KeyCode.Alpha1)) SetMotion(MariAnimator.Motion.Idle);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SetMotion(MariAnimator.Motion.Walk);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SetMotion(MariAnimator.Motion.Run);
        if (Input.GetKeyDown(KeyCode.Alpha4)) SetMotion(MariAnimator.Motion.Crouch);
        if (Input.GetKeyDown(KeyCode.Space)) PlayJump();
        for (int i = 0; i < EarPoses.Length; i++)
            if (Input.GetKeyDown(EarKeys[i])) mariRig?.SetEarPose(EarPoses[i]);
        if (Input.GetKeyDown(KeyCode.F1)) SetExpression(MariFace.Expression.Neutral);
        if (Input.GetKeyDown(KeyCode.F2)) SetExpression(MariFace.Expression.BothClosed);
        if (Input.GetKeyDown(KeyCode.F3)) SetExpression(MariFace.Expression.HalfClosed);
        if (Input.GetKeyDown(KeyCode.F4)) SetExpression(MariFace.Expression.LeftWink);
        if (Input.GetKeyDown(KeyCode.F5)) SetExpression(MariFace.Expression.RightWink);
        if (jumpGroundedAt > 0f && Time.time >= jumpGroundedAt)
        {
            mariAnimator?.SetGrounded(true);
            jumpGroundedAt = -1f;
        }
    }

    public void SetExpression(MariFace.Expression expression)
    {
        facePreview?.SelectExpression(expression, expressionStrength);
    }

    private void SetMotion(MariAnimator.Motion motion)
    {
        jumpGroundedAt = -1f;
        mariAnimator?.SetGrounded(true);
        mariAnimator?.SetMotion(motion);
    }

    private void PlayJump()
    {
        if (mariAnimator == null) return;
        mariAnimator.SetGrounded(false);
        mariAnimator.TriggerJump();
        jumpGroundedAt = Time.time + 0.65f;
    }

    private void OnGUI()
    {
        if (!fontChecked)
        {
            fontChecked = true;
            foreach (var name in Font.GetOSInstalledFontNames())
                if (name == "Malgun Gothic")
                {
                    uiFont = Font.CreateDynamicFontFromOSFont(name, 14);
                    uiFont.hideFlags = HideFlags.HideAndDontSave;
                    break;
                }
        }
        var previousFont = GUI.skin.font;
        if (uiFont != null) GUI.skin.font = uiFont;
        try
        {
            if (wrappedLabel == null) wrappedLabel = new GUIStyle(GUI.skin.label) { wordWrap = true };
            if (sectionLabel == null) sectionLabel = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            Rect rect = ControlsRect;
            Color previousColor = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previousColor;
            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, rect.height - 16f));
            if (nativeMotion != null)
            {
                DrawNativeMotion();
                GUILayout.EndArea();
                return;
            }
            GUILayout.Label("마리 표정·모션 프리뷰");
            var nextPanel = (Panel)GUILayout.Toolbar((int)selectedPanel, PanelLabels, GUILayout.Height(28f));
            if (nextPanel != selectedPanel) { selectedPanel = nextPanel; scroll = Vector2.zero; }
            scroll = GUILayout.BeginScrollView(scroll);
            DrawCameraControls();
            GUILayout.Space(8f);
            if (selectedPanel == Panel.Expressions) DrawExpressions();
            else DrawMotionControls();
            GUILayout.Space(8f);
            GUILayout.Label("패널 밖 드래그: 회전, 휠: 확대·축소", wrappedLabel);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
        finally { GUI.skin.font = previousFont; }
    }

    private void DrawNativeMotion()
    {
        GUILayout.Label("행위");
        DrawCameraControls();
        GUILayout.Space(6f);
        var clip = nativeMotion.CurrentClip;
        GUILayout.Label(clip != null ? $"{MariMotionCatalog.ActionName(clip.name)}  {nativeMotion.Position:0.00} / {clip.length:0.00}초" : "행위 선택");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(nativeMotion.Paused ? "재생" : "일시정지")) nativeMotion.SetPaused(!nativeMotion.Paused);
        if (GUILayout.Button("처음부터")) nativeMotion.SetPosition(0f);
        nativeMotion.Loop = GUILayout.Toggle(nativeMotion.Loop, "반복");
        GUILayout.EndHorizontal();
        if (clip != null)
        {
            float currentPosition = nativeMotion.Position;
            float position = GUILayout.HorizontalSlider(currentPosition, 0f, clip.length);
            bool seeking = Event.current.type == EventType.MouseDown || Event.current.type == EventType.MouseDrag;
            if (seeking && Mathf.Abs(position - currentPosition) > 0.001f) nativeMotion.SetPosition(position);
        }
        GUILayout.Label($"재생 속도: {nativeMotion.Speed:0.00}배");
        float speed = GUILayout.HorizontalSlider(nativeMotion.Speed, 0.1f, 2f);
        if (!Mathf.Approximately(speed, nativeMotion.Speed)) nativeMotion.SetSpeed(speed);
        GUILayout.Space(4f);
        GUILayout.Label("정지: 전신 장전 · 이동: 상체 장전");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("정지")) nativeMotion.PlayLocomotion("Normal_Idle");
        if (GUILayout.Button("걷기")) nativeMotion.PlayLocomotion("Cafe_Walk");
        if (GUILayout.Button("달리기")) nativeMotion.PlayLocomotion("Move_Ing");
        if (GUILayout.Button("장전")) nativeMotion.PlayReloadMotion("Normal_Reload", 1.4f,
            nativeMotion.CurrentClip != null && (nativeMotion.CurrentClip.name == "Cafe_Walk" || nativeMotion.CurrentClip.name == "Move_Ing"),
            nativeMotion.CurrentClip != null ? nativeMotion.CurrentClip.name : "Normal_Idle", 0f);
        GUILayout.EndHorizontal();
        if (nativeMotion.IsReloadPlaying)
            GUILayout.Label($"장전: {nativeMotion.ReloadProgress:P0}");
        if (look != null)
        {
            GUILayout.Space(4f);
            GUILayout.Label("시선·얼굴·몸 방향");
            GUILayout.Label($"눈 (얼굴 기준): {eyeYaw:0.0}°");
            float nextEyeYaw = GUILayout.HorizontalSlider(eyeYaw, -25f, 25f);
            if (!Mathf.Approximately(nextEyeYaw, eyeYaw)) { eyeYaw = nextEyeYaw; look.SetEyeYaw(eyeYaw); }
            GUILayout.Label($"얼굴 (몸 기준): {faceYaw:0.0}°");
            float nextFaceYaw = GUILayout.HorizontalSlider(faceYaw, -70f, 70f);
            if (!Mathf.Approximately(nextFaceYaw, faceYaw)) { faceYaw = nextFaceYaw; look.SetFaceYaw(faceYaw); }
            GUILayout.Label($"몸 (캐릭터 기준): {bodyYaw:0.0}°");
            float nextBodyYaw = GUILayout.HorizontalSlider(bodyYaw, -180f, 180f);
            if (!Mathf.Approximately(nextBodyYaw, bodyYaw)) { bodyYaw = nextBodyYaw; look.SetBodyYaw(bodyYaw); }
            if (GUILayout.Button("방향 초기화")) { eyeYaw = faceYaw = bodyYaw = 0f; look.ResetYaw(); }
        }
        GUILayout.Label("눈동자는 중앙 시선을 유지하며 표정·귀·머리·치마를 함께 재생해요.", wrappedLabel);
        scroll = GUILayout.BeginScrollView(scroll);
        for (int section = 0; section < actionClips.Length; section++)
        {
            if (actionClips[section].Length == 0) continue;
            if (section > 0) GUILayout.Space(8f);
            GUILayout.Label(MariMotionCatalog.ActionSections[section], sectionLabel);
            foreach (var item in actionClips[section])
            {
                bool reload = item.name.EndsWith("_Reload", System.StringComparison.Ordinal);
                if (ChoiceButton(MariMotionCatalog.ActionName(item.name), clip == item || nativeMotion.UpperBodyClip == item))
                {
                    if (reload) nativeMotion.PlayReloadMotion(item.name, 1.4f,
                        item.name != "Kneel_Reload" && nativeMotion.CurrentClip != null &&
                        (nativeMotion.CurrentClip.name == "Cafe_Walk" || nativeMotion.CurrentClip.name == "Move_Ing"),
                        nativeMotion.CurrentClip != null ? nativeMotion.CurrentClip.name : "Normal_Idle", 0f);
                    else { nativeMotion.CancelReload(true); nativeMotion.PlayMotion(item.name); }
                }
            }
        }
        GUILayout.EndScrollView();
        GUILayout.Label("패널 밖 드래그: 회전, 휠: 확대·축소", wrappedLabel);
    }

    private void DrawCameraControls()
    {
        GUILayout.BeginHorizontal();
        if (ChoiceButton("전신", previewCamera != null && previewCamera.CurrentFocus == MariPreviewCamera.FocusMode.FullBody)) previewCamera?.SetFocus(MariPreviewCamera.FocusMode.FullBody);
        if (ChoiceButton("얼굴 확대", previewCamera != null && previewCamera.CurrentFocus == MariPreviewCamera.FocusMode.Face)) previewCamera?.SetFocus(MariPreviewCamera.FocusMode.Face);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        for (int i = 0; i < ViewLabels.Length; i++)
            if (GUILayout.Button(ViewLabels[i], GUILayout.Height(28f))) previewCamera?.SetPreset((MariPreviewCamera.ViewPreset)i);
        GUILayout.EndHorizontal();
    }

    private void DrawExpressions()
    {
        if (facePreview == null || facePreview.ChannelNames.Count == 0)
        {
            GUILayout.Label("이 모델에는 표정 키가 없어요.", wrappedLabel);
            return;
        }
        GUILayout.Label("표정 선택 (F1~F5)");
        bool hasBoth = facePreview.HasBlinkLeft && facePreview.HasBlinkRight;
        GUILayout.BeginHorizontal();
        PresetButton("기본 열림 (F1)", MariFace.Expression.Neutral, true);
        PresetButton("양쪽 감기 (F2)", MariFace.Expression.BothClosed, hasBoth);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        PresetButton("반쯤 감기 (F3)", MariFace.Expression.HalfClosed, hasBoth);
        PresetButton("눈웃음", MariFace.Expression.EyeSmile, facePreview.HasEyeSmile);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        PresetButton("왼쪽 윙크 (F4)", MariFace.Expression.LeftWink, facePreview.HasBlinkLeft);
        PresetButton("오른쪽 윙크 (F5)", MariFace.Expression.RightWink, facePreview.HasBlinkRight);
        GUILayout.EndHorizontal();
        GUILayout.Space(6f);
        GUILayout.Label("입 표정 선택 (눈 표정과 별개)");
        if (facePreview.HasMouthAtlas)
        {
            GUILayout.BeginHorizontal();
            MouthPoseButton("기본 미소", MariFace.MouthPose.Neutral);
            MouthPoseButton("웃으며 입 벌리기", MariFace.MouthPose.SmileOpen);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            MouthPoseButton("작게 말하기", MariFace.MouthPose.TalkSmall);
            MouthPoseButton("넓게 말하기", MariFace.MouthPose.TalkWide);
            GUILayout.EndHorizontal();
            MouthPoseButton("둥글게 말하기", MariFace.MouthPose.TalkRound);
        }
        else GUILayout.Label("입 아틀라스를 찾지 못했어요.", wrappedLabel);
        GUILayout.Space(6f);
        GUILayout.Label($"표정 강도: {expressionStrength:0}%");
        float strength = GUILayout.HorizontalSlider(expressionStrength, 0f, 100f);
        if (!Mathf.Approximately(strength, expressionStrength))
        {
            expressionStrength = strength;
            if (facePreview.CurrentExpression != MariFace.Expression.Custom) SetExpression(facePreview.CurrentExpression);
        }
        bool automatic = GUILayout.Toggle(facePreview.AutomaticBlink, "깜빡임 재생");
        if (automatic != facePreview.AutomaticBlink) facePreview.SetAutomaticBlink(automatic);
        GUILayout.Space(6f);
        if (!facePreview.HasEyeSmile) GUILayout.Label("눈웃음은 아직 모델에 없어요.", wrappedLabel);
        GUILayout.Label("눈 표정 키와 입 그림을 각각 조절해요.", wrappedLabel);
        GUILayout.Space(6f);
        GUILayout.Label("개별 표정 키");
        foreach (var name in facePreview.ChannelNames)
        {
            float target = facePreview.GetChannelTarget(name);
            float effective = facePreview.GetEffectiveChannelWeight(name);
            string label = name == "Blink_L" ? "왼쪽 눈 감기" : name == "Blink_R" ? "오른쪽 눈 감기"
                : name == "EyeSmile_L" ? "왼쪽 눈웃음" : name == "EyeSmile_R" ? "오른쪽 눈웃음"
                : name == "EyeSmile" ? "양쪽 눈웃음" : name;
            GUILayout.Label($"{label}: {target:0}% (적용 {effective:0}%)");
            float weight = GUILayout.HorizontalSlider(target, 0f, 100f);
            if (!Mathf.Approximately(weight, target)) facePreview.SetChannelWeight(name, weight);
        }
        GUILayout.Label("좌우는 마리 기준이에요.", wrappedLabel);
    }

    private void PresetButton(string label, MariFace.Expression expression, bool available)
    {
        bool previous = GUI.enabled;
        GUI.enabled = previous && available;
        if (ChoiceButton(label, facePreview.CurrentExpression == expression)) SetExpression(expression);
        GUI.enabled = previous;
    }

    private void MouthPoseButton(string label, MariFace.MouthPose pose)
    {
        if (ChoiceButton(label, facePreview.CurrentMouthPose == pose)) facePreview.SetMouthPose(pose);
    }

    private void DrawMotionControls()
    {
        GUILayout.Label("몸 모션 (숫자 1~4)");
        for (int row = 0; row < 2; row++)
        {
            GUILayout.BeginHorizontal();
            for (int column = 0; column < 2; column++)
            {
                int i = row * 2 + column;
                if (ChoiceButton(MotionLabels[i], mariAnimator != null && (int)mariAnimator.CurrentMotion == i)) SetMotion((MariAnimator.Motion)i);
            }
            GUILayout.EndHorizontal();
        }
        if (GUILayout.Button("점프 (Space)", GUILayout.Height(28f))) PlayJump();
        GUILayout.Space(6f);
        GUILayout.Label("귀 포즈");
        for (int row = 0; row < 3; row++)
        {
            GUILayout.BeginHorizontal();
            for (int column = 0; column < 2; column++)
            {
                int i = row * 2 + column;
                if (ChoiceButton(EarLabels[i], mariRig != null && mariRig.CurrentEarPose == EarPoses[i])) mariRig?.SetEarPose(EarPoses[i]);
            }
            GUILayout.EndHorizontal();
        }
    }

    private static bool ChoiceButton(string label, bool selected)
    {
        Color previous = GUI.backgroundColor;
        if (selected) GUI.backgroundColor = new Color(0.5f, 0.8f, 1f);
        bool clicked = GUILayout.Button(label, GUILayout.Height(30f));
        GUI.backgroundColor = previous;
        return clicked;
    }

    private void OnDestroy()
    {
        if (uiFont != null) Destroy(uiFont);
    }
}
