
using System.Collections.Generic;

namespace GrassFlow.Localization {
    public class English : Locale {

        new public static string GetTooltip(string memberName) {
            _tooltips.TryGetValue(memberName, out var tip);
            return tip;
        }

        new public static string GetMatTooltip(string memberName) {
            _materialTooltips.TryGetValue(memberName, out var tip);
            return tip;
        }

        new public static string GetName(string memberName) {
            _names.TryGetValue(memberName, out var name);
            return name;
        }

        new public static string GetMsg(string memberName) {
            _messages.TryGetValue(memberName, out var msg);
            return msg;
        }

        internal new static string isoCodeStatic => "en";
        internal override string isoCode => isoCodeStatic;
        internal override Dictionary<string, string> names => _names;
        internal override Dictionary<string, string> tooltips => _tooltips;
        internal override Dictionary<string, string> messages => _messages;
        internal override Dictionary<string, string> materialTooltips => _materialTooltips;


        static readonly Dictionary<string, string> _names = new Dictionary<string, string> {
            { nameof(GrassMesh.asyncInitialization), "Async Initialization"},
            { nameof(GrassMesh.receiveShadows), "Receive Shadows"},
            { nameof(GrassMesh.castShadows), "Cast Shadows"},
            { nameof(GrassMesh.renderingLayer), "Rendering Layer Mask"},
            { nameof(GrassMesh.instanceCount), "Instance Count"},
            { nameof(GrassMesh.lodSteps), "LOD Steps"},
            { nameof(GrassMesh.renderType), "Render Type"},
            { nameof(GrassMesh.grassMesh), "Terrain Mesh"},
            { nameof(GrassMesh.customMeshLods), "Grass Lods"},
            { nameof(GrassMesh.crossFade), "Cross Fade"},
            { nameof(GrassMesh.crossFadeRange), "Cross Fade Range"},
            { nameof(GrassMesh.customGrassMesh), "Custom Grass Mesh"},
            { nameof(GrassMesh.mainGrassMat), "Main Grass Mat"},
            { nameof(GrassMesh.terrainObject), "Terrain Object"},
            { nameof(GrassMesh.terrainTransform), "Terrain Transform"},
            { nameof(GrassMesh.colorMap), "Color Map"},
            { nameof(GrassMesh.paramMap), "Param Map"},
            { nameof(GrassMesh.typeMap), "Type Map"},
            { nameof(GrassMesh.grassPerTri), "Grass Per Instance"},
            { nameof(GrassMesh.terrainGrassDensity), "Terrain Grass Density"},
            { nameof(GrassMesh.normalizeMaxRatio), "Normalize Max Ratio"},
            { nameof(GrassMesh.terrainSlopeThresh), "Terrain Slope Threshold"},
            { nameof(GrassMesh.terrainSlopeFade), "Terrain Slope Fade"},
            { nameof(GrassMesh.lodParams), "LOD Parameters"},
            { nameof(GrassMesh.maxRenderDist), "Max Render Distance"},
            { nameof(GrassMesh.expandBounds), "Expand Bounds by Grass Height"},
            { nameof(GrassMesh.frustumCull), "Frustum Cull"},
            { nameof(GrassMesh.frustumCullThresh), "Frustum Cull Threshold"},
            { nameof(GrassMesh.bakeDensity), "Bake Density"},
            { nameof(GrassMesh.bakeData), "Bake Data"},

            { nameof(GrassFlowRenderer.visualizeChunkBounds), "Visualize Chunk Bounds"},
            { nameof(GrassFlowRenderer.renderLayer), "Render Layer"},
            { nameof(GrassFlowRenderer.updateBuffers), "Update Buffers"},
            { nameof(GrassFlowRenderer.useMaterialInstance), "Use Material Instance"},
            { nameof(GrassFlowRenderer.terrainExpansion), "Terrain Expansion"},
            { nameof(GrassFlowRenderer.normalizeMeshDensity), "Normalize Mesh Density"},

            
			//--------------------------------------------------------------------------------
			//inspector stuff---------------------------------------------------------------
			//--------------------------------------------------------------------------------			
            { "refreshButton", "Refresh" },
            { "settingsTab", "Settings" },
            { "paintModeTab", "Paint Mode" },
            { "globalRendererSettings", "Global Renderer Settings" },
            { "grassRenderProperties", "Grass Render Properties" },
            { "terrainHeader", "Terrain" },
            { "maxRatio", "Max Ratio" },
            { "lodHeader", "LOD" },
            { "meshLodChunks", "Mesh Lod Chunks" },
            { "terrainLodChunks", "Terrain Lod Chunks" },
            { "grassMeshesHeader", "Grass Meshes" },
            { "addCopyGrassMesh", "Add/Copy Grass Mesh" },
            { "addFromSelected", "Add From Selected" },
            { "detailMapsHeader", "Detail Maps" },
            { "revertChangesButton", "Revert Changes" },
            { "bakeDensityToMeshButton", "Bake Density to Mesh" },
            { "settingsHeader", "Settings" },
            { "brushColor", "Brush Color" },
            { "useBrushOpacity", "Use Brush Opacity" },
            { "grassTypeIndex", "Grass Type Index" },
            { "clampRange", "Clamp Range" },
            { "brushSize", "Brush Size" },
            { "brushStrength", "Brush Strength" },
            { "raycastLayerMask", "Raycast Layer Mask" },
            { "paintContinuously", "Paint Continuously" },
            { "useDeltaTimePaint", "Use Delta Time Paint" },
            { "splatMapsHeader", "Splat Maps" },
            { "splatLayer", "Splat Layer" },
            { "tolerance", "Tolerance" },
            { "applyAdditive", "Apply Additive" },
            { "applySubtractive", "Apply Subtractive" },
            { "applyReplace", "Apply Replace" },
            { "paintGrassColor", "Paint Grass Color" },
            { "paintGrassDensity", "Paint Grass Density" },
            { "paintGrassHeight", "Paint Grass Height" },
            { "paintGrassFlatness", "Paint Grass Flatness" },
            { "paintGrassWindStrength", "Paint Grass Wind Strength" },
            { "paintGrassType", "Paint Grass Type" },
            { "brushesHeader", "Brushes" },
            { "noBrushesDefined", "No brushes defined." },

            
			//--------------------------------------------------------------------------------
			//material stuff---------------------------------------------------------------
			//--------------------------------------------------------------------------------
			// Grass Properties
            { "_Color", "Grass Color" },
            { "bladeHeight", "Blade Height" },
            { "bladeWidth", "Blade Width" },
            { "flatTint", "Flatness Tint" },
            { "altCol", "Variation Color" },
            { "DISABLE_DECALS", "Disable Decals" },
            { "_ReceiveShadows", "Receive Shadows" },
            { "specTint", "Specular Tint" },
            { "_Metallic", "Metallic" },
            { "_Gloss", "Gloss" },
            { "_noiseScale2", "Secondary Noise Scale" },
            { "_noiseSpeed2", "Secondary Noise Speed" },
            { "occMult", "Occlusion Strength" },
            { "bladeOffset", "Blade Offset" },
            { "bladeSharp", "Blade Sharpness" },
            { "seekSun", "Seek Sun" },
            { "topViewPush", "Top View Adjust" },
            { "flatnessMult", "Flatness Adjust" },
            { "_BILLBOARD", "Billboard" },
            { "variance", "Variances (p,h,c,w)" },

            // Lighting
            { "_ppLights", "Per-Pixel Lights" },
            { "_AO", "AO" },
            { "ambientCO", "Ambient" },
            { "ambientCOShadow", "Shadow Ambient" },
            { "edgeLight", "Edge On Light" },
            { "edgeLightSharp", "Edge On Light Sharpness" },
            { "blendNormal", "Blend Surface Normal" },
            { "_GF_SPECULAR", "Enable Specular" },
            { "specSmooth", "Smoothness" },
            { "specularMult", "Specular Mult" },
            { "specHeight", "Specular Height Adjust" },
            { "_GF_NORMAL_MAP", "Enable Normal Mapping" },
            { "normalStrength", "Strength" },
            { "bumpMap", "Normal Map" },

            // Self Shadow
            { "GF_SELF_SHADOW", "Fake Self Shadow" },
            { "selfShadowWind", "Self Shadow Wind" },
            { "selfShadowScaleOffset", "Self Shadow Scale/Offset" },

            // LOD
            { "_ALPHA_TO_MASK", "Alpha To Mask" },
            { "widthLODscale", "Width LOD Scale" },
            { "_GF_USE_DITHER", "Use Dither" },
            { "grassFade", "Grass Fade" },
            { "grassFadeSharpness", "Fade Sharpness" },
            { "_LOD_SCALING", "Use LOD Scaling" },

            // Wind
            { "windMult", "Wind Strength Mult" },
            { "windTint", "Wind Tint" },
            { "_noiseScale", "Noise Scale" },
            { "_noiseSpeed", "Noise Speed" },
            { "windDir", "Wind Direction" },
            { "windDir2", "Secondary Wind Direction" },

            // Bend
            { "_MULTI_SEGMENT", "Multi Segment" },
            { "bladeLateralCurve", "Curvature" },
            { "bladeVerticalCurve", "Droop" },
            { "bladeStiffness", "Floppyness" },

            // Maps and Texturing
            { "_SEMI_TRANSPARENT", "Enable Alpha Clip" },
            { "alphaLock", "Discard Texture Alpha" },
            { "alphaMult", "Alpha Multiplier" },
            { "alphaClip", "Alpha Clip" },
            { "numTextures", "Number of Textures" },
            { "textureAtlasScalingCutoff", "Type Texture Scaling Cutoff" },
            { "_SpecMap", "Specular Map" },
            { "_OccMap", "Occlusion Map" },
            { "_MainTex", "Grass Texture" },
            { "dhfParamMap", "Parameter Map" },

            // Optimization
            { "_Cull", "Culling Mode" },
            { "MESH_COLORS", "Use Vertex Height Colors" },
            { "MESH_NORMALS", "Use Mesh Normals" },
            { "MESH_UVS", "Use Mesh UVs" },
            { "MAP_COLOR", "Dynamic Color Map" },
            { "MAP_PARAM", "Dynamic Param Map" },
            { "MAP_TYPE", "Dynamic Type Map" },
            { "GRASS_RIPPLES", "Allow Ripples" },
            { "GRASS_FORCES", "Allow Multiple Forces" },
        };

