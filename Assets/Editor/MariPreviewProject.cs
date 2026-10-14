using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MariPreviewProject
{
    public const string ScenePath = "Assets/MariPreview/Scenes/MariPreview.unity";

    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        if (Application.isBatchMode) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (sceneAsset == null) return;
            EditorSceneManager.playModeStartScene = sceneAsset;
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path) && !scene.isDirty)
                EditorSceneManager.OpenScene(ScenePath);
        };
    }

    [MenuItem("Tools/마리 프리뷰/Windows 실행 파일 빌드")]
    public static void BuildWindows()
    {
        EditorSceneManager.OpenScene(ScenePath);
        var controls = UnityEngine.Object.FindAnyObjectByType<MariPreviewController>();
        var motion = UnityEngine.Object.FindAnyObjectByType<MariNativeMotion>();
        var look = UnityEngine.Object.FindAnyObjectByType<MariLook>();
        if (controls == null || motion == null || look == null || !look.IsConfigured)
            throw new InvalidOperationException("프리뷰의 모델·모션·시선 연결을 확인해 주세요.");
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) != 0)
                    throw new InvalidOperationException("스크립트 연결이 누락됐습니다: " + transform.name);
        if (motion.Clips == null || motion.Clips.Length != 37 || motion.Clips.Any(c => c == null || c.length <= 0f))
            throw new InvalidOperationException("37개 모션 클립을 모두 불러오지 못했습니다.");
        var animator = motion.GetComponentInChildren<Animator>();
        if (animator == null || animator.runtimeAnimatorController == null || animator.avatar == null || !animator.avatar.isValid)
            throw new InvalidOperationException("모델의 Animator 또는 Avatar 연결이 누락됐습니다.");
        foreach (var renderer in motion.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
                if (material == null || material.shader == null || material.shader.name == "Hidden/InternalErrorShader")
                    throw new InvalidOperationException("재질 또는 셰이더 연결이 누락됐습니다: " + renderer.name);

        PlayerSettings.productName = "MariPreview";
        PlayerSettings.companyName = "MariPreview";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.local.maripreview");
        PlayerSettings.defaultScreenWidth = 1440;
        PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();

        string rootPath = Path.GetDirectoryName(Application.dataPath);
        string executable = Path.Combine(rootPath, "Builds/Windows/MariPreview.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable));
        int clipCount = motion.Clips.Length;
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = executable,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        Directory.CreateDirectory(Path.Combine(rootPath, "Review"));
        var audit = new BuildAudit
        {
            result = report.summary.result.ToString(),
            errors = (int)report.summary.totalErrors,
            warnings = (int)report.summary.totalWarnings,
            animationClips = clipCount,
            dependencies = AssetDatabase.GetDependencies(ScenePath, true),
            sizeBytes = (long)report.summary.totalSize
        };
        File.WriteAllText(Path.Combine(rootPath, "Review/build.json"),
            JsonUtility.ToJson(audit, true).Replace("\n", "\r\n"), new System.Text.UTF8Encoding(false));
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Windows 빌드에 실패했습니다. 빌드 로그를 확인해 주세요.");
        Debug.Log("마리 프리뷰 Windows 빌드를 완료했습니다: " + executable);
    }

    [Serializable]
    private sealed class BuildAudit
    {
        public string result;
        public int errors, warnings, animationClips;
        public long sizeBytes;
        public string[] dependencies;
    }
}