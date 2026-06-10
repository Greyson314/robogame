Shader "Robogame/ShieldBubble"
{
    // Translucent energy-shield membrane for the garage "bubble in space" look.
    // Fresnel rim glow: nearly clear face-on, bright at grazing angles, additive
    // so it reads as an energy field over the Polyverse space skybox. Double-
    // sided + ZWrite off so it works whether the camera is inside or outside the
    // dome. Session 120.
    Properties
    {
        _Color        ("Shield Color", Color)        = (0.30, 0.70, 1.0, 1.0)
        _RimPower     ("Rim Power", Range(0.5, 8))   = 3.0
        _RimIntensity ("Rim Intensity", Range(0, 4)) = 1.6
        _BaseAlpha    ("Base Alpha", Range(0, 1))    = 0.05
        _Pulse        ("Pulse Speed", Float)         = 0.6
        _PulseAmount  ("Pulse Amount", Range(0, 1))  = 0.15
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ShieldForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One   // additive glow
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _RimPower;
                float  _RimIntensity;
                float  _BaseAlpha;
                float  _Pulse;
                float  _PulseAmount;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 viewDirWS   : TEXCOORD1;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = p.positionCS;
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS   = GetWorldSpaceViewDir(p.positionWS);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
                float3 v = normalize(IN.viewDirWS);
                float fres = pow(1.0 - saturate(abs(dot(n, v))), _RimPower);
                float pulse = 1.0 + _PulseAmount * sin(_Time.y * _Pulse);
                float a = saturate(_BaseAlpha + fres * _RimIntensity) * pulse;
                float3 col = _Color.rgb * (0.4 + fres * _RimIntensity);
                return half4(col, a * _Color.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
