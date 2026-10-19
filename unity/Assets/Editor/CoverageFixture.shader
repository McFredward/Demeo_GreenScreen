// Owned analytic geometry for the coverage GPU checks. Never included in the mod.
Shader "Hidden/Demeo/Tests/CoverageFixture"
{
    SubShader
    {
        Pass
        {
            Cull Off ZWrite On ZTest Always Blend Off
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            float _Side;
            float4 vert(uint id : SV_VertexID) : SV_POSITION
            {
                float2 p = id == 0 ? float2(-1, -1) : (id == 1 ? float2(1, -1) : float2(-1, 1));
                return float4(p.x * _Side, p.y, .5, 1);
            }
            float4 frag() : SV_Target { return float4(.6, .4, .2, .25); }
            ENDCG
        }
    }
}
