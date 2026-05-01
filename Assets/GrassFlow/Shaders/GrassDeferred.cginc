


#ifdef UNITY_COLORSPACE_GAMMA
#define grass_ColorSpaceDielectricSpec half4(0.220916301, 0.220916301, 0.220916301, 1.0 - 0.220916301)
#else // Linear values
#define grass_ColorSpaceDielectricSpec half4(0.04, 0.04, 0.04, 1.0 - 0.04) // standard dielectric reflectivity coef at incident angle (= 4%)
#endif


//these functions are just copied from the unity built in cgincs so that we always have access
//even in srp garbage, since basicaly implementing a manual GBuffer function rather than using the srp one
//since srp garbage has a whole setup of its own weird structs with 'surfacedata' and whatnot
inline half GrassOneMinusReflectivityFromMetallic(half metallic)
{
    // We'll need oneMinusReflectivity, so
    //   1-reflectivity = 1-lerp(dielectricSpec, 1, metallic) = lerp(1-dielectricSpec, 0, metallic)
    // store (1-dielectricSpec) in unity_ColorSpaceDielectricSpec.a, then
    //   1-reflectivity = lerp(alpha, 0, metallic) = alpha + metallic*(0 - alpha) =
    //                  = alpha - metallic * alpha
    half oneMinusDielectricSpec = grass_ColorSpaceDielectricSpec.a;
    return oneMinusDielectricSpec - metallic * oneMinusDielectricSpec;
}

inline half3 GrassDiffuseAndSpecularFromMetallic (half3 albedo, half metallic, out half3 specColor, out half oneMinusReflectivity)
{
    specColor = lerp (grass_ColorSpaceDielectricSpec.rgb, albedo, metallic);
    oneMinusReflectivity = GrassOneMinusReflectivityFromMetallic(metallic);
    return albedo * oneMinusReflectivity;
}




#if defined(HDRP)

struct FragmentOutput {
    GBufferType0 GBuffer0 : SV_Target0;
    GBufferType1 GBuffer1 : SV_Target1;
    #if GBUFFERMATERIAL_COUNT >= 3
    GBufferType2 GBuffer2 : SV_Target2;
    #endif
    #if GBUFFERMATERIAL_COUNT >= 4
    GBufferType3 GBuffer3 : SV_Target3;
    #endif
    #if GBUFFERMATERIAL_COUNT >= 5
    GBufferType4 GBuffer4 : SV_Target4;
    #endif
    #if GBUFFERMATERIAL_COUNT >= 6
    GBufferType5 GBuffer5 : SV_Target5;
    #endif
    #if GBUFFERMATERIAL_COUNT >= 7
    GBufferType6 GBuffer6 : SV_Target6;
    #endif
    #if GBUFFERMATERIAL_COUNT >= 8
    GBufferType7 GBuffer7 : SV_Target7;
    #endif
};

