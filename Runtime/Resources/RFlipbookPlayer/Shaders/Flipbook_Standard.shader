Shader "Custom/Flipbook_Standard"
{
    Properties
    {
        [MainTexture] _MainTex ("Flipbook Texture", 2D) = "white" {}
        _Row ("Row Count", Float) = 16
        _Col ("Column Count", Float) = 16
        _TotalFrame ("Total Frames", Float) = 144
        _CurrentFrame ("Current Frame", Float) = 0
        _FrameMode ("Frame Mode", Float) = 0
        _FrameRect ("Frame UV Rect", Vector) = (0, 0, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        LOD 100
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct attributes
            {
                float4 position_os : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct varyings
            {
                float4 position_hcs : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(FlipbookProps)
                UNITY_DEFINE_INSTANCED_PROP(float, _Row)
                UNITY_DEFINE_INSTANCED_PROP(float, _Col)
                UNITY_DEFINE_INSTANCED_PROP(float, _TotalFrame)
                UNITY_DEFINE_INSTANCED_PROP(float, _CurrentFrame)
                UNITY_DEFINE_INSTANCED_PROP(float, _FrameMode)
                UNITY_DEFINE_INSTANCED_PROP(float4, _FrameRect)
            UNITY_INSTANCING_BUFFER_END(FlipbookProps)

            varyings vert(attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);

                varyings output;
                output.position_hcs = TransformObjectToHClip(input.position_os.xyz);

                float row = UNITY_ACCESS_INSTANCED_PROP(FlipbookProps, _Row);
                float col = UNITY_ACCESS_INSTANCED_PROP(FlipbookProps, _Col);
                float totalFrame = UNITY_ACCESS_INSTANCED_PROP(FlipbookProps, _TotalFrame);
                float currentFrame = UNITY_ACCESS_INSTANCED_PROP(FlipbookProps, _CurrentFrame);
                float frameMode = UNITY_ACCESS_INSTANCED_PROP(FlipbookProps, _FrameMode);
                float4 frameRect = UNITY_ACCESS_INSTANCED_PROP(FlipbookProps, _FrameRect);
                float frame = clamp(floor(currentFrame), 0.0, totalFrame - 1.0);
                if (frameMode > 0.5)
                    output.uv = TRANSFORM_TEX(input.uv, _MainTex) * frameRect.zw + frameRect.xy;
                else
                {
                    float colIndex = fmod(frame, col);
                    float rowIndex = row - 1.0 - floor(frame / col);
                    float2 size = float2(1.0 / col, 1.0 / row);
                    float2 offset = float2(colIndex, rowIndex) * size;
                    output.uv = TRANSFORM_TEX(input.uv, _MainTex) * size + offset;
                }
                return output;
            }

            half4 frag(varyings input) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
