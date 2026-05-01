#ifndef GRASS_INCLUDE
#define GRASS_INCLUDE



///////////////////////////////////////////////////////////////////////////////
//                      Global Defines                                       //
///////////////////////////////////////////////////////////////////////////////

#define TRIANGLE_VERTEX_COUNT 3
#define MAX_GRASS_LAYER_COUNT 16
#define FIN_COUNT 6



///////////////////////////////////////////////////////////////////////////////
//                      Includes                                             //
///////////////////////////////////////////////////////////////////////////////

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
// See ShaderVariablesFunctions.hlsl in com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl



///////////////////////////////////////////////////////////////////////////////
//                      Properties                                           //
///////////////////////////////////////////////////////////////////////////////

CBUFFER_START(UnityPerMaterial)
    // COLOR
    TEXTURE2D(_MainTex);
    SAMPLER(sampler_MainTex);
    float4 _MainTex_ST;

    TEXTURE2D(_GroundTex);
    SAMPLER(sampler_GroundTex);
    float4 _GroundTex_ST;

    float3 _BaseColor;
    float3 _TopColor;

    
    // GEOMETRY
    int _ShellCount;
    float _MaximumHeight;
    int _FinsEnabled;
    int _ShellsEnabled;


    // COVERAGE
    TEXTURE2D(_GrassCoverageTexture);
    SAMPLER(sampler_GrassCoverageTexture);
    float4 _GrassCoverageTexture_ST;


    // NOISE
    TEXTURE2D(_ShapeNoiseTexture);
    SAMPLER(sampler_ShapeNoiseTexture);
    float _ShapeNoiseScale;
    float4 _ShapeNoiseTexture_ST;
    float _ShapeNoiseStrength;

    TEXTURE2D(_DetailNoiseTexture);
    SAMPLER(sampler_DetailNoiseTexture);
    float _DetailNoiseScale;
    float4 _DetailNoiseTexture_ST;
    float _DetailNoiseStrength;


    // INTERACTIVITY
    float3 _GrassTintColor;
    float _GrassInteractivityCutAmount;

    // WIND
    float3 _WindColor;

    float2 _WindDirection;

    TEXTURE2D(_WindTexture);
    SAMPLER(sampler_WindTexture);
    float4 _WindTexture_ST;

    float _WindMainStrength;

    float _WindPulseStrength;
    float _WindPulseFrequency;

    float _WindTurbulenceStrength;
    float _WindTurbulenceScale;
    float _WindTurbulenceSpeed;


    // DIRECTION
    TEXTURE2D(_GrassDirectionMap);
    SAMPLER(sampler_GrassDirectionMap);
    float _GrassDirectionStrength;


    // RENDER SETTINGS
    int _SurfaceNormalExclusionEnabled;
    float _SurfaceNormalPower;

    float _FadeStartDistance;
    float _MaximumDistance;

    int _TextureSamplingMethod;
    float _WorldScale;
    

    
    // LIGHTING SETTINGS
    int _ReceiveShadowsEnabled;
    int _ReceiveAmbientLightingEnabled;
    int _ReceiveFogEnabled;
    int _ReceiveDirectLightingEnabled;
CBUFFER_END



///////////////////////////////////////////////////////////////////////////////
//                      Global Vars                                          //
///////////////////////////////////////////////////////////////////////////////

float3 _GrassInteractivityCameraPosition;
float _GrassInteractivityCameraSize;
TEXTURE2D(_GrassInteractivityMap);
SAMPLER(sampler_GrassInteractivityMap);
bool _GrassInteractivityEnabled = 0;



///////////////////////////////////////////////////////////////////////////////
//                      Static Vars                                          //
///////////////////////////////////////////////////////////////////////////////

static float _InvWorldScale = 1.0 / _WorldScale;
static float _InvShellCount = 1.0 / _ShellCount;
static float _InvWindTurbulenceScale = 1.0 / _WindTurbulenceScale;
static float _InvShapeNoiseScale = 1.0 / _ShapeNoiseScale;
static float _InvDetailNoiseScale = 1.0 / _DetailNoiseScale;
static float3 Up = float3(0,1,0);



///////////////////////////////////////////////////////////////////////////////
//                      Helper Functions                                     //
///////////////////////////////////////////////////////////////////////////////