FragmentOutput GrassToGBuffer(g2f i, half4 col
#ifdef _DEPTHOFFSET_ON
				, out float outputDepth : SV_Depth
#endif
){

    FragmentOutput output;

    PackedVaryingsMeshToPS vmesh = (PackedVaryingsMeshToPS)1;

    vmesh.positionCS = i.pos;
#ifdef VARYINGS_NEED_POSITION_WS
    vmesh.interpolators0 = i.worldPos;
#endif


#ifdef VARYINGS_NEED_COLOR
    vmesh.interpolators5 = 1;
#endif
    

    FragInputs input = UnpackVaryingsMeshToFragInputs(vmesh);
	PositionInputs posInput = GetPositionInput(input.positionSS.xy, _ScreenSize.zw, input.positionSS.z, input.positionSS.w, input.positionRWS);

	float3 V = GetWorldSpaceNormalizeViewDir(input.positionRWS);

    SurfaceData surfaceData;
	BuiltinData builtinData;
	GetSurfaceAndBuiltinData(input, V, posInput, surfaceData, builtinData);
        
    half3 specular;
	half specularMonochrome;
	half3 diffuseColor = GrassDiffuseAndSpecularFromMetallic(col.rgb, _Metallic, specular, specularMonochrome);

	float occSamp = lerp(1, tex2D(_OccMap, i.uv).r, occMult);
	float specSamp = tex2D(_SpecMap, i.uv).r * occSamp;

    //half3 packedNormalWS = PackNormal(i.normal);
    half3 packedNormalWS = (i.normal);

    uint materialFlags = surfaceData.materialFeatures;

    //this works fine
    half3 GI = GET_GI(i.normal, i.worldPos) * diffuseColor * occSamp;

    #ifdef GF_SPECULAR
    materialFlags |= MATERIALFEATUREFLAGS_LIT_SPECULAR_COLOR;
    //materialFlags |= MATERIALFEATUREFLAGS_LIT_SUBSURFACE_SCATTERING;
    specular = i.specVec.x * _Gloss * specSamp;
	surfaceData.perceptualSmoothness = specular;
    surfaceData.specularColor =  saturate(specTint * specularMult - 0.9);
    #else
    specular = 0.0.xxx;
    #endif

    //builtinData = (BuiltinData)1;
    //surfaceData = (SurfaceData)1;
    surfaceData.baseColor += diffuseColor;
    builtinData.emissiveColor += GI * 100;
    //builtinData.bakeDiffuseLighting = 100000000;
    surfaceData.ambientOcclusion = 1 - occSamp;
	surfaceData.normalWS = i.normal;
	surfaceData.metallic = _Metallic;
	surfaceData.subsurfaceMask = 0;
	surfaceData.coatMask = 0;
	surfaceData.diffusionProfileHash = 0;
	surfaceData.thickness = 0.1;
	surfaceData.transmissionMask = 0;
	surfaceData.materialFeatures = materialFlags;

    ENCODE_INTO_GBUFFER(surfaceData, builtinData, posInput.positionSS, output.GBuffer);

    #ifdef _DEPTHOFFSET_ON
    outputDepth = int.pos.z;
    #endif

    return output;
}


#elif defined(URP)

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"


