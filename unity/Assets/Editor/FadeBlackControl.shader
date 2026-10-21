// GPU negative control: ordinary black source-over makes empty coverage opaque.
// Fade the existing RGB contribution AND coverage to the real room.
Shader "Hidden/Demeo/FadeBlackControl"
{
    Properties { _PassthroughFade ("Fade opacity", Range(0,1)) = 0 }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" }
        Pass
        {
            Name "ROOM_FADE"
            Cull Off ZWrite Off ZTest Always ColorMask RGBA AlphaToMask Off
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            Stencil { WriteMask 0 Comp Always Pass Keep Fail Keep ZFail Keep }
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ STEREO_INSTANCING_ON
            #include "UnityCG.cginc"
            struct Attributes
            {
                float4 position : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 position : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            float _PassthroughFade;
            Varyings vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                UNITY_INITIALIZE_OUTPUT(Varyings, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.position);
                return output;
            }
            float4 frag(Varyings input) : SV_Target
            {
                return float4(0, 0, 0, saturate(_PassthroughFade));
            }
            ENDCG
        }
    }
    Fallback Off
}
