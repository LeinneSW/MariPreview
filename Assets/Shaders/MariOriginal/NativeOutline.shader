Shader "Mari/NativeOutline"
{
    Properties
    {
        _OutlineWidth ("외곽선 폭", Float) = 0.0005
        _OutlineColor ("외곽선 색", Color) = (0,0,0,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry-1" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _OutlineWidth;
                half4 _OutlineColor;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionVS = TransformWorldToView(TransformObjectToWorld(input.positionOS.xyz));
                float3 normalVS = normalize(mul((float3x3)UNITY_MATRIX_V, TransformObjectToWorldNormal(input.normalOS)));
                positionVS += normalVS * _OutlineWidth * max(-positionVS.z, 0.05);
                output.positionCS = mul(UNITY_MATRIX_P, float4(positionVS,1));
                return output;
            }
            half4 Frag(Varyings input) : SV_Target { return _OutlineColor; }
            ENDHLSL
        }
    }
}
