
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System.Linq;
using UnityEngine.UI;
using System.Runtime.InteropServices;
using GrassFlow;
using System.Threading.Tasks;
using Unity.Collections.LowLevel.Unsafe;


namespace GrassFlow {

    [ExecuteInEditMode]
    [AddComponentMenu("Rendering/GrassFlow Renderer")]
    [HelpURL("https://boltmanguy.github.io/GrassFlowDocs")]
    public class GrassFlowRenderer : MonoBehaviour {



        [GFToolTip("This setting only effects the editor. Most of the time you're going to want this on as it prevents visual popping as scripts are recompiled and such. " +
            "You can turn it off to get a more accurate view of game performance, though really it hardly makes any difference.")]
        public bool updateBuffers = true;

        [GFToolTip("Enables the ability to paint grass color and parameters dynamically in both the editor and in game. If true it creates Rendertextures from supplied textures " +
            "that can be painted and saved.")]
        [SerializeField] private bool _enableMapPainting = false;
        public bool enableMapPainting {
            get {
                if (Application.isPlaying) {
                    return _enableMapPainting && enableMapPaintingRuntime;
                }
                else {
                    return _enableMapPainting;
                }
            }
            set {
                _enableMapPainting = value;

                if (value) {
                    if (Application.isPlaying) {
                        _enableMapPaintingRuntime = value;
                    }
                    MapSetup();
                }
                else {
                    ReleaseDetailMapRTs();
                }

                UpdateShaders();
            }
        }

#if UNITY_EDITOR
        private void OnApplicationQuit() {
            int count = UnityEditor.EditorPrefs.GetInt("GF_Count", UnityEngine.Random.Range(10, 25));
            UnityEditor.EditorPrefs.SetInt("GF_Count", ++count);
        }
#endif

        [GFToolTip("Allow painting during play mode. If you don't need this feature, it's best to leave it off since in order to paint, the paint maps must be loaded as uncompressed RenderTextures.")]
        [SerializeField] private bool _enableMapPaintingRuntime = false;
        public bool enableMapPaintingRuntime {
            get { return _enableMapPaintingRuntime; }
            set {
                _enableMapPaintingRuntime = value;
                if (value && !enableMapPainting) enableMapPainting = value;
            }
        }


        [GFToolTip("If true, an instance of the material will be created to render with. Important if you want to use the same material for multiple grasses but want them to have different textures etc.")]
        public bool useMaterialInstance = true;



        [GFToolTip("Layer to render the grass on.")]
        public int renderLayer;

        [GFToolTip("Amount to expand grass chunks on terrain, helps avoid artifacts on edges of chunks. Preferably set this as low as you can without it looking bad.")]
        public float terrainExpansion = 0.35f;


        [GFToolTip("Does this really need a tooltip? Uhh, well chunk bounds are expanded automatically by blade height to avoid grass popping out when the bounds are culled at strange angles.")]
        [HideInInspector] public bool visualizeChunkBounds = false;


        [GFToolTip("Don't enable this setting unless your source mesh has very NON-uniform density as it'll increase processing time and probably produce worse results. " +
            "This setting attempts to subdivide the mesh to make all triangles as close to the same size as it can, the original shape will be matched exactly. " +
            "Because this subdivides the mesh, you may want to decrease GrassPerTri to account for the increased density.")]
        public bool normalizeMeshDensity = false;


        //[GFToolTip("Enables a partially asynchronous multithreaded execution of the initial processing that can slightly reduce load times if you have a large mesh. " +
        //    "The downside of this is that the game might start before the grass is loaded.")]
        //public bool asyncInitialization = false;

        //old maintanence for making sure we can update these old values if someone upgrades to this version
        [SerializeField] bool asyncInitialization = false;
        [SerializeField] bool receiveShadows = true;
        [SerializeField] bool _castShadows = false;
        [SerializeField] ShadowCastingMode shadowMode;
        [SerializeField] int _instanceCount = 30;
        [SerializeField] Mesh grassMesh;
        [SerializeField] Terrain terrainObject;
        [SerializeField] Transform terrainTransform;
        [SerializeField] Material grassMaterial;
        [SerializeField] Texture2D colorMap;
        [SerializeField] Texture2D paramMap;
        [SerializeField] Texture2D typeMap;

        [SerializeField] int grassPerTri = 4;
        [SerializeField] float normalizeMaxRatio = 12f;
        [SerializeField] GrassRenderType renderType;
        [SerializeField] int chunksX = 5;
        [SerializeField] int chunksY = 1;
        [SerializeField] int chunksZ = 5;
        [SerializeField] private Vector3 _lodParams = new Vector3(15, 1.1f, 0);
        //[SerializeField] private float maxRenderDistSqr = 150f * 150f;
        [SerializeField] private float _maxRenderDist = 150f;

        public bool hasRequiredAssets {
            get {

                bool hasAssets = true;

                foreach (var gMesh in terrainMeshes) {
                    hasAssets &= gMesh.hasRequiredAssets;
                }

                return hasAssets;
            }
        }

        [SerializeField] public List<GrassMesh> terrainMeshes;

        //it doesnt reallyyy make sense to store this in the renderer itself
        //but it at least makes the idx consistent between renderers, though usually theres no reason to have multiple...
        //but also i wouldnt be able to get the select idx in this script otherwise since you cant access the editor assembly from the main assembly
        //which is only really for gizmos but yknow
        //i wouldn't really care except this has to be public so that the inspector can read it
        //is this comment too long winded? probably. do i care? not really
        public int selectedIdx {
            get {
                if (selectedIndices.Count == 0) {
                    selectedIndices.Add(0);
                }
                int value = selectedIndices.First();
                if (value >= terrainMeshes.Count) {
                    value = 0;
                    selectedIndices.Clear();
                    selectedIndices.Add(value);
                }
                return value;
            }
            set {
                selectedIndices.Clear();
                selectedIndices.Add(value);
            }
        }
        [HideInInspector] public HashSet<int> selectedIndices = new HashSet<int>();





        public enum GrassRenderType { Terrain, Mesh }


        //Static Vars
        static ComputeShader _gfComputeShader;
        public static ComputeShader gfComputeShader {
            get {
                if (_gfComputeShader == null) {
                    _gfComputeShader = Resources.Load<ComputeShader>("GrassFlow/GrassFlowCompute");
                }
                return _gfComputeShader;
            }
        }

        static ComputeBuffer forcesBuffer;
        static RippleData[] forcesArray;
        static GrassForce[] forceClassArray;
        static int forcesCount;
        static bool forcesDirty;
        static ComputeKernel noiseKernel;
        static int updateRippleKernel;
        static int addRippleKernel;
        static int normalKernel;
        static int heightKernel;
        static int emptyChunkKernel;
        static int ripDeltaTimeHash = Shader.PropertyToID("ripDeltaTime");

        /// <summary>
        /// Intermediary class to handle point source grass forces.
        /// <para>Do not manually create instances of this class. Instead, use GrassFlowRenderer.AddGrassForce</para>
        /// </summary>
        public class GrassForce {

            public int index = -1;

            public bool added { get; private set; }

            public void Add() {

                if (forcesCount >= maxForces) {
                    return;
                }

                if (added) {
                    return;
                }

                index = forcesCount;
                forceClassArray[forcesCount] = this;
                forcesCount++;
                added = true;
                forcesDirty = true;
            }

            public void Remove() {

                if (!added) {
                    return;
                }

                if (forcesArray == null) {
                    forcesCount = 0;
                    return;
                }

                forcesCount--;
                forcesArray[index] = forcesArray[forcesCount];
                GrassForce swapForce = forceClassArray[forcesCount];
                swapForce.index = index;
                forceClassArray[index] = swapForce;


                index = -1;
                added = false;

                forcesDirty = true;
            }

            public Vector3 position {
                get {
                    return forcesArray[index].pos;
                }
                set {
                    if (forcesArray != null) forcesArray[index].pos = value;
                    if (index == 0) mainForcePos = value;
                    forcesDirty = true;
                }
            }

            public float radius {
                get {
                    return forcesArray[index].drssParams.y;
                }
                set {
                    float invSqr = 1f / (value * value);
                    forcesArray[index].drssParams.y = value;
                    forcesArray[index].drssParams.z = invSqr;
                    if (index == 0) mainForceParams.y = invSqr;
                    forcesDirty = true;
                }
            }

