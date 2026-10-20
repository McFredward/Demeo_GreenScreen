Shader "Hidden/Demeo/ProbeFixture"
{
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest Always Blend Off AlphaToMask Off
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            float _BaseAlpha;
            float4 vert(uint id : SV_VertexID) : SV_POSITION
            {
                float2 uv=float2((id<<1)&2,id&2);
                return float4(uv*2-1,0,1);
            }
            float4 frag(float4 position : SV_POSITION) : SV_Target
            { return float4(.1,.2,.3,_BaseAlpha+(position.x>=64 ? .25 : 0)); }
            ENDCG
        }
    }
}
