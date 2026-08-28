Shader "Custom/Flipbook_Standard_Builtin"
{
    Properties
    {
        _MainTex ("Flipbook Texture", 2D) = "white" {}
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
        }

        LOD 100
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2_f
            {
                float2 uv : TEXCOORD0;
                float4 pos : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            UNITY_INSTANCING_BUFFER_START(FlipbookProps)
                UNITY_DEFINE_INSTANCED_PROP(float, _Row)
                UNITY_DEFINE_INSTANCED_PROP(float, _Col)
                UNITY_DEFINE_INSTANCED_PROP(float, _TotalFrame)
                UNITY_DEFINE_INSTANCED_PROP(float, _CurrentFrame)
                UNITY_DEFINE_INSTANCED_PROP(float, _FrameMode)
                UNITY_DEFINE_INSTANCED_PROP(float4, _FrameRect)
            UNITY_INSTANCING_BUFFER_END(FlipbookProps)

            v2_f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);

                v2_f o;
                o.pos = UnityObjectToClipPos(v.vertex);

                float row = UNITY_ACCESS_INSTANCED_PROP(FlipbookProps, _Row);
                float col = UNITY_ACCESS_INSTANCED_PROP(FlipbookProps, _Col);
                float totalFrame = UNITY_ACCESS_INSTANCED_PROP(FlipbookProps, _TotalFrame);
                float currentFrame = UNITY_ACCESS_INSTANCED_PROP(FlipbookProps, _CurrentFrame);
                float frameMode = UNITY_ACCESS_INSTANCED_PROP(FlipbookProps, _FrameMode);
                float4 frameRect = UNITY_ACCESS_INSTANCED_PROP(FlipbookProps, _FrameRect);
                float frame = clamp(floor(currentFrame), 0.0, totalFrame - 1.0);
                if (frameMode > 0.5)
                    o.uv = TRANSFORM_TEX(v.uv, _MainTex) * frameRect.zw + frameRect.xy;
                else
                {
                    float colIndex = fmod(frame, col);
                    float rowIndex = row - 1.0 - floor(frame / col);
                    float2 size = float2(1.0 / col, 1.0 / row);
                    float2 offset = float2(colIndex, rowIndex) * size;
                    o.uv = TRANSFORM_TEX(v.uv, _MainTex) * size + offset;
                }
                return o;
            }

            fixed4 frag(v2_f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv);
            }
            ENDCG
        }
    }
    FallBack "Unlit/Transparent"
}
