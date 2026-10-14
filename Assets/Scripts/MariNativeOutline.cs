using System;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(500)]
public sealed class MariNativeOutline : MonoBehaviour
{
    [Serializable]
    public struct Pair
    {
        public SkinnedMeshRenderer source;
        public SkinnedMeshRenderer outline;
    }
    [Serializable]
    public struct StaticPair
    {
        public MeshFilter source;
        public MeshFilter outline;
    }
    [SerializeField] private Pair[] pairs;
    [SerializeField] private StaticPair[] staticPairs;
    public Pair[] Pairs => pairs;
    public StaticPair[] StaticPairs => staticPairs;
    public void Configure(Pair[] value, StaticPair[] staticValue) { pairs = value; staticPairs = staticValue; }

    private void LateUpdate()
    {
        if (pairs == null) return;
        foreach (var pair in pairs)
        {
            if (pair.source == null || pair.outline == null) continue;
            pair.outline.transform.SetLocalPositionAndRotation(pair.source.transform.localPosition, pair.source.transform.localRotation);
            pair.outline.transform.localScale = pair.source.transform.localScale;
            pair.outline.enabled = pair.source.enabled;
            for (int i = 0; i < pair.source.sharedMesh.blendShapeCount; i++)
                pair.outline.SetBlendShapeWeight(i, pair.source.GetBlendShapeWeight(i));
        }
        if (staticPairs == null) return;
        foreach (var pair in staticPairs)
        {
            if (pair.source == null || pair.outline == null) continue;
            pair.outline.transform.SetLocalPositionAndRotation(pair.source.transform.localPosition, pair.source.transform.localRotation);
            pair.outline.transform.localScale = pair.source.transform.localScale;
        }
    }
}