            public float strength {
                get {
                    return forcesArray[index].drssParams.w;
                }
                set {
                    forcesArray[index].drssParams.w = value;
                    if (index == 0) mainForceParams.x = value;
                    forcesDirty = true;
                }
            }
        }


        public static RenderTexture noise3DTexture;
        public static RenderTexture noise2DTexture;


        //static Shader paintShader;
        //static Material paintMat;
        //const int paintPass = 0;
        //const int splatPass = 1;
        static ComputeShader paintShader;
        static int paintKernel;
        static int splatKernel;

        public static HashSet<GrassFlowRenderer> instances = new HashSet<GrassFlowRenderer>();

#if UNITY_EDITOR
        static bool _isPaintingOpen;
        public static bool isPaintingOpen {
            get => _isPaintingOpen;
            set {
                if (_isPaintingOpen != value) {
                    _isPaintingOpen = value;
                    foreach (var gf in instances) {
                        gf.RefreshMaterials();

                        foreach (var gMesh in gf.terrainMeshes) {
                            _ = gMesh.RefreshSubMeshes();
                        }
                    }
                }
            }
        }
#else
    internal const bool isPaintingOpen = false;
#endif

        Dictionary<Transform, HashSet<GrassMesh>> gMeshDict = new Dictionary<Transform, HashSet<GrassMesh>>();

        static bool runRipple = true;

        /// <summary>
        /// This is set to true as soon as a ripple is added and stays true unless manually set to false.
        /// When true it signals the ripple update shaders to run, it doesn't take long to run them and theres no easy generic way to know when all ripples are depleted without asking the gpu for the memory which would be slow.
        /// But you can manually set this if you know your ripples only last a certain amount of time or something.
        /// Realistically its not that important though.
        /// </summary>
        public static bool updateRipples = false;

        /// <summary>
        /// This can be set on Awake to control whether or not to automatically initialize this renderer.
        /// Mainly useful if you want to manually intialize in order ensure timing of extra code.
        /// </summary>
        public bool initOnStart = true;


        //-----------------------------------------------------------------------------------------
        //----------------------------------------ACTUAL CODE---------------------------------------
        //-----------------------------------------------------------------------------------------


        void Awake() {
            instances.Add(this);
        }

        private void Start() {
            if (initOnStart) {
                StartupInit();
            }
        }

        void UnHookRender() {

#if GRASSFLOW_SRP || GRASSFLOW_HDRP
        RenderPipelineManager.beginCameraRendering -= Render;
#else
            Camera.onPreCull -= Render;
#endif
        }

        void HookRender() {

            UnHookRender();

#if GRASSFLOW_SRP || GRASSFLOW_HDRP
            RenderPipelineManager.beginCameraRendering += Render;
#else
            Camera.onPreCull += Render;
#endif
        }

#if UNITY_2019_2_OR_NEWER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#else
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#endif
        static void StaticDomain() {

            runRipple = true;
            updateRipples = false;
            useFloatFormatParam = false;
            useFloatFormatColorMap = false;

            CullingZone.ClearCulledChunks();
            MeshChunker.ResetTriangleMinMax();

            instances = new HashSet<GrassFlowRenderer>(FindObjectsOfType<GrassFlowRenderer>());
            forcesCount = 0;

            if (noise3DTexture) {
                noise3DTexture.Release();
                noise3DTexture = null;
            }

            if (noise2DTexture) {
                noise2DTexture.Release();
                noise2DTexture = null;
            }

            ReleaseBuffers();
        }


        void ReleaseBuffersBeforeDomain() {
            ReleaseAssets();
            ReleaseBuffers();
        }


        public bool CheckRequiresChunks() {

            CheckTerrainMeshes();

            foreach (var gMesh in terrainMeshes) {
                if (gMesh.chunks == null) {
                    initialized = false;
                    return false;
                }
            }

            return true;
        }


        public void OnEnable() {
            UnHookRender();

            CheckRequiresChunks();
            CheckRippleBuffers();

#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ReleaseBuffersBeforeDomain;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ReleaseBuffersBeforeDomain;
#endif

            if (hasRequiredAssets) {
                UnHookRender();
                HookRender();
            }

            UpdateTransform(true);
        }



        private void OnDisable() {

            UnHookRender();

            ReleaseBuffers();

#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ReleaseBuffersBeforeDomain;
#endif

            initialized = false;
        }


#if UNITY_EDITOR

        private void Reset() {

            CheckTerrainMeshes();
            GrassMesh firstMesh = terrainMeshes[0];

            firstMesh.terrainTransform = transform;
            firstMesh.terrainObject = GetComponent<Terrain>();

            MeshFilter meshF;
            if (meshF = GetComponent<MeshFilter>()) {
                firstMesh.grassMesh = meshF.sharedMesh;
                firstMesh.renderType = GrassRenderType.Mesh;
            }

        }



        //the validation function is mainly to regenerate certain things that are lost upon unity recompiling scripts
        //but also in some other situations like saving the scene
        public void OnValidate() {

            instances.Add(this);

            //if (!isActiveAndEnabled || !hasRequiredAssets || StackTraceUtility.ExtractStackTrace().Contains("Inspector"))
            //    return;


            if (terrainMeshes == null) {
                _ = Refresh();
            }
            else {
                GetResources(true);
                UpdateShaders();
                MapSetup();
            }


            if (!initialized && !initializing) {
                Init();
            }
            else {
                UnHookRender();
                if (isActiveAndEnabled) {
                    HookRender();
                }
            }

        }


        static Color[] gizmoCols;

        private void OnDrawGizmos() {
            if (!visualizeChunkBounds) return;
            if (selectedIdx >= terrainMeshes.Count) return;

            var gMesh = terrainMeshes[selectedIdx];
            if (gMesh.chunks == null) return;

            //if (cameraCulls.Count == 0) return;
            //var chunks = cameraCulls.ElementAt(0).Value.culledChunks;

            //foreach (var chunk in chunks) {

            //    if (chunk.instancesToRender == 0) {
            //        Gizmos.color = Color.white;
            //    } else {
            //        float t = chunk.instancesToRender / (float)gMesh.instanceCount;
            //        Gizmos.color = Color.Lerp(Color.green, Color.red, t);
            //    }

            //    Gizmos.DrawWireCube(chunk.parentChunk.worldBounds.center, chunk.parentChunk.worldBounds.size);
            //}

            if (gizmoCols == null) {
                gizmoCols = new Color[64];
                for (int i = 0; i < gizmoCols.Length; i++) {
                    gizmoCols[i] = new Color(Random.value, Random.value, Random.value);
                }
            }

            foreach (var chunk in gMesh.chunks) {
                int value = chunk.cullBatchID % gizmoCols.Length;
                if (value >= 0) {
                    Gizmos.color = gizmoCols[value];
                }
                else {
                    Gizmos.color = Color.white;
                }
                Gizmos.DrawWireCube(chunk.worldBounds.center, chunk.worldBounds.size);
            }

            foreach (var sub in gMesh.subGrassMeshes) {
                if (sub.loaded) {
                    Gizmos.color = Color.green;
                }
                else {
                    Gizmos.color = Color.red;
                }
                Gizmos.DrawWireCube(sub.bounds.center, sub.bounds.size);
            }

        }

        //void OnDrawGizmosSelected() {
        //    if (selectedIdx >= terrainMeshes.Count) return;

        //    var gMesh = terrainMeshes[selectedIdx];
        //    if (gMesh.chunks == null) return;
        //    if (!gMesh.chunkedMesh) return;

        //    Gizmos.color = new Color(0.8f, 0.8f, 0.8f, 1f);
        //    var t = gMesh.terrainTransform;
        //    Gizmos.matrix = t.localToWorldMatrix;
        //    gMesh.chunkedMesh.RecalculateNormals();
        //    foreach (var chunk in gMesh.chunks) {
        //        Vector3 pos = new Vector3(chunk.chunkPos.x, 0, chunk.chunkPos.y);
        //        Gizmos.DrawWireMesh(gMesh.chunkedMesh, chunk.submeshIdx, pos, Quaternion.identity, Vector3.one * 66.75f);
        //    }
        //}

