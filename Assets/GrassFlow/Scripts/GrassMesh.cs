
namespace GrassFlow {

    using System;
    using System.Linq;
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using System.Threading.Tasks;

    using Object = UnityEngine.Object;
    using Action = System.Action;
    using GrassRenderType = GrassFlowRenderer.GrassRenderType;
    using CullChunk = CullingZone.CullChunk;
    using CullResults = CullingZone.CameraCullResults;
    using CullGrassMesh = CullingZone.CullGrassMesh;
    using MeshChunk = GrassFlow.MeshChunker.MeshChunk;
    using UnityEngine.Rendering;
    using GrassFlow;

    [Serializable]
    public class GrassMesh {



        //serialized
        public GrassFlowRenderer owner;

        [GFToolTip("Enables a partially multithreaded asynchronus execution of the initial processing that can slightly reduce load times if you have a large mesh. " +
            "The downside of this is that the game might start before the grass is loaded.")]
        public bool asyncInitialization = false;

        [GFToolTip("Receive shadows on the grass. Can be expensive, especially with cascaded shadows on. (Requires the grass shader with depth pass to render properly)")]
        public bool receiveShadows = true;

        [InspectorName("Rendering Layer Mask")]
        [GFToolTip("URP/HDRP rendering layer mask.")]
        public uint renderingLayer = 1;