        static readonly Dictionary<string, string> _tooltips = new Dictionary<string, string> {
            { nameof(GrassMesh.asyncInitialization), "Enables a partially multithreaded asynchronus execution of the initial processing that can slightly reduce load times if you have a large mesh. The downside of this is that the game might start before the grass is loaded."},
            { nameof(GrassMesh.receiveShadows), "Receive shadows on the grass. Can be expensive, especially with cascaded shadows on. (Requires the grass shader with depth pass to render properly)"},
            { nameof(GrassMesh.castShadows), "Grass casts shadows. Fairly expensive option. (Also requires the grass shader with depth pass to render at all)"},
            { nameof(GrassMesh.renderingLayer), "URP/HDRP rendering layer mask."},
            { nameof(GrassMesh.instanceCount), "This value is factored in with 'Grass Per Instance' and the number of triangles in the underlying source geometry to calculate the total possible instances that can be rendered."},
            { nameof(GrassMesh.lodSteps), "Number of steps that grass density can decrease by with the LOD system to decrease number of instances in the distance.\nIMPORTANT: Make sure to have this high enough as this setting also controls the minimum amount of grass that can be shown. i.e. the higher this setting, a smaller amount of grass can shown in the distance."},
            { nameof(GrassMesh.renderType), "Mode this grass is for. Mesh will attach grass to the triangles of a mesh, terrain will attach grass to surface of a unity terrain object."},
            { nameof(GrassMesh.grassMesh), "Mesh to attach grass to in mesh mode."},
            { nameof(GrassMesh.customMeshLods), "Meshes, materials, and distances to use when rendering grass. Individual lods only allowed when not using frustum culling."},
            { nameof(GrassMesh.crossFade), "If enabled, LODs will be draw with some overlap and faded between to smooth out the transition.\nHas some performance impact as chunks will be drawn twice during the overlap fade range, and currently fading chunks cannot be batched.\nNOTE: Dithering must be enabled on the materials used for the LODs."},
            { nameof(GrassMesh.crossFadeRange), "Range over which crossfading happens between LODs"},
            { nameof(GrassMesh.customGrassMesh), "Mesh used to render grass."},
            { nameof(GrassMesh.mainGrassMat), "Material used to render grass. Should use one of the GrassFlow shaders."},
            { nameof(GrassMesh.terrainObject), "Terrain object to attach grass to in terrain mode."},
            { nameof(GrassMesh.terrainTransform), "Transform that the grass belongs to."},
            { nameof(GrassMesh.colorMap), "Texture that controls grass color. The alpha channel of this texture is used to control how the color gets applied. If alpha is 1, the color is also multiplied by material color, if 0, material color is ignored. Inbetween values work too."},
            { nameof(GrassMesh.paramMap), "Texture that controls various parameters of the grass. Red channel = density. Green channel = height, Blue channel = flattenedness. Alpha channel = wind strength."},
            { nameof(GrassMesh.typeMap), "Texture that controls which texture to use from the atlas in the grass texture atlas (if using one). NOTE: Read the documentation for information about how this texture works."},
            { nameof(GrassMesh.grassPerTri), "If unsure, leave at 1.\nNumber of times to duplicate rendered mesh geometry. Basically makes it so that more geometry is 'real' Vs. instanced.\nThere's a certain threshhold of real vs instanced geometry that is fastest, so you'll just have to play around with it to see what is good for you.\nNOTE: May or may not help on mobile.\nNOTE: Setting this too high may cause weird lod popping and should be set to 1 for frustum culling."},
            { nameof(GrassMesh.terrainGrassDensity), "Base level of grass to render in terrain mode. This amount will be multiplied by instance count to control LOD falloff."},
            { nameof(GrassMesh.normalizeMaxRatio), "Maximum ratio at which the largest triangle can be subdivided. Basically it just controls the subdivision density when attempting to normalize the mesh. You probably want to set this as low as possible while still providing good results."},
            { nameof(GrassMesh.terrainSlopeThresh), "-1 to 1 angle threshhold for spawning grass on terrain compare to the up direction (0, 1, 0)."},
            { nameof(GrassMesh.terrainSlopeFade), "Distance from the terrain slope thresh at which grass will be scaled to 'fade' out."},
            { nameof(GrassMesh.lodParams), "Controls the LOD parameter of the grass. X = render distance. Y = density falloff sharpness (how quickly the amount of grass is reduced to zero). Z = offset, basically a positive number prevents blades from popping out within this distance."},
            { nameof(GrassMesh.maxRenderDist), "Controls max render dist of the grass chunks. This value is mostly just used to quickly reject far away chunks for rendering."},
            { nameof(GrassMesh.expandBounds), "Usefull if you're rendering grass onto a mesh that doesn't just face up, i.e a sphere,as grass can end up pointing in any direction, the bounds of the chunk need to be expanded by the maximum potential height of the grass.\nBut since most terrains will only have upward facing grass, it can be more optimal to not expand the bounds in every direction.\nNote that even when this is disabled, the bounds are still expanded vertically."},
            { nameof(GrassMesh.frustumCull), "Whether or not to use frustum culling (discard grass outside of camera view) for grass. Uses additional VRAM. Generally this doesn't help performance much unless rendering high chunk counts, and using this can cause issues with shadows as it's easy for grass outside the view of the camera to cast a shadow into the view of the camera.\nIf this is off, Unity will simply handle culling on a per-chunk basis and render each thread individually.\nIf this is on, a compute shader manually culls each grass instance, then one draw call is issued to render everything in one go."},
            { nameof(GrassMesh.frustumCullThresh), "Threshholds for horiztonal and vertical view to determine how far outside the cameras view grass must be to be culled. Generally these should be set as low as possible without being able to see grass pop out at the edges of the view."},
            { nameof(GrassMesh.bakeDensity), "If using a parameter map, this will only generate grass based on the density channel. This is significantly more efficent, with the only caveat that grass density cannot be dynamically painted at runtime."},
            { nameof(GrassMesh.bakeData), "Increases memory cost (by 37%), but is more efficient when using color/param/type maps.\nIf you disable this and are using maps, you should enable the dynamic map settings on the material for those maps.\nIMPORTANT: Frustum culling is currently incompatible with this setting being off."},

            { nameof(GrassFlowRenderer.visualizeChunkBounds), "Does this really need a tooltip? Uhh, well chunk bounds are expanded automatically by blade height to avoid grass popping out when the bounds are culled at strange angles."},
            { nameof(GrassFlowRenderer.renderLayer), "Layer to render the grass on."},
            { nameof(GrassFlowRenderer.updateBuffers), "This setting only effects the editor. Most of the time you're going to want this on as it prevents visual popping as scripts are recompiled and such. " +
            "You can turn it off to get a more accurate view of game performance, though really it hardly makes any difference."},
            { nameof(GrassFlowRenderer.useMaterialInstance), "If true, an instance of the material will be created to render with. Important if you want to use the same material for multiple grasses but want them to have different textures etc."},
            { nameof(GrassFlowRenderer.terrainExpansion), "Amount to expand grass chunks on terrain, helps avoid artifacts on edges of chunks. Preferably set this as low as you can without it looking bad."},
            { nameof(GrassFlowRenderer.normalizeMeshDensity), "Don't enable this setting unless your source mesh has very NON-uniform density as it'll increase processing time and probably produce worse results. " +
            "This setting attempts to subdivide the mesh to make all triangles as close to the same size as it can, the original shape will be matched exactly. " +
            "Because this subdivides the mesh, you may want to decrease GrassPerTri to account for the increased density."},

            
			//--------------------------------------------------------------------------------
			//inspector stuff---------------------------------------------------------------
			//--------------------------------------------------------------------------------
            { "refreshButton", "Releases/destroys all current data and resets everything. Use to reset grass after changing certain things." },
            { "globalRendererSettings", "These settings are shared across all Grass Meshes" },
            { "meshLodChunks", "Number of chunks to use for LOD culling. Distance to each chunk controls amount of grass that will be rendered there. In MESH mode, generally you won't need more than one chunk in the Y direction but if you have incredibly vertical terrain it might be useful. Too many chunks is bad for performance, but not enough chunks will look bad and blocky when culling grass, so set this to have as few chunks as you can while also not looking bad. (Tip: you don't need as many as you think you do.)" },
            { "terrainLodChunks", "Number of chunks to use for LOD culling. Distance to each chunk controls amount of grass that will be rendered there. Too many chunks is bad for performance, but not enough chunks will look bad and blocky when culling grass, so set this to have as few chunks as you can while also not looking bad. (Tip: you don't need as many as you think you do.)" },
            { "addCopyGrassMesh", "Adds an additional GrassMesh that can render grass on a separate mesh or terrain, copying settings from the currently selected GrassMesh." },
            { "addFromSelected", "Attempts to add additional GrassMeshes from the selected objects in the hierarchy, automatically filling transforms and meshes. You'll probably need to lock the inspector and then select objects for this to work." },
            { "revertChangesButton", "Discards changes to detail maps since they were last saved. The maps are saved whenever the project assets are saved e.g. on Ctrl+S. Revert hotkey: Shift-R. This action \"should\" have undo/redo support, it probably works." },
            { "bakeDensityToMeshButton", "Creates a new mesh based on the density information in the parameter map. You can use this mesh to more efficiently only render grass on certain parts of your mesh. Does NOT automatically apply the resulting mesh." },
            { "useBrushOpacity", "Whether or not to use the brush opacity when painting. When painting grass type at full strength, turning this off can be ideal to avoid artifacts where brush opacity affects density undesirably." },
            { "grassTypeIndex", "Index into the grass texture atlas. For selecting which texture to paint." },
            { "clampRange", "Min and max range for parameters while painting. This can be used to essentially paint a set value instead of being additive or subtractive." },
            { "raycastLayerMask", "This mask is used when raycasting the terrain/mesh for painting. You can use this to only paint on the layer your terrain is on and paint through blocking objects, or vice versa." },
            { "paintContinuously", "If off the mouse needs to be moved to paint, otherwise it will paint continuously while the mouse is down." },
            { "useDeltaTimePaint", "If on the brush strength is multiplied by delta time to make painting strength framerate independent. It's useful to turn this off if you want to use brushes more like stamps and use strength of 1 and apply the full brush to the grass with a single click." },
            { "splatLayer", "The index of the splat texture layer you want to use to mask where grass appears." },
            { "tolerance", "Controls opacity tolerance when applying splat map layers." },
            { "applyAdditive", "Adds grass based on the selected layer, but does not remove any existing grass." },
            { "applySubtractive", "Removes grass based on the selected layer, but does not affect grass outside of the splat map." },
            { "applyReplace", "Adds grass based on the selected layer, removing and overwriting existing grass." },
            { "paintGrassColor", "Click to paint color. Simple." },
            { "paintGrassDensity", "Click to fill in grass. Shift+Click to erase grass." },
            { "paintGrassHeight", "Click to raise grass. Shift+Click to lower grass." },
            { "paintGrassFlatness", "Click to flatten grass. Shift+Click to unflatten grass." },
            { "paintGrassWindStrength", "Click to increase wind strength. Shift+Click to decrease." },
            { "paintGrassType", "Click to paint which texture from the grass texture atlas (if using one) is shown. Shift+Click to paint first texture. Brush strength controls density of selected type." },
            { "paintToolColor", "Color" },
            { "paintToolDensity", "Density" },
            { "paintToolHeight", "Height" },
            { "paintToolFlatness", "Flatness" },
            { "paintToolWind", "Wind Strength" },
            { "paintToolType", "Grass Type" },
        };