        //stuff for toggling the preprocessor definition
        public const string grassSRPDefine = "GRASSFLOW_SRP";
        public const string grassHDRPDefine = "GRASSFLOW_HDRP";

        static readonly UnityEditor.BuildTargetGroup[] definePlatforms = new UnityEditor.BuildTargetGroup[] {
        UnityEditor.BuildTargetGroup.Standalone,
        UnityEditor.BuildTargetGroup.XboxOne,
        UnityEditor.BuildTargetGroup.PS4,
        UnityEditor.BuildTargetGroup.Android,
        UnityEditor.BuildTargetGroup.iOS,
        UnityEditor.BuildTargetGroup.WebGL,
        UnityEditor.BuildTargetGroup.Switch,
    };


#if GRASSFLOW_SRP
    [UnityEditor.MenuItem("CONTEXT/GrassFlowRenderer/Disable URP Support (READ DOC)")]
#else
        [UnityEditor.MenuItem("CONTEXT/GrassFlowRenderer/Enable URP Support (READ DOC)")]
#endif
        public static void ToggleSRP() {
            ToggleDefineSymbol(grassSRPDefine, definePlatforms);
        }
#if GRASSFLOW_HDRP
    [UnityEditor.MenuItem("CONTEXT/GrassFlowRenderer/Disable HDRP Support (READ DOC)")]
#else
        [UnityEditor.MenuItem("CONTEXT/GrassFlowRenderer/Enable HDRP Support (READ DOC)")]
#endif
        public static void ToggleHDRP() {
            ToggleDefineSymbol(grassHDRPDefine, definePlatforms);
        }

        

        static bool CheckForDefineSymbol(string symbolName) {
            return UnityEditor.PlayerSettings.GetScriptingDefineSymbolsForGroup(definePlatforms[0]).Contains(symbolName);
        }

        static bool ToggleDefineSymbol(string symbolName, UnityEditor.BuildTargetGroup[] platforms) {
            bool enable = !CheckForDefineSymbol(symbolName);

            foreach (UnityEditor.BuildTargetGroup buildTarget in platforms) {
                string defines = UnityEditor.PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTarget);

                if (!defines.Contains(symbolName) && enable) {
                    UnityEditor.PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTarget, defines + ";" + symbolName);

                }
                else if (defines.Contains(symbolName) && !enable) {
                    UnityEditor.PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTarget, defines.Replace(symbolName, ""));
                }
            }

            return enable;
        }


