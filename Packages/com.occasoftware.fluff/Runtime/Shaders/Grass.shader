Shader "OccaSoftware/Fluff/Grass"
{
    

    Properties
    {
        _MainTex ("Grass Color", 2D) = "white" {}
        [HDR] _TopColor("Grass Color", Color) = (1,1,1,1)
         
        _GroundTex ("Ground Color", 2D) = "white" {}
        [HDR] _BaseColor("Ground Color", Color) = (1,1,1,1)
       
        [HDR] _WindColor("Wind Color", Color) = (1,1,1,1)
        
        _ShellCount("Shell Count", Range(4, 16)) = 16
        _MaximumHeight("Maximum Height", Float) = 0.5
        
        _GrassCoverageTexture("Coverage", 2D) = "white" {}
        _ShapeNoiseTexture("Shape Noise", 2D) = "white" {}
        _ShapeNoiseScale("Shape Noise Scale", Float) = 1
        _ShapeNoiseStrength("Shape Noise Strength", Range(0,1)) = 1
        
        _DetailNoiseTexture("Detail Noise", 2D) = "white" {}
        _DetailNoiseScale("Detail Noise Scale", Float) = 1
        _DetailNoiseStrength("Detail Noise Strength", Range(0,1)) = 1
        
        // Wind
        _WindDirection("Wind Direction", Vector) = (1, 1, 0, 0)
        _WindTexture("Wind Texture", 2D) = "black" {}
        _WindMainStrength("Main Wind Strength", Float) = 1
        _WindPulseStrength("Wind Pulse Magnitude", Float) = 1
        _WindPulseFrequency("Wind Pulse Frequency", Float) = 1
        
        _WindTurbulenceSpeed("Wind Turbulence Speed", Float) = 1
        _WindTurbulenceScale("Wind Turbulence Scale", Float) = 1
        _WindTurbulenceStrength("Wind Turbulence Strength", Float) = 1
        
        _GrassDirectionMap("Grass Direction Map", 2D) = "black" {}
        _GrassDirectionStrength("Grass Direction Strength", Float) = 0.2
        
        _FadeStartDistance("Fade Start Distance", Float) = 50
        _MaximumDistance("Maximum Distance", Float) = 100

        // Interactivity
        [HDR] _GrassTintColor("Grass Tint Color", Color) = (1,1,1,1)
        _GrassInteractivityCutAmount("Grass Cut Amount", Float) = 0.7
        
        [Toggle(_SurfaceNormalExclusionEnabled)] _SurfaceNormalExclusionEnabled("Fade Grass on Steep Surfaces", Float) = 1
        _SurfaceNormalPower("Surface Normal Fading Power", Float) = 1
        
        
        [Toggle(_ShellsEnabled)] _ShellsEnabled("Use Shells", Float) = 1
        [Toggle(_FinsEnabled)] _FinsEnabled("Use Fins", Float) = 1
        
        
        _TextureSamplingMethod("Texture Sampling Method", Float) = 0
        _WorldScale("World Scale", Float) = 20
        
        [Toggle(_CastShadowsEnabled)] _CastShadowsEnabled("Cast Shadows", Float) = 1
        [Toggle(_ReceiveShadowsEnabled)] _ReceiveShadowsEnabled("Receive Shadows", Float) = 1
        [Toggle(_ReceiveDirectLightingEnabled)] _ReceiveDirectLightingEnabled("Receive Direct Lighting", Float) = 1
        [Toggle(_ReceiveAmbientLightingEnabled)] _ReceiveAmbientLightingEnabled("Receive Ambient Lighting", Float) = 1
        [Toggle(_ReceiveFogEnabled)] _ReceiveFogEnabled("Receive Fog", Float) = 1
    }
    
    
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        
        Pass
        {
            Name "ForwardLit"
            Tags {"LightMode" = "UniversalForwardOnly"}
            Cull Off
            ZWrite On
            ZTest LEqual
            ZClip Off
            
            HLSLPROGRAM
            #pragma require geometry
            
            #pragma multi_compile _ _FORWARD_PLUS
            
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_instancing
            
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            
            
            #pragma vertex Vertex
            #pragma geometry Geometry
            #pragma fragment Fragment
            
            #include "Grass.hlsl"
            ENDHLSL
        }
        
        Pass
        {
            Name "ShadowCaster"
            
            Tags {"LightMode" = "ShadowCaster"}
            Cull Off
            ZWrite On
            ZTest LEqual
            ZClip Off
            
            
            HLSLPROGRAM
            #pragma require geometry
            
            #pragma multi_compile_instancing
            #define CAST_SHADOWS_PASS 1
            
            #pragma vertex Vertex
            #pragma geometry Geometry
            #pragma fragment Fragment
            
            
            #include "Grass.hlsl"
            
            ENDHLSL
        }
        
        Pass
        {
            Name "DepthOnly"
            
            Tags {"LightMode" = "DepthOnly"}
            Cull Off
            ZWrite On
            ZTest LEqual
            ZClip Off
            
            
            HLSLPROGRAM
            #pragma require geometry
            
            #pragma multi_compile_instancing
            #define DepthOnlyPass 1
            
            #pragma vertex Vertex
            #pragma geometry Geometry
            #pragma fragment Fragment
            
            #include "Grass.hlsl"
            
            ENDHLSL
        }
        
        Pass
        {
            Name "DepthNormalsOnly"
            
            Tags {"LightMode" = "DepthNormalsOnly"}
            Cull Off
            ZWrite On
            ZTest LEqual
            ZClip Off
            
            
            HLSLPROGRAM
            #pragma require geometry
            
            #pragma multi_compile_instancing
            #define DepthNormalsOnlyPass 1
            
            #pragma vertex Vertex
            #pragma geometry Geometry
            #pragma fragment Fragment
            
            #include "Grass.hlsl"
            
            ENDHLSL
        }
    }
   
    CustomEditor "OccaSoftware.Fluff.Editor.GrassMaterialEditorGUI"
}