        static Dictionary<string, string> _materialTooltips = new Dictionary<string, string>() {
            { "_Color", "Base color of the grass blades." },
            { "bladeHeight", "Overall height of the grass blades." },
            { "bladeWidth", "Overall width of the grass blades." },
            { "flatTint", "Color tint applied to flattened grass." },
            { "altCol", "Alternate color used for variation." },
            { "DISABLE_DECALS", "Disable decals on the grass." },
            { "_ReceiveShadows", "Whether the grass should receive shadows." },
            { "specTint", "Color tint for specular highlights." },
            { "_Metallic", "Controls the metallic look of the grass." },
            { "_Gloss", "Controls the glossiness of the grass for specular reflections." },
            { "_noiseScale2", "Scale of the secondary wind noise." },
            { "_noiseSpeed2", "Speed of the secondary wind noise." },
            { "occMult", "Strength of the occlusion map." },

            {"bladeOffset", "Adds a height offset to the position of the grass on the terrain, can be useful for fine tuning."},
            {"bladeSharp", "Controls sharpness of grass blades, 0 is perfect point, 1 is rectangular."},
            {"seekSun", "Controls how much the grass aligns to the surface normal. 0 aligns all the way, 1 points up."},
            {"topViewPush", "Attempts to add a slight offset to the grass when viewed from above which can help to give more depth and density when looking down."},
            {"flatnessMult", "Controls how \"flat\" the grass is pushed when using the flatness channel of the parameter map."},
            {"_BILLBOARD", "Whether or not the grass should always face the camera."},
            {"variance", "These four values control how randomized the grass is in certain ways. The values are: X = Position, Y = Height, Z = Color, W = Width"},

            //Lighting
            {"_ppLights","Calculate shading per pixel. Slightly slower, only really noticeable when using custom grass meshes but is required for normal mapping."},
            {"_AO", "Controls how dark the bottom of the grass blades are, 0 is darker, 1 is no darkness. "},
            {"ambientCO", "Controls how dark the shading can be."},
            {"ambientCOShadow", "On top of the light source shadow strength setting, this allows you to further tune received shadow strength."},
            {"edgeLight", "Controls strength of added brightness when the light direction is edge on to the grass blades."},
            {"edgeLightSharp", "Controls sharpness of the added edge on light brightness."},
            {"blendNormal", "Blends the mesh normals with the terrain surface normal. This allows for better control over shading and specular."},
            {"_GF_SPECULAR", "Enable specular highlights. Adds a small performance cost about 0.1ms in the worst case."},
            {"specSmooth", "Controls smoothness/blurryness of the surface for specular highlights/reflections."},
            {"specularMult", "Multiplier for specular highlight intensity."},
            {"specHeight", "Height adjustment for specular reflections, can be used to tune so that the base of grass doesn't have specular highlights."},
            {"_GF_NORMAL_MAP", "Enable normal mapping. Has a moderate performance cost, about 1ms in the worst case, 0.1ms in a reasonable case."},
            {"normalStrength", "Intensity of normal mapping effect."},
            {"bumpMap", "Texture to use for normal mapping."},

            //Self Shadow
            {"GF_SELF_SHADOW", "Enables a cheap technique to add fake shadows to grass without actually rendering shadows. This works basically by reprojecting your grass texture onto the grass from the perspective of the main light.\n" +
                "Assumes your grass mesh's vertices are -0.5 to 0.5 on the x/z axis, and 0 to 1 on the y axis.\n" +
                "Will look best with a cutout texture and grass cards."},
            {"selfShadowWind", "How much self shadow is modulated by wind to give it motion."},
            {"selfShadowScaleOffset", "(x,y): The scale applied to the shadow projection.\n(z,w): Offset applied to the shadow projection.\nYou can tweak these to fine tune the placement for your particular mesh."},


            //LOD
            {"_ALPHA_TO_MASK", "If enabled, AlphaToMask is turned on in the shader. And the performance of this is quite complicated. Sometimes grass looks better with it enabled and sometimes it doesn't."},
            {"widthLODscale", "Controls how the width of blades grows as distance from camera increases. This helps less grass cover the same area while not being very noticeable."},
            {"_GF_USE_DITHER", "Will dither the grass to further hide LOD transitions within a certain distance to camera, or always in deferred mode. " +
                "Most of the time it looks better with this on, but causes some artifacts that may not be desired." +
                "Leave this off unless you notice particularly bad popping on LOD transitions"},
            {"grassFade", "distance the grass visually fades at. NOTE: This does NOT control lod settings, those must be set separately from the GrassFlow component, this setting is visual only."},
            {"grassFadeSharpness", "Sharpness of the grass fade."},
            {"_LOD_SCALING", "Will vertically scale grass for LOD fade-in."},

            //wind
            {"windMult", "Overall wind strength multiplier."},
            {"windTint", "Color the grass is tinted when the wind affects them strongly, alpha controls strength."},
            {"_noiseScale", "Scale of the noise sampling for wind, Sort of controls wind gust size."},
            {"_noiseSpeed", "How fast the noise scrolls accross the grass to change wind patterns. Sort of acts like wind speed but you'll need to adjust wind strength to match."},
            {"windDir  ", "Direction the wind blows, the size of these values determines strength essentially."},
            {"windDir2", "Same as wind direction but controls secondary wind direction, helps give more variety to the wind instead of always being blown in one direction."},
            
            //bend
            {"_MULTI_SEGMENT", "Adds extra segments to each grass blade, allowing it to bend either from the wind, or from curvature. " +
                "The minimum and maximum number of segments can be changed by adjusting the number at the top of the GrassFlow/Shaders/GrassStructsVars.cginc file. " +
                "Based on the LOD settings the number of grass segments is reduced over distance."},
            {"bladeLateralCurve", "How much natural bend the grass has."},
            {"bladeVerticalCurve", "Sort've pulls the grass down towards the surface."},
            {"bladeStiffness", "Controls how much the grass bends in response to wind/ripples."},

            //maps and texturing
            {"_SEMI_TRANSPARENT", "Enables use of textures with alpha."},
            {"alphaLock", "Discards the alpha from the grass texture itself while still applying alpha clipping. Can be useful if your texture has bad alpha or you just don't want to use it."},
            {"alphaMult", "Multiplier for texture alpha, increasing this can allow you to fine tune your texture's alpha if it isn't sharp enough."},
            {"alphaClip", "Controls how sensitive the clipping of transparent textures is."},
            {"numTextures", "Set this to the number of textures in the type map texture atlas. Only used when using a type map."},
            {"textureAtlasScalingCutoff", "Texture index for the type map at which LOD width scaling is turned off. For example: set it to 3 and scaling would only apply to the first three textures in the atlas. " +
                "Only used when using a type map."},
            {"_SpecMap", "Specular map for deferred rendering."},
            {"_OccMap", "Occlusion map for deferred rendering. "},
            {"_MainTex", "Texture used to detail the grass blades/quads. This is the texture used for alpha clip. Can be a horizontal texture atlas used in combination with the type map, make sure to also set the number of textures property if so."},
            {"colorMap", "Color map for GrassFlow. Usually this is set by the GrassFlowRenderer, don't touch this unless you know what you're doing."},
            {"dhfParamMap", "Parameter map for GrassFlow. Usually this is set by the GrassFlowRenderer, don't touch this unless you know what you're doing."},
            {"typeMap", "Type map for GrassFlow. Usually this is set by the GrassFlowRenderer, don't touch this unless you know what you're doing."},


            //optimization
            {"_Cull", "Culling mode for rendering. You may want it set to 'off' if your mesh has double-sided 'gons. Otherwise most of the time you'll probably just want this on backface culling since it's most efficient."},
            {"MESH_COLORS", "Enables use of custom vertex colors on your mesh to determine sensetivity to wind. The red channel of the color is used."},
            {"MESH_NORMALS", "Enables use of the normals on your mesh, otherwise the normal of the terrain is used. For simple grass cards you likely don't want this enabled anyway."},
            {"MESH_UVS", "Enables use of the UVs on your mesh, used for texturing. You'll almost always want this on, but you may as well turn it off if you don't put a texture on your grass."},
            {"MAP_COLOR", "Enables ability to paint the color map at runtime. Otherwise the color is baked in. Enabling this uses an extra texture sample in the shader. Best to leave off for mobile."},
            {"MAP_PARAM", "Enables ability to paint the param map at runtime. Otherwise the values are baked in. Enabling this uses an extra texture sample in the shader. Best to leave off for mobile."},
            {"MAP_TYPE",  "Enables ability to paint the type map at runtime. Otherwise the values are baked in. Enabling this uses an extra texture sample in the shader. Best to leave off for mobile."},

            {"GRASS_RIPPLES",  "Enables ability to receive ripples. This can be expensive particularly on mobile because it requires reading from a buffer, even if you're not using ripples, so best to leave off if you don't need it."},
            {"GRASS_FORCES",  "Allows multiple forces on the grass, this can be expensive particularly on mobile because it requires reading from a buffer. If off, one force can still be applied to the grass which is best used for a main character."},
        };