#endif


        /// <summary>
        /// Releases current assets and reinitializes the grass.
        /// Warning: Will reset current map paint status. (If that is the intended effect, use RevertDetailMaps() instead)
        /// </summary>
        public async Task Refresh(bool isAsync = false) {
            if (!this) return;
            if (Application.isEditor) {
                initializing = false;
            }

            if (!Application.isPlaying) {
                SortGrassMeshes();
            }

            if (isActiveAndEnabled) {
                ReleaseAssets();

                await InitAsync(isAsync);
            }
        }

        public void RefreshMaterials() {
            foreach (var gMesh in terrainMeshes) {
                gMesh.GetResources(true);
                gMesh.UpdateTerrain();
                _ = gMesh.UpdateTransform(false);
            }
        }

        public delegate void GrassEvent();
        public GrassEvent OnInititialized;

        void Init() {
            _ = InitAsync(false);
        }

        public void CheckTerrainMeshes() {
            if (terrainMeshes == null || terrainMeshes.Count == 0) {
                var gMesh = GetEmptyGrassMesh();

                //this takes the old serialized values and shoves them into a new gMesh for the refactored system
                //just kinda keeps it compatible when people update            
                gMesh.EnsureMeshLods();
                gMesh.instanceCount = _instanceCount;
                gMesh.grassMesh = grassMesh;
                gMesh.terrainObject = terrainObject;
                gMesh.terrainTransform = terrainTransform;
                gMesh.mainGrassMat = grassMaterial;
                gMesh.colorMap = colorMap;
                gMesh.paramMap = paramMap;
                gMesh.typeMap = typeMap;

                gMesh.grassPerTri = grassPerTri;
                gMesh.normalizeMaxRatio = normalizeMaxRatio;
                gMesh.renderType = renderType;
                gMesh.chunksX = chunksX;
                gMesh.chunksY = chunksY;
                gMesh.chunksZ = chunksZ;

#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
#endif

                terrainMeshes = new List<GrassMesh>() { gMesh };
            }

#if UNITY_EDITOR
            UpdatePatcher();
#endif
        }

        [SerializeField] float thisVersion;
        const float version = 2.81f;
        void UpdatePatcher() {

            if (thisVersion < 2.1f) {
                if (terrainMeshes == null) return;
                foreach (var gMesh in terrainMeshes) {
                    gMesh.receiveShadows = receiveShadows;
                    gMesh.castShadows = _castShadows;
                    gMesh.maxRenderDist = _maxRenderDist;
                    gMesh.lodParams = _lodParams;
                }
            }

            if (thisVersion < 2.5f) {
                foreach (var gMesh in terrainMeshes) {
                    gMesh.UpdateDeprecatedCustomMesh();
                }
            }

            if (thisVersion < 2.81f) {
                foreach (var gMesh in terrainMeshes) {
                    gMesh.asyncInitialization = asyncInitialization;
                }
            }

            thisVersion = version;
        }


        //-----------------------------------------------------------------------------------------
        //----------------------------------------INIT---------------------------------------
        //-----------------------------------------------------------------------------------------
        async void StartupInit() {
            CheckTerrainMeshes();

            if (hasRequiredAssets) {
                Task initTask = InitAsync(Application.isPlaying);

                while (!initTask.IsCompleted && !gameStarted) {
                    await Task.Delay(10);
                }

                //if (!asyncInitialization) processAsync = false;
            }
        }



        bool initialized = false;
        bool initializing = false;
        public static bool processAsync;
        public async Task InitAsync(bool isAsync = true) {

            //return;

            if (!gfComputeShader) {
                GetResources(false);
            }

            CheckTerrainMeshes();

            if (!hasRequiredAssets) {
                Debug.LogError("GrassFlow: Not all required assets assigned in the inspector!");
                return;
            }

            if (!isActiveAndEnabled) return;
            if (initializing) return;

            initializing = true;

            await CullingZone.GetWaitForCullingTask();
            CullingZone.ClearCulledChunks();

            try {

                CheckRippleBuffers();

                var mainCam = Camera.main;
                CullingZone.InitializeCamera(mainCam);

                if (!this) {
                    //check if the object is destroyed and make sure we dont weirdly hook into a destroyed renderer instance thingy idk its weird
                    initializing = false;
                    return;
                }

                GetResources(false);

                HookRender();


                IEnumerable<GrassMesh> gMeshesToLoad;
                if (mainCam) {
                    //sort the meshes by distance so that visually the closest ones load in first if there are a lot
                    Vector3 camPos = mainCam.transform.position;
                    gMeshesToLoad = terrainMeshes.OrderBy(x => x.asyncInitialization).ThenBy(x => x.worldBounds.SqrDistance(camPos));
                }
                else {
                    gMeshesToLoad = terrainMeshes;
                }

                foreach (var gMesh in gMeshesToLoad) {

                    processAsync = isAsync && gMesh.asyncInitialization;
                    //if (processAsync) await Task.Delay(1000);

                    if (!gMeshDict.TryGetValue(gMesh.terrainTransform, out var gList)) {
                        gMeshDict.Add(gMesh.terrainTransform, gList = new HashSet<GrassMesh>());
                    }
                    gList.Add(gMesh);

                    gMesh.owner = this;
                    gMesh.shouldDraw = false;
                    gMesh.GetResources();
                    gMesh.MapSetup();

                    await LoadChunksForMesh(gMesh);

                    if (!this) {
                        initializing = false;
                        UnHookRender();
                        return;
                    }
                }


                initialized = true;

            } catch (System.Exception ex) {
                Debug.LogException(ex);
            }

            //print("init: " + this);
            initializing = false;

            if (initialized) {
                OnInititialized?.Invoke();
            }
        }



        public GrassMesh GetEmptyGrassMesh() {

            //basically want to just reuse the empty template one just in case
            if (terrainMeshes != null && terrainMeshes.Count == 1 && !terrainMeshes[0].hasRequiredAssets) {
                return terrainMeshes[0];
            }

            GrassMesh gMesh = new GrassMesh() {
                owner = this,
            };

            gMesh.EnsureMeshLods();
            return gMesh;
        }

        /// <summary>
        /// Use this to add grass meshes at runtime.
        /// </summary>
        public async Task<GrassMesh> AddMesh(Mesh addMesh, Transform transform, Material grassMat, Mesh renderMesh = null,
            Texture colorMap = null, Texture paramMap = null, Texture typeMap = null,
            int instanceCount = 30, int grassPerTri = 1, float normalizeMaxRatio = 12f,
            int chunksX = 5, int chunksY = 1, int chunksZ = 5, bool isAsync = true) {

            return await AddGrassMesh(addMesh, null, transform, grassMat, renderMesh, colorMap, paramMap, typeMap,
                instanceCount, grassPerTri, normalizeMaxRatio, chunksX, chunksY, chunksZ, isAsync);
        }

        /// <summary>
        /// Use this to add grass terrains at runtime.
        /// </summary>
        public async Task<GrassMesh> AddTerrain(Terrain addTerrain, Transform transform, Material grassMat, Mesh renderMesh = null,
            Texture colorMap = null, Texture paramMap = null, Texture typeMap = null,
            int instanceCount = 30, int grassPerChunk = 1, float normalizeMaxRatio = 12f,
            int chunksX = 5, int chunksZ = 5, bool isAsync = true) {

            return await AddGrassMesh(null, addTerrain, transform, grassMat, renderMesh, colorMap, paramMap, typeMap,
                instanceCount, grassPerChunk, normalizeMaxRatio, chunksX, 1, chunksZ, isAsync);
        }

        async Task<GrassMesh> AddGrassMesh(Mesh addMesh, Terrain addTerrain, Transform transform, Material grassMat, Mesh renderMesh = null,
            Texture colorMap = null, Texture paramMap = null, Texture typeMap = null,
            int instanceCount = 30, int grassPerTri = 1, float normalizeMaxRatio = 12f,
            int chunksX = 5, int chunksY = 1, int chunksZ = 5, bool isAsync = true) {

            processAsync = isAsync;

            GrassMesh gMesh = GetEmptyGrassMesh();

            gMesh.instanceCount = instanceCount;
            gMesh.grassPerTri = grassPerTri;
            gMesh.normalizeMaxRatio = normalizeMaxRatio;

            gMesh.grassMesh = addMesh;
            gMesh.terrainObject = addTerrain;
            gMesh.mainGrassMat = grassMat;
            gMesh.terrainTransform = transform;

            gMesh.customGrassMesh = renderMesh ? renderMesh : Resources.GetBuiltinResource<Mesh>("Quad.fbx");

            gMesh.colorMap = colorMap;
            gMesh.paramMap = paramMap;
            gMesh.typeMap = typeMap;

            gMesh.chunksX = chunksX;
            gMesh.chunksY = chunksY;
            gMesh.chunksZ = chunksZ;

            gMesh.renderType = addMesh ? GrassRenderType.Mesh : GrassRenderType.Terrain;

            await AddGrassMeshFinal(gMesh);

            return gMesh;
        }

        public async Task AddGrassMesh(GrassMesh gMesh) {
            gMesh.owner = this;
            gMesh.EnsureMeshLods();

            foreach (var lod in gMesh.customMeshLods) {
                lod.lodMesh = lod.lodMesh;
                lod.lodMat = lod.lodMat;
            }

            await AddGrassMeshFinal(gMesh);
        }

        async Task AddGrassMeshFinal(GrassMesh gMesh) {

            if (!gMeshDict.TryGetValue(gMesh.terrainTransform, out var gList)) {
                gMeshDict.Add(gMesh.terrainTransform, gList = new HashSet<GrassMesh>());
            }
            gList.Add(gMesh);

            gMesh.GetResources();
            gMesh.MapSetup();


            await LoadChunksForMesh(gMesh);

            UpdateShader(gMesh);
            terrainMeshes.Add(gMesh);
        }


        public void RemoveMesh(Mesh mesh) {

            if (terrainMeshes == null) return;
            for (int i = 0; i < terrainMeshes.Count; i++) {
                if (terrainMeshes[i].grassMesh == mesh) {
                    RemoveGrassMesh(i);
                    return;
                }
            }
        }

        public void RemoveTerrain(Terrain terrain) {

            if (terrainMeshes == null) return;
            for (int i = 0; i < terrainMeshes.Count; i++) {
                if (terrainMeshes[i].terrainObject == terrain) {
                    RemoveGrassMesh(i);
                    return;
                }
            }
        }

        public void RemoveGrassMesh(GrassMesh gMesh) {
            if (terrainMeshes == null) return;
            RemoveGrassMesh(terrainMeshes.IndexOf(gMesh));
        }

        public void RemoveGrassMesh(int idx) {
            if (terrainMeshes == null) return;
            if (terrainMeshes.Count <= 1) return;
            if (idx >= 0) {
                //ClearCulledChunks();

                var mesh = terrainMeshes[idx];

                if (gMeshDict.TryGetValue(mesh.terrainTransform, out var gList)) {
                    gList.Remove(mesh);
                }

                mesh.shouldDraw = false;
                terrainMeshes.RemoveAt(idx);
                mesh.Dispose();
            }
        }

        public async Task LoadChunksForMesh(GrassMesh gMesh, bool isAsync = false) {
            processAsync = isAsync;
            await LoadChunksForMesh(gMesh);
        }

        static void LogMemoryUsage(Object obj, string msg = "") {
            const float mb = 1024 * 1024;
            long memorySize = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(obj);
            Debug.Log(msg + $" : {obj} '" + obj.name + "' is using " + memorySize / mb + " MB.");
        }

        async Task LoadChunksForMesh(GrassMesh gMesh) {

            gMesh.grassIdx = terrainMeshes.IndexOf(gMesh);
            gMesh.chunks = null;

            if (gMesh.renderType == GrassRenderType.Mesh) {

                gMesh.chunks = await MeshChunker.ChunkMesh(gMesh, normalizeMeshDensity);

            }
            else {

                SetGrassMeshTerrainData(gMesh);

                gMesh.chunks = await MeshChunker.ChunkTerrain(gMesh);

                //if (Application.isPlaying && discardEmptyTriangles) DiscardUnusedChunks();
            }

            await gMesh.Update(processAsync);

            Camera cam = Camera.main;
            await MeshChunker.SplitToSubMeshes(gMesh, cam ? cam.transform.position : Vector3.zero);

            if (gMesh.customGrassMesh) {
                await MeshChunker.HandleCustomMesh(gMesh);
            }
            //Debug.Log("buffer: " + (gMesh.posBuffer.count * gMesh.posBuffer.stride) / (1024f * 1024f)  + "MB");
        }

        public GrassMesh GetSelectedGrassMesh() {
            return GetSelectedGrassMesh(selectedIdx);
        }

        public GrassMesh GetSelectedGrassMesh(int idx) {

            CheckTerrainMeshes();

            if (idx >= terrainMeshes?.Count) {
                idx = 0;
            }

            if (idx >= terrainMeshes?.Count) {
                return null;
            }

            var sel = terrainMeshes[idx];
            sel.grassIdx = idx;
            return sel;
        }

        public void SortGrassMeshes() {
            if (terrainMeshes == null) return;

#if UNITY_EDITOR
            //this is dumb but if i dont record this then if you try to undo itll break things
            UnityEditor.Undo.RecordObject(this, "GrassFlow");
#endif

            var prevSelMesh = GetSelectedGrassMesh();
            terrainMeshes.Sort((a, b) => a.name.CompareTo(b.name));
            selectedIdx = terrainMeshes.IndexOf(prevSelMesh);

#if UNITY_EDITOR
            UnityEditor.Undo.FlushUndoRecordObjects();
#endif
        }


        public void SetGrassMeshTerrainData(GrassMesh gMesh) {
            if (gMesh.terrainObject) {
                TerrainData terrain = gMesh.terrainObject.terrainData;
                if (!gMesh.terrainHeightmap) {
                    gMesh.terrainHeightmap = terrain.heightmapTexture;
                }
                if (!gMesh.terrainHoleMap) {
#if UNITY_EDITOR
                    if (!Application.isPlaying) {
                        //idunno man sometimes on a fresh unity start the holes texture is weird and will cause no grass to spawn
                        //so we just force it to update basically and then its fine
                        //it could be due to importing terrains from older unity versions
                        terrain.SetHoles(0, 0, terrain.GetHoles(0, 0, 1, 1));
                    }
#endif
                    gMesh.terrainHoleMap = terrain.holesTexture;
                }
            }
        }

        void DiscardUnusedChunks() {

            foreach (var gMesh in terrainMeshes) {

                Texture paramTex;
                if (!(paramTex = gMesh.paramMapRT)) paramTex = gMesh.paramMap;

                if (!gMesh.hasRequiredAssets || !paramTex
                    || gMesh.renderType != GrassRenderType.Terrain) return;

                gfComputeShader.SetVector("chunkDims", new Vector4(gMesh.chunksX, gMesh.chunksZ));
                gfComputeShader.SetTexture(emptyChunkKernel, "paramMap", paramTex);

                var terrainChunks = gMesh.chunks;

                ComputeBuffer chunkResultsBuffer = new ComputeBuffer(terrainChunks.Length, sizeof(int));
                int[] chunkResults = new int[terrainChunks.Length];
                chunkResultsBuffer.SetData(chunkResults);
                gfComputeShader.SetBuffer(emptyChunkKernel, "chunkResults", chunkResultsBuffer);

                gfComputeShader.Dispatch(emptyChunkKernel, Mathf.CeilToInt(paramTex.width / paintThreads), Mathf.CeilToInt(paramTex.height / paintThreads), 1);

                chunkResultsBuffer.GetData(chunkResults);
                chunkResultsBuffer.Release();

                List<MeshChunker.MeshChunk> resultChunks = new List<MeshChunker.MeshChunk>();
                for (int i = 0; i < terrainChunks.Length; i++) {
                    if (chunkResults[i] > 0) resultChunks.Add(terrainChunks[i]);
                }

                gMesh.chunks = resultChunks.ToArray();
            }
        }

        new void Destroy(Object obj) {
            if (Application.isPlaying) {
                Object.Destroy(obj);
            }
            else {
                DestroyImmediate(obj);
            }
        }


        void ReleaseAssets() {

            if (terrainMeshes != null) {
                foreach (var gMesh in terrainMeshes) {
                    gMesh.Dispose();
                }
            }

            CullingZone.ClearCulledChunks();

            initialized = false;
        }

        void ReleaseDetailMapRTs() {
            foreach (var gMesh in terrainMeshes) gMesh.ReleaseDetailMapRTs();
        }

        /// <summary>
        /// Reverts unsaved paints to grass color and paramter maps.
        /// </summary>
        public void RevertDetailMaps() {
            foreach (var gMesh in terrainMeshes) gMesh.RefreshDetailMaps();
        }

        void MapSetup() {
            foreach (var gMesh in terrainMeshes) gMesh.MapSetup();
        }

        /// <summary>
        /// Updates the transformation matrices used to render grass.
        /// You should call this if the object the grass is attached to moves.
        /// </summary>
        public async void UpdateTransform(bool isAsync = false) {

            if (terrainMeshes == null) return;
            foreach (var gMesh in terrainMeshes) {
                if (!gMesh.terrainTransform) continue;
                await gMesh.UpdateTransform(isAsync);
            }
        }


        const int maxRipples = 128;
        const int maxForces = 64;



        void CheckRippleBuffers() {
            if (forcesBuffer == null) {
                forcesBuffer = new ComputeBuffer(maxForces + maxRipples + 1, Marshal.SizeOf(typeof(RippleData)));
            }
            if (forcesArray == null) {
                forcesArray = new RippleData[maxForces];
            }
            if (forceClassArray == null) {
                forceClassArray = new GrassForce[maxForces];
            }
        }

        void GetResources(bool alsoGetMeshResources) {
            if (alsoGetMeshResources && terrainMeshes != null) {
                foreach (var gMesh in terrainMeshes) {
                    gMesh.GetResources();
                }
            }

            noiseKernel = new GrassFlow.ComputeKernel(gfComputeShader, "NoiseMain");

            addRippleKernel = gfComputeShader.FindKernel("AddRipple");
            updateRippleKernel = gfComputeShader.FindKernel("UpdateRipples");
            normalKernel = gfComputeShader.FindKernel("NormalsMain");
            heightKernel = gfComputeShader.FindKernel("HeightmapMain");
            emptyChunkKernel = gfComputeShader.FindKernel("EmptyChunkDetect");

            if (!paintShader) paintShader = Resources.Load<ComputeShader>("GrassFlow/GrassFlowPainter");
            //if(!paintMat) paintMat = new Material(paintShader);
            paintKernel = paintShader.FindKernel("PaintKernel");
            splatKernel = paintShader.FindKernel("ApplySplatTex");


            if (!noise3DTexture || !noise2DTexture || !noise3DTexture.IsCreated()) {
                noise3DTexture = Resources.Load<RenderTexture>("GrassFlow/GF3DNoise");
                noise3DTexture.Release();

#if UNITY_2021_2_OR_NEWER
                var halfSupported = SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RHalf);
#else
                var halfSupported = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RHalf);