// Math
half InverseLerp(half a, half b, half v)
{
	return (v - a) / (b - a);
}

half RemapUnclamped(half iMin, half iMax, half oMin, half oMax, half v)
{
	half t = InverseLerp(iMin, iMax, v);
	return lerp(oMin, oMax, t);
}

half Remap(half iMin, half iMax, half oMin, half oMax, half v)
{
	v = clamp(v, iMin, iMax);
	return RemapUnclamped(iMin, iMax, oMin, oMax, v);
}

float CheapSqrt(float a)
{
    return 1.0 - ((1.0 - a) * (1.0 - a));
}


// Utilities
float Wind(float vec, float wavelength)
{
    return saturate(0.5 * (sin(vec * wavelength) + 1.0));
}


// Transforms
float3 _LightDirection;
float4 GetClipSpacePosition(float3 positionWS, float3 normalWS)
{
    #if CAST_SHADOWS_PASS
        float4 positionHCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
        
        #if UNITY_REVERSED_Z
            positionHCS.z = min(positionHCS.z, positionHCS.w * UNITY_NEAR_CLIP_VALUE);
        #else
            positionHCS.z = max(positionHCS.z, positionHCS.w * UNITY_NEAR_CLIP_VALUE);
        #endif
        
        return positionHCS;
    #endif
    
    return TransformWorldToHClip(positionWS);
}


float4 GetShadowCoord(float3 positionWS, float4 positionHCS)
{
    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
        return ComputeScreenPos(positionHCS);
    #else
        return TransformWorldToShadowCoord(positionWS);
    #endif
}

half4 GetMainLightShadowCoord(float3 PositionWS)
{
    #ifdef SHADOWS_SCREEN
        half4 clipPos = TransformWorldToHClip(PositionWS);
        return ComputeScreenPos(clipPos);
    #else
		return TransformWorldToShadowCoord(PositionWS);
    #endif
}


// Lighting
float3 CalculateAdjustedNormal(float3 lightDirection, float height, float3 normalWS)
{
    // When normalWS = lightDirection, adjustedNormal = 0 => Glitched result. 
    // Using min here avoids that issue by ensuring result != 0.
    float3 adjustedNormal = lightDirection - min(dot(normalWS, lightDirection), 0.99) * normalWS; 
    adjustedNormal = lerp(normalWS, adjustedNormal, height);
    return normalWS;
    return normalize(adjustedNormal);
}

void GetMainLightData(float3 PositionWS, out Light light)
{
    float4 shadowCoord = GetMainLightShadowCoord(PositionWS);
    light = GetMainLight(shadowCoord);
}

void GetAdditionalLightData(float3 PositionWS, float3 NormalWS, float2 NormalizedScreenSpaceUV, float height, out float3 col)
{
    col = 0;

	#if defined(_ADDITIONAL_LIGHTS)
        uint count = GetAdditionalLightsCount();
        uint meshRenderingLayers = GetMeshRenderingLayer();
    
        #if USE_FORWARD_PLUS
            ClusterIterator clusterIterator = ClusterInit(NormalizedScreenSpaceUV, PositionWS, 0);
            uint lightIndex = 0;
            [loop] while (ClusterNext(clusterIterator, lightIndex)) 
            {
                lightIndex += URP_FP_DIRECTIONAL_LIGHTS_COUNT;
                FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK

                Light light = GetAdditionalLight(lightIndex, PositionWS, half4(1,1,1,1));
    
                #if defined(_LIGHT_LAYERS)
                if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
                #endif
                {
                    float3 attenuatedLightColor = light.color * (light.distanceAttenuation * light.shadowAttenuation);
                    float3 adjustedNormal = CalculateAdjustedNormal(light.direction, height, NormalWS); // Readjusts normal to face the light source
		            col += attenuatedLightColor * saturate(dot(light.direction, adjustedNormal));
                }
            }
        #else
            for(uint lightIndex = 0; lightIndex < count; lightIndex++)
            {
                Light light = GetAdditionalLight(lightIndex, PositionWS, half4(1,1,1,1));

                #if defined(_LIGHT_LAYERS)
                if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
                #endif
                {
                    float3 attenuatedLightColor = light.color * (light.distanceAttenuation * light.shadowAttenuation);
                    float3 adjustedNormal = CalculateAdjustedNormal(light.direction, height, NormalWS); // Readjusts normal to face the light source
		            col += attenuatedLightColor * saturate(dot(light.direction, adjustedNormal));
                }
            }
        #endif
    #endif
}