        static readonly Dictionary<string, string> _messages = new Dictionary<string, string> {
            { "urpDetected", "URP project detected." },
            { "enableURPSupport", "Enable URP support?" },
            { "hdrpDetected", "HDRP project detected." },
            { "enableHDRPSupport", "Enable HDRP support?" },
            { "urpModeActive", "GrassFlow is in URP mode." },
            { "hdrpModeActive", "GrassFlow is in HDRP mode." },
            { "enjoyingGrassFlow", "⚠ Enjoying GrassFlow? : " },
            { "leaveReview", "Leave a Review" },
            { "dismiss", "Dismiss" },
            { "multipleRenderersWarning", "There are multiple GrassFlowRenderers in the scene!.\nYou should only use one renderer and assign all of your terrains/meshes to that single instance.\nNot doing so will cause performance and rendering issues." },
            { "terrainTransformMissing", "Terrain Transform Missing." },
            { "grassMeshMissing", "Grass Mesh Missing." },
            { "terrainMissing", "Terrain Missing." },
            { "paintTextureMissing", "Texture for the selected paint type is missing." },
            { "grassMaterialMissing", "Grass Material Missing." },
            { "customMeshMissing", "No custom mesh set in renderer component." },
            { "undoRevertMaps", "GrassFlow Revert Maps" },
            { "undoChangeVariable", "GrassFlow Change Variable" },
            { "undoAddGrassMesh", "Add Grass Mesh" },
            { "undoAddFromSelection", "Add Grass Meshes from Selection" },
            { "undoDeleteGrassMesh", "Delete Grass Mesh" },
            { "undoSelectGrassMesh", "Select GrassFlow Mesh" },
            { "undoSetDetailMap", "GrassFlow Set Detail Map" },
            { "undoChangeBrush", "GrassFlow Change Brush" },
            { "missingTerrainLayers", "Terrain has missing layers! Check your terrain object for issues." },
            { "noSplatLayers", "No splat layers on the terrain." },
            { "assignTerrainObject", "Please assign terrain object in settings." },
            { "undoPaint", "GrassFlow Paint" },
            { "errorPaintTextureMissing", "GrassFlow: Texture for selected paint mode not set." },
            { "errorCantSaveMapNoFile", "Cant save texture map! Probably because it has no file." },
            { "errorMapNotPng", "Detail maps need to be .png format!" },
            { "saveDialogTitle", "GrassFlow" },
            { "saveDialogMessage", "GrassFlow detail map(s) have been modified.\nSave changes?\n\nThis CANNOT be un-done." },
            { "saveDialogYes", "Yes" },
            { "saveDialogNo", "No" },
            { "undoChangePaintTool", "GrassFlow Change Paint Tool" },

            
			//--------------------------------------------------------------------------------
			//MISC---------------------------------------------------------------
			//--------------------------------------------------------------------------------
			{ "languageSelectorTitle", "Grassflow Language Selector" },
            { "selectLanguagePrompt", "Please select a language:" },
            { "languageLabel", "Language:" },
            { "confirmSelectionButton", "Confirm Selection" },
        };
    }
}