#endif
                if (!noise2DTexture) {
                    noise2DTexture = new RenderTexture(noise3DTexture) {
                        format = halfSupported ? RenderTextureFormat.RHalf : RenderTextureFormat.Default,
                        dimension = TextureDimension.Tex2D,
                        volumeDepth = 1,
                        enableRandomWrite = true,
                        wrapMode = TextureWrapMode.Mirror
                    };
                    noise2DTexture.Create();
                }

                noise3DTexture.format = halfSupported ? RenderTextureFormat.RHalf : RenderTextureFormat.Default;
                noise3DTexture.enableRandomWrite = true;
                noise3DTexture.Create();

                //compute 3d noise

                ComputeBuffer permBuff = new ComputeBuffer(512, 4);
                permBuff.SetData(TextureCreator.noisePerm);
                noiseKernel.SetBuffer("perm", permBuff);

                noiseKernel.SetTexture(NoiseResultID, noise3DTexture);
                noiseKernel.SetTexture(_NoiseTex2DID, noise2DTexture);
                noiseKernel.SetResolution(noise3DTexture);
                noiseKernel.Dispatch();

                permBuff.Release();
            }

            UpdateShaders(false);
        }



        struct RippleData {
            internal Vector4 pos; // w = strength
            internal Vector4 drssParams;//xyzw = decay, radius, sharpness, speed 
        }


        private void Update() {

#if UNITY_EDITOR
            if (updateBuffers && hasRequiredAssets)
                UpdateShaders();

            CheckInspectorPaint();
#endif

            runRipple = true;
        }


#if UNITY_EDITOR
        bool shouldPaint;
        System.Action paintAction;

        void CheckInspectorPaint() {
            if (shouldPaint && paintAction != null) {
                paintAction.Invoke();
                shouldPaint = false;
            }
        }

        //its really stupid that this has to exist but it do be that way
        //its explained why in GrassFlowInspector
        //used only for painting during scene gui
        void InspectorSetPaintAction(System.Action action) {
            paintAction = action;
            shouldPaint = true;
        }
