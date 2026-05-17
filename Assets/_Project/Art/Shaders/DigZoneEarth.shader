Shader "Robogame/DigZoneEarth"
{
    // Triplanar dirt for the diggable voxel terrain. The Surface-Nets
    // mesher emits positions + normals but NO UVs, so a UV-sampled
    // texture would render as garbage. This samples the dirt texture by
    // world position projected onto the three axis planes, weighted by
    // the surface normal — cut faces, tunnel walls and the floor all
    // read as dirt with no UV authoring. Lit by the main directional
    // light (Lambert + shadow) plus spherical-harmonic ambient so it
    // sits under the same toon-ish lighting as the rest of the arena.
    Properties
    {
        _BaseMap   ("Dirt Texture", 2D)        = "white" {}
        _BaseColor ("Tint", Color)             = (1,1,1,1)
        _MapScale  ("Metres per tile", Float)  = 3.0
        _BlendSharpness ("Triplanar Sharpness", Range(1,8)) = 4.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _MapScale;
                float  _BlendSharpness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float  fogFactor   : TEXCOORD2;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   n = GetVertexNormalInputs(IN.normalOS);
                OUT.positionHCS = p.positionCS;
                OUT.positionWS  = p.positionWS;
                OUT.normalWS    = n.normalWS;
                OUT.fogFactor   = ComputeFogFactor(p.positionCS.z);
                return OUT;
            }

            float3 SampleTriplanar(float3 posWS, float3 n)
            {
                float invScale = 1.0 / max(_MapScale, 0.0001);
                float2 uvX = posWS.zy * invScale;
                float2 uvY = posWS.xz * invScale;
                float2 uvZ = posWS.xy * invScale;

                float3 w = pow(abs(n), _BlendSharpness);
                w /= max(w.x + w.y + w.z, 0.0001);

                float3 cX = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX).rgb;
                float3 cY = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvY).rgb;
                float3 cZ = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ).rgb;
                return cX * w.x + cY * w.y + cZ * w.z;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 nWS = normalize(IN.normalWS);
                float3 albedo = SampleTriplanar(IN.positionWS, nWS) * _BaseColor.rgb;

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float ndotl = saturate(dot(nWS, mainLight.direction));
                float3 lit = albedo * mainLight.color
                             * (ndotl * mainLight.shadowAttenuation);

                float3 ambient = albedo * SampleSH(nWS);
                float3 color = lit + ambient;

                color = MixFog(color, IN.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
