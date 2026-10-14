using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public sealed class MariFace : MonoBehaviour
{
    public enum Expression
    {
        Neutral,
        BothClosed,
        HalfClosed,
        LeftWink,
        RightWink,
        EyeSmile,
        Custom
    }

    public enum MouthPose
    {
        Neutral,
        SmileOpen,
        TalkSmall,
        TalkWide,
        TalkRound
    }

    private sealed class Binding
    {
        public SkinnedMeshRenderer renderer;
        public Mesh mesh;
        public int index;
        public string name;
        public float baseline;

        public bool IsValid => renderer != null && renderer.sharedMesh == mesh && mesh != null
            && index >= 0 && index < mesh.blendShapeCount && mesh.GetBlendShapeName(index) == name;
    }

    private sealed class Channel
    {
        public string name;
        public readonly List<Binding> bindings = new List<Binding>();
        public float target;
        public float weight;
        public float velocity;
    }

    [SerializeField, InspectorName("캐릭터")] private Transform characterRoot;
    [SerializeField, Min(0f), InspectorName("전환 시간")] private float transitionTime = 0.14f;
    [SerializeField, Min(0.3f), InspectorName("깜빡임 간격")] private float blinkInterval = 3.5f;

    private const float BlinkCloseTime = 0.09f;
    private const float BlinkHoldTime = 0.035f;
    private const float BlinkOpenTime = 0.13f;
    private static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");
    private static readonly int MainTexSt = Shader.PropertyToID("_MainTex_ST");
    private static readonly Vector2[] MouthOffsets =
    {
        Vector2.zero,
        new Vector2(7f / 8f, -1f / 8f),
        new Vector2(0f, 4f / 8f),
        new Vector2(1f / 8f, 4f / 8f),
        new Vector2(0f, 3f / 8f)
    };
    private readonly Dictionary<string, Channel> channels = new Dictionary<string, Channel>(StringComparer.Ordinal);
    private readonly List<string> channelNames = new List<string>();
    private MaterialPropertyBlock mouthOriginalBlock;
    private MaterialPropertyBlock mouthWorkingBlock;
    private Transform boundCharacterRoot;
    private SkinnedMeshRenderer mouthRenderer;
    private Material mouthMaterial;
    private int mouthMaterialIndex = -1;
    private bool mouthHadPropertyBlock;
    private MouthPose currentMouthPose;
    private Expression currentExpression = Expression.Neutral;
    private bool automaticBlink;
    private float nextBlinkAt;
    private float blinkStartedAt = -1f;

    public IReadOnlyList<string> ChannelNames => channelNames;
    public bool HasBlinkLeft => channels.ContainsKey("Blink_L");
    public bool HasBlinkRight => channels.ContainsKey("Blink_R");
    public bool HasEyeSmile => channels.ContainsKey("EyeSmile")
        || (channels.ContainsKey("EyeSmile_L") && channels.ContainsKey("EyeSmile_R"));
    public bool AutomaticBlink => automaticBlink;
    public Expression CurrentExpression => currentExpression;
    public MouthPose CurrentMouthPose => currentMouthPose;
    public bool HasMouthAtlas => mouthRenderer != null && mouthMaterial != null;

    public void Configure(Transform character)
    {
        characterRoot = character;
        RefreshBindings();
    }

    public void RefreshBindings()
    {
        if (characterRoot == null) characterRoot = transform;
        EnsureMouthBlocks();
        bool sameCharacter = boundCharacterRoot == characterRoot;
        var previous = sameCharacter ? new Dictionary<string, Channel>(channels, StringComparer.Ordinal) : null;
        // 자체 적용 값을 다음 바인딩의 원래 값으로 잘못 저장하지 않도록 먼저 복원합니다.
        RestoreBaselineWeights();
        RestoreMouthMaterial();
        channels.Clear();
        channelNames.Clear();
        boundCharacterRoot = characterRoot;
        if (!sameCharacter)
        {
            currentExpression = Expression.Neutral;
            currentMouthPose = MouthPose.Neutral;
            automaticBlink = false;
            blinkStartedAt = -1f;
        }
        if (characterRoot == null) return;

        foreach (var renderer in characterRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Mesh mesh = renderer.sharedMesh;
            if (mesh == null) continue;
            for (int index = 0; index < mesh.blendShapeCount; index++)
            {
                string name = mesh.GetBlendShapeName(index);
                if (string.IsNullOrEmpty(name) || name == "Basis") continue;
                float baseline = renderer.GetBlendShapeWeight(index);
                if (!channels.TryGetValue(name, out Channel channel))
                {
                    Channel oldChannel = null;
                    if (previous != null) previous.TryGetValue(name, out oldChannel);
                    channel = new Channel
                    {
                        name = name,
                        target = oldChannel != null ? oldChannel.target : 0f,
                        weight = oldChannel != null ? oldChannel.weight : ClampWeight(baseline),
                        velocity = oldChannel != null ? oldChannel.velocity : 0f
                    };
                    channels.Add(name, channel);
                }
                channel.bindings.Add(new Binding
                {
                    renderer = renderer,
                    mesh = mesh,
                    index = index,
                    name = name,
                    baseline = baseline
                });
            }
        }
        channelNames.AddRange(channels.Keys);
        channelNames.Sort(StringComparer.Ordinal);
        BindMouthRenderer();
        if (automaticBlink && (!HasBlinkLeft || !HasBlinkRight)) SetAutomaticBlink(false);
        if (isActiveAndEnabled)
        {
            ApplyWeights();
            ApplyMouthPose();
        }
    }

    public void SetMouthPose(MouthPose pose)
    {
        if (!Enum.IsDefined(typeof(MouthPose), pose)) return;
        currentMouthPose = pose;
        if (isActiveAndEnabled) ApplyMouthPose();
    }

    public float GetChannelTarget(string name)
    {
        return name != null && channels.TryGetValue(name, out Channel channel) ? channel.target : 0f;
    }

    public float GetChannelWeight(string name)
    {
        return name != null && channels.TryGetValue(name, out Channel channel) ? channel.weight : 0f;
    }

    public float GetEffectiveChannelWeight(string name)
    {
        if (name == null || !channels.TryGetValue(name, out Channel channel)) return 0f;
        bool left = name == "Blink_L" || name == "EyeSmile_L";
        bool right = name == "Blink_R" || name == "EyeSmile_R";
        if (!left && !right && name != "EyeSmile") return channel.weight;

        float common = GetChannelWeight("EyeSmile");
        float leftSum = GetChannelWeight("Blink_L") + GetChannelWeight("EyeSmile_L");
        float rightSum = GetChannelWeight("Blink_R") + GetChannelWeight("EyeSmile_R");
        float sum = common > 0f || name == "EyeSmile"
            ? common + Mathf.Max(leftSum, rightSum) : left ? leftSum : rightSum;
        // 같은 눈에 감기와 웃기를 겹쳐도 원본을 넘는 이중 변형이 생기지 않게 합니다.
        return channel.weight * (100f / Mathf.Max(100f, sum));
    }

    public int GetChannelRendererCount(string name)
    {
        if (name == null || !channels.TryGetValue(name, out Channel channel)) return 0;
        int count = 0;
        foreach (Binding binding in channel.bindings)
            if (binding.IsValid) count++;
        return count;
    }

    public void SelectExpression(Expression expression, float strength = 100f, bool immediate = false)
    {
        SetAutomaticBlink(false);
        if (expression == Expression.EyeSmile && !HasEyeSmile) return;
        currentExpression = expression;
        if (expression == Expression.Custom) return;
        foreach (Channel channel in channels.Values) channel.target = 0f;
        strength = ClampWeight(strength);
        switch (expression)
        {
            case Expression.BothClosed:
                SetTarget("Blink_L", strength);
                SetTarget("Blink_R", strength);
                break;
            case Expression.HalfClosed:
                SetTarget("Blink_L", strength * 0.5f);
                SetTarget("Blink_R", strength * 0.5f);
                break;
            case Expression.LeftWink:
                SetTarget("Blink_L", strength);
                break;
            case Expression.RightWink:
                SetTarget("Blink_R", strength);
                break;
            case Expression.EyeSmile:
                // 공통 키와 좌우 키를 동시에 더해 같은 표정이 이중으로 변형되지 않게 합니다.
                if (channels.ContainsKey("EyeSmile")) SetTarget("EyeSmile", strength);
                else
                {
                    SetTarget("EyeSmile_L", strength);
                    SetTarget("EyeSmile_R", strength);
                }
                break;
            default:
                currentExpression = Expression.Neutral;
                break;
        }
        if (!immediate) return;
        foreach (Channel channel in channels.Values)
        {
            channel.weight = channel.target;
            channel.velocity = 0f;
        }
        ApplyWeights();
    }

    public void SetChannelWeight(string name, float weight, bool immediate = false)
    {
        if (name == null || !channels.TryGetValue(name, out Channel channel)) return;
        SetAutomaticBlink(false);
        currentExpression = Expression.Custom;
        channel.target = ClampWeight(weight);
        if (!immediate) return;
        channel.weight = channel.target;
        channel.velocity = 0f;
        ApplyWeights();
    }

    public void SetAutomaticBlink(bool enabled)
    {
        enabled = enabled && HasBlinkLeft && HasBlinkRight;
        if (automaticBlink == enabled) return;
        automaticBlink = enabled;
        blinkStartedAt = -1f;
        nextBlinkAt = Time.unscaledTime + SafeBlinkInterval();
        if (!enabled) return;
        SetTarget("Blink_L", 0f);
        SetTarget("Blink_R", 0f);
        SetTarget("EyeSmile", 0f);
        SetTarget("EyeSmile_L", 0f);
        SetTarget("EyeSmile_R", 0f);
        currentExpression = Expression.Neutral;
    }

    private void Awake()
    {
        RefreshBindings();
    }

    private void OnEnable()
    {
        RefreshBindings();
        blinkStartedAt = -1f;
        nextBlinkAt = Time.unscaledTime + SafeBlinkInterval();
    }

    private void LateUpdate()
    {
        float blinkWeight = UpdateAutomaticBlink(out bool blinking);
        float dt = Mathf.Max(0f, Time.unscaledDeltaTime);
        foreach (Channel channel in channels.Values)
        {
            if (blinking && (channel.name == "Blink_L" || channel.name == "Blink_R"))
            {
                // 자동 재생도 현재 모델 키만 사용하며 완전 감기 값 100까지 도달하게 합니다.
                channel.weight = blinkWeight;
                channel.velocity = 0f;
            }
            else if (transitionTime <= 0f)
            {
                channel.weight = channel.target;
                channel.velocity = 0f;
            }
            else if (dt > 0f)
            {
                channel.weight = ClampWeight(Mathf.SmoothDamp(channel.weight, channel.target, ref channel.velocity,
                    transitionTime, Mathf.Infinity, dt));
                if (Mathf.Abs(channel.weight - channel.target) <= 0.02f)
                {
                    channel.weight = channel.target;
                    channel.velocity = 0f;
                }
            }
        }
        ApplyWeights();
    }

    private float UpdateAutomaticBlink(out bool applyBlink)
    {
        applyBlink = false;
        if (!automaticBlink) return 0f;
        float now = Time.unscaledTime;
        if (blinkStartedAt < 0f)
        {
            if (now < nextBlinkAt) return 0f;
            blinkStartedAt = now;
        }
        applyBlink = true;
        float elapsed = now - blinkStartedAt;
        if (elapsed < BlinkCloseTime) return Mathf.SmoothStep(0f, 100f, elapsed / BlinkCloseTime);
        if (elapsed < BlinkCloseTime + BlinkHoldTime) return 100f;
        if (elapsed < BlinkCloseTime + BlinkHoldTime + BlinkOpenTime)
            return Mathf.SmoothStep(100f, 0f, (elapsed - BlinkCloseTime - BlinkHoldTime) / BlinkOpenTime);
        nextBlinkAt = blinkStartedAt + SafeBlinkInterval();
        blinkStartedAt = -1f;
        return 0f;
    }

    private float SafeBlinkInterval()
    {
        return float.IsNaN(blinkInterval) || float.IsInfinity(blinkInterval) ? 3.5f : Mathf.Max(0.3f, blinkInterval);
    }

    private void SetTarget(string name, float target)
    {
        if (channels.TryGetValue(name, out Channel channel)) channel.target = target;
    }

    private void ApplyWeights()
    {
        foreach (Channel channel in channels.Values) ApplyChannel(channel);
    }

    private void BindMouthRenderer()
    {
        mouthRenderer = null;
        mouthMaterial = null;
        mouthMaterialIndex = -1;
        mouthOriginalBlock.Clear();
        mouthHadPropertyBlock = false;
        if (characterRoot == null) return;
        foreach (var renderer in characterRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer.name != "입") continue;
            Material[] materials = renderer.sharedMaterials;
            for (int index = 0; index < materials.Length; index++)
            {
                Material material = materials[index];
                if (material == null || material.name != "mouth_Pbr" || !material.HasProperty("_BaseMap")) continue;
                mouthRenderer = renderer;
                mouthMaterial = material;
                mouthMaterialIndex = index;
                renderer.GetPropertyBlock(mouthOriginalBlock, index);
                mouthHadPropertyBlock = !mouthOriginalBlock.isEmpty;
                return;
            }
        }
    }

    private void ApplyMouthPose()
    {
        if (!HasMouthAtlas || mouthMaterialIndex < 0) return;
        Vector2 offset = MouthOffsets[(int)currentMouthPose];
        mouthRenderer.GetPropertyBlock(mouthWorkingBlock, mouthMaterialIndex);
        SetAtlasOffset(mouthWorkingBlock, BaseMapSt, mouthMaterial.GetTextureScale("_BaseMap"),
            mouthMaterial.GetTextureOffset("_BaseMap"), offset);
        if (mouthMaterial.HasProperty("_MainTex"))
            SetAtlasOffset(mouthWorkingBlock, MainTexSt, mouthMaterial.GetTextureScale("_MainTex"),
                mouthMaterial.GetTextureOffset("_MainTex"), offset);
        mouthRenderer.SetPropertyBlock(mouthWorkingBlock, mouthMaterialIndex);
    }

    private static void SetAtlasOffset(MaterialPropertyBlock block, int property,
        Vector2 scale, Vector2 originalOffset, Vector2 atlasOffset)
    {
        block.SetVector(property, new Vector4(scale.x, scale.y,
            originalOffset.x + atlasOffset.x * scale.x,
            originalOffset.y + atlasOffset.y * scale.y));
    }

    private void RestoreMouthMaterial()
    {
        if (mouthRenderer != null && mouthMaterialIndex >= 0)
            mouthRenderer.SetPropertyBlock(mouthHadPropertyBlock && mouthOriginalBlock != null ? mouthOriginalBlock : null,
                mouthMaterialIndex);
        mouthRenderer = null;
        mouthMaterial = null;
        mouthMaterialIndex = -1;
        mouthWorkingBlock?.Clear();
    }

    private void EnsureMouthBlocks()
    {
        if (mouthOriginalBlock == null) mouthOriginalBlock = new MaterialPropertyBlock();
        if (mouthWorkingBlock == null) mouthWorkingBlock = new MaterialPropertyBlock();
    }

    private void ApplyChannel(Channel channel)
    {
        float effectiveWeight = GetEffectiveChannelWeight(channel.name);
        foreach (Binding binding in channel.bindings)
            if (binding.IsValid) binding.renderer.SetBlendShapeWeight(binding.index, effectiveWeight);
    }

    private void RestoreBaselineWeights()
    {
        foreach (Channel channel in channels.Values)
            foreach (Binding binding in channel.bindings)
                if (binding.IsValid) binding.renderer.SetBlendShapeWeight(binding.index, binding.baseline);
    }

    private void OnDisable()
    {
        RestoreBaselineWeights();
        RestoreMouthMaterial();
        blinkStartedAt = -1f;
    }

    private static float ClampWeight(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp(value, 0f, 100f);
    }
}