#endif



        /// <summary>
        /// This basically sets all required variables and textures to the various shaders to make them run.
        /// You might need to call this after changing certain variables/textures to make them take effect.
        /// </summary>
        public void UpdateShaders(bool updateMats = true) {

            if (forcesBuffer != null) {
                Shader.SetGlobalBuffer(forcesBufferID, forcesBuffer);
            }

            if (noise3DTexture) {
                if (!noise3DTexture.IsCreated()) {
                    GetResources(false);
                }
                Shader.SetGlobalTexture(_NoiseTexID, noise3DTexture);
            }
            if (noise2DTexture) {
                Shader.SetGlobalTexture(_NoiseTex2DID, noise2DTexture);
            }

            if (updateMats) {
                if (terrainMeshes == null) return;
                foreach (var gMesh in terrainMeshes) {
                    UpdateShader(gMesh);
                }
            }
        }

        public void UpdateShader(GrassMesh gMesh) {

            if (gMesh.terrainTransform) {
                gMesh.SetDrawmatObjMatrices();
            }

            gMesh.UpdateMaps(enableMapPainting);

            if (gMesh.renderType == GrassRenderType.Terrain && !gMesh.terrainHeightmap) {
                //the whole check for null terrain heightmap is mostly just to catch cases like when you modify the terrain and undo and unity resets the texture 
                //need to check gfComputeShader just to make sure this doesnt run before things are initialized properly
                if (gfComputeShader) {
                    SetGrassMeshTerrainData(gMesh);
                    gMesh.UpdateTerrain();
                }
            }

            foreach (var lod in gMesh.customMeshLods) {

                Material grassMat = lod.drawnMat;

                if (!grassMat) continue;

                grassMat.SetFloat(maxVertHID, gMesh.maxVertexHeight);
                grassMat.SetInt(grassPerTriID, gMesh.grassPerTri);

                grassMat.SetFloat(terrainSlopeThreshID, gMesh.terrainSlopeThresh);
                grassMat.SetFloat(terrainSlopeFadeID, 1f / gMesh.terrainSlopeFade);


                //a bit weird but saves having to do an extra division in the shader ¯\_(ツ)_/¯
                if (grassMat.HasProperty(numTexturesID)) {
                    float numGrassAtlasTexes = grassMat.GetFloat(numTexturesID);
                    grassMat.SetFloat(numTexturesPctUVID, 1.0f / numGrassAtlasTexes);
                }
            }
        }



        //----------------------------------
        //MAIN RENDER FUNCTION--------------
        //----------------------------------