        [SerializeField] private bool _castShadows = false;
        [SerializeField] public ShadowCastingMode shadowMode;
        [GFToolTip("Grass casts shadows. Fairly expensive option. (Also requires the grass shader with depth pass to render at all)")]
        public bool castShadows {
            get { return _castShadows; }
            set {
                _castShadows = value;
                shadowMode = value ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
        }

        [SerializeField] private int _instanceCount = 30;
        [GFToolTip("This value is factored in with 'Grass Per Instance' and the number of triangles in the underlying source geometry " +
            "to calculate the total possible instances that can be rendered.")]
        public int instanceCount {
            get { return _instanceCount; }
            set {
                bool diff = _instanceCount != value;
                _instanceCount = value;
                if (_instanceCount <= 0) _instanceCount = 1;

                _ = UpdateTransform(false);

                if (diff && hasRequiredAssets) {
                    _ = MeshChunker.RefreshPosBufferInitialize(this);
                }
            }
        }

        [GFToolTip("Number of steps that grass density can decrease by with the LOD system to decrease number of instances in the distance.\n" +
            "IMPORTANT: Make sure to have this high enough as this setting also controls the minimum amount of grass that can be shown. " +
            "i.e. the higher this setting, a smaller amount of grass can shown in the distance.")]
        public int lodSteps = 30;

        [Serializable]
        public class CustomMeshLod {
            [InspectorName("Mesh"), GFToolTip("Rendered mesh for this LOD.")]
            [SerializeField] private Mesh mesh;
            [GFToolTip("Material used to render grass for this LOD.")]
            [SerializeField] private Material mat;
            [InspectorName("Distance"), GFToolTip("Distance at which to start rendering this LOD.")]
            public float distance;

            //this is so dumb
            public Mesh lodMesh {
                get => mesh;
                set {
                    hasMesh = value;
                    mesh = value;
                    hasDrawnMesh = false;
                }
            }

            //this is so dumb
            public Material lodMat {
                get => mat;
                set {
                    hasMat = value;
                    mat = value;
                }
            }

            [HideInInspector] public Material drawnMat;
            [HideInInspector] public Mesh drawnMesh;
            [HideInInspector] public bool hasMesh; //this is so dumb
            [HideInInspector] public bool hasMat; //this is so dumb
            [HideInInspector] public bool hasGeneratedMesh; //this is so dumb
            [HideInInspector] public bool hasDrawnMesh; //this is so dumb
            [HideInInspector] public float invVertCount;
        }

        public void UpdateDeprecatedCustomMesh() {
            customMeshLods = new CustomMeshLod[]{
                new CustomMeshLod(){
                    lodMesh = customMesh ? customMesh : Resources.GetBuiltinResource<Mesh>("Quad.fbx"),
                    lodMat = grassMaterial
                }
            };

            _instanceCount *= grassPerTri;
        }


        [GFToolTip("Mode this grass is for. Mesh will attach grass to the triangles of a mesh, terrain will attach grass to surface of a unity terrain object.")]
        public GrassRenderType renderType;

        [InspectorName("Terrain Mesh"), GFToolTip("Mesh to attach grass to in mesh mode.")]
        public Mesh grassMesh;

        [SerializeField] private Mesh customMesh;

        [InspectorName("Grass Lods"), GFInline(false), GFToolTip("Meshes, materials, and distances to use when rendering grass. Individual lods only allowed when not using frustum culling.")]
        public CustomMeshLod[] customMeshLods;

        [GFToolTip("If enabled, LODs will be draw with some overlap and faded between to smooth out the transition.\n" +
            "Has some performance impact as chunks will be drawn twice during the overlap fade range, and currently fading chunks cannot be batched.\n" +
            "NOTE: Dithering must be enabled on the materials used for the LODs.")]
        public bool crossFade = false;

        public float crossFadeRange = 5;

        public CustomMeshLod[] EnsureMeshLods() {
            if (customMeshLods == null || customMeshLods.Length == 0) {
                customMeshLods = new CustomMeshLod[] { new CustomMeshLod() };
            }
            return customMeshLods;
        }
        [GFToolTip("Mesh used to render grass.")]
        public Mesh customGrassMesh {
            get {
                EnsureMeshLods();
                return customMeshLods[0].lodMesh;
            }
            set => customMeshLods[0].lodMesh = value;
        }

        [GFToolTip("Terrain object to attach grass to in terrain mode.")]
        public Terrain terrainObject;

        [GFToolTip("Transform that the grass belongs to.")]
        public Transform terrainTransform;

        [GFToolTip("Material to use to render the grass. The material should use one of the grassflow shaders.")]
        [SerializeField] private Material grassMaterial;

        [GFToolTip("Texture that controls grass color. The alpha channel of this texture is used to control how the color gets applied. " +
        "If alpha is 1, the color is also multiplied by material color, if 0, material color is ignored. Inbetween values work too.")]
        public Texture colorMap;

        [GFToolTip("Texture that controls various parameters of the grass. Red channel = density. Green channel = height, Blue channel = flattenedness. Alpha channel = wind strength.")]
        public Texture paramMap;

        [GFToolTip("Texture that controls which texture to use from the atlas in the grass texture atlas (if using one). " +
            "NOTE: Read the documentation for information about how this texture works.")]
        public Texture typeMap;

        [InspectorName("Grass Per Instance"), GFToolTip("If unsure, leave at 1.\n" +
            "Number of times to duplicate rendered mesh geometry. Basically makes it so that more geometry is 'real' Vs. instanced.\n" +
            "There's a certain threshhold of real vs instanced geometry that is fastest, so you'll just have to play around with it to see what is good for you.\n" +
            "NOTE: May or may not help on mobile.\n" +
            "NOTE: Setting this too high may cause weird lod popping and should be set to 1 for frustum culling.")]
        public int grassPerTri = 1;

        [GFToolTip("Base level of grass to render in terrain mode. This amount will be multiplied by instance count to control LOD falloff.")]
        public const int terrainGrassDensity = 12;


        [GFToolTip("Maximum ratio at which the largest triangle can be subdivided. Basically it just controls the subdivision density when attempting to normalize the mesh. " +
            "You probably want to set this as low as possible while still providing good results.")]
        public float normalizeMaxRatio = 12f;

        [GFToolTip("-1 to 1 angle threshhold for spawning grass on terrain compare to the up direction (0, 1, 0).")]
        public float terrainSlopeThresh = 0f;

        [GFToolTip("Distance from the terrain slope thresh at which grass will be scaled to 'fade' out.")]
        public float terrainSlopeFade = 0f;

        public int chunksX = 5;
        public int chunksY = 1;
        public int chunksZ = 5;

        public Vector3 _lodParams = new Vector3(15, 1.1f, 0);
        [GFToolTip("Controls the LOD parameter of the grass. X = render distance. Y = density falloff sharpness (how quickly the amount of grass is reduced to zero). " +
        "Z = offset, basically a positive number prevents blades from popping out within this distance.")]
        public Vector3 lodParams {
            get { return _lodParams; }
            set {
                _lodParams = value;

                foreach (var lod in customMeshLods) if (lod.drawnMat) lod.drawnMat.SetVector(_LODID, value);
            }
        }

        public float maxRenderDistSqr = 150f * 150f;

        [SerializeField] float _maxRenderDist = 150f;
        [GFToolTip("Controls max render dist of the grass chunks. This value is mostly just used to quickly reject far away chunks for rendering.")]
        public float maxRenderDist {
            get { return _maxRenderDist; }
            set {
                _maxRenderDist = value;
                maxRenderDistSqr = value * value;
            }
        }


        [GFToolTip("Usefull if you're rendering grass onto a mesh that doesn't just face up, i.e a sphere," +
            "as grass can end up pointing in any direction, the bounds of the chunk need to be expanded by the maximum " +
            "potential height of the grass.\n" +
            "But since most terrains will only have upward facing grass, it can be more optimal to not expand the bounds " +
            "in every direction.\n" +
            "Note that even when this is disabled, the bounds are still expanded vertically."),
            InspectorName("Expand Bounds by Grass Height")]
        public bool expandBounds = false;


        //frustum cull params
        [GFToolTip("Whether or not to use frustum culling (discard grass outside of camera view) for grass. Uses additional VRAM. Generally this doesn't help performance much unless rendering high chunk counts, " +
            "and using this can cause issues with shadows as it's easy for grass outside the view of the camera to cast " +
            "a shadow into the view of the camera.\n" +
            "If this is off, Unity will simply handle culling on a per-chunk basis and render each thread individually.\n" +
            "If this is on, a compute shader manually culls each grass instance, then one draw call is issued to render everything in one go.")]
        public bool frustumCull;
        [GFToolTip("Threshholds for horiztonal and vertical view to determine how far outside the cameras view grass must be to be culled. " +
            "Generally these should be set as low as possible without being able to see grass pop out at the edges of the view.")]
        public Vector2 frustumCullThresh = new Vector2(1.2f, 1.5f);

        [GFToolTip("If using a parameter map, this will only generate grass based on the density channel. " +
            "This is significantly more efficent, with the only caveat that grass density cannot be dynamically painted at runtime.")]
        public bool bakeDensity = true;

        [GFToolTip("Increases memory cost (by 37%), but is more efficient when using color/param/type maps.\n" +
            "If you disable this and are using maps, you should enable the dynamic map settings on the material for those maps.\n" +
            "IMPORTANT: Frustum culling is currently incompatible with this setting being off.")]
        public bool bakeData = true;

        //nonserialized
        //[NonSerialized] public ComputeBuffer posBuffer;

        //we need this because the shader will complain if we dont set the posbuffer at all even though we just want the count
        [NonSerialized] public ComputeBuffer dummyPosBuffer;

        [NonSerialized] public ComputeBuffer chunkCountBuff;

        [NonSerialized] public ComputeKernel meshPosKernel;
        [NonSerialized] public ComputeKernel terrPosKernel;

        //its kinda dumb we gotta do this but for memory reasons we def need a way of splitting up the data and loading it based on camera position
        public class SubGrassMesh {
            public GrassMesh gMesh;
            public MeshChunk[] chunks;
            public Bounds bounds;

            public MaterialPropertyBlock pBlock;

            public int totalInstances = -1;
            public bool loaded;
            public bool shouldLoad;
            public bool shouldUnload;
            public int framesInvisible;
            public int currentMemUsage;
            public int frustumMem;


            ComputeBuffer _posBuffer;
            public ComputeBuffer posBuffer {
                get => _posBuffer;
                set {
                    _posBuffer = value;
                    if (_posBuffer != null) {
                        pBlock.SetBuffer(grassPosBufferID, _posBuffer);
                        currentMemUsage = _posBuffer.stride * _posBuffer.count;
                    }
                    else {
                        currentMemUsage = 0;
                    }
                }
            }

            void RefreshInspector() {
#if UNITY_EDITOR
                if (!Application.isPlaying) {
                    UnityEditor.EditorUtility.SetDirty(gMesh.owner);
                }
#endif
            }

            public void HandleLoadUnload() {
                if (shouldLoad) {
                    _ = MeshChunker.RefreshPosBuffer(this);
                    shouldLoad = false;
                    loaded = true;

                    //Debug.Log("load");
                    RefreshInspector();
                }
                else if (shouldUnload) {
                    if (posBuffer != null) {
                        posBuffer = posBuffer.ReleaseIfValid();

                        CullingZone.ReleaseResourcesForSubMesh(this);

                        //Debug.Log("unload");
                        RefreshInspector();
                    }
                    shouldUnload = false;
                    loaded = false;
                }
            }

            public static implicit operator GrassMesh(SubGrassMesh s) => s.gMesh;
        }

        [GFToolTip("Material used to render grass. Should use one of the GrassFlow shaders.")]
        public Material mainGrassMat {
            get {
                EnsureMeshLods();
                return customMeshLods[0].lodMat;
            }
            set => customMeshLods[0].lodMat = value;
        }

        public Material mainDrawnMat {
            get {
                EnsureMeshLods();
                return customMeshLods[0].drawnMat;
            }
            set => customMeshLods[0].drawnMat = value;
        }

        [NonSerialized] public bool shouldDraw;
        [NonSerialized] public int grassIdx;


        public float maxVertexHeight;
        [NonSerialized] public Mesh chunkedMesh;
        [NonSerialized] public MeshChunk[] chunks;
        [NonSerialized] public MeshChunk[,,] chunks3D; //only used for convenience
        [NonSerialized] public SubGrassMesh[] subGrassMeshes;

        [NonSerialized] public RenderTexture terrainHeightmap;
        [NonSerialized] public Texture terrainHoleMap;

        [NonSerialized] public RenderTexture colorMapRT;
        [NonSerialized] public RenderTexture paramMapRT;
        [NonSerialized] public RenderTexture typeMapRT;


        public ComputeBuffer vertexDataBuffer;

        [NonSerialized] public int bufferMem;


        [NonSerialized] public Mesh paintMesh;
        public Bounds worldBounds;

        public Vector2 colorMapHalfPixUV;
        public Vector2 paramMapHalfPixUV;
        public Vector2 typeMapHalfPixUV;

        public string name {
            get {
                return (terrainTransform ? terrainTransform.name : "No Transform") + " : " +
                    (renderType == GrassRenderType.Mesh ? (grassMesh ? grassMesh.name : "No Mesh") : (terrainObject ? terrainObject.name : "No Terrain")) + " : " +
                    (mainGrassMat ? mainGrassMat.name : "No Mat");
            }
        }

        public bool hasLodAssets() {
            bool sharedAssets = true;
            foreach (var lod in customMeshLods) {
                sharedAssets &= lod.hasMat = lod.lodMat;
                sharedAssets &= lod.hasMesh = lod.lodMesh;
            }
            return sharedAssets;
        }

        public bool hasRequiredAssets {
            get {
                bool sharedAssets = terrainTransform;

                EnsureMeshLods();
                sharedAssets &= hasLodAssets();

                if (renderType == GrassRenderType.Mesh) {
                    sharedAssets &= grassMesh;
                }
                else {
                    sharedAssets &= terrainObject;
                }

                return sharedAssets;
            }
        }


        public void Dispose() {
            ReleaseDetailMapRTs();
            ReleaseAssets();
            foreach (var lod in customMeshLods) lod.drawnMat = null;
            Destroy(chunkedMesh);
            chunkedMesh = null;
            paintMesh = null;
        }

        void Destroy(Object obj) {
            if (Application.isPlaying) {
                Object.Destroy(obj);
            }
            else {
                Object.DestroyImmediate(obj);
            }
        }


        //used internally by the inspector to refresh current mesh after changing settings
        /// <summary>
        /// Force a full reload of this GrassMesh.
        /// </summary>
        public async void Reload() {
            shouldDraw = false;
            CullingZone.ClearCulledChunks();
            ReleaseAssets();
            GetResources(true);
            MapSetup();
            await owner.LoadChunksForMesh(this);
        }

        /// <summary>
        /// Same as RefreshPosBuffer, except it forces the grass buffer to be regenerated and recounted, and for ALL submeshes, not just loaded ones.
        /// It's slower, but will re-bake grass density properly.
        /// </summary>
        public async Task RefreshSubMeshes() {

            if (subGrassMeshes == null) return;

            await MeshChunker.RefreshPosBufferInitialize(this);

            //foreach (var sub in subGrassMeshes) {
            //    if (sub.loaded) {
            //        sub.totalInstances = -1; //force counts to be regenerated
            //        await MeshChunker.RefreshPosBuffer(sub, false, canBakeDensity);
            //    }
            //}
        }

        /// <summary>
        /// <para>Refreshes the positional data for each LOADED submesh in this GrassMesh.
        /// This will reuse compute buffers and NOT recalculate anything else.</para> 
        /// <para>This is the fastest way to refresh the grass positions, but is only really useful for updating if a transform has moved or terrain heightmap changes.
        /// Also worth noting that pre-multiplied world positions for grass can be disabled by commented the line at the top of GrassFlowCompute.compute, 
        /// which will allow grass to follow the transform without extra updates, at the cost of an extra matrix multiplication in the shader.</para>
        /// Mesh vertices are cached and will not be reflected by this refresh, 
        /// you would need to implement a way of setting those buffers yourself before calling this if you want that functionality, 
        /// or use the slower reload function to do a full reset.
        /// <para>This also does not re-count grass, so things will not be reflected properly if the density channel of the parameter map has changed and you are using baked density (Which you should be [probably]).</para>
        /// </summary>
        public void RefreshPosBuffer() {
            foreach (var sub in subGrassMeshes) {
                if (sub.loaded) {
                    _ = MeshChunker.RefreshPosBuffer(sub, false);
                }
            }
        }

        public void RefreshDetailMaps() {
            ReleaseDetailMapRTs();
            MapSetup();
        }

        public void ReleaseDetailMapRTs() {
            if (colorMapRT) Destroy(colorMapRT); colorMapRT = null;
            if (paramMapRT) Destroy(paramMapRT); paramMapRT = null;
            if (typeMapRT) Destroy(typeMapRT); typeMapRT = null;
        }

        const GraphicsDeviceType openGL = GraphicsDeviceType.OpenGLCore | GraphicsDeviceType.OpenGLES2 | GraphicsDeviceType.OpenGLES3;

        public void MapSetup() {

            //basically calculate what a half pixel offset would be in UV space
            if (colorMap) colorMapHalfPixUV = new Vector2(1f / colorMap.width, 1f / colorMap.height) * 0.5f;
            if (paramMap) paramMapHalfPixUV = new Vector2(1f / paramMap.width, 1f / paramMap.height) * 0.5f;
            if (typeMap) typeMapHalfPixUV = new Vector2(1f / typeMap.width, 1f / typeMap.height) * 0.5f;

            if (!owner.enableMapPainting) return;
            CheckMap(colorMap, ref colorMapRT, GrassFlowRenderer.useFloatFormatColorMap ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32);
            CheckMap(paramMap, ref paramMapRT, GrassFlowRenderer.useFloatFormatParam ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32);

            RenderTextureFormat typeFormat = openGL.HasFlag(SystemInfo.graphicsDeviceType) ? RenderTextureFormat.ARGB32 : RenderTextureFormat.R8;
            CheckMap(typeMap, ref typeMapRT, typeFormat);
        }

        void CheckMap(Texture srcMap, ref RenderTexture outRT, RenderTextureFormat format) {
            if (srcMap && !outRT) {
                RenderTexture oldRT = RenderTexture.active;
                outRT = new RenderTexture(srcMap.width, srcMap.height, 0, format, RenderTextureReadWrite.Linear) {
                    enableRandomWrite = true, filterMode = srcMap.filterMode, wrapMode = srcMap.wrapMode, name = srcMap.name + "RT"
                };
                outRT.Create();
                Graphics.Blit(srcMap, outRT);
                RenderTexture.active = oldRT;
            }
        }

        public void ReleaseAssets() {
            terrainHeightmap = null;
            terrainHoleMap = null;

            if (chunks != null) {
                foreach (var chunk in chunks) {
                    chunk.triBuff = chunk.triBuff.ReleaseIfValid();
                }
            }

            chunkCountBuff = chunkCountBuff.ReleaseIfValid();
            vertexDataBuffer = vertexDataBuffer.ReleaseIfValid();
            dummyPosBuffer = dummyPosBuffer.ReleaseIfValid();

            if (subGrassMeshes != null) {
                foreach (var sub in subGrassMeshes) {
                    sub.posBuffer = sub.posBuffer.ReleaseIfValid();
                }
            }


            foreach (var lod in customMeshLods) {
                if (lod.drawnMesh && lod.lodMesh && lod.drawnMesh != lod.lodMesh && lod.hasGeneratedMesh) {
                    Destroy(lod.drawnMesh);
                    lod.hasGeneratedMesh = false;
                }
                lod.hasDrawnMesh = false;
            }

            meshPosKernel = null;
            terrPosKernel = null;
        }

        public void UpdateMaps(bool enableMapPainting) {
            foreach (var lod in customMeshLods) {
                if (enableMapPainting) {
                    if (colorMapRT) lod.drawnMat.SetTexture(colorMapID, colorMapRT);
                    if (paramMapRT) lod.drawnMat.SetTexture(dhfParamMapID, paramMapRT);
                    if (typeMapRT) lod.drawnMat.SetTexture(typeMapID, typeMapRT);
                }
                else {
                    if (colorMap) lod.drawnMat.SetTexture(colorMapID, colorMap);
                    if (paramMap) lod.drawnMat.SetTexture(dhfParamMapID, paramMap);
                    if (typeMap) lod.drawnMat.SetTexture(typeMapID, typeMap);
                }
            }
        }

        //kinda dumb legacy code stuff but needs to work and is used for painting still idk
        public void UpdateTerrain() {

            if (!terrainObject) return;
            if (chunks == null) return;

            foreach (MeshChunk chunk in chunks) {

                var pBlock = chunk.pBlock;

                if (terrainHeightmap) pBlock.SetTexture(terrainHeightMapID, terrainHeightmap);

                Vector3 terrainScale = terrainObject.terrainData.size;
                pBlock.SetVector(terrainSizeID, new Vector4(terrainScale.x, terrainScale.y, terrainScale.z));
                //use the inverse terrain XZ scale  here to save using divisions in the shader
                pBlock.SetVector(invTerrainSizeID, new Vector4(1f / terrainScale.x, 1f / terrainScale.y, 1f / terrainScale.z));
                pBlock.SetVector(terrainChunkSizeID, new Vector4(terrainScale.x / chunksX, terrainScale.z / chunksZ));
                pBlock.SetFloat(terrainExpansionID, owner.terrainExpansion);

                //offset by half a pixel so it aligns properly
                if (terrainHeightmap) {
                    pBlock.SetFloat(terrainMapOffsetID, 1f / terrainHeightmap.width * 0.5f);
                }

                pBlock.SetVector(_chunkPosID, chunk.chunkPos);
            }



            //if (terrainHeightmap) drawnMat.SetTexture(terrainHeightMapID, terrainHeightmap);
            //if (owner.useTerrainNormalMap && terrainNormalMap) drawnMat.SetTexture(terrainNormalMapID, terrainNormalMap);
            //else drawnMat.SetTexture(terrainNormalMapID, null);

            //Vector3 terrainScale = terrainObject.terrainData.size;
            //drawnMat.SetVector(terrainSizeID, new Vector4(terrainScale.x, terrainScale.y, terrainScale.z));
            ////use the inverse terrain XZ scale  here to save using divisions in the shader
            //drawnMat.SetVector(invTerrainSizeID, new Vector4(1f / terrainScale.x, 1f / terrainScale.y, 1f / terrainScale.z));
            //drawnMat.SetVector(terrainChunkSizeID, new Vector4(terrainScale.x / chunksX, terrainScale.z / chunksZ));
            //drawnMat.SetFloat(terrainExpansionID, owner.terrainExpansion);

            ////offset by half a pixel so it aligns properly
            //if (terrainHeightmap) {
            //    drawnMat.SetFloat(terrainMapOffsetID, 1f / terrainHeightmap.width * 0.5f);
            //}
        }




        public async Task Update(bool isAsync) {
            GetResources(true);
            owner.UpdateShader(this);
            UpdateTerrain();
            await UpdateTransform(isAsync);
        }

        public async Task UpdateTransform(bool isAsync) {

            if (!terrainTransform || chunks == null) return;

            Matrix4x4 tMatrix = terrainTransform.localToWorldMatrix;
            Vector3 pos = terrainTransform.position;

            SetDrawmatObjMatrices();

            Action asyncAction = new Action(() => {
                CalcWorldBounds(renderType, tMatrix, pos);
            });
            if (isAsync) await Task.Run(asyncAction); else asyncAction();

        }

        public void UpdateMaxVertHeight() {
            var cMesh = customGrassMesh;
            if (!cMesh) return;
            var cVerts = cMesh.vertices;
            float maxH = 1f / cVerts.Max(x => x.y);
            maxVertexHeight = maxH;
        }

        public void CalcWorldBounds(GrassRenderType renderType, Matrix4x4 tMatrix, Vector3 terrainPos) {


            for (int i = 0; i < chunks.Length; i++) {

                var chunk = chunks[i];

                if (renderType == GrassRenderType.Mesh) {

                    //need to transform the chunk bounds to match the new matrix
                    //kinda dumb and inefficient but its the easiest way to make sure
                    //the bounds encapsulate the mesh if its been rotated
                    Vector3 size = chunk.meshBounds.extents;
                    chunk.worldBounds.extents = Vector3.zero;

                    chunk.worldBounds.center = (tMatrix.MultiplyPoint3x4(new Vector3(size.x, size.y, size.z)));
                    chunk.worldBounds.Encapsulate(tMatrix.MultiplyPoint3x4(new Vector3(-size.x, size.y, size.z)));
                    chunk.worldBounds.Encapsulate(tMatrix.MultiplyPoint3x4(new Vector3(size.x, -size.y, size.z)));
                    chunk.worldBounds.Encapsulate(tMatrix.MultiplyPoint3x4(new Vector3(size.x, size.y, -size.z)));
                    chunk.worldBounds.Encapsulate(tMatrix.MultiplyPoint3x4(new Vector3(-size.x, -size.y, size.z)));
                    chunk.worldBounds.Encapsulate(tMatrix.MultiplyPoint3x4(new Vector3(size.x, -size.y, -size.z)));
                    chunk.worldBounds.Encapsulate(tMatrix.MultiplyPoint3x4(new Vector3(-size.x, size.y, -size.z)));
                    chunk.worldBounds.Encapsulate(tMatrix.MultiplyPoint3x4(new Vector3(-size.x, -size.y, -size.z)));

                    chunk.worldBounds.center = tMatrix.MultiplyPoint3x4(chunk.meshBounds.center);

                    //Vector3 ext = tMatrix.MultiplyVector(chunk.meshBounds.extents);
                    //float maxExt = Mathf.Max(
                    //    Mathf.Abs(ext.x),
                    //    Mathf.Abs(ext.y),
                    //    Mathf.Abs(ext.z)
                    //);
                    //chunk.worldBounds.extents = new Vector3(maxExt, maxExt, maxExt);


                }
                else {
                    chunk.worldBounds.center = chunk.meshBounds.center + terrainPos;
                }

                if (i == 0) {
                    worldBounds = chunk.worldBounds;
                }
                else {
                    worldBounds.Encapsulate(chunk.worldBounds);
                }
            }
        }


        public void GetResources(bool refreshMat = false) {

            foreach (var lod in customMeshLods) {
                var gMat = lod.lodMat;

#if UNITY_EDITOR
                if (!Application.isPlaying) {
                    if (ShaderVariantHelper.CheckShaderNeedsRecompilation(gMat)) {
                        ShaderVariantHelper.HandleVariantGuiAndCompilation(owner, gMat, 0, true, false);
                    }
                }
#endif
                if (!lod.drawnMat || refreshMat) {
                    lod.drawnMat = owner.useMaterialInstance ? GameObject.Instantiate(gMat) : gMat;
                }
            }

            owner.UpdateShader(this);

            SetKeyword("FRUSTUM_CULLED", frustumCull);
            SetKeyword("BAKED_DATA", bakeData);

#if UNITY_EDITOR
            SetKeyword("USE_MAPS_OVERRIDE", GrassFlowRenderer.isPaintingOpen);
#endif

#if GRASSFLOW_SRP
            foreach (var lod in customMeshLods) {
                var gMat = lod.drawnMat;
                if (gMat) {
                    SetKeyword("_RECEIVE_SHADOWS_OFF", !receiveShadows);
                    lod.drawnMat.SetFloat("_ReceiveShadows", receiveShadows ? 1 : 0);
                    lod.lodMat.SetFloat("_ReceiveShadows", receiveShadows ? 1 : 0);
                }
            }
#endif
        }

        public void SetKeyword(string kw, bool state) {
            if (state) {
                EnableKeyword(kw);
            }
            else {
                DisableKeyword(kw);
            }
        }

        public void EnableKeyword(string kw) {
            foreach (var lod in customMeshLods) {
                if (lod.drawnMat) {
                    lod.drawnMat.EnableKeyword(kw);
                }
                if (lod.lodMat) {
                    lod.lodMat.EnableKeyword(kw);
                }
            }
        }
        public void DisableKeyword(string kw) {
            foreach (var lod in customMeshLods) {
                if (lod.drawnMat) {
                    lod.drawnMat.DisableKeyword(kw);
                }
                if (lod.lodMat) {
                    lod.lodMat.DisableKeyword(kw);
                }
            }
        }

        public void SetDrawmatObjMatrices() {
            foreach (var lod in customMeshLods) {
                if (lod.drawnMat) {
                    lod.drawnMat.SetMatrix(objToWorldMatrixID, terrainTransform.localToWorldMatrix);
                    lod.drawnMat.SetMatrix(worldToObjMatrixID, terrainTransform.worldToLocalMatrix);
                }
            }
        }

        public GrassMesh Clone() {
            var gMesh = MemberwiseClone() as GrassMesh;

            gMesh.grassMesh = null;
            gMesh.terrainObject = null;
            gMesh.chunkedMesh = null;
            gMesh.paintMesh = null;
            gMesh.shouldDraw = false;

            gMesh.customMeshLods = new CustomMeshLod[customMeshLods.Length];
            for (int i = 0; i < customMeshLods.Length; i++) {
                var lod = customMeshLods[i];
                var newLod = new CustomMeshLod();
                newLod.lodMat = lod.lodMat;
                newLod.lodMesh = lod.lodMesh;
                newLod.distance = lod.distance;
                gMesh.customMeshLods[i] = newLod;
            }

            return gMesh;
        }

        //shader prop IDs
        static int grassPosBufferID = Shader.PropertyToID("grassPosBuffer");
        static int _LODID = Shader.PropertyToID("_LOD");
        static int _chunkPosID = Shader.PropertyToID("_chunkPos");

        static int colorMapID = Shader.PropertyToID("colorMap");
        static int dhfParamMapID = Shader.PropertyToID("dhfParamMap");
        static int typeMapID = Shader.PropertyToID("typeMap");

        static int objToWorldMatrixID = Shader.PropertyToID("objToWorldMatrix");
        static int worldToObjMatrixID = Shader.PropertyToID("worldToObjMatrix");

        static int terrainHeightMapID = Shader.PropertyToID("terrainHeightMap");
        static int terrainSizeID = Shader.PropertyToID("terrainSize");
        static int invTerrainSizeID = Shader.PropertyToID("invTerrainSize");
        static int terrainChunkSizeID = Shader.PropertyToID("terrainChunkSize");
        static int terrainExpansionID = Shader.PropertyToID("terrainExpansion");
        static int terrainMapOffsetID = Shader.PropertyToID("terrainMapOffset");

        public static implicit operator bool(GrassMesh gMesh) => gMesh != null;
    }

    public class GFInlineAttribute : PropertyAttribute {

        public bool useLabel = false;

        public GFInlineAttribute(bool drawLabel = false) {
            useLabel = drawLabel;
        }
    }

    public class GFToolTipAttribute : Attribute {

        public string tooltip;
        public GFToolTipAttribute(string tooltip) {
            this.tooltip = tooltip;
        }
    }
}