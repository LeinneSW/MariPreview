using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class MariNativeImport
{
    const string Model = "Assets/Models/MariRig.fbx";

    [MenuItem("Tools/마리/Blender 변환 반영")]
    public static void VerifyCurrent() => Synchronize();

    [MenuItem("Tools/마리/Blender 변환 반영", true)]
    static bool CanSynchronize() => !EditorApplication.isPlayingOrWillChangePlaymode;

    static void Synchronize()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Play를 종료한 뒤 Blender 변환 반영 메뉴를 실행해 주세요.");
            return;
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mari.prefab");
        if (prefab == null || prefab.GetComponent<MariNativeMotion>() == null) return;
        try
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToArray();
            if (clips.Length < 34 || !clips.Any(c => c.name == "Move_Ing")) throw new Exception("원본 모션 연결을 확인해 주세요.");
            foreach (var imported in clips)
            {
                var target = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/MariOriginal/" + imported.name + ".anim");
                if (target == null)
                {
                    target = new AnimationClip();
                    AssetDatabase.CreateAsset(target, "Assets/Animations/MariOriginal/" + imported.name + ".anim");
                }
                EditorUtility.CopySerialized(imported, target);
                target.name = imported.name;
                target.hideFlags = HideFlags.None;
                var settings = AnimationUtility.GetAnimationClipSettings(target);
                settings.loopTime = imported.name.EndsWith("Idle") || imported.name == "Move_Ing" || imported.name == "Cafe_Walk"
                    || imported.name == "Victory_End" || imported.name == "Jump_Air";
                settings.loopBlend = false;
                AnimationUtility.SetAnimationClipSettings(target, settings);
                ReduceKeys(target);
                EditorUtility.SetDirty(target);
            }
            ConnectClips();
            foreach (var pair in prefab.GetComponent<MariNativeOutline>().Pairs)
            {
                var target = pair.outline.sharedMesh;
                string name = target.name;
                EditorUtility.CopySerialized(pair.source.sharedMesh, target);
                target.name = name;
                target.normals = SmoothNormals(target);
                EditorUtility.SetDirty(target);
            }
            foreach (var pair in prefab.GetComponent<MariNativeOutline>().StaticPairs)
            {
                var target = pair.outline.sharedMesh;
                string name = target.name;
                EditorUtility.CopySerialized(pair.source.sharedMesh, target);
                target.name = name;
                target.normals = SmoothNormals(target);
                EditorUtility.SetDirty(target);
            }
            AssetDatabase.SaveAssets();
            Validate(prefab, clips);
            Debug.Log($"마리 원본 연결 검증 완료: 모션 {clips.Length}개, 입 채널·외곽선 동기화");
        }
        catch (Exception error) { Debug.LogException(error); }
    }

    static void ConnectClips()
    {
        const string path = "Assets/Prefabs/Mari.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var animator = root.GetComponentInChildren<Animator>();
            var controller = animator.runtimeAnimatorController as AnimatorController;
            if (controller == null) throw new Exception("마리 Animator Controller 연결을 확인해 주세요.");
            var available = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .Select(c => AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animations/MariOriginal/" + c.name + ".anim"))
                .OrderBy(c => c.name, StringComparer.Ordinal).ToArray();
            OrganizeController(controller, available);
            ConfigureUpperBody(controller, root, available);
            root.GetComponent<MariNativeMotion>().Configure(animator, available);
            ConfigureLook(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    [MenuItem("Tools/마리/Animator 정리")]
    public static void OrganizeAnimator()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Play를 종료한 뒤 Animator 정리를 실행해 주세요.");
            return;
        }
        var prefab = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Mari.prefab");
        try
        {
            var controller = prefab.GetComponentInChildren<Animator>().runtimeAnimatorController as AnimatorController;
            var clips = prefab.GetComponent<MariNativeMotion>().Clips;
            OrganizeController(controller, clips);
            ConfigureUpperBody(controller, prefab, clips);
            ConfigureLook(prefab);
            PrefabUtility.SaveAsPrefabAsset(prefab, "Assets/Prefabs/Mari.prefab");
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        AssetDatabase.SaveAssets();
        Debug.Log("마리 Animator 정리 완료: 7개 모션 그룹·상체 장전 레이어");
    }

    public static void ConfigureLook(GameObject prefab)
    {
        var animator = prefab.GetComponentInChildren<Animator>();
        animator.Rebind();
        animator.Update(0f);
        animator.Play(MariMotionCatalog.ResolveState(animator, "Normal_Idle"), 0, 0f);
        animator.Update(0f);
        var transforms = prefab.GetComponentsInChildren<Transform>(true);
        var head = transforms.Single(t => t.name == "Bip001 Head");
        var left = transforms.Single(t => t.name == "Bip001 Xtra_eyeL");
        var right = transforms.Single(t => t.name == "Bip001 Xtra_eyeR");
        var look = prefab.GetComponent<MariLook>() ?? prefab.AddComponent<MariLook>();
        Vector3 Pivot(Transform bone)
        {
            var outline = prefab.GetComponent<MariNativeOutline>();
            var renderers = outline.Pairs.Select(p => p.source).Where(r => r != null);
            foreach (var renderer in renderers)
            {
                int index = Array.IndexOf(renderer.bones, bone);
                if (index < 0) continue;
                var weights = renderer.sharedMesh.boneWeights;
                var baked = new Mesh();
                try
                {
                    renderer.BakeMesh(baked);
                    var vertices = baked.vertices;
                    Vector3 center = Vector3.zero;
                    float count = 0f, minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
                    for (int i = 0; i < weights.Length; i++)
                    {
                        var w = weights[i];
                        float influence = (w.boneIndex0 == index ? w.weight0 : 0f) + (w.boneIndex1 == index ? w.weight1 : 0f)
                            + (w.boneIndex2 == index ? w.weight2 : 0f) + (w.boneIndex3 == index ? w.weight3 : 0f);
                        if (influence < 0.9f) continue;
                        Vector3 point = renderer.transform.TransformPoint(vertices[i]);
                        center += point;
                        count++;
                        float x = prefab.transform.InverseTransformPoint(point).x;
                        minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    }
                    if (count <= 0f) continue;
                    center /= count;
                    float radius = Mathf.Clamp((maxX - minX) * 0.5f, 0.01f, 0.035f);
                    Vector3 pivot = center - prefab.transform.forward * radius * prefab.transform.lossyScale.x;
                    return bone.parent.InverseTransformPoint(pivot);
                }
                finally { UnityEngine.Object.DestroyImmediate(baked); }
            }
            throw new Exception("눈동자 중심을 찾을 수 없습니다: " + bone.name);
        }
        look.Configure(prefab.transform, head, left, right, Pivot(left), Pivot(right));
        EditorUtility.SetDirty(look);
    }

    static void OrganizeController(AnimatorController controller, AnimationClip[] clips)
    {
        if (controller == null) throw new Exception("마리 Animator Controller가 없습니다.");
        var root = controller.layers[0].stateMachine;
        var allMachines = new List<AnimatorStateMachine>();
        void Collect(AnimatorStateMachine machine)
        {
            allMachines.Add(machine);
            foreach (var child in machine.stateMachines) Collect(child.stateMachine);
        }
        Collect(root);
        var oldStates = allMachines.SelectMany(m => m.states).Select(s => s.state).Distinct().ToArray();
        var names = new HashSet<string>(clips.Where(c => c != null).Select(c => c.name), StringComparer.Ordinal);
        var retained = oldStates.Where(s => names.Contains(s.name)).GroupBy(s => s.name)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var machine in allMachines)
        {
            foreach (var transition in machine.anyStateTransitions) UnityEngine.Object.DestroyImmediate(transition, true);
            foreach (var transition in machine.entryTransitions) UnityEngine.Object.DestroyImmediate(transition, true);
            machine.anyStateTransitions = Array.Empty<AnimatorStateTransition>();
            machine.entryTransitions = Array.Empty<AnimatorTransition>();
            machine.states = Array.Empty<ChildAnimatorState>();
            machine.defaultState = null;
        }
        root.stateMachines = Array.Empty<ChildAnimatorStateMachine>();
        foreach (var machine in allMachines.Where(m => m != root)) UnityEngine.Object.DestroyImmediate(machine, true);
        foreach (var state in oldStates)
        {
            foreach (var transition in state.transitions) UnityEngine.Object.DestroyImmediate(transition, true);
            state.transitions = Array.Empty<AnimatorStateTransition>();
            if (!retained.Values.Contains(state)) UnityEngine.Object.DestroyImmediate(state, true);
        }
        var groups = new Dictionary<string, AnimatorStateMachine>(StringComparer.Ordinal);
        for (int index = 0; index < MariMotionCatalog.Groups.Length; index++)
        {
            string name = MariMotionCatalog.Groups[index];
            var machine = root.AddStateMachine(name, new Vector3(280f + index % 3 * 290f, 60f + index / 3 * 160f));
            machine.entryPosition = new Vector3(20f, 30f);
            machine.anyStatePosition = new Vector3(20f, -40f);
            machine.exitPosition = new Vector3(850f, 30f);
            groups.Add(name, machine);
        }
        foreach (string group in MariMotionCatalog.Groups)
        {
            var machine = groups[group];
            var selected = clips.Where(c => c != null && MariMotionCatalog.Group(c.name) == group)
                .OrderBy(c => c.name, StringComparer.Ordinal).ToArray();
            var children = new List<ChildAnimatorState>();
            for (int index = 0; index < selected.Length; index++)
            {
                var clip = selected[index];
                var state = retained.TryGetValue(clip.name, out var previous) ? previous : machine.AddState(clip.name);
                state.motion = clip;
                children.Add(new ChildAnimatorState { state = state,
                    position = new Vector3(250f + index % 3 * 250f, 40f + index / 3 * 90f) });
                EditorUtility.SetDirty(state);
            }
            machine.states = children.ToArray();
            string defaultName = group == "이동" ? "Normal_Idle" : group == "점프·착지" ? "Jump_Start" : group == "카페" ? "Cafe_Idle" : null;
            machine.defaultState = children.Select(c => c.state).FirstOrDefault(s => s.name == defaultName)
                ?? children.Select(c => c.state).FirstOrDefault();
            EditorUtility.SetDirty(machine);
        }
        root.entryPosition = new Vector3(40f, 200f);
        root.anyStatePosition = new Vector3(40f, 40f);
        root.exitPosition = new Vector3(1160f, 200f);
        root.AddEntryTransition(groups["이동"]);
        controller.parameters = Array.Empty<AnimatorControllerParameter>();
        EditorUtility.SetDirty(root);
        EditorUtility.SetDirty(controller);
    }

    static void ConfigureUpperBody(AnimatorController controller, GameObject prefab, AnimationClip[] clips)
    {
        const string maskPath = "Assets/Animations/MariOriginal/UpperBodyReload.mask";
        var animator = prefab.GetComponentInChildren<Animator>();
        var transforms = animator.GetComponentsInChildren<Transform>(true);
        var pelvis = transforms.Single(t => t.name == "Bip001 Pelvis");
        var spine = transforms.Single(t => t.name == "Bip001 Spine");
        var weapon = transforms.Single(t => t.name == "Bip001_Weapon");
        if (pelvis.parent != weapon.parent) throw new Exception("장전용 총과 골반의 기준 계층을 확인해 주세요.");
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
        if (mask == null)
        {
            mask = new AvatarMask { name = "상체 장전 마스크" };
            AssetDatabase.CreateAsset(mask, maskPath);
        }
        mask.transformCount = transforms.Length;
        for (int i = 0; i < transforms.Length; i++)
        {
            mask.SetTransformPath(i, AnimationUtility.CalculateTransformPath(transforms[i], animator.transform));
            mask.SetTransformActive(i, transforms[i].IsChildOf(spine) || transforms[i].IsChildOf(weapon));
        }
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
        foreach (var part in new[] { AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head,
            AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
            AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers })
            mask.SetHumanoidBodyPartActive(part, true);
        EditorUtility.SetDirty(mask);

        if (!controller.layers.Any(l => l.name == MariNativeMotion.UpperBodyLayer))
            controller.AddLayer(MariNativeMotion.UpperBodyLayer);
        var layers = controller.layers;
        var layer = layers.Single(l => l.name == MariNativeMotion.UpperBodyLayer);
        layer.avatarMask = mask;
        layer.defaultWeight = 0f;
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        layer.syncedLayerIndex = -1;
        layer.iKPass = false;
        var machine = layer.stateMachine;
        machine.entryPosition = new Vector3(30f, 80f);
        machine.anyStatePosition = new Vector3(30f, -40f);
        machine.exitPosition = new Vector3(850f, 80f);
        var idle = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == "대기")
            ?? machine.AddState("대기", new Vector3(260f, 20f));
        idle.motion = null;
        idle.writeDefaultValues = false;
        machine.defaultState = idle;
        foreach (var stale in machine.states.Select(s => s.state).Where(s => s.name == "Kneel_Reload").ToArray())
        {
            machine.RemoveState(stale);
            UnityEngine.Object.DestroyImmediate(stale, true);
        }
        var reloads = clips.Where(c => c != null && c.name == "Normal_Reload")
            .OrderBy(c => c.name, StringComparer.Ordinal).ToArray();
        for (int i = 0; i < reloads.Length; i++)
        {
            var clip = reloads[i];
            var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == clip.name)
                ?? machine.AddState(clip.name, new Vector3(260f + i * 260f, 130f));
            state.motion = clip;
            state.writeDefaultValues = false;
            state.speedParameterActive = true;
            state.speedParameter = MariNativeMotion.UpperBodySpeedParameter;
            EditorUtility.SetDirty(state);
        }
        controller.layers = layers;
        if (!controller.parameters.Any(p => p.name == MariNativeMotion.UpperBodySpeedParameter))
            controller.AddParameter(MariNativeMotion.UpperBodySpeedParameter, AnimatorControllerParameterType.Float);
        var parameters = controller.parameters;
        var speed = parameters.Single(p => p.name == MariNativeMotion.UpperBodySpeedParameter);
        speed.defaultFloat = 1f;
        controller.parameters = parameters;
        controller.AddParameter(MariNativeMotion.FullBodyReloadSpeedParameter, AnimatorControllerParameterType.Float);
        parameters = controller.parameters;
        parameters.Single(p => p.name == MariNativeMotion.FullBodyReloadSpeedParameter).defaultFloat = 1f;
        controller.parameters = parameters;
        foreach (var group in controller.layers[0].stateMachine.stateMachines)
        foreach (var child in group.stateMachine.states)
        {
            if (child.state.name != "Normal_Reload" && child.state.name != "Kneel_Reload") continue;
            child.state.speedParameterActive = true;
            child.state.speedParameter = MariNativeMotion.FullBodyReloadSpeedParameter;
            EditorUtility.SetDirty(child.state);
        }

        string pelvisPath = AnimationUtility.CalculateTransformPath(pelvis, animator.transform);
        string[] properties = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z",
            "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
        float[] defaults = { pelvis.localPosition.x, pelvis.localPosition.y, pelvis.localPosition.z,
            pelvis.localRotation.x, pelvis.localRotation.y, pelvis.localRotation.z, pelvis.localRotation.w };
        var references = reloads.Select(clip => new MariNativeMotion.ReloadReference
        {
            clip = clip,
            pelvisCurves = properties.Select((property, i) => AnimationUtility.GetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(pelvisPath, typeof(Transform), property))
                ?? AnimationCurve.Constant(0f, clip.length, defaults[i])).ToArray()
        }).ToArray();
        AnimationCurve[] PoseCurves() => Enumerable.Range(0, 7).Select(_ => new AnimationCurve()).ToArray();
        foreach (var reference in references)
        {
            reference.pelvisRootCurves = PoseCurves();
            reference.spineRootCurves = PoseCurves();
            reference.weaponSpineCurves = PoseCurves();
            animator.Rebind(); animator.Update(0f);
            int state = MariMotionCatalog.ResolveState(animator, reference.clip.name);
            int sampleCount = Mathf.CeilToInt(reference.clip.length * 120f);
            void Record(AnimationCurve[] curves, float time, Vector3 position, Quaternion rotation)
            {
                float[] values = { position.x, position.y, position.z, rotation.x, rotation.y, rotation.z, rotation.w };
                for (int index = 0; index < values.Length; index++)
                {
                    var keys = curves[index].keys;
                    float slope = keys.Length > 0 ? (values[index] - keys[keys.Length - 1].value) / (time - keys[keys.Length - 1].time) : 0f;
                    if (keys.Length > 0)
                    {
                        var last = keys[keys.Length - 1]; last.outTangent = slope;
                        curves[index].MoveKey(keys.Length - 1, last);
                    }
                    curves[index].AddKey(new Keyframe(time, values[index], slope, slope));
                }
            }
            for (int sample = 0; sample <= sampleCount; sample++)
            {
                float progress = (float)sample / sampleCount;
                float time = progress * reference.clip.length;
                animator.Play(state, 0, progress); animator.Update(0f);
                Record(reference.pelvisRootCurves, time, animator.transform.InverseTransformPoint(pelvis.position),
                    Quaternion.Inverse(animator.transform.rotation) * pelvis.rotation);
                Record(reference.spineRootCurves, time, animator.transform.InverseTransformPoint(spine.position),
                    Quaternion.Inverse(animator.transform.rotation) * spine.rotation);
                Record(reference.weaponSpineCurves, time, spine.InverseTransformPoint(weapon.position),
                    Quaternion.Inverse(spine.rotation) * weapon.rotation);
            }
            animator.Rebind(); animator.Update(0f);
        }
        prefab.GetComponent<MariNativeMotion>().ConfigureReload(pelvis, weapon, spine, references);
        EditorUtility.SetDirty(idle);
        EditorUtility.SetDirty(machine);
        EditorUtility.SetDirty(controller);
    }

    public static Vector3[] SmoothNormals(Mesh mesh)
    {
        var vertices = mesh.vertices;
        var keys = vertices.Select(v => new Vector3Int(Mathf.RoundToInt(v.x * 10000f), Mathf.RoundToInt(v.y * 10000f), Mathf.RoundToInt(v.z * 10000f))).ToArray();
        var sums = new Dictionary<Vector3Int, Vector3>();
        foreach (var key in keys) sums.TryAdd(key, Vector3.zero);
        var triangles = mesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            var n = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            sums[keys[a]] += n; sums[keys[b]] += n; sums[keys[c]] += n;
        }
        return keys.Select(key => sums[key].normalized).ToArray();
    }

    public static void ReduceKeys(AnimationClip clip)
    {
        var bindings = AnimationUtility.GetCurveBindings(clip);
        var compactCurves = new AnimationCurve[bindings.Length];
        for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
        {
            var binding = bindings[bindingIndex];
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            var keys = curve.keys;
            if (keys.Length < 3) { compactCurves[bindingIndex] = curve; continue; }
            bool step = binding.propertyName.StartsWith("blendShape.");
            if (step)
                for (int i = 0; i < keys.Length; i++) keys[i].value = keys[i].value >= 50f ? 100f : 0f;
            var keep = new SortedSet<int> { 0, keys.Length - 1 };
            if (step)
            {
                for (int i = 1; i < keys.Length; i++)
                    if (!Mathf.Approximately(keys[i].value, keys[i - 1].value)) keep.Add(i);
            }
            else
            {
                float tolerance = binding.propertyName.Contains("Rotation") ? 0.000001f : 0.00001f;
                var ranges = new Stack<(int, int)>();
                ranges.Push((0, keys.Length - 1));
                while (ranges.Count > 0)
                {
                    var (first, last) = ranges.Pop();
                    float duration = keys[last].time - keys[first].time;
                    float maximum = tolerance;
                    int selected = -1;
                    for (int i = first + 1; i < last; i++)
                    {
                        float factor = duration > 0f ? (keys[i].time - keys[first].time) / duration : 0f;
                        float expected = Mathf.LerpUnclamped(keys[first].value, keys[last].value, factor);
                        float error = Mathf.Abs(keys[i].value - expected);
                        if (error > maximum) { maximum = error; selected = i; }
                    }
                    if (selected >= 0)
                    {
                        keep.Add(selected);
                        ranges.Push((first, selected));
                        ranges.Push((selected, last));
                    }
                }
            }
            var reduced = keep.Select(i => keys[i]).ToArray();
            for (int i = 0; i < reduced.Length; i++)
            {
                float left = i > 0 ? (reduced[i].value - reduced[i-1].value) / (reduced[i].time - reduced[i-1].time) : 0f;
                float right = i+1 < reduced.Length ? (reduced[i+1].value - reduced[i].value) / (reduced[i+1].time - reduced[i].time) : 0f;
                reduced[i] = new Keyframe(reduced[i].time, reduced[i].value,
                    step ? float.PositiveInfinity : left, step ? float.PositiveInfinity : right);
            }
            compactCurves[bindingIndex] = new AnimationCurve(reduced) {
                preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
        }
        AnimationUtility.SetEditorCurves(clip, bindings, compactCurves);
    }

    static void Validate(GameObject prefab, AnimationClip[] clips)
    {
        var animator = prefab.GetComponentInChildren<Animator>();
        var report = new ImportReport { clips = clips.Length, avatarValid = animator.avatar != null && animator.avatar.isValid,
            avatarHuman = animator.avatar != null && animator.avatar.isHuman,
            skinnedRenderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>().Count(r => !r.name.EndsWith("_외곽선")),
            mouthChannels = prefab.GetComponentsInChildren<SkinnedMeshRenderer>().Count(r => r.name.StartsWith("입_") && !r.name.EndsWith("_외곽선")) };
        foreach (var clip in clips)
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                if (!string.IsNullOrEmpty(binding.path) && animator.transform.Find(binding.path) == null)
                    report.missingPaths++;
        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>())
            if (renderer.sharedMaterials.Any(m => m == null || m.shader == null || !m.shader.isSupported)) report.missingMaterials++;
        if (!report.avatarValid || report.missingPaths > 0 || report.missingMaterials > 0)
            throw new Exception($"마리 연결 오류: Avatar={report.avatarValid}, 경로={report.missingPaths}, 재질={report.missingMaterials}");
        string root = Path.GetDirectoryName(Application.dataPath);
        string folder = Path.Combine(root, "Review/MariOriginal");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "unity-import.json"), JsonUtility.ToJson(report, true).Replace("\n", "\r\n"), new System.Text.UTF8Encoding(false));
    }

    [Serializable]
    class ImportReport
    {
        public int clips, skinnedRenderers, mouthChannels, missingPaths, missingMaterials;
        public bool avatarValid, avatarHuman;
    }
}
