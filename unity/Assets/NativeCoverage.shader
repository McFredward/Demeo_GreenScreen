// Same-eye opaque-depth prefix, adapted from McFredward/GloomhavenVR (GPL-3.0).
Shader "Hidden/Demeo/NativeCoverage"
{
    SubShader
    {
        Cull Off ZWrite Off ColorMask A Blend Off AlphaToMask Off
        Stencil { WriteMask 0 Comp Always Pass Keep Fail Keep ZFail Keep }
        CGINCLUDE
        #pragma target 3.5
        #include "UnityCG.cginc"
        struct Attributes
        {
            uint id : SV_VertexID;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 position : SV_POSITION;
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings vert(Attributes input)
        {
            UNITY_SETUP_INSTANCE_ID(input);
            Varyings output;
            UNITY_INITIALIZE_OUTPUT(Varyings, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            float2 uv = float2((input.id << 1) & 2, input.id & 2);
            #if defined(UNITY_REVERSED_Z)
            const float farDepth = 0;
            #else
            const float farDepth = 1;
            #endif
            output.position = float4(uv * 2 - 1, farDepth, 1);
            return output;
        }
        float4 reset() : SV_Target { return 0; }
        float4 stamp() : SV_Target { return float4(0, 0, 0, 1); }
        ENDCG
        Pass
        {
            Name "RESET"
            ZTest Always
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment reset
            #pragma multi_compile _ STEREO_INSTANCING_ON
            ENDCG
        }
        Pass
        {
            Name "STAMP"
            ZTest Greater
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment stamp
            #pragma multi_compile _ STEREO_INSTANCING_ON
            ENDCG
        }
    }
    Fallback Off
}