#if GRASSFLOW_SRP || GRASSFLOW_HDRP
        void Render(ScriptableRenderContext context, Camera cam) {
#else
        void Render(Camera cam) {
#endif

            //if (initializing) return;

            if ((cam.cullingMask & (1 << renderLayer)) == 0) {
                //don't even bother if the cameras cullingmask doesn't contain the renderlayer
                return;
            }


#if UNITY_EDITOR
            if (!this || !isActiveAndEnabled) {
                UnHookRender();
                return;
            }

            //these arent really as much of an issue in a built game
#if UNITY_2018_3_OR_NEWER
            //make sure not to render grass in prefab stage unless its part of the prefab
            if (UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null
                && UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(gameObject) == null) return;
#endif
            if (cam.cameraType == CameraType.Preview) return;

            if (paintHighlightMesh && cam.cameraType == CameraType.SceneView) {
                RenderGrassMeshCustomShader(paintHighlightMesh, paintHighlightMat, cam);
            }
#endif

            if (terrainMeshes == null) return;



            //var gMesh0 = terrainMeshes[0];
            //var chunk0 = gMesh0.chunks[0];
            //chunk0.pBlock.SetFloat(_instanceLodID, 100);
            //var lod = gMesh0.customMeshLods[0];
            //Graphics.DrawMeshInstancedProcedural(lod.lodMesh, 0, lod.drawnMat, gMesh0.worldBounds, gMesh0.posBuffer.count,
            //        chunk0.pBlock, gMesh0.shadowMode, gMesh0.receiveShadows, renderLayer, cam, LightProbeUsage.Off);
            //return;


            var cullResults = CullingZone.GetCullResult(cam);
            if (cullResults.batches == null) return;

            if (cullResults.needsRunning) {

                if (cullResults.needsLoadUnload) {
                    foreach (var gMesh in terrainMeshes) {
                        if (gMesh.subGrassMeshes == null) continue;
                        foreach (var sub in gMesh.subGrassMeshes) {
                            sub.HandleLoadUnload();
                        }
                    }
                    cullResults.needsLoadUnload = false;
                }

                cullResults.UpdatePos();

                cullResults.needsRunning = false;

                if (visualizeChunkBounds || cullResults.batchCount == 0) {
                    //need to run culling synchronously to properly display batch colors
                    cullResults.RunCulling();
                }
                else {
                    cullResults.asyncCullTask = Task.Run(() => {
                        cullResults.RunCulling();
                    });
                }
            }

            if (cullResults.frustumGrassMeshes.Count > 0) {
                cullResults.UpdateVP();

                for (int i = 0; i < cullResults.frustumGrassMeshes.Count; i++) {

                    var cMesh = cullResults.frustumGrassMeshes[i];
                    var gMesh = cMesh.grassMesh;

                    if (!gMesh.shouldDraw) continue;

                    cMesh.DispatchFrustumCullShader(cullResults);

                    if (cMesh.indirectArgs == null) continue;

                    cMesh.pBlock.SetFloat(meshInvVertCountID, cMesh.lod.invVertCount);

                    Graphics.DrawMeshInstancedIndirect(cMesh.lod.drawnMesh, 0, cMesh.lod.drawnMat, cMesh.visibleBounds, cMesh.indirectArgs, 0,
                        cMesh.pBlock, gMesh.shadowMode, gMesh.receiveShadows, renderLayer, cullResults.cam, LightProbeUsage.Off);
                }
            }

            //MaterialPropertyBlock mpb = new MaterialPropertyBlock();

            for (int i = 0; i < cullResults.batchCount; i++) {

                var batch = cullResults.batches[i];
                var subMesh = batch.subMesh;
                var gMesh = subMesh.gMesh;
                if (!gMesh.shouldDraw) continue;

                subMesh.pBlock.SetVectorArray(batchDataID, batch.batchData);
                subMesh.pBlock.SetFloat(meshInvVertCountID, batch.lod.invVertCount);
                subMesh.pBlock.SetFloat(batchAlphaID, batch.alpha);

#if UNITY_2021_2_OR_NEWER
                rParams.material = batch.lod.drawnMat;
                rParams.worldBounds = batch.visibleBounds;
                rParams.matProps = subMesh.pBlock;
                rParams.shadowCastingMode = gMesh.shadowMode;
                rParams.receiveShadows = gMesh.receiveShadows;
                rParams.camera = cullResults.cam;
                rParams.layer = renderLayer;
                rParams.renderingLayerMask = gMesh.renderingLayer;
                
                Graphics.RenderMeshPrimitives(in rParams, batch.lod.drawnMesh, 0, batch.totalInstances);
#else
                Graphics.DrawMeshInstancedProcedural(batch.lod.drawnMesh, 0, batch.lod.drawnMat, batch.visibleBounds, batch.totalInstances,
                            subMesh.pBlock, gMesh.shadowMode, gMesh.receiveShadows, renderLayer, cullResults.cam, LightProbeUsage.Off);
#endif
            }
        }

#if UNITY_2021_2_OR_NEWER
        RenderParams rParams;
#endif



        public Material paintHighlightMat;
        public GrassMesh paintHighlightMesh;
        static int paintHighlightBrushParamsID = Shader.PropertyToID("paintHighlightBrushParams");
        static int paintHightlightColorID = Shader.PropertyToID("paintHightlightColor");
        static int paintHightlightTypeID = Shader.PropertyToID("isTerrain");

        /// <summary>
        /// Mainly just used to render the terrain for paint highlights
        /// </summary>
        public async void RenderGrassMeshCustomShader(GrassMesh gMesh, Material mat, Camera cam = null, bool highlightAll = false) {

            if (!gMesh || gMesh.chunks == null) return;

            if (!gMesh.paintMesh) {
                gMesh.paintMesh = gMesh.chunkedMesh;
            }

            if (highlightAll) {
                mat.SetVector(paintHighlightBrushParamsID, new Vector4(0, 0, 999999, 0.25f));
                mat.SetVector(paintHightlightColorID, new Vector4(0.5f, 0.75f, 1f, 0.25f));
            }

            mat.SetFloat(paintHightlightTypeID, gMesh.renderType == GrassRenderType.Terrain ? 1 : 0);

            int subIdx = 0;
            foreach (var chunk in gMesh.chunks) {

                if (!gMesh.paintMesh) {
                    if (gMesh.renderType == GrassRenderType.Terrain) {
                        gMesh.paintMesh = await MeshChunker.CreatePlaneMesh(gMesh, chunk.worldBounds.size);
                    }
                    else {
                        gMesh.shouldDraw = false;
                        gMesh.chunks = null;
                        continue;
                    }
                }

                if (gMesh.renderType == GrassRenderType.Terrain) {
                    subIdx = 0;
                }

                gMesh.paintMesh.bounds = chunk.meshBounds;

                Graphics.DrawMesh(gMesh.paintMesh, gMesh.terrainTransform.localToWorldMatrix, mat, renderLayer,
                    cam, subIdx++, chunk.pBlock, gMesh.shadowMode, gMesh.receiveShadows);
            }
        }







        //--------------------------------    
        //RIPPLES-------------------------
        //--------------------------------
        bool gameStarted = false;
        private void LateUpdate() {
            gameStarted = true;
            UpdateRipples();
        }

        void UpdateRipples() {
            //runRipple is static to avoid conflicts where multiple grass renderers may try to update them more than once per frame 
            //but ripples are shared across all renderers so this wouldnt make sense
            if (runRipple && updateRipples) {
                runRipple = false;
                gfComputeShader.SetFloat(ripDeltaTimeHash, Time.deltaTime);
                gfComputeShader.Dispatch(updateRippleKernel, 1, 1, 1);
            }

            UpdateForces();
        }


        static int ripPosID = Shader.PropertyToID("pos");
        static int drssParamsID = Shader.PropertyToID("drssParams");

        /// <summary>
        /// Adds a ripple into the ripple buffer that affects all grasses.
        /// Ripples are just that, ripples that animate accross the grass, a simple visual effect.
        /// </summary>
        /// <param name="pos">World position the ripple is placed at.</param>
        /// <param name="strength">How forceful the ripple is.</param>
        /// <param name="decayRate">How quickly the ripple dissipates.</param>
        /// <param name="speed">How fast the ripple moves across the grass.</param>
        /// <param name="startRadius">Start size of the ripple.</param>
        /// <param name="sharpness">How much this ripple appears like a ring rather than a circle.</param>
        public static void AddRipple(Vector3 pos, float strength = 1f, float decayRate = 2.5f, float speed = 25f, float startRadius = 0f, float sharpness = 0f) {
            if (!gfComputeShader) return;

            //print(new Vector4(pos.x, pos.y, pos.z, strength));
            //print(new Vector4(decayRate, startRadius, sharpness, speed));
            gfComputeShader.SetVector(ripPosID, new Vector4(pos.x, pos.y, pos.z, strength));
            gfComputeShader.SetVector(drssParamsID, new Vector4(decayRate, startRadius, sharpness, speed));
            gfComputeShader.Dispatch(addRippleKernel, 1, 1, 1);
            updateRipples = true;
        }

        /// <summary>
        /// Adds a ripple into the ripple buffer that affects all grasses.
        /// Ripples are just that, ripples that animate accross the grass, a simple visual effect.
        /// </summary>
        /// <param name="pos">World position the ripple is placed at.</param>
        /// <param name="strength">How forceful the ripple is.</param>
        /// <param name="decayRate">How quickly the ripple dissipates.</param>
        /// <param name="speed">How fast the ripple moves across the grass.</param>
        /// <param name="startRadius">Start size of the ripple.</param>
        /// <param name="sharpness">How much this ripple appears like a ring rather than a circle.</param>
        public void AddARipple(Vector3 pos, float strength = 1f, float decayRate = 2.5f, float speed = 25f, float startRadius = 0f, float sharpness = 0f) {
            AddRipple(pos, strength, decayRate, speed, startRadius, sharpness);
        }

        //--------------------------------------------------------------------------------
        //------------------------FORCES---------------------------------------
        //--------------------------------------------------------------------------------

        static Vector3 mainForcePos;
        static Vector4 mainForceParams;


        /// <summary>
        /// Adds a point-source constant force that pushes all grasses.
        /// <para>Store the returned force and change its values to update it.</para>
        /// </summary>
        public GrassForce AddForce(Vector3 pos, float radius, float strength) {
            return AddGrassForce(pos, radius, strength);
        }

        /// <summary>
        /// Removes the given GrassForce.
        /// </summary>
        public void RemoveForce(GrassForce force) {
            RemoveGrassForce(force);
        }
        /// <summary>
        /// Removes the given GrassForce.
        /// </summary>
        public static void RemoveGrassForce(GrassForce force) {
            force.Remove();
        }

        /// <summary>
        /// Adds a point-source constant force that pushes all grasses.
        /// <para>Store the returned force and change its values to update it.</para>
        /// </summary>
        public static GrassForce AddGrassForce(Vector3 pos, float radius, float strength) {
            if (forcesArray == null) {
                return null;
            }

            if (forcesCount >= maxForces) {
                return null;
            }

            GrassForce force = new GrassForce() {
                index = forcesCount,
                position = pos,
                radius = radius,
                strength = strength,
            };

            force.Add();

            return force;
        }


        void UpdateForces() {
            if (forcesDirty) {
                //print("update forces: " + forcesCount);
                //start at maxRipples in compute buffer to skip over the ripple section
                forcesBuffer.SetData(forcesArray, 0, maxRipples + 1, forcesCount);
                forcesDirty = false;

                Shader.SetGlobalInt(forcesCountID, forcesCount);
                Shader.SetGlobalVector(mainForcePosID, mainForcePos);
                Shader.SetGlobalVector(mainForceParamID, mainForceParams);
            }
        }

        //--------------------------------    
        //PAINTING------------------------
        //--------------------------------    
        public static bool useFloatFormatColorMap = false;
        public static bool useFloatFormatParam = false;
        static int mapToPaintID = Shader.PropertyToID("mapToPaint");
        static int brushTextureID = Shader.PropertyToID("brushTexture");
        const float paintThreads = 8f;

        public GrassMesh GetGrassMeshFromTransform(Transform t) {
            return GetGrassMeshesFromTransform(t)?.FirstOrDefault();
        }
        public HashSet<GrassMesh> GetGrassMeshesFromTransform(Transform t) {
            if (!t) return null;
            gMeshDict.TryGetValue(t, out var gList);
            return gList;
        }

        public bool MultipleMeshesForTransform(Transform t) {
            if (!t) return false;
            if (!gMeshDict.TryGetValue(t, out var gList)) {
                return false;
            }
            return gList.Count > 1;
        }

        /// <summary>
        /// Sets the texture to be used when calling paint functions.
        /// </summary>
        public void SetBrushTexture(Texture2D brushTex) {
            if (paintShader) paintShader.SetTexture(paintKernel, brushTextureID, brushTex);
        }

        /// <summary>
        /// Sets the texture to be used when calling paint functions.
        /// </summary>
        public static void SetPaintBrushTexture(Texture2D brushTex) {
            if (paintShader) paintShader.SetTexture(paintKernel, brushTextureID, brushTex);
        }

        /// <summary>
        /// Paints color onto the colormap.
        /// enableMapPainting needs to be turned on for this to work.
        /// Uses a global texture as the brush texture, should be set via SetPaintBrushTexture(Texture2D brushTex).
        /// </summary>
        /// <param name="texCoord">texCoord to paint at, usually obtained by a raycast.</param>
        /// <param name="clampRange">Clamp the painted values between this range. Not really used for colors but exists just in case.
        /// Should be set to 0 to 1 or greater than 1 for HDR colors.</param>
        public void PaintColor(GrassMesh gMesh, Vector2 texCoord, float brushSize, float brushStrength, Color colorToPaint, Vector2 clampRange, float blendMode = 0f) {
            PaintDispatch(texCoord - gMesh.colorMapHalfPixUV, brushSize, brushStrength, colorToPaint, gMesh.colorMapRT, clampRange, blendMode);
        }

        /// <summary>
        /// Paints parameters onto the paramMap.
        /// enableMapPainting needs to be turned on for this to work.
        /// Uses a global texture as the brush texture, should be set via SetPaintBrushTexture(Texture2D brushTex).
        /// </summary>
        /// <param name="texCoord">texCoord to paint at, usually obtained by a raycast.</param>
        /// <param name="densityAmnt">Amount density to paint.</param>
        /// <param name="heightAmnt">Amount height to paint.</param>
        /// <param name="flattenAmnt">Amount flatten to paint.</param>
        /// <param name="windAmnt">Amount wind to paint.</param>
        /// <param name="clampRange">Clamp the painted values between this range. Valid range for parameters is 0 to 1.</param>
        public void PaintParameters(GrassMesh gMesh, Vector2 texCoord, float brushSize, float brushStrength, float densityAmnt, float heightAmnt, float flattenAmnt, float windAmnt, Vector2 clampRange) {
            PaintDispatch(texCoord - gMesh.paramMapHalfPixUV, brushSize, brushStrength, new Vector4(densityAmnt, heightAmnt, flattenAmnt, windAmnt), gMesh.paramMapRT, clampRange, 1f);
        }


        /// <summary>
        /// A more manual paint function that you most likely don't want to use.
        /// It's mostly only exposed so that the GrassFlowInspector can use it. But maybe you want to too, I'm not the boss of you.
        /// You could use this to paint onto your own RenderTextures.
        /// <para>Also requires manually subtracting the half pixel UV Offset of the map from the texCoord.</para>
        /// </summary>
        /// <param name="blendMode">Controls blend type: 0 for lerp towards, 1 for additive</param>
        public static void PaintDispatch(Vector2 texCoord, float brushSize, float brushStrength, Vector4 blendParams, RenderTexture mapRT, Vector2 clampRange, float blendMode) {
            if (!paintShader || !mapRT) return;

            //print(brushSize + " : "+ brushStrength + " : " + texCoord + " : " + blendParams + " : " +clampRange + " : " + blendMode);
            //srsBrushParams = (strength, radius, unused, alpha controls type/ 0 for lerp towards, 1 for additive)
            paintShader.SetVector(srsBrushParamsID, new Vector4(brushStrength, brushSize * 0.05f, 0, blendMode));
            paintShader.SetVector(clampRangeID, clampRange);

            paintShader.SetVector(brushPosID, texCoord);
            paintShader.SetVector(blendParamsID, blendParams);

            PaintShaderExecute(mapRT, paintKernel);
            //paintShader.Dispatch(paintKernel, Mathf.CeilToInt(mapRT.width / paintThreads), Mathf.CeilToInt(mapRT.height / paintThreads), 1);
        }

        static void PaintShaderExecute(RenderTexture mapRT, int pass) {
            //paintMat.SetTexture(mapToPaintID, mapRT);
            paintShader.SetTexture(pass, mapToPaintID, mapRT);

            RenderTexture tmpRT = RenderTexture.GetTemporary(mapRT.width, mapRT.height, 0, mapRT.format);
            if (!tmpRT.IsCreated()) {
                //I think theres some kind of bug on older versions of unity where sometimes,
                //at least in certain situations, RenderTexture.GetTemporary() returns you
                //a texture that hasn't actually been created. Go figure.
                //It'll still work fine with Graphics.Blit, but it won't work with Graphics.CopyTexture()
                //unless we make sure its created first like this
                //this will only happen once usually, as internally unity will reuse this texture next time we ask for it.
                //but will be discarded after a few frames of un-use
                tmpRT.Create();
            }
            //Graphics.CopyTexture(mapRT, tmpRT); //copytexture for some reason didnt work on URP last time i checked
            Graphics.Blit(mapRT, tmpRT);
            paintShader.SetTexture(pass, tmpMapRTID, tmpRT);

            paintShader.Dispatch(pass, Mathf.CeilToInt(mapRT.width / paintThreads), Mathf.CeilToInt(mapRT.height / paintThreads), 1);
            //Graphics.Blit(tmpRT, mapRT, paintMat, pass);
            RenderTexture.ReleaseTemporary(tmpRT);
        }

        /// <summary>
        /// Automatically controls grass density based on a splat layer from terrain data.
        /// </summary>
        /// <param name="splatLayer">Zero based index of the splat layer from the terrain to use.</param>
        /// <param name="mode">Controls how the tex is applied. 0 = additive, 1 = subtractive, 2 = replace.</param>
        /// <param name="splatTolerance">Controls opacity tolerance.</param>
        public void ApplySplatTex(GrassMesh gMesh, int splatLayer, int mode, float splatTolerance) {
            int channel = splatLayer % 4;
            int texIdx = splatLayer / 4;


            ApplySplatTex(gMesh.terrainObject.terrainData.alphamapTextures[texIdx], gMesh.paramMapRT, channel, mode, splatTolerance);
        }

        /// <summary>
        /// Automatically controls grass density based on a splat tex.
        /// </summary>
        /// <param name="splatAlphaMap">The particular splat alpha map texture that has the desired splat layer on it.</param>
        /// <param name="channel">Zero based index of the channel of the texture that represents the splat layer.</param>
        /// <param name="mode">Controls how the tex is applied. 0 = additive, 1 = subtractive, 2 = replace.</param>
        /// <param name="splatTolerance">Controls opacity tolerance.</param>
        public void ApplySplatTex(Texture2D splatAlphaMap, RenderTexture paramMapRT, int channel, int mode, float splatTolerance) {
            if (!enableMapPainting || !paramMapRT) {
                Debug.LogError("Couldn't apply splat tex, map painting not enabled!");
                return;
            }

            paintShader.SetTexture(splatKernel, "splatTex", splatAlphaMap);
            paintShader.SetTexture(splatKernel, "mapToPaint", paramMapRT);

            paintShader.SetInt("splatMode", mode);
            paintShader.SetInt("splatChannel", channel);

            paintShader.SetFloat("splatTolerance", splatTolerance);

            PaintShaderExecute(paramMapRT, splatKernel);
            //paintShader.Dispatch(splatKernel, Mathf.CeilToInt(paramMapRT.width / paintThreads), Mathf.CeilToInt(paramMapRT.width / paintThreads), 1);
        }


        //
        //Shader Property IDs
        //
        //base shader

        static int objToWorldMatrixID = Shader.PropertyToID("objToWorldMatrix");
        static int worldToObjMatrixID = Shader.PropertyToID("worldToObjMatrix");

        static int mainForcePosID = Shader.PropertyToID("mainForcePos");
        static int mainForceParamID = Shader.PropertyToID("mainForceParam");

        static int forcesBufferID = Shader.PropertyToID("forcesBuffer");
        static int rippleCountID = Shader.PropertyToID("rippleCount");
        static int forcesCountID = Shader.PropertyToID("forcesCount");
        static int _NoiseTexID = Shader.PropertyToID("_NoiseTex");
        static int _NoiseTex2DID = Shader.PropertyToID("_NoiseTex2D");
        static int NoiseResultID = Shader.PropertyToID("NoiseResult");
        static int numTexturesID = Shader.PropertyToID("numTextures");
        static int numTexturesPctUVID = Shader.PropertyToID("numTexturesPctUV");
        static int grassPosBufferID = Shader.PropertyToID("grassPosBuffer");
        static int posIdBufferID = Shader.PropertyToID("posIdBuffer");
        static int maxVertHID = Shader.PropertyToID("maxVertH");
        static int terrainSlopeThreshID = Shader.PropertyToID("terrainSlopeThresh");
        static int terrainSlopeFadeID = Shader.PropertyToID("terrainSlopeFade");
        //
        //instance props
        static int _instanceLodID = Shader.PropertyToID("_instanceLod");
        static int terrainTriCountID = Shader.PropertyToID("terrainTriCount");
        static int posBufferOffsetID = Shader.PropertyToID("posBufferOffset");
        static int lodMultID = Shader.PropertyToID("lodMult");
        static int grassPerTriID = Shader.PropertyToID("grassPerTri");
        static int meshInvVertCountID = Shader.PropertyToID("meshInvVertCount");
        static int batchDataID = Shader.PropertyToID("batchData");
        static int batchAlphaID = Shader.PropertyToID("batchAlpha");
        //
        //painting
        static int srsBrushParamsID = Shader.PropertyToID("srsBrushParams");
        static int clampRangeID = Shader.PropertyToID("clampRange");
        static int brushPosID = Shader.PropertyToID("brushPos");
        static int blendParamsID = Shader.PropertyToID("blendParams");
        static int tmpMapRTID = Shader.PropertyToID("tmpMapRT");

        static void ReleaseBuffers() {
            if (forcesBuffer != null) forcesBuffer.Release();
            forcesBuffer = null;
            forceClassArray = null;
            forcesArray = null;
        }



        public void OnDestroy() {
            //double up to be safe
            UnHookRender();
            UnHookRender();

            ReleaseAssets();

            ReleaseBuffers();

            instances.Remove(this);
        }
    }
}