FragmentOutput GrassToGBuffer(g2f i, half4 col){

    half3 specular;
	half specularMonochrome;
	half3 diffuseColor = GrassDiffuseAndSpecularFromMetallic(col.rgb, _Metallic, specular, specularMonochrome);

	float occSamp = lerp(1, tex2D(_OccMap, i.uv).r, occMult);
	float specSamp = tex2D(_SpecMap, i.uv).r * occSamp;

    half3 packedNormalWS = PackNormal(i.normal);

    uint materialFlags = 0;

    //tried doing all this dumb stuff to maybe do it more properly but it doesnt work and idunno why
    //could just be down to like not using the right shader feature keywords and stuff but eh
    //BRDFData brdfData;
    //float alpha = 1;
    //InitializeBRDFData(diffuseColor, _Metallic, specular, _Gloss, alpha, brdfData);

    //Light mainLight = GetMainLight(shadowCoord, i.worldPos, shadowMask);
    //MixRealtimeAndBakedGI(mainLight, i.normal, bakedGI, shadowMask);
    ////half3 GI = bakedGI * diffuseColor * ambientCO;
    //half3 GI = GlobalIllumination(brdfData, bakedGI, 1, i.worldPos, i.normal, 1) * ambientCO;

    //this works fine
    half3 GI = GET_GI(i.normal, i.worldPos) * diffuseColor * occSamp * ambientCO;

    // SimpleLit does not use _SPECULARHIGHLIGHTS_OFF to disable specular highlights.

    #ifdef GF_SPECULAR
    materialFlags |= kMaterialFlagSpecularSetup;
    specular = specTint * i.specVec.x * specularMult * 0.1 * _Gloss * specSamp;
    #else
    materialFlags |= kMaterialFlagSpecularHighlightsOff;
    specular = 0.0.xxx;
    #endif

    #ifdef _RECEIVE_SHADOWS_OFF
    materialFlags |= kMaterialFlagReceiveShadowsOff;
    #endif

    #if defined(LIGHTMAP_ON) && defined(_MIXED_LIGHTING_SUBTRACTIVE)
    materialFlags |= kMaterialFlagSubtractiveMixedLighting;
    #endif

    FragmentOutput output;
    output.GBuffer0 = half4(diffuseColor, PackMaterialFlags(materialFlags));   // albedo          albedo          albedo          materialFlags   (sRGB rendertarget)
    output.GBuffer1 = half4(specular, 1 - occSamp);                            // specular        specular        specular        occlusion
    output.GBuffer2 = half4(packedNormalWS, _Gloss * specSamp);                           // encoded-normal  encoded-normal  encoded-normal  smoothness
    output.GBuffer3 = half4(GI, 1);                                      // GI              GI              GI              [optional: see OutputAlpha()] (lighting buffer)
    #if _RENDER_PASS_ENABLED
    output.GBuffer4 = i.pos.z;
    #endif

    #if defined(OUTPUT_SHADOWMASK) || defined(GBUFFER_FEATURE_SHADOWMASK)
        #if defined(GBUFFER_FEATURE_SHADOWMASK)
        #define GBUFFER_SHADOWMASK GBuffer5
        #endif
    output.GBUFFER_SHADOWMASK = 0;
    #endif

    #if defined(_LIGHT_LAYERS) || defined(GBUFFER_FEATURE_RENDERING_LAYERS)
        #if defined(GBUFFER_FEATURE_RENDERING_LAYERS)
        #define GBUFFER_LIGHT_LAYERS GBuffer6
        #endif

        #if UNITY_VERSION >= 60020002
        output.GBUFFER_LIGHT_LAYERS = EncodeMeshRenderingLayer();
        #else
        uint renderingLayers = GetMeshRenderingLayer();
        output.GBUFFER_LIGHT_LAYERS = float4(EncodeMeshRenderingLayer(renderingLayers), 0.0, 0.0, 0.0);
        #endif
    #endif

    return output;
}

#else

//annoying to put this here but URP will also define this with the same name so it cant be in the main structs
struct FragmentOutput {
	float4 albedo : SV_Target0;
	float4 specular : SV_Target1;
	float4 normal : SV_Target2;
	float4 light : SV_Target3;
};

FragmentOutput GrassToGBuffer(g2f i, half4 col){

	FragmentOutput deferredData;

	half3 specular;
	half specularMonochrome;
	half3 diffuseColor = GrassDiffuseAndSpecularFromMetallic(col.rgb, _Metallic, specular, specularMonochrome);

	float occSamp = lerp(1, tex2D(_OccMap, i.uv).r, occMult);
	float specSamp = tex2D(_SpecMap, i.uv).r * occSamp;

	deferredData.albedo.rgb = diffuseColor; //albedo	
	deferredData.albedo.a = 1 - occSamp; //occulusion

    #ifdef GF_SPECULAR
    specular = specTint * i.specVec.x * specularMult * 0.1 * _Gloss;
	deferredData.specular.rgb = specular * specSamp; //specular tint
	deferredData.specular.a = _Gloss * specSamp; //shinyness
    #else
    specular = 0;
	deferredData.specular = 0;
    #endif



	deferredData.normal = float4(i.normal.xyz * 0.5 + 0.5, 0);
	//deferredData.normal = float4(0, 1, 0, 1);

	//indirect lighting
	float3 sh9 = GET_GI(i.normal, i.worldPos);

	deferredData.light.rgb = diffuseColor * sh9 * occSamp;

	#if !defined(UNITY_HDR_ON)
	deferredData.light.rgb = exp2(-deferredData.light.rgb);
	#endif

	return deferredData;
}

#endif