float CookTorranceGSF(float NoH, float NoV, float NoL, float VoH)
{
    float gsf = min(1.0, min(2.0 * NoH * NoV / VoH, 2.0 * NoH * NoL / VoH));
    return gsf;
}

float ImplicitGSF(float NoL, float NoV)
{
    float gsf = NoL*NoV;
	return gsf;
}

float SchlickFresnel(float NoV)
{
    float v = 1.0 - NoV;
    float v5 = v * v * v * v * v;
    return v5;
}





/////////////////////////////////////////////////////////////////////////////////
/////////////////////////////////////////////////////////////////////////////////
///                                                                           ///
///                      SHADER BODY                                          ///
///                                                                           ///
/////////////////////////////////////////////////////////////////////////////////
/////////////////////////////////////////////////////////////////////////////////

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 uv         : TEXCOORD0;
    float color       : COLOR;
};
            
struct Varyings
{
    float3 positionWS : TEXCOORD0;
    float3 normalWS   : TEXCOORD1;
    float3 uv         : TEXCOORD2;
    float color       : COLOR;
};

struct Geoms
{
    float4 uv          : TEXCOORD0;
    float3 positionWS  : TEXCOORD1;
    float3 normalWS    : TEXCOORD2;
    float4 positionHCS : SV_POSITION;
    float color        : COLOR;
};



///////////////////////////////////////////////////////////////////////////////
//                      Vertex                                               //
///////////////////////////////////////////////////////////////////////////////

Varyings Vertex(Attributes IN)
{
    Varyings OUT;
    OUT.positionWS = mul(unity_ObjectToWorld, IN.positionOS).xyz;
    OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
    OUT.uv.xy = TRANSFORM_TEX(IN.uv, _MainTex).xy;
    
    //uv.z stores the distance to camera mapped remapped to [0,1] from [_FadeStartDistance,_MaximumDistance].
    OUT.uv.z = saturate(RemapUnclamped(_FadeStartDistance, _MaximumDistance, 0.0, 1.0, distance(_WorldSpaceCameraPos, OUT.positionWS)));
    OUT.color = IN.color;
    return OUT;
}



///////////////////////////////////////////////////////////////////////////////
//                      Geometry                                             //
///////////////////////////////////////////////////////////////////////////////

void SetupVertex(Varyings input, inout Geoms output, float height) 

{
    // Extrude the position outwards along the normal based on the passed in height
    float3 positionWS = input.positionWS + input.normalWS * (height * _MaximumHeight);

    output.positionWS = positionWS;
    output.normalWS = input.normalWS;
    
    // uv.w stores the layer height.
    output.uv = float4(input.uv, height); 
    output.positionHCS = GetClipSpacePosition(positionWS, input.normalWS);
    output.color = input.color;
}

// This DOES follow a pattern (001, 0hh; 011, 0h0), but I'm going to leave it handwritten as-is.
void CreateStructuredFins(triangle Varyings IN[3], float finHeight, inout TriangleStream<Geoms> OUT)
{
    Geoms TRIANGLE;
    
    // First triangle ( 0 -> 1)
    SetupVertex(IN[0], TRIANGLE, 0);
    OUT.Append(TRIANGLE);
    
    SetupVertex(IN[0], TRIANGLE, finHeight);
    OUT.Append(TRIANGLE);
    
    SetupVertex(IN[1], TRIANGLE, finHeight);
    OUT.Append(TRIANGLE);
    
    OUT.RestartStrip();
    
    
    SetupVertex(IN[0], TRIANGLE, 0);
    OUT.Append(TRIANGLE);
    
    SetupVertex(IN[1], TRIANGLE, finHeight);
    OUT.Append(TRIANGLE);
    
    SetupVertex(IN[1], TRIANGLE, 0);
    OUT.Append(TRIANGLE);
    
    OUT.RestartStrip();
    
    
    
    // Second triangle ( 1 -> 2)
    SetupVertex(IN[1], TRIANGLE, 0);
    OUT.Append(TRIANGLE);
    
    SetupVertex(IN[1], TRIANGLE, finHeight);
    OUT.Append(TRIANGLE);
    
    SetupVertex(IN[2], TRIANGLE, finHeight);
    OUT.Append(TRIANGLE);
    
    OUT.RestartStrip();
    
    
    SetupVertex(IN[1], TRIANGLE, 0);
    OUT.Append(TRIANGLE);
    
    SetupVertex(IN[2], TRIANGLE, finHeight);
    OUT.Append(TRIANGLE);
    
    SetupVertex(IN[2], TRIANGLE, 0);
    OUT.Append(TRIANGLE);
    
    OUT.RestartStrip();
    
    // Third triangle ( 1 -> 2)
    SetupVertex(IN[2], TRIANGLE, 0);
    OUT.Append(TRIANGLE);
    
    SetupVertex(IN[2], TRIANGLE, finHeight);
    OUT.Append(TRIANGLE);
    
    SetupVertex(IN[0], TRIANGLE, finHeight);
    OUT.Append(TRIANGLE);
    
    OUT.RestartStrip();
    
    
    SetupVertex(IN[2], TRIANGLE, 0);
    OUT.Append(TRIANGLE);
    
    SetupVertex(IN[0], TRIANGLE, finHeight);
    OUT.Append(TRIANGLE);
    
    SetupVertex(IN[0], TRIANGLE, 0);
    OUT.Append(TRIANGLE);
    
    OUT.RestartStrip();
}

void CreateShells(triangle Varyings IN[3], inout TriangleStream<Geoms> OUT)
{
    Geoms TRIANGLE;
    
    for(int c = 1; c < _ShellCount; c++)
    {
        float h = _InvShellCount * c;
        
        for(int i = 0; i < 3; i++)
        {
            SetupVertex(IN[i], TRIANGLE, h);
            OUT.Append(TRIANGLE);
        }
        OUT.RestartStrip();
    }
}

void CreateMesh(triangle Varyings IN[3], inout TriangleStream<Geoms> OUT)
{
    Geoms TRIANGLE;
    for(int i = 0; i < 3; i++)
    {
        SetupVertex(IN[i], TRIANGLE, 0);
        OUT.Append(TRIANGLE);
    }
    OUT.RestartStrip();
}

[maxvertexcount(TRIANGLE_VERTEX_COUNT * FIN_COUNT + TRIANGLE_VERTEX_COUNT * MAX_GRASS_LAYER_COUNT)]
void Geometry(triangle Varyings IN[3], inout TriangleStream<Geoms> OUT)
{
    CreateMesh(IN, OUT);
    
    float d = min(min(IN[0].uv.z, IN[1].uv.z), IN[2].uv.z);
    
    if(d < 1.0)
    {
        if(_ShellsEnabled == 1)
        {
            CreateShells(IN, OUT);
        }
        
        #if !CAST_SHADOWS_PASS
        if(d <= 0.0 && _FinsEnabled == 1)
        {
             CreateStructuredFins(IN, 1.0, OUT);
        }
        #endif
    }
}



///////////////////////////////////////////////////////////////////////////////
//                      Fragment                                             //
///////////////////////////////////////////////////////////////////////////////

