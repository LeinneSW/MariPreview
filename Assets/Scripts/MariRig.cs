using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class MariRig : MonoBehaviour
{
    [Flags]
    public enum ProceduralParts
    {
        None = 0, Skirt = 1, Ears = 2, Hair = 4, Hands = 8,
        All = Skirt | Ears | Hair | Hands
    }

    public enum EarPose
    {
        Neutral,
        Alert,
        Drooped,
        Back,
        Curious,
        Forward
    }

    [Serializable]
    public sealed class SkirtPanel
    {
        public Transform root;
        public Transform mid;
        public Transform hem;
        public Vector3 outward;
        public Quaternion restRotation;
        public Quaternion midRotation;
        public Quaternion hemRotation;
        public Vector3[] hemProbes;
        public Vector3 restDirection;
        [NonSerialized] public float angle;
        [NonSerialized] public float velocity;
    }

    [Serializable]
    public sealed class Ear
    {
        public Transform root;
        public Transform mid;
        public Transform tip;
        public Transform end;
        public Quaternion restRotation;
        public Quaternion midRotation;
        public Quaternion tipRotation;
        public Quaternion endRotation;
        public Vector3 direction;
        public Vector3 outward;
        [NonSerialized] public Vector3 poseBend;
        [NonSerialized] public Vector3 poseVelocity;
        [NonSerialized] public Vector3 tipPoseBend;
        [NonSerialized] public Vector3 tipPoseVelocity;
        [NonSerialized] public Vector3 bend;
        [NonSerialized] public Vector3 velocity;
        [NonSerialized] public Vector3 tipBend;
        [NonSerialized] public Vector3 tipVelocity;
    }

    [Serializable]
    public sealed class HairStrand
    {
        public Transform root;
        public Transform tip;
        public Quaternion restRotation;
        public Quaternion tipRotation;
        [NonSerialized] public Vector3 bend;
        [NonSerialized] public Vector3 velocity;
        [NonSerialized] public Vector3 tipBend;
        [NonSerialized] public Vector3 tipVelocity;
        [NonSerialized] public Vector3 armAvoidance;
        [NonSerialized] public Vector3 armAvoidanceVelocity;
        [NonSerialized] public Vector3 tipArmAvoidance;
        [NonSerialized] public Vector3 tipArmAvoidanceVelocity;
    }

    [Serializable]
    public sealed class FingerPose
    {
        public Transform bone;
        public Quaternion restRotation;
        public Quaternion runRotation;
    }

    [SerializeField, InspectorName("자동 보정 부위")] private ProceduralParts proceduralParts = ProceduralParts.All;
    private const float SecondaryMotion = 0.5f;
    private const float EarTransitionTime = 0.18f;
    private const float EarElasticity = 0.7f;
    private const float FistTransitionTime = 0.14f;
    private EarPose earPose = EarPose.Neutral;
    private float leftEarLift;
    private float rightEarLift;
    [Header("뒷머리")]
    [InspectorName("움직임 강도"), SerializeField, Range(0f, 1.5f)] private float hairMotionStrength = 1f;
    [InspectorName("위팔 충돌 반경"), SerializeField, Range(0.015f, 0.05f)] private float hairArmClearance = 0.03f;
    [InspectorName("팔뚝 충돌 반경"), SerializeField, Range(0.02f, 0.06f)] private float hairForearmRadius = 0.04f;
    [InspectorName("손 충돌 반경"), SerializeField, Range(0.015f, 0.045f)] private float hairHandRadius = 0.027f;
    [SerializeField, HideInInspector] private Transform reference;
    [SerializeField, HideInInspector] private Transform hips;
    [SerializeField, HideInInspector] private Transform head;
    [SerializeField, HideInInspector] private Transform[] thighs;
    [SerializeField, HideInInspector] private Transform[] knees;
    [SerializeField, HideInInspector] private Quaternion hipsRestRotation;
    [SerializeField, HideInInspector] private Quaternion headRestRotation;
    [SerializeField, HideInInspector] private SkirtPanel[] skirt = Array.Empty<SkirtPanel>();
    [SerializeField, HideInInspector] private Ear[] ears = Array.Empty<Ear>();
    [SerializeField, HideInInspector] private HairStrand[] hair = Array.Empty<HairStrand>();
    [SerializeField, HideInInspector] private FingerPose[] fingers = Array.Empty<FingerPose>();
    private Transform[] upperArms = Array.Empty<Transform>();
    private Transform[] forearms = Array.Empty<Transform>();
    private Transform[] hands = Array.Empty<Transform>();
    private Transform[] handEnds = Array.Empty<Transform>();
    private Vector3 previousHips, previousVelocity;
    private Vector3 previousReferencePosition;
    private Quaternion previousHead;
    private MariAnimator motionController;
    private float fistAmount, fistVelocity;
    private float hairMotion, hairMotionVelocity, hairPhase;
    private const int HairProbeCount = 32;
    private const int HairCollisionPasses = 6;
    private const float HairRootAvoidanceLimit = 24f;
    private const float HairTipAvoidanceLimit = 18f;
    private sealed class HairSurface
    {
        public SkinnedMeshRenderer renderer;
        public Mesh bakedMesh;
        public readonly List<Vector3> vertices = new List<Vector3>();
    }

    private HairSurface[] hairSurfaces = Array.Empty<HairSurface>();
    private int[][] hairProbeIndices = Array.Empty<int[]>();
    private float[][] hairRootInfluences = Array.Empty<float[]>();
    private float[][] hairTipInfluences = Array.Empty<float[]>();
    private Vector3[][] hairRootProbePositions = Array.Empty<Vector3[]>();
    private Vector3[][] hairTipProbePositions = Array.Empty<Vector3[]>();
    private readonly Vector3[] hairContactRootGradients = new Vector3[HairProbeCount * 6];
    private readonly Vector3[] hairContactTipGradients = new Vector3[HairProbeCount * 6];
    private readonly float[] hairContactDepths = new float[HairProbeCount * 6];
    private bool initialized;
    private float[] skirtTargets = Array.Empty<float>();
    private float[] skirtClearances = Array.Empty<float>();
    private float[] skirtFloorRequired = Array.Empty<float>();
    private float[] skirtLimits = Array.Empty<float>();
    private float[] skirtAdjusted = Array.Empty<float>();
    private Vector3[] skirtAxes = Array.Empty<Vector3>();
    private Quaternion[] skirtBases = Array.Empty<Quaternion>();
    private bool[] skirtValid = Array.Empty<bool>();
    private readonly Dictionary<string, Transform> auxiliaryBones = new Dictionary<string, Transform>(StringComparer.Ordinal);
    private readonly Dictionary<string, Quaternion> boneOffsets = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
    private static readonly string[] SkirtSectors =
    {
        "Front", "FrontToFrontL", "FrontL", "FrontLToLeft", "Left", "LeftToBackL",
        "BackL", "BackLToBack", "Back", "BackToBackR", "BackR", "BackRToRight",
        "Right", "RightToFrontR", "FrontR", "FrontRToFront"
    };

    public int SkirtDirectionCount => skirt.Length;
    public int AuxiliaryBoneCount => auxiliaryBones.Count;
    public Transform GetAuxiliaryBone(string name) => name != null && auxiliaryBones.TryGetValue(name, out var bone) ? bone : null;
    public bool SetBoneOffset(string name, Vector3 localEulerDegrees)
    {
        if (GetAuxiliaryBone(name) == null) return false;
        if (float.IsNaN(localEulerDegrees.x) || float.IsNaN(localEulerDegrees.y) || float.IsNaN(localEulerDegrees.z)
            || float.IsInfinity(localEulerDegrees.x) || float.IsInfinity(localEulerDegrees.y) || float.IsInfinity(localEulerDegrees.z)) return false;
        boneOffsets[name] = Quaternion.Euler(localEulerDegrees);
        return true;
    }
    public void ClearBoneOffsets() => boneOffsets.Clear();
    private Quaternion BoneOffset(Transform bone) => bone != null && boneOffsets.TryGetValue(bone.name, out var offset)
        ? offset : Quaternion.identity;

    public ProceduralParts ActiveProceduralParts => proceduralParts;
    public void SetProceduralParts(ProceduralParts parts)
    {
        proceduralParts = parts & ProceduralParts.All;
        initialized = false;
    }
    private bool Controls(ProceduralParts part) => (proceduralParts & part) != 0;

    public EarPose CurrentEarPose => earPose;
    public float HairArmResidualPenetration { get; private set; }
    public void SetEarPose(EarPose pose) => earPose = pose;
    public void SetEarLift(float left, float right)
    {
        leftEarLift = Mathf.Clamp(left, -1f, 1f);
        rightEarLift = Mathf.Clamp(right, -1f, 1f);
    }

    public void SetRunFistPose(IReadOnlyDictionary<string, Quaternion> rotations)
    {
        if (rotations == null) throw new ArgumentNullException(nameof(rotations));
        foreach (var finger in fingers)
            if (finger != null && finger.bone != null && rotations.TryGetValue(finger.bone.name, out var rotation))
                finger.runRotation = rotation;
    }

    public void RebindModel(Transform model)
    {
        if (model == null) throw new ArgumentNullException(nameof(model));

        var bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
        foreach (Transform bone in model.GetComponentsInChildren<Transform>(true))
            if (!bones.ContainsKey(bone.name)) bones.Add(bone.name, bone);

        Transform Find(string name) => bones.TryGetValue(name, out var bone)
            ? bone : throw new InvalidOperationException($"마리 본을 찾을 수 없습니다: {name}");

        Transform newHips = Find("DEF-spine");
        Transform newHead = Find("DEF-spine.006");
        var newSkirt = new List<SkirtPanel>();
        foreach (string sector in SkirtSectors)
        {
            string name = "Mari_Skirt_" + sector;
            if (!bones.TryGetValue(name, out Transform root)) continue;
            bones.TryGetValue(name + "_Mid", out Transform mid);
            Transform hem = Find(name + "_Hem");
            SkirtPanel old = Array.Find(skirt ?? Array.Empty<SkirtPanel>(), panel => panel?.root != null && panel.root.name == name);
            var probes = old?.hemProbes == null ? Array.Empty<Vector3>() : new Vector3[old.hemProbes.Length];
            for (int j = 0; j < probes.Length; j++)
            {
                Vector3 point = old.root.TransformPoint(old.hemProbes[j]);
                probes[j] = root.InverseTransformPoint(point);
            }

            Vector3 outward = model.InverseTransformDirection(root.position - newHips.position);
            outward.y = 0f;
            outward.Normalize();
            newSkirt.Add(new SkirtPanel
            {
                root = root,
                mid = mid,
                hem = hem,
                outward = outward,
                restRotation = Quaternion.Inverse(model.rotation) * root.rotation,
                midRotation = mid != null ? mid.localRotation : Quaternion.identity,
                hemRotation = hem.localRotation,
                hemProbes = probes,
                restDirection = model.InverseTransformDirection((hem.position - root.position).normalized)
            });
        }

        var newEars = new Ear[2];
        for (int i = 0; i < newEars.Length; i++)
        {
            string name = i == 0 ? "Mari_Ear_L" : "Mari_Ear_R";
            Transform root = Find(name);
            bones.TryGetValue(name + "_Mid", out Transform mid);
            Transform tip = Find(name + "_Tip");
            bones.TryGetValue(name + "_End", out Transform end);
            Ear old = Array.Find(ears ?? Array.Empty<Ear>(), ear => ear?.root != null && ear.root.name == name);
            newEars[i] = new Ear
            {
                root = root,
                mid = mid,
                tip = tip,
                end = end,
                restRotation = Quaternion.Inverse(newHead.rotation) * root.rotation,
                midRotation = mid != null ? mid.localRotation : Quaternion.identity,
                tipRotation = tip.localRotation,
                endRotation = end != null ? end.localRotation : Quaternion.identity,
                direction = model.InverseTransformDirection(((end != null ? end : tip).position - root.position).normalized),
                outward = old == null ? (i == 0 ? Vector3.left : Vector3.right) : old.outward
            };
        }

        var newHair = new HairStrand[2];
        for (int i = 0; i < newHair.Length; i++)
        {
            string name = i == 0 ? "Mari_Hair_L" : "Mari_Hair_R";
            Transform root = Find(name);
            Transform tip = Find(name + "_Tip");
            newHair[i] = new HairStrand
            {
                root = root,
                tip = tip,
                restRotation = Quaternion.Inverse(newHead.rotation) * root.rotation,
                tipRotation = Quaternion.Inverse(root.rotation) * tip.rotation
            };
        }

        reference = model;
        hips = newHips;
        head = newHead;
        thighs = new[] { Find("DEF-thigh.L"), Find("DEF-thigh.R") };
        knees = new[] { Find("DEF-shin.L"), Find("DEF-shin.R") };
        hipsRestRotation = Quaternion.Inverse(model.rotation) * hips.rotation;
        headRestRotation = Quaternion.Inverse(model.rotation) * head.rotation;
        skirt = newSkirt.ToArray();
        ears = newEars;
        hair = newHair;
        CacheSkirtHemProbes();
        CacheAuxiliaryBones();
        CacheArms();
        if (Application.isPlaying) CacheHairSurface();
        var newFingers = new FingerPose[24];
        int fingerIndex = 0;
        foreach (string side in new[] { "L", "R" })
            foreach (string digit in new[] { "Index", "Mid", "Ring", "Pinky" })
                for (int section = 1; section <= 3; section++)
                {
                    Transform bone = Find($"CC_Base_{side}_{digit}{section}");
                    FingerPose old = Array.Find(fingers ?? Array.Empty<FingerPose>(), finger => finger?.bone != null && finger.bone.name == bone.name);
                    newFingers[fingerIndex++] = new FingerPose
                    {
                        bone = bone,
                        restRotation = bone.localRotation,
                        runRotation = old != null ? old.runRotation : bone.localRotation
                    };
                }
        fingers = newFingers;
        initialized = false;
    }

    private void CacheAuxiliaryBones()
    {
        auxiliaryBones.Clear();
        void Add(Transform bone) { if (bone != null) auxiliaryBones[bone.name] = bone; }
        foreach (var panel in skirt) { Add(panel.root); Add(panel.mid); Add(panel.hem); }
        foreach (var ear in ears) { Add(ear.root); Add(ear.mid); Add(ear.tip); Add(ear.end); }
    }

    private void CacheSkirtHemProbes()
    {
        SkinnedMeshRenderer renderer = null;
        foreach (var candidate in reference.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (candidate.name == "치마" && candidate.sharedMesh != null) { renderer = candidate; break; }
        if (renderer == null) return;
        var mesh = new Mesh { name = "마리 치마 밑단 평가", hideFlags = HideFlags.HideAndDontSave };
        var baseline = new List<Vector3>();
        var moved = new List<Vector3>();
        try
        {
            renderer.BakeMesh(mesh, false);
            mesh.GetVertices(baseline);
            foreach (var panel in skirt)
            {
                var points = new List<Vector3>();
                Quaternion rotation = panel.hem.localRotation;
                Vector3 axis = Vector3.Cross(-reference.up, reference.TransformDirection(panel.outward)).normalized;
                try
                {
                    panel.hem.rotation = Quaternion.AngleAxis(0.5f, axis) * panel.hem.rotation;
                    renderer.BakeMesh(mesh, false);
                    mesh.GetVertices(moved);
                }
                finally { panel.hem.localRotation = rotation; }
                for (int index = 0; index < Mathf.Min(baseline.Count, moved.Count); index++)
                    if ((moved[index] - baseline[index]).sqrMagnitude > 1e-12f)
                        points.Add(renderer.transform.TransformPoint(baseline[index]));
                points.Sort((a, b) => reference.InverseTransformPoint(a).y.CompareTo(reference.InverseTransformPoint(b).y));
                var selected = new List<Vector3>();
                foreach (Vector3 point in points)
                {
                    bool distinct = true;
                    foreach (Vector3 old in selected)
                        if ((point - old).sqrMagnitude < 0.0001f) { distinct = false; break; }
                    if (!distinct) continue;
                    selected.Add(point);
                    if (selected.Count == 4) break;
                }
                if (selected.Count == 0) continue;
                panel.hemProbes = new Vector3[selected.Count];
                for (int index = 0; index < selected.Count; index++)
                    panel.hemProbes[index] = panel.root.InverseTransformPoint(selected[index]);
            }
        }
        finally
        {
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }
    }

    private bool HasExpandedBinding()
    {
        if (reference == null || hips == null || head == null || skirt.Length != 16 || ears.Length != 2) return false;
        foreach (var panel in skirt)
            if (panel?.root == null || panel.mid == null || panel.hem == null) return false;
        foreach (var ear in ears)
            if (ear?.root == null || ear.mid == null || ear.tip == null || ear.end == null) return false;
        return true;
    }

    private void Awake()
    {
        motionController = GetComponent<MariAnimator>();
        if (reference == null)
        {
            var animator = GetComponentInChildren<Animator>(true);
            if (animator != null) reference = animator.transform;
        }
        if (reference != null && !HasExpandedBinding()) RebindModel(reference);
        else
        {
            CacheAuxiliaryBones();
            CacheArms();
            CacheHairSurface();
        }
    }

    private void CacheArms()
    {
        upperArms = new Transform[2];
        forearms = new Transform[2];
        hands = new Transform[2];
        handEnds = new Transform[2];
        if (reference == null) return;
        foreach (Transform bone in reference.GetComponentsInChildren<Transform>(true))
        {
            switch (bone.name)
            {
                case "DEF-upper_arm.L": upperArms[0] = bone; break;
                case "DEF-forearm.L": forearms[0] = bone; break;
                case "DEF-hand.L": hands[0] = bone; break;
                case "DEF-upper_arm.R": upperArms[1] = bone; break;
                case "DEF-forearm.R": forearms[1] = bone; break;
                case "DEF-hand.R": hands[1] = bone; break;
                case "CC_Base_L_Mid3": handEnds[0] = bone; break;
                case "CC_Base_R_Mid3": handEnds[1] = bone; break;
            }
        }
    }

    private void CacheHairSurface()
    {
        DisposeHairSurfaces();
        hairProbeIndices = Array.Empty<int[]>();
        hairRootInfluences = Array.Empty<float[]>();
        hairTipInfluences = Array.Empty<float[]>();
        hairRootProbePositions = Array.Empty<Vector3[]>();
        hairTipProbePositions = Array.Empty<Vector3[]>();
        HairArmResidualPenetration = 0f;
        if (reference == null || hair == null || hair.Length == 0) return;

        var surfaces = new List<HairSurface>();
        foreach (var renderer in reference.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer.sharedMesh == null) continue;
            bool hairMaterial = false;
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || !material.name.StartsWith("hair_Pbr", StringComparison.Ordinal)) continue;
                hairMaterial = true;
                break;
            }
            if (!hairMaterial) continue;
            var surface = new HairSurface
            {
                renderer = renderer,
                bakedMesh = new Mesh { name = "마리 뒷머리 충돌 평가", hideFlags = HideFlags.HideAndDontSave }
            };
            surface.bakedMesh.MarkDynamic();
            surface.vertices.Capacity = renderer.sharedMesh.vertexCount;
            renderer.BakeMesh(surface.bakedMesh, false);
            surface.bakedMesh.GetVertices(surface.vertices);
            surfaces.Add(surface);
        }
        if (surfaces.Count == 0) return;
        hairSurfaces = new HairSurface[hair.Length];
        hairProbeIndices = new int[hair.Length][];
        for (int strandIndex = 0; strandIndex < hair.Length; strandIndex++)
        {
            hairProbeIndices[strandIndex] = Array.Empty<int>();
            var strand = hair[strandIndex];
            if (strand == null || strand.root == null) continue;
            Vector3 root = reference.InverseTransformPoint(strand.root.position);
            float side = root.x < 0f ? -1f : 1f;
            int selectedCount = 0;
            foreach (var surface in surfaces)
            {
                var candidates = new List<int>();
                var points = new List<Vector3>();
                for (int vertexIndex = 0; vertexIndex < surface.vertices.Count; vertexIndex++)
                {
                    Vector3 point = reference.InverseTransformPoint(surface.renderer.transform.TransformPoint(surface.vertices[vertexIndex]));
                    // 묶은 부분 바로 아래부터 평가하고 머리통과 앞머리는 제외합니다.
                    if (point.y >= Mathf.Min(0.64f, root.y - 0.02f) || point.x * side <= 0.055f || point.z >= -0.055f)
                        continue;
                    candidates.Add(vertexIndex);
                    points.Add(point);
                }
                // 분할 모델과 합본 모델에서 모두 해당 묶은 머리의 표면을 선택합니다.
                if (candidates.Count <= selectedCount) continue;
                selectedCount = candidates.Count;
                hairSurfaces[strandIndex] = surface;
                hairProbeIndices[strandIndex] = SelectHairProbes(candidates, points, root);
            }
        }
        foreach (var surface in surfaces)
            if (Array.IndexOf(hairSurfaces, surface) < 0) DisposeHairSurface(surface);
        CacheHairInfluences();
    }

    private void CacheHairInfluences()
    {
        hairRootInfluences = new float[hair.Length][];
        hairTipInfluences = new float[hair.Length][];
        hairRootProbePositions = new Vector3[hair.Length][];
        hairTipProbePositions = new Vector3[hair.Length][];
        const float testAngle = 0.5f;
        Quaternion testRotation = Quaternion.AngleAxis(testAngle, reference.right);
        for (int strandIndex = 0; strandIndex < hair.Length; strandIndex++)
        {
            var strand = hair[strandIndex];
            var surface = hairSurfaces[strandIndex];
            int[] probes = hairProbeIndices[strandIndex];
            hairRootInfluences[strandIndex] = new float[probes.Length];
            hairTipInfluences[strandIndex] = new float[probes.Length];
            hairRootProbePositions[strandIndex] = new Vector3[probes.Length];
            hairTipProbePositions[strandIndex] = new Vector3[probes.Length];
            if (strand?.root == null || strand.tip == null || surface == null || probes.Length == 0) continue;
            var baseline = new Vector3[probes.Length];
            for (int i = 0; i < probes.Length; i++)
            {
                baseline[i] = surface.renderer.transform.TransformPoint(surface.vertices[probes[i]]);
                hairRootProbePositions[strandIndex][i] = strand.root.InverseTransformPoint(baseline[i]);
                hairTipProbePositions[strandIndex][i] = strand.tip.InverseTransformPoint(baseline[i]);
            }
            Quaternion rootRotation = strand.root.rotation;
            Quaternion tipRotation = strand.tip.rotation;
            Vector3 rootPosition = strand.root.position;
            Vector3 tipPosition = strand.tip.position;
            // 작은 시험 회전으로 머리 본과 머리통의 실제 웨이트 비율을 함께 반영합니다.
            strand.root.rotation = testRotation * rootRotation;
            BakeHairSurface();
            MeasureHairInfluence(surface, probes, baseline, rootPosition, testRotation, hairRootInfluences[strandIndex]);
            strand.root.rotation = rootRotation;
            strand.tip.rotation = testRotation * tipRotation;
            BakeHairSurface();
            MeasureHairInfluence(surface, probes, baseline, tipPosition, testRotation, hairTipInfluences[strandIndex]);
            strand.tip.rotation = tipRotation;
            BakeHairSurface();
        }
    }

    private void MeasureHairInfluence(HairSurface surface, int[] probes, Vector3[] baseline, Vector3 pivot,
        Quaternion testRotation, float[] influences)
    {
        for (int i = 0; i < probes.Length; i++)
        {
            float rigidDistance = (testRotation * (baseline[i] - pivot) + pivot - baseline[i]).magnitude;
            Vector3 point = surface.renderer.transform.TransformPoint(surface.vertices[probes[i]]);
            influences[i] = rigidDistance < 0.000001f ? 0f : Mathf.Clamp01((point - baseline[i]).magnitude / rigidDistance);
        }
    }

    private static int[] SelectHairProbes(List<int> candidates, List<Vector3> points, Vector3 root)
    {
        if (candidates.Count == 0) return Array.Empty<int>();
        var selected = new int[Mathf.Min(HairProbeCount, candidates.Count)];
        var distances = new float[candidates.Count];
        int next = 0;
        float farthest = -1f;
        for (int i = 0; i < candidates.Count; i++)
        {
            distances[i] = float.PositiveInfinity;
            float distance = (points[i] - root).sqrMagnitude;
            if (distance <= farthest) continue;
            farthest = distance;
            next = i;
        }
        int count = 0;
        while (count < selected.Length)
        {
            selected[count++] = candidates[next];
            Vector3 point = points[next];
            farthest = -1f;
            for (int i = 0; i < candidates.Count; i++)
            {
                distances[i] = Mathf.Min(distances[i], (points[i] - point).sqrMagnitude);
                if (distances[i] <= farthest) continue;
                farthest = distances[i];
                next = i;
            }
            // UV 경계의 중복 정점보다 서로 떨어진 표면 위치를 선택합니다.
            if (farthest < 0.0000001f) break;
        }
        if (count != selected.Length) Array.Resize(ref selected, count);
        return selected;
    }

    private void BakeHairSurface()
    {
        for (int i = 0; i < hairSurfaces.Length; i++)
        {
            var surface = hairSurfaces[i];
            if (surface == null || surface.renderer == null || surface.bakedMesh == null || !IsUniqueHairSurface(i))
                continue;
            surface.renderer.BakeMesh(surface.bakedMesh, false);
            surface.bakedMesh.GetVertices(surface.vertices);
        }
    }

    private bool IsUniqueHairSurface(int index)
    {
        for (int i = 0; i < index; i++)
            if (hairSurfaces[i] == hairSurfaces[index]) return false;
        return true;
    }

    private void DisposeHairSurfaces()
    {
        for (int i = 0; i < hairSurfaces.Length; i++)
            if (hairSurfaces[i] != null && IsUniqueHairSurface(i)) DisposeHairSurface(hairSurfaces[i]);
        hairSurfaces = Array.Empty<HairSurface>();
    }

    private void DisposeHairSurface(HairSurface surface)
    {
        if (surface.bakedMesh == null) return;
        if (Application.isPlaying) Destroy(surface.bakedMesh);
        else DestroyImmediate(surface.bakedMesh);
    }

    private void OnEnable()
    {
        initialized = false;
        if (hair == null) hair = Array.Empty<HairStrand>();
        if (fingers == null) fingers = Array.Empty<FingerPose>();
        fistAmount = fistVelocity = 0f;
        hairMotion = hairMotionVelocity = hairPhase = 0f;
    }

    private void LateUpdate()
    {
        if (reference == null || hips == null || head == null) return;
        float elapsed = Time.deltaTime;
        if (elapsed <= 0f) return;
        float dt = Mathf.Min(elapsed, 0.1f);
        Vector3 position = reference.InverseTransformPoint(hips.position);
        Quaternion headRotation = Quaternion.Inverse(reference.rotation) * head.rotation;
        if (!initialized)
        {
            previousHips = position;
            previousVelocity = Vector3.zero;
            previousReferencePosition = reference.position;
            previousHead = headRotation;
            foreach (var panel in skirt) { panel.angle = 0f; panel.velocity = 0f; }
            foreach (var ear in ears)
            {
                ear.poseBend = ear.poseVelocity = ear.tipPoseBend = ear.tipPoseVelocity = Vector3.zero;
                ear.bend = ear.velocity = ear.tipBend = ear.tipVelocity = Vector3.zero;
            }
            foreach (var strand in hair)
                if (strand != null)
                {
                    strand.bend = strand.velocity = strand.tipBend = strand.tipVelocity = Vector3.zero;
                    strand.armAvoidance = strand.armAvoidanceVelocity = Vector3.zero;
                    strand.tipArmAvoidance = strand.tipArmAvoidanceVelocity = Vector3.zero;
                }
            initialized = true;
        }
        Vector3 velocity = (position - previousHips) / elapsed;
        Vector3 acceleration = Vector3.ClampMagnitude((velocity - previousVelocity) / elapsed, 12f);
        previousHips = position;
        previousVelocity = velocity;
        float forwardSpeed = Mathf.Clamp(
            Vector3.Dot((reference.position - previousReferencePosition) / elapsed, reference.forward), 0f, 6f);
        previousReferencePosition = reference.position;
        Quaternion delta = headRotation * Quaternion.Inverse(previousHead);
        delta.ToAngleAxis(out float degrees, out Vector3 axis);
        if (degrees > 180f) degrees -= 360f;
        Vector3 headVelocity = degrees < 0.0001f && degrees > -0.0001f ? Vector3.zero : axis * (degrees / elapsed);
        previousHead = headRotation;
        if (Controls(ProceduralParts.Skirt)) UpdateSkirt(acceleration, dt);
        if (Controls(ProceduralParts.Ears)) UpdateEars(Vector3.ClampMagnitude(headVelocity, 180f), dt);
        if (Controls(ProceduralParts.Hair)) UpdateHair(Vector3.ClampMagnitude(headVelocity, 180f), acceleration, forwardSpeed, dt);
        if (Controls(ProceduralParts.Hands)) UpdateHands(dt);
    }

    private void UpdateHands(float dt)
    {
        if (motionController != null && !motionController.UsesMotionPreset)
        {
            fistAmount = fistVelocity = 0f;
            return;
        }
        float target = motionController != null && motionController.CurrentMotion == MariAnimator.Motion.Run ? 1f : 0f;
        fistAmount = Mathf.SmoothDamp(fistAmount, target, ref fistVelocity, FistTransitionTime, 10f, dt);
        if (target == 0f && fistAmount < 0.001f && Mathf.Abs(fistVelocity) < 0.01f)
        {
            fistAmount = fistVelocity = 0f;
            return;
        }
        for (int i = 0; i < fingers.Length; i++)
        {
            var finger = fingers[i];
            if (finger?.bone == null) continue;
            finger.bone.localRotation = Quaternion.Slerp(finger.restRotation, finger.runRotation, fistAmount);
        }
    }

    private void UpdateSkirt(Vector3 acceleration, float dt)
    {
        if (skirt == null || skirt.Length == 0) return;
        Quaternion hipDelta = hips.rotation * Quaternion.Inverse(reference.rotation * hipsRestRotation);
        Quaternion follow = Quaternion.Slerp(Quaternion.identity, hipDelta, 0.18f);
        Vector3 down = -reference.up;
        int count = skirt.Length;
        float neighborGap = 8f * 8f / count;
        if (skirtTargets.Length != count)
        {
            skirtTargets = new float[count];
            skirtClearances = new float[count];
            skirtFloorRequired = new float[count];
            skirtLimits = new float[count];
            skirtAdjusted = new float[count];
            skirtAxes = new Vector3[count];
            skirtBases = new Quaternion[count];
            skirtValid = new bool[count];
        }
        var targets = skirtTargets;
        var clearances = skirtClearances;
        var floorRequired = skirtFloorRequired;
        var limits = skirtLimits;
        var axes = skirtAxes;
        var bases = skirtBases;
        var valid = skirtValid;
        Array.Clear(valid, 0, count);
        for (int panelIndex = 0; panelIndex < count; panelIndex++)
        {
            var panel = skirt[panelIndex];
            if (panel == null || panel.root == null || panel.hem == null) continue;
            valid[panelIndex] = true;
            float maxAngle = Mathf.Abs(panel.outward.x) > 0.35f && Mathf.Abs(panel.outward.z) > 0.35f ? 12f : 15f;
            Vector3 outward = reference.TransformDirection(panel.outward);
            Vector3 axis = Vector3.Cross(down, outward).normalized;
            float restAngle = Vector3.Angle(Vector3.down, panel.restDirection);
            float clearance = 0f;
            for (int i = 0; i < Mathf.Min(thighs.Length, knees.Length); i++)
            {
                Vector3 thigh = thighs[i].position - panel.root.position;
                Vector3 knee = knees[i].position - panel.root.position;
                for (int sample = 1; sample <= 3; sample++)
                {
                    Vector3 legPoint = Vector3.Lerp(thigh, knee, sample / 3f);
                    float height = Vector3.Dot(legPoint, down);
                    if (height <= 0.035f) continue;
                    float distance = Vector3.Dot(legPoint, outward);
                    float legAngle = Mathf.Atan2(distance + 0.055f, height) * Mathf.Rad2Deg;
                    clearance = Mathf.Max(clearance, legAngle - restAngle + 6f);
                }
            }
            float inertia = -Vector3.Dot(acceleration, panel.outward) * SecondaryMotion * 0.45f;
            float target = Mathf.Clamp(clearance + inertia, -2f, maxAngle);
            Quaternion basis = follow * reference.rotation * panel.restRotation;
            // 허리를 깊게 낮춰도 긴 뒷자락이 발밑으로 접히지 않도록 끝점 높이를 제한합니다.
            bool needsFloorClearance = false;
            for (int step = 0; step < 18; step++)
            {
                Quaternion rotation = Quaternion.AngleAxis(target, axis) * basis;
                float minimum = float.PositiveInfinity;
                if (panel.hemProbes != null)
                    foreach (Vector3 point in panel.hemProbes)
                        minimum = Mathf.Min(minimum, reference.InverseTransformPoint(panel.root.position + rotation * point).y);
                if (minimum >= 0.02f) break;
                needsFloorClearance = true;
                if (target >= maxAngle) break;
                target = Mathf.Min(target + 4f, maxAngle);
            }
            targets[panelIndex] = target;
            clearances[panelIndex] = clearance;
            floorRequired[panelIndex] = needsFloorClearance ? target : -2f;
            limits[panelIndex] = maxAngle;
            axes[panelIndex] = axis;
            bases[panelIndex] = basis;
        }

        // 한 자락이 들릴 때 이웃 자락도 조금 따라가도록 각도 차를 제한합니다.
        var adjusted = skirtAdjusted;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < count; i++)
            {
                if (!valid[i]) continue;
                int left = (i + count - 1) % count;
                int right = (i + 1) % count;
                float neighbor = -2f;
                if (valid[left]) neighbor = Mathf.Max(neighbor, targets[left] - neighborGap);
                if (valid[right]) neighbor = Mathf.Max(neighbor, targets[right] - neighborGap);
                adjusted[i] = Mathf.Clamp(Mathf.Max(targets[i], neighbor), -2f, limits[i]);
            }
            Array.Copy(adjusted, targets, count);
        }

        for (int i = 0; i < count; i++)
        {
            if (!valid[i]) continue;
            var panel = skirt[i];
            panel.angle = Mathf.SmoothDamp(panel.angle, targets[i], ref panel.velocity, 0.10f, 300f, dt);
            int left = (i + count - 1) % count;
            int right = (i + 1) % count;
            float neighbor = -2f;
            if (valid[left]) neighbor = Mathf.Max(neighbor, targets[left] - neighborGap);
            if (valid[right]) neighbor = Mathf.Max(neighbor, targets[right] - neighborGap);
            float angle = Mathf.Clamp(Mathf.Max(Mathf.Max(Mathf.Max(panel.angle, clearances[i]), floorRequired[i]), neighbor), -2f, limits[i]);
            panel.root.rotation = Quaternion.AngleAxis(angle, axes[i]) * bases[i] * BoneOffset(panel.root);
            float hemLag = Mathf.Clamp((panel.angle - targets[i]) * 0.28f, -5f, 5f) * SecondaryMotion;
            Transform hemParent = panel.root;
            if (panel.mid != null)
            {
                panel.mid.rotation = Quaternion.AngleAxis(hemLag * 0.4f, axes[i]) * panel.root.rotation
                    * panel.midRotation * BoneOffset(panel.mid);
                hemParent = panel.mid;
                hemLag *= 0.6f;
            }
            panel.hem.rotation = Quaternion.AngleAxis(hemLag, axes[i]) * hemParent.rotation
                * panel.hemRotation * BoneOffset(panel.hem);
        }
    }

    private void UpdateEars(Vector3 angularVelocity, float dt)
    {
        for (int i = 0; i < ears.Length; i++)
        {
            var ear = ears[i];
            if (ear.root == null || ear.tip == null) continue;
            bool left = i == 0;
            float lift = left ? leftEarLift : rightEarLift;
            float forward = 0f;
            float tipForward = 0f;
            float spread = 0f;
            switch (earPose)
            {
                case EarPose.Alert: lift += 1f; break;
                case EarPose.Drooped:
                    forward = left ? 9f : 7f;
                    tipForward = left ? 18f : 14f;
                    spread = 5f;
                    break;
                case EarPose.Forward:
                    forward = left ? 12f : 10f;
                    tipForward = left ? 25f : 20f;
                    break;
                case EarPose.Back:
                    forward = left ? -12f : -10f;
                    tipForward = left ? -10f : -7f;
                    break;
                case EarPose.Curious:
                    if (left) lift += 1f;
                    else { forward = 9f; tipForward = 17f; }
                    break;
            }
            lift = Mathf.Clamp(lift, -1f, 1f);
            // 영상의 바깥으로 벌어진 실루엣을 유지하며 조금만 세웁니다.
            Vector3 direction = Vector3.RotateTowards(ear.direction, Vector3.up, (left ? 6f : 3f) * Mathf.Max(0f, lift) * Mathf.Deg2Rad, 0f);
            Quaternion expression = Quaternion.FromToRotation(ear.direction, direction);
            expression.ToAngleAxis(out float angle, out Vector3 axis);
            Vector3 targetBend = angle < 0.001f ? Vector3.zero : axis * angle;
            forward += Mathf.Max(0f, -lift) * (left ? 12f : 10f);
            tipForward += Mathf.Max(0f, -lift) * (left ? 25f : 20f);
            targetBend += new Vector3(Mathf.Clamp(forward, -28f, 32f), 0f, -ear.outward.x * spread);
            Vector3 tipTarget = Vector3.right * Mathf.Clamp(tipForward, -16f, left ? 25f : 20f);
            float swayPhase = Time.time * 2f + (left ? 0f : 0.55f);
            targetBend.x += Mathf.Sin(swayPhase) * 6f * SecondaryMotion;
            tipTarget.x += Mathf.Sin(swayPhase - 0.7f) * 10f * SecondaryMotion;
            targetBend.x = Mathf.Clamp(targetBend.x, left ? -12f : -10f, left ? 12f : 10f);
            tipTarget.x = Mathf.Clamp(tipTarget.x, left ? -25f : -20f, left ? 25f : 20f);
            // 자세의 이동 속도와 작은 반동을 분리해 큰 각도 변경도 부드럽게 연결합니다.
            float transition = Mathf.Clamp(EarTransitionTime, 0.08f, 0.5f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt * 120f));
            float step = dt / steps;
            for (int j = 0; j < steps; j++)
            {
                ear.poseBend = Vector3.SmoothDamp(ear.poseBend, targetBend, ref ear.poseVelocity, transition, 180f, step);
                ear.tipPoseBend = Vector3.SmoothDamp(ear.tipPoseBend, tipTarget, ref ear.tipPoseVelocity, transition + 0.06f, 140f, step);
                Vector3 reaction = -ear.poseVelocity * (0.018f * EarElasticity);
                reaction -= angularVelocity * (0.022f * SecondaryMotion);
                Vector3 tipReaction = -ear.tipPoseVelocity * (0.025f * EarElasticity);
                tipReaction -= angularVelocity * (0.030f * SecondaryMotion);
                float damping = Mathf.Lerp(1f, 0.36f, EarElasticity);
                Spring(ref ear.bend, ref ear.velocity, Vector3.ClampMagnitude(reaction, 4f), 4.4f, damping, step);
                Spring(ref ear.tipBend, ref ear.tipVelocity, Vector3.ClampMagnitude(tipReaction, 6f), 3.6f, Mathf.Max(0.4f, damping - 0.1f), step);
            }
            ear.bend = Vector3.ClampMagnitude(ear.bend, 3f);
            ear.tipBend = Vector3.ClampMagnitude(ear.tipBend, 4f);
            Quaternion frame = head.rotation * Quaternion.Inverse(headRestRotation);
            Vector3 rootPose = ear.poseBend + ear.bend;
            rootPose.x = Mathf.Clamp(rootPose.x, left ? -12f : -10f, left ? 12f : 10f);
            Quaternion bend = Rotation(rootPose);
            ear.root.rotation = frame * bend * Quaternion.Inverse(frame) * head.rotation * ear.restRotation * BoneOffset(ear.root);
            Vector3 tipPose = ear.tipPoseBend + ear.tipBend;
            tipPose.x = Mathf.Clamp(tipPose.x, left ? -25f : -20f, left ? 25f : 20f);
            float midShare = ear.mid != null ? 0.45f : 0f;
            float endShare = ear.end != null ? 0.20f : 0f;
            Transform tipParent = ear.root;
            if (ear.mid != null)
            {
                ApplyEarSegment(ear.mid, ear.root, ear.midRotation, tipPose * midShare, frame);
                tipParent = ear.mid;
            }
            ApplyEarSegment(ear.tip, tipParent, ear.tipRotation, tipPose * (1f - midShare - endShare), frame);
            if (ear.end != null) ApplyEarSegment(ear.end, ear.tip, ear.endRotation, tipPose * endShare, frame);
        }
    }

    private void ApplyEarSegment(Transform bone, Transform parent, Quaternion rest, Vector3 bend, Quaternion frame)
    {
        bone.rotation = frame * Rotation(bend) * Quaternion.Inverse(frame) * parent.rotation * rest * BoneOffset(bone);
    }

    private void UpdateHair(Vector3 angularVelocity, Vector3 acceleration, float forwardSpeed, float dt)
    {
        if (hair == null || hair.Length == 0) return;
        float motionTarget = 0f;
        if (motionController != null && motionController.UsesMotionPreset)
        {
            switch (motionController.CurrentMotion)
            {
                case MariAnimator.Motion.Walk: motionTarget = 0.42f; break;
                case MariAnimator.Motion.Run: motionTarget = 1f; break;
                case MariAnimator.Motion.Crouch: motionTarget = 0.28f; break;
            }
        }
        motionTarget = Mathf.Max(motionTarget, Mathf.Clamp01(forwardSpeed / 3.7f));
        hairMotion = Mathf.SmoothDamp(hairMotion, motionTarget, ref hairMotionVelocity, 0.16f, 4f, dt);
        hairPhase = Mathf.Repeat(hairPhase + dt * Mathf.Lerp(4.5f, 15f, hairMotion), Mathf.PI * 4f);

        Quaternion frame = head.rotation * Quaternion.Inverse(headRestRotation);
        for (int i = 0; i < hair.Length; i++)
        {
            var strand = hair[i];
            if (strand == null || strand.root == null || strand.tip == null) continue;

            float outward = i == 0 ? -1f : 1f;
            float phase = hairPhase + i * 0.55f;
            float flow = hairMotion * hairMotionStrength;
            Vector3 target = new Vector3(
                flow * (44f + 3.2f * Mathf.Sin(phase)) - angularVelocity.x * 0.022f - acceleration.y * 0.16f,
                -angularVelocity.y * 0.012f,
                outward * (flow * (3.5f + 1.3f * Mathf.Sin(phase * 0.5f))
                    + hairMotionStrength * Mathf.Sin(Time.time * 1.3f + i) * 0.4f) - angularVelocity.z * 0.018f);
            Vector3 tipTarget = new Vector3(
                flow * (23f + 5.5f * Mathf.Sin(phase - 0.85f)) - angularVelocity.x * 0.028f - acceleration.y * 0.22f,
                -angularVelocity.y * 0.017f,
                outward * flow * (5.5f + 2.4f * Mathf.Sin(phase - 0.75f)) - angularVelocity.z * 0.025f);

            Spring(ref strand.bend, ref strand.velocity, Vector3.ClampMagnitude(target, 55f), 3.4f, 0.72f, dt);
            Spring(ref strand.tipBend, ref strand.tipVelocity, Vector3.ClampMagnitude(tipTarget, 34f), 2.7f, 0.62f, dt);
            strand.bend = Vector3.ClampMagnitude(strand.bend, 55f);
            strand.tipBend = Vector3.ClampMagnitude(strand.tipBend, 34f);
            // 접촉이 끝난 뒤의 복귀를 부드럽게 하고 관통 보정은 아래에서 즉시 적용합니다.
            Spring(ref strand.armAvoidance, ref strand.armAvoidanceVelocity, Vector3.zero, 4f, 0.9f, dt);
            Spring(ref strand.tipArmAvoidance, ref strand.tipArmAvoidanceVelocity, Vector3.zero, 3.5f, 0.9f, dt);
            ApplyHairPose(strand, frame, strand.bend + strand.armAvoidance,
                strand.tipBend + strand.tipArmAvoidance);
        }
        ResolveHairArmContacts(frame);
    }

    private void ApplyHairPose(HairStrand strand, Quaternion frame, Vector3 rootBend, Vector3 tipBend)
    {
        strand.root.rotation = frame * Rotation(rootBend) * Quaternion.Inverse(frame) * head.rotation * strand.restRotation;
        strand.tip.rotation = frame * Rotation(tipBend) * Quaternion.Inverse(frame) * strand.root.rotation * strand.tipRotation;
    }

    private void ResolveHairArmContacts(Quaternion frame)
    {
        HairArmResidualPenetration = 0f;
        if (hairSurfaces.Length != hair.Length || hairProbeIndices.Length != hair.Length) return;
        Quaternion inverseFrame = Quaternion.Inverse(frame);
        float startingRun = motionController != null && motionController.UsesMotionPreset && motionController.CurrentMotion == MariAnimator.Motion.Run
            ? Mathf.Clamp01(1f - hairMotion) : 0f;
        // 달리기 전환 중에는 갑자기 뒤로 오는 팔을 피할 여유를 주고 정상 달리기에서는 원래 제한으로 돌아갑니다.
        float rootLimit = HairRootAvoidanceLimit + startingRun * 8f;
        float tipLimit = HairTipAvoidanceLimit + startingRun * 6f;
        for (int pass = 0; pass < HairCollisionPasses; pass++)
        {
            // 같은 프레임에서 보정 뒤의 실제 스킨 정점까지 다시 평가합니다.
            BakeHairSurface();
            bool changed = false;
            for (int i = 0; i < hair.Length; i++)
            {
                var strand = hair[i];
                if (strand?.root == null || strand.tip == null) continue;
                int contacts = CacheHairArmContacts(i, frame);
                if (contacts == 0) continue;
                Vector3 rootAngular = Vector3.zero;
                Vector3 tipAngular = Vector3.zero;
                // 같은 머리 덩어리의 여러 접촉을 함께 풀어 한쪽을 피하다 다른 쪽에 끼지 않게 합니다.
                for (int sweep = 0; sweep < 4; sweep++)
                    for (int contact = 0; contact < contacts; contact++)
                    {
                        Vector3 rootGradient = hairContactRootGradients[contact];
                        Vector3 tipGradient = hairContactTipGradients[contact];
                        float remaining = hairContactDepths[contact] - Vector3.Dot(rootGradient, rootAngular)
                            - Vector3.Dot(tipGradient, tipAngular);
                        if (remaining <= 0.0001f) continue;
                        float divisor = rootGradient.sqrMagnitude + tipGradient.sqrMagnitude;
                        if (divisor < 0.000001f) continue;
                        float correction = remaining / divisor;
                        rootAngular = Vector3.ClampMagnitude(rootAngular + rootGradient * correction, 7f * Mathf.Deg2Rad);
                        tipAngular = Vector3.ClampMagnitude(tipAngular + tipGradient * correction, 8f * Mathf.Deg2Rad);
                    }
                Vector3 rootChange = HairRotationIncrement(strand.bend + strand.armAvoidance,
                    inverseFrame * rootAngular * Mathf.Rad2Deg);
                Vector3 tipChange = HairRotationIncrement(strand.tipBend + strand.tipArmAvoidance,
                    inverseFrame * tipAngular * Mathf.Rad2Deg);
                Vector3 rootAvoidance = Vector3.ClampMagnitude(strand.armAvoidance + rootChange, rootLimit);
                Vector3 tipAvoidance = Vector3.ClampMagnitude(strand.tipArmAvoidance + tipChange, tipLimit);
                if ((rootAvoidance - strand.armAvoidance).sqrMagnitude + (tipAvoidance - strand.tipArmAvoidance).sqrMagnitude < 0.0000001f)
                    continue;
                strand.armAvoidance = rootAvoidance;
                strand.tipArmAvoidance = tipAvoidance;
                ApplyHairPose(strand, frame, strand.bend + strand.armAvoidance, strand.tipBend + strand.tipArmAvoidance);
                changed = true;
            }
            if (!changed)
            {
                MeasureHairResidual();
                return;
            }
            HairArmResidualPenetration = 0f;
        }
        BakeHairSurface();
        MeasureHairResidual();
    }

    private int CacheHairArmContacts(int strandIndex, Quaternion frame)
    {
        var surface = hairSurfaces[strandIndex];
        if (surface == null) return 0;
        var strand = hair[strandIndex];
        Quaternion tipSwing = frame * Rotation(strand.tipBend + strand.tipArmAvoidance) * Quaternion.Inverse(frame);
        Quaternion inverseTipSwing = Quaternion.Inverse(tipSwing);
        Vector3 scale = reference.lossyScale;
        float modelScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        float upperArmRadius = (hairArmClearance + 0.003f) * modelScale;
        float forearmRadius = (hairForearmRadius + 0.003f) * modelScale;
        float handRadius = (hairHandRadius + 0.003f) * modelScale;
        int contacts = 0;
        for (int probeIndex = 0; probeIndex < hairProbeIndices[strandIndex].Length; probeIndex++)
        {
            float tipInfluence = hairTipInfluences[strandIndex][probeIndex];
            float rootTotal = hairRootInfluences[strandIndex][probeIndex];
            if (rootTotal < 0.025f && tipInfluence < 0.025f) continue;
            int vertexIndex = hairProbeIndices[strandIndex][probeIndex];
            if (vertexIndex >= surface.vertices.Count) continue;
            Vector3 point = surface.renderer.transform.TransformPoint(surface.vertices[vertexIndex]);
            Vector3 rootPoint = strand.root.TransformPoint(hairRootProbePositions[strandIndex][probeIndex]);
            Vector3 tipPoint = strand.tip.TransformPoint(hairTipProbePositions[strandIndex][probeIndex]);
            // 혼합 전 좌표를 이용해 머리통에 고정된 웨이트를 회전 레버에서 제외합니다.
            Vector3 rootLever = (rootPoint - strand.root.position) * Mathf.Max(0f, rootTotal - tipInfluence)
                + (strand.tip.position - strand.root.position) * tipInfluence;
            Vector3 tipLever = (tipPoint - strand.tip.position) * tipInfluence;
            for (int armIndex = 0; armIndex < upperArms.Length; armIndex++)
            {
                if (upperArms[armIndex] == null || forearms[armIndex] == null || hands[armIndex] == null) continue;
                AddHairArmContact(point, upperArmRadius, upperArms[armIndex].position, forearms[armIndex].position,
                    rootLever, tipLever, inverseTipSwing, ref contacts);
                AddHairArmContact(point, forearmRadius, forearms[armIndex].position, hands[armIndex].position,
                    rootLever, tipLever, inverseTipSwing, ref contacts);
                if (handEnds[armIndex] != null)
                    AddHairArmContact(point, handRadius, hands[armIndex].position, handEnds[armIndex].position,
                        rootLever, tipLever, inverseTipSwing, ref contacts);
            }
        }
        return contacts;
    }

    private void AddHairArmContact(Vector3 point, float radius, Vector3 start, Vector3 end,
        Vector3 rootLever, Vector3 tipLever, Quaternion inverseTipSwing, ref int contacts)
    {
        if (contacts >= hairContactDepths.Length) return;
        float penetration = 0f;
        Vector3 contactPoint = Vector3.zero, push = Vector3.zero;
        CheckHairArmContact(point, radius, start, end, ref penetration, ref contactPoint, ref push);
        if (penetration <= 0.0005f) return;
        Vector3 normal = push.normalized;
        Vector3 tipGradient = Vector3.Cross(tipLever, normal);
        hairContactRootGradients[contacts] = Vector3.Cross(rootLever, normal) + inverseTipSwing * tipGradient;
        hairContactTipGradients[contacts] = tipGradient;
        hairContactDepths[contacts++] = push.magnitude;
    }

    private static Vector3 HairRotationIncrement(Vector3 currentDegrees, Vector3 angularDegrees)
    {
        Vector3 current = currentDegrees * Mathf.Deg2Rad;
        Vector3 angular = angularDegrees * Mathf.Deg2Rad;
        float angle = current.magnitude;
        float coefficient = angle < 0.01f ? 1f / 12f
            : (1f - angle * 0.5f / Mathf.Tan(angle * 0.5f)) / (angle * angle);
        Vector3 cross = Vector3.Cross(current, angular);
        // 이미 뒤로 젖힌 자세에서도 의도한 월드 회전축으로 밀어냅니다.
        return (angular - cross * 0.5f + Vector3.Cross(current, cross) * coefficient) * Mathf.Rad2Deg;
    }

    private void MeasureHairResidual()
    {
        HairArmResidualPenetration = 0f;
        for (int i = 0; i < hair.Length; i++)
            HairArmResidualPenetration = Mathf.Max(HairArmResidualPenetration, FindHairArmContact(i, out _, out _, out _));
    }

    private float FindHairArmContact(int strandIndex, out Vector3 contactPoint, out Vector3 push,
        out int contactProbeIndex, bool movableOnly = false)
    {
        contactPoint = push = Vector3.zero;
        contactProbeIndex = -1;
        var surface = hairSurfaces[strandIndex];
        if (surface == null) return 0f;
        float largestPenetration = 0f;
        Vector3 scale = reference.lossyScale;
        float modelScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        float upperArmRadius = (hairArmClearance + 0.003f) * modelScale;
        float forearmRadius = (hairForearmRadius + 0.003f) * modelScale;
        float handRadius = (hairHandRadius + 0.003f) * modelScale;
        for (int probeIndex = 0; probeIndex < hairProbeIndices[strandIndex].Length; probeIndex++)
        {
            if (movableOnly && hairRootInfluences[strandIndex][probeIndex] < 0.025f
                && hairTipInfluences[strandIndex][probeIndex] < 0.025f) continue;
            int vertexIndex = hairProbeIndices[strandIndex][probeIndex];
            if (vertexIndex >= surface.vertices.Count) continue;
            Vector3 point = surface.renderer.transform.TransformPoint(surface.vertices[vertexIndex]);
            float previousPenetration = largestPenetration;
            for (int armIndex = 0; armIndex < upperArms.Length; armIndex++)
            {
                if (upperArms[armIndex] == null || forearms[armIndex] == null || hands[armIndex] == null) continue;
                CheckHairArmContact(point, upperArmRadius, upperArms[armIndex].position, forearms[armIndex].position,
                    ref largestPenetration, ref contactPoint, ref push);
                CheckHairArmContact(point, forearmRadius, forearms[armIndex].position, hands[armIndex].position,
                    ref largestPenetration, ref contactPoint, ref push);
                if (handEnds[armIndex] != null)
                    CheckHairArmContact(point, handRadius, hands[armIndex].position, handEnds[armIndex].position,
                        ref largestPenetration, ref contactPoint, ref push);
            }
            if (largestPenetration > previousPenetration) contactProbeIndex = probeIndex;
        }
        return largestPenetration;
    }

    private void CheckHairArmContact(Vector3 point, float radius, Vector3 start, Vector3 end,
        ref float largestPenetration, ref Vector3 contactPoint, ref Vector3 push)
    {
        Vector3 segment = end - start;
        float fraction = segment.sqrMagnitude < 0.000001f ? 0f
            : Mathf.Clamp01(Vector3.Dot(point - start, segment) / segment.sqrMagnitude);
        Vector3 closest = start + segment * fraction;
        Vector3 away = point - closest;
        float distance = away.magnitude;
        float penetration = radius - distance;
        if (penetration <= largestPenetration) return;
        largestPenetration = penetration;
        contactPoint = point;
        if (distance > 0.00001f) away /= distance;
        else
        {
            away = Vector3.ProjectOnPlane(point - head.position, segment);
            if (away.sqrMagnitude < 0.000001f) away = Vector3.Cross(segment, reference.up);
            away = away.sqrMagnitude < 0.000001f ? -reference.forward : away.normalized;
        }
        push = away * (penetration + 0.001f);
    }

    private void OnDestroy()
    {
        DisposeHairSurfaces();
    }

    public void PulseEars(float strength = 1f)
    {
        if (!isActiveAndEnabled || !Application.isPlaying || !initialized) return;
        strength = Mathf.Clamp01(strength);
        for (int i = 0; i < ears.Length; i++)
        {
            float scale = (i == 0 ? 1f : 0.6f) * strength * EarElasticity;
            ears[i].velocity = Vector3.ClampMagnitude(ears[i].velocity + Vector3.right * (35f * scale), 90f);
            ears[i].tipVelocity = Vector3.ClampMagnitude(ears[i].tipVelocity + Vector3.right * (50f * scale), 120f);
        }
    }


    private static Quaternion Rotation(Vector3 degrees) => degrees.sqrMagnitude < 0.00001f
        ? Quaternion.identity : Quaternion.AngleAxis(degrees.magnitude, degrees.normalized);

    private static void Spring(ref Vector3 value, ref Vector3 velocity, Vector3 target, float frequency, float damping, float dt)
    {
        float omega = 2f * Mathf.PI * frequency;
        Vector3 offset = value - target;
        float decay = Mathf.Exp(-damping * omega * dt);
        if (damping >= 0.999f)
        {
            Vector3 c = velocity + omega * offset;
            value = target + (offset + c * dt) * decay;
            velocity = (velocity - omega * c * dt) * decay;
            return;
        }
        // 해석해를 사용해 낮은 프레임률에서도 반동이 커지거나 발산하지 않게 합니다.
        float damped = omega * Mathf.Sqrt(1f - damping * damping);
        float sin = Mathf.Sin(damped * dt);
        float cos = Mathf.Cos(damped * dt);
        value = target + (offset * cos + (velocity + damping * omega * offset) * (sin / damped)) * decay;
        velocity = (velocity * cos - (damping * omega * velocity + omega * omega * offset) * (sin / damped)) * decay;
    }

    private void OnDisable()
    {
        if (reference == null || hips == null || head == null) return;
        if (Controls(ProceduralParts.Skirt))
        {
            foreach (var panel in skirt)
            {
                if (panel.root != null) panel.root.rotation = hips.rotation * Quaternion.Inverse(hipsRestRotation) * panel.restRotation;
                if (panel.mid != null) panel.mid.localRotation = panel.midRotation;
                if (panel.hem != null) panel.hem.localRotation = panel.hemRotation;
            }
        }
        if (Controls(ProceduralParts.Ears))
        {
            foreach (var ear in ears)
            {
                if (ear.root != null) ear.root.rotation = head.rotation * ear.restRotation;
                if (ear.mid != null) ear.mid.localRotation = ear.midRotation;
                if (ear.tip != null) ear.tip.localRotation = ear.tipRotation;
                if (ear.end != null) ear.end.localRotation = ear.endRotation;
            }
        }
        if (Controls(ProceduralParts.Hair))
        {
            foreach (var strand in hair)
            {
                if (strand == null) continue;
                if (strand.root != null) strand.root.rotation = head.rotation * strand.restRotation;
                if (strand.tip != null) strand.tip.localRotation = strand.tipRotation;
            }
        }
        if (Controls(ProceduralParts.Hands) && (motionController == null || motionController.UsesMotionPreset))
        {
            foreach (var finger in fingers)
                if (finger?.bone != null) finger.bone.localRotation = finger.restRotation;
        }
        fistAmount = fistVelocity = 0f;
        initialized = false;
    }
}
