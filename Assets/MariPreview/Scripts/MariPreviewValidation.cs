using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class MariPreviewValidation : MonoBehaviour
{
    [Serializable]
    private sealed class Report
    {
        public bool passed;
        public string scene;
        public int animationClips;
        public List<string> checks = new List<string>();
        public List<string> errors = new List<string>();
        public List<string> warnings = new List<string>();
        public List<string> screenshots = new List<string>();
    }

    private readonly Report report = new Report();
    private string output;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-mari-preview-validate") < 0) return;
        new GameObject("마리 프리뷰 실행 검증").AddComponent<MariPreviewValidation>();
    }

    private void Awake() => Application.logMessageReceived += OnLog;
    private void OnDestroy() => Application.logMessageReceived -= OnLog;
    private void OnLog(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            report.errors.Add(message + "\n" + stack);
        else if (type == LogType.Warning) report.warnings.Add(message);
    }

    private void Check(bool passed, string description)
    {
        if (passed) report.checks.Add(description);
        else report.errors.Add(description);
    }

    private IEnumerator Start()
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-mari-preview-review");
        output = index >= 0 && index + 1 < args.Length ? args[index + 1]
            : Path.Combine(Application.persistentDataPath, "Review");
        Directory.CreateDirectory(output);
        while (!UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
        Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
        yield return new WaitForSecondsRealtime(0.35f);
        Screen.SetResolution(1440, 900, FullScreenMode.Windowed);
        yield return new WaitForSecondsRealtime(0.35f);
        yield return null;
        yield return null;
        var controls = FindAnyObjectByType<MariPreviewController>();
        var motion = FindAnyObjectByType<MariNativeMotion>();
        var look = FindAnyObjectByType<MariLook>();
        var cameraRig = FindAnyObjectByType<MariPreviewCamera>();
        report.scene = SceneManager.GetActiveScene().name;
        Check(report.scene == "MariPreview", "독립 실행 시작 씬");
        Check(controls != null && motion != null && look != null && cameraRig != null, "프리뷰 구성 요소 연결");
        if (controls == null || motion == null || look == null || cameraRig == null)
        {
            Finish();
            yield break;
        }
        report.animationClips = motion.Clips.Length;
        Check(report.animationClips == 37, "37개 모션 로드");
        Check(look.IsConfigured, "시선 본 연결");
        var stage = GameObject.Find("IdolStage");
        Check(stage != null, "IdolStage 무대 배치");
        Check(GameObject.Find("프리뷰 바닥") == null, "기존 평면 바닥 제거");
        if (stage != null)
        {
            var stageRenderers = stage.GetComponentsInChildren<Renderer>();
            Check(stageRenderers.Length > 0, "무대 메시 표시");
            foreach (var renderer in stageRenderers)
                foreach (var material in renderer.sharedMaterials)
                    Check(material != null && material.shader != null && material.shader.isSupported
                        && material.mainTexture != null, "무대 재질·텍스처: " + renderer.name);
            foreach (var preset in new[] { MariPreviewCamera.ViewPreset.Front, MariPreviewCamera.ViewPreset.ThreeQuarter })
            {
                cameraRig.SetPreset(preset);
                yield return null;
                yield return new WaitForEndOfFrame();
                CaptureFrame("IdolStage-" + preset);
            }
            cameraRig.SetPreset(MariPreviewCamera.ViewPreset.Front);
        }
        var scrollField = typeof(MariPreviewController).GetField("scroll",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        foreach (float offset in new[] { 0f, 300f, 600f, 900f, 1200f })
        {
            scrollField.SetValue(controls, new Vector2(0f, offset));
            yield return null;
            yield return new WaitForEndOfFrame();
            CaptureFrame("Actions-" + offset.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
        }
        scrollField.SetValue(controls, Vector2.zero);
        var animator = motion.GetComponentInChildren<Animator>();
        foreach (var clip in motion.Clips)
        {
            if (clip == null) { Check(false, "누락된 모션 클립"); continue; }
            bool played = motion.PlayMotion(clip.name, 0f);
            yield return null;
            yield return null;
            Check(played && motion.CurrentClip == clip && animator.HasState(0, MariMotionCatalog.ResolveState(animator, clip.name)),
                "모션 재생: " + clip.name);
        }
        motion.PlayMotion("Normal_Idle", 0f);
        yield return null;
        float previous = motion.Position;
        yield return new WaitForSecondsRealtime(0.12f);
        Check(motion.Position > previous, "모션 시간 진행");
        motion.SetPaused(true);
        motion.SetPosition(motion.CurrentClip.length * 0.4f);
        previous = motion.Position;
        yield return new WaitForSecondsRealtime(0.1f);
        Check(Mathf.Abs(motion.Position - previous) < 0.002f, "일시정지와 재생 위치 이동");
        motion.SetSpeed(0.5f);
        Check(Mathf.Approximately(motion.Speed, 0.5f), "재생 속도 변경");
        motion.SetSpeed(1f);
        motion.SetPaused(false);
        motion.Loop = true;
        motion.SetPosition(motion.CurrentClip.length - 0.03f);
        yield return new WaitForSecondsRealtime(0.15f);
        Check(motion.Position < 0.5f, "모션 반복");
        motion.PlayMotion("Cafe_Walk", 0f);
        Check(motion.PlayUpperBodyMotion("Normal_Reload", 1.4f), "걷기 중 상체 장전 시작");
        yield return new WaitForSecondsRealtime(0.2f);
        Check(motion.UpperBodyClip != null && animator.GetLayerWeight(animator.GetLayerIndex(MariNativeMotion.UpperBodyLayer)) > 0f,
            "상체 장전 레이어 재생");
        motion.SetPaused(true);
        previous = motion.UpperBodyProgress;
        yield return new WaitForSecondsRealtime(0.1f);
        Check(Mathf.Abs(motion.UpperBodyProgress - previous) < 0.002f, "상체 장전 일시정지");
        motion.StopUpperBodyMotion(true);
        motion.SetPaused(false);
        foreach (string name in new[] { "Normal_Reload", "Kneel_Reload" })
        {
            motion.CancelReload(true);
            motion.Loop = false;
            Check(motion.PlayReloadMotion(name, 0.3f, false, null, 0f), "전신 장전 시작: " + name);
            yield return new WaitForSecondsRealtime(0.65f);
            Check(motion.CurrentClip.name == name && motion.IsReloadPlaying && motion.ReloadProgress >= 0.99f
                && !motion.Loop, "전신 장전 종료 후 선택과 마지막 자세 유지: " + name);
            float endPosition = motion.Position;
            yield return new WaitForSecondsRealtime(0.15f);
            Check(motion.CurrentClip.name == name && Mathf.Abs(motion.Position - endPosition) < 0.002f,
                "대기 모션 자동 복귀 없음: " + name);
            yield return new WaitForEndOfFrame();
            CaptureFrame(name + "-end");
            Check(motion.PlayReloadMotion(name, 0.3f, false, null, 0f), "같은 장전 다시 선택: " + name);
            yield return null;
            yield return null;
            Check(motion.ReloadProgress < 0.8f && !motion.Loop, "같은 장전 처음부터 재생: " + name);
            motion.Loop = true;
            motion.PlayReloadMotion(name, 0.3f, false, null, 0f);
            float lastProgress = 0f;
            bool repeated = false;
            for (int frame = 0; frame < 16; frame++)
            {
                yield return new WaitForSecondsRealtime(0.05f);
                float progress = motion.ReloadProgress;
                if (progress + 0.2f < lastProgress) repeated = true;
                lastProgress = progress;
            }
            Check(repeated && motion.CurrentClip.name == name && motion.IsReloadPlaying && motion.Loop,
                "전신 장전 반복 설정 적용: " + name);
        }
        Check(motion.PlayLocomotion("Normal_Idle") && motion.CurrentClip.name == "Normal_Idle"
            && !motion.IsReloadPlaying, "무릎 장전에서 수동 모션 전환");
        foreach (bool loop in new[] { false, true })
        {
            motion.CancelReload(true);
            motion.Loop = loop;
            Check(motion.PlayReloadMotion("Normal_Reload", 0.3f, true, "Cafe_Walk", 0f),
                "이동 중 상체 장전 시작: " + loop);
            float lastProgress = 0f;
            bool repeated = false;
            for (int frame = 0; frame < 16; frame++)
            {
                yield return new WaitForSecondsRealtime(0.05f);
                float progress = motion.ReloadProgress;
                if (progress + 0.2f < lastProgress) repeated = true;
                lastProgress = progress;
            }
            Check(motion.CurrentClip.name == "Cafe_Walk" && motion.UpperBodyClip != null
                && motion.UpperBodyClip.name == "Normal_Reload" && motion.IsUpperBodyPlaying
                && animator.GetLayerWeight(animator.GetLayerIndex(MariNativeMotion.UpperBodyLayer)) > 0.99f,
                "상체 장전 종료 후 레이어 유지: " + loop);
            Check(motion.Loop == loop && (loop ? repeated : motion.ReloadProgress >= 0.99f),
                "상체 장전 반복 또는 마지막 자세 유지: " + loop);
            if (!loop)
            {
                yield return new WaitForEndOfFrame();
                CaptureFrame("UpperBody_Reload-end");
            }
        }
        motion.CancelReload(true);
        motion.Loop = true;
        motion.PlayMotion("Normal_Idle", 0f);
        look.SetYaw(15f, 20f, 30f, true);
        yield return null;
        Check(Mathf.Abs(look.EyeYaw - 15f) < 0.1f && Mathf.Abs(look.FaceYaw - 20f) < 0.1f
            && Mathf.Abs(look.BodyYaw - 30f) < 0.1f, "눈·얼굴·몸 방향 조절");
        look.ResetYaw(true);
        Check(Mathf.Abs(look.EyeYaw) < 0.1f && Mathf.Abs(look.FaceYaw) < 0.1f && Mathf.Abs(look.BodyYaw) < 0.1f,
            "방향 초기화");
        var outline = motion.GetComponent<MariNativeOutline>();
        Check(outline != null && outline.Pairs != null && outline.Pairs.Length > 0, "윤곽선 연결");
        foreach (var renderer in motion.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
                Check(material != null && material.shader != null && material.shader.isSupported
                    && material.shader.name != "Hidden/InternalErrorShader", "렌더링 재질: " + renderer.name);
        cameraRig.SetFocus(MariPreviewCamera.FocusMode.Face);
        cameraRig.SetPreset(MariPreviewCamera.ViewPreset.Side);
        yield return null;
        Check(cameraRig.CurrentFocus == MariPreviewCamera.FocusMode.Face && Mathf.Abs(cameraRig.CurrentYaw - 90f) < 0.1f,
            "카메라 얼굴 확대와 시점 변경");
        cameraRig.SetFocus(MariPreviewCamera.FocusMode.FullBody);
        cameraRig.SetPreset(MariPreviewCamera.ViewPreset.Front);
        foreach (var size in new[] { new Vector2Int(1440, 900), new Vector2Int(1280, 720), new Vector2Int(1920, 1080) })
        {
            Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(0.35f);
            yield return new WaitForEndOfFrame();
            var rect = controls.ControlsRect;
            Check(Screen.width == size.x && Screen.height == size.y, "창 크기 변경: " + size);
            Check(Mathf.Abs(rect.height - (Screen.height - 24f)) < 0.01f
                && Mathf.Abs(rect.yMax - (Screen.height - 12f)) < 0.01f, "패널 최대 높이: " + size);
            var camera = cameraRig.GetComponent<Camera>();
            Check(Mathf.Abs(camera.rect.x - (rect.xMax + 12f) / Screen.width) < 0.001f, "카메라와 UI 영역 분리: " + size);
            string screenshot = Path.Combine(output, size.x + "x" + size.y + ".png");
            var texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0f, 0f, Screen.width, Screen.height), 0, 0);
            texture.Apply();
            Check(texture.GetPixel((int)(rect.xMax + 32f), texture.height / 2).maxColorComponent > 0.05f,
                "화면 렌더링: " + size);
            File.WriteAllBytes(screenshot, texture.EncodeToPNG());
            Destroy(texture);
            report.screenshots.Add(screenshot);
        }
        Finish();
    }

    private void CaptureFrame(string name)
    {
        var texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0f, 0f, Screen.width, Screen.height), 0, 0);
        texture.Apply();
        string screenshot = Path.Combine(output, name + ".png");
        File.WriteAllBytes(screenshot, texture.EncodeToPNG());
        Destroy(texture);
        report.screenshots.Add(screenshot);
    }

    private void Finish()
    {
        report.passed = report.errors.Count == 0;
        File.WriteAllText(Path.Combine(output, "runtime.json"),
            JsonUtility.ToJson(report, true).Replace("\n", "\r\n"), new System.Text.UTF8Encoding(false));
        Application.Quit(report.passed ? 0 : 1);
    }
}