float3 Fragment(Geoms IN) : SV_Target
{

    ///////////////////////////////
    //   SETUP                   //
    ///////////////////////////////
    
    _WindTurbulenceStrength *= 0.01;
    _WindMainStrength *= 0.01;
    _WindPulseStrength *= 0.01;
    _GrassDirectionStrength *= 0.01;
    
    IN.normalWS = normalize(IN.normalWS);
    
    float2 uv = IN.uv.xy;
    if(_TextureSamplingMethod == 1)
    {
        uv = IN.positionWS.xz * _InvWorldScale;
    }
    
    
    float height = IN.uv.w;
    float sqrtHeight = CheapSqrt(height);
    float height2 = height * height;
    
    // Displacement
    float3 grassVectorWS = ((SAMPLE_TEXTURE2D(_GrassDirectionMap, sampler_GrassDirectionMap, uv.xy).rgb * 2.0) - 1.0);
    grassVectorWS = normalize(IN.normalWS + grassVectorWS * 0.5); // Rotates the Grass Vector to a 90 degree cone around the normalWS direction
    float3 grassDirection = _GrassDirectionStrength * grassVectorWS;
    
    uv -= grassDirection.xy * sqrtHeight;
    
    
    
    
    
    ///////////////////////////////
    //   WIND                    //
    ///////////////////////////////
    
    _WindDirection = normalize(_WindDirection);
    
    // Texture Wind
    float2 windTurbulentUV = ((uv.xy * _InvWindTurbulenceScale) - _Time.yy * _WindDirection * _WindTurbulenceSpeed);
    float2 windTurbulent = (SAMPLE_TEXTURE2D(_WindTexture, sampler_WindTexture, windTurbulentUV).xy * 2.0) - 1.0;
    windTurbulent *= _WindTurbulenceStrength;
    float windTurbulentStrength = abs(windTurbulent.x) + abs(windTurbulent.y);
    
    
    // Periodic Wind
    float vec = dot(_WindDirection, uv) - _Time.y;
    
    float windPeriodicStrength = (Wind(vec, _WindPulseFrequency) + Wind(vec * 0.5, _WindPulseFrequency * 0.3)) * _WindPulseStrength;
    float2 periodicWind = _WindDirection * windPeriodicStrength;
    
    
    // Final Wind
    float2 windVal = (_WindDirection * _WindMainStrength) + periodicWind + windTurbulent;
    float2 wind = windVal * sqrtHeight;
    uv -= wind;
    
    
    
    
    ///////////////////////////////
    //   NOISE                   //
    ///////////////////////////////
    
    // Sampling the noise here...
    float shapeNoise, detailNoise, noise;
    shapeNoise = SAMPLE_TEXTURE2D(_ShapeNoiseTexture, sampler_ShapeNoiseTexture, uv * _InvShapeNoiseScale).r;
    detailNoise = SAMPLE_TEXTURE2D(_DetailNoiseTexture, sampler_DetailNoiseTexture, uv * _InvDetailNoiseScale).r;
    
    shapeNoise = lerp(1.0, shapeNoise, _ShapeNoiseStrength);
    detailNoise = lerp(1.0, detailNoise, _DetailNoiseStrength);
    noise = shapeNoise * detailNoise;
    
    
    // Fading the grass by normal & distance.
    float fading = 1.0;
    
    if(_SurfaceNormalExclusionEnabled == 1)
    {
        float fadeByNormal = saturate(dot(IN.normalWS, Up));
        fadeByNormal = pow(fadeByNormal, abs(_SurfaceNormalPower));
        fading *= fadeByNormal;
    }

    // Fade by Distance
    fading *= (1.0 - IN.uv.z);
    
    // Fade by Vertex Color Coverage
    fading *= IN.color;
    
    // Fade by Grass Coverage Map
    fading *= SAMPLE_TEXTURE2D(_GrassCoverageTexture, sampler_GrassCoverageTexture, uv).r;
    
    // Fade by Interactivity Map
    float2 x = _GrassInteractivityCameraPosition.xz - _GrassInteractivityCameraSize.xx;
    float2 y = _GrassInteractivityCameraPosition.xz + _GrassInteractivityCameraSize.xx;
    
    float2 cameraSpace;
    cameraSpace.x = Remap(x.x, y.x, 0, 1, IN.positionWS.x);
    cameraSpace.y = Remap(x.y, y.y, 0, 1, IN.positionWS.z);
    
    float2 interactivityFading = (cameraSpace - 0.5) * 2.0; // [0, 1] -> [-1, 1]
    float interactivityDistance = length(interactivityFading);
    
    // R = Remove
    // G = Partial Remove (Cut)
    // B = Tint Strength
    float3 interactivityColors = float3(0, 0, 0);
    if(interactivityDistance < 1)
    {
        interactivityColors = saturate((SAMPLE_TEXTURE2D(_GrassInteractivityMap, sampler_GrassInteractivityMap, cameraSpace).rgb));
        float falloff = saturate(Remap(0.8, 1.0, 1.0, 0.0, interactivityDistance));
        interactivityColors *= falloff;
        fading *= 1.0 - interactivityColors.r;
        fading *= (1.0 - (interactivityColors.g * _GrassInteractivityCutAmount));
    }
    
    noise *= fading;
    
    
    
    
    
    ///////////////////////////////
    //   CLIP                    //
    ///////////////////////////////
    
    // clip the grass if the noise is greater than the height
    clip(noise - height);
    
    #if CAST_SHADOWS_PASS
    return 0;
    #endif
    
    #if DepthNormalsOnlyPass
    return normalize(IN.normalWS);
    #endif

    #if DepthOnlyPass
    return 0;
    #endif
    
    
    
    
    
    ///////////////////////////////
    //   LIGHTING                //
    ///////////////////////////////
    
    // Blend between the normalWS and the GrassVector by Grass Height
    IN.normalWS = lerp(IN.normalWS, grassVectorWS, sqrtHeight); 
    
    
    // Setup Lighting
    Light mainLight;
    
    GetMainLightData(IN.positionWS, mainLight);
    float3 additionalLights = 0;

    float2 normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionHCS);

    GetAdditionalLightData(IN.positionWS, IN.normalWS, normalizedScreenSpaceUV, sqrtHeight, additionalLights);
    float3 adjustedNormal = CalculateAdjustedNormal(mainLight.direction, sqrtHeight, IN.normalWS);
    
    
    // Albedo
    float3 grassColor, groundColor, albedo, tint;
    grassColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv.xy).rgb;
    groundColor = SAMPLE_TEXTURE2D(_GroundTex, sampler_GroundTex, uv.xy).rgb;
    tint = lerp(float3(1,1,1), _GrassTintColor, interactivityColors.b);
    albedo = lerp(_BaseColor * groundColor, _TopColor * grassColor, sqrtHeight) * tint;
    
    
    // Wind Color
    float relativeWindStrength = 0.5 * (windTurbulentStrength + windPeriodicStrength) / max((_WindTurbulenceStrength + _WindPulseStrength), 0.00001);
    relativeWindStrength *= relativeWindStrength;
    relativeWindStrength *= relativeWindStrength;
    float3 windColor = _WindColor * relativeWindStrength * sqrtHeight * saturate(mainLight.shadowAttenuation + 0.5);
    
    
    
    // Lighting
    if(_ReceiveShadowsEnabled == 0)
    {
        mainLight.shadowAttenuation = 1;
    }
    
    float specularity, diffuse, subsurface, lightingModel;
    float NoV, NoL, NoH, VoH, VoL;
    float ndf, gsf, fresnel;
    
    float3 color = 0;
    
    float3 viewDir = normalize(GetWorldSpaceViewDir(IN.positionWS));
    float3 H = normalize(mainLight.direction + viewDir);
    
    NoV = dot(adjustedNormal, viewDir);
    NoL = dot(adjustedNormal, mainLight.direction);
    NoH = dot(adjustedNormal, H);
    VoH = dot(viewDir, H);
    VoL = dot(viewDir, mainLight.direction);
    
    // #define PI 3.14159
    // #define Roughness 1.0
    // Assume Roughness is 1.0. When Roughness is 1.0, the GGX NDF is 0.3183 for every value of NoH.
    // Therefore, we can skip the calculation and just use 0.3183 as a constant.
    // ndf = (Roughness * Roughness) / (PI * pow(((NoH * NoH) * ((Roughness * Roughness) - 1.0) + 1.0), 2);
    ndf = 0.3183;
    gsf = ImplicitGSF(NoL, NoV);
    fresnel = SchlickFresnel(NoV);
    
    specularity = (gsf * fresnel * ndf) / (4.0 * (NoL * NoV));
    diffuse = ImplicitGSF(NoL, NoV);
    subsurface = pow(saturate(-VoL), 5.0) * NoL;
    
    lightingModel = (diffuse + specularity + subsurface) * mainLight.shadowAttenuation * mainLight.color;
    
    
    
    
    ///////////////////////////////
    //   CALCULATE COLOR         //
    ///////////////////////////////
    
    // Setup lighting
    if(_ReceiveAmbientLightingEnabled == 1)
    {
        float3 ambient = SampleSH(adjustedNormal);
        color += ambient;
    }
    
    if(_ReceiveDirectLightingEnabled == 1)
    {
        color += lightingModel;
        color += additionalLights;
    }
    
    color += windColor;
    
    
    // Apply Decals
    #ifdef _DBUFFER
        ApplyDecalToBaseColor(IN.positionHCS, albedo);
    #endif
    
    
    color *= albedo;
    
    
    // Mix Fog
    if(_ReceiveFogEnabled == 1)
    {
        float fogFactor = InitializeInputDataFog(float4(IN.positionWS, 1), 0);
        color = MixFog(color, fogFactor);
    }
    
    return color;
}
#endif