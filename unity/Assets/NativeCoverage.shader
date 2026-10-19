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
        float4 vert(uint id : SV_VertexID) : SV_POSITION
        {
            float2 uv = float2((id << 1) & 2, id & 2);
            #if defined(UNITY_REVERSED_Z)
            const float farDepth = 0;
            #else
            const float farDepth = 1;
            #endif
            return float4(uv * 2 - 1, farDepth, 1);
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
            ENDCG
        }
        Pass
        {
            Name "STAMP"
            ZTest Greater
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment stamp
            ENDCG
        }
    }
    Fallback Off
}
