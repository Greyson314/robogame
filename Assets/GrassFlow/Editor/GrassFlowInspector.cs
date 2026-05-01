#if UNITY_EDITOR

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEditorInternal;
using System.Reflection;
using System.Linq;
using System.Linq.Expressions;
using System.IO;
using GrassFlow.Localization;

using static GrassFlow.GrassMesh;

namespace GrassFlow {

    [CustomEditor(typeof(GrassFlowRenderer)), CanEditMultipleObjects]
    public class GrassFlowInspector : Editor {

        GUIStyle bold = new GUIStyle();
        GUIStyle boldFold = new GUIStyle();
        GUIStyle selectedLabel = new GUIStyle();
        GUIStyle center = new GUIStyle();
        GUIStyle label = new GUIStyle();
        GUIStyle errorLabel = new GUIStyle();
        GUIStyle xBtn = new GUIStyle();
        GUIStyle plusBtn = new GUIStyle();

        Color errorRed = new Color(0.85f, 0.15f, 0.15f);
        Color selectedBlue = new Color(0.25f, 0.35f, 0.55f);
        Color selectedBlueText = new Color(0.25f, 0.35f, 0.55f);
        Color editorTextCol;



        void SetStyles() {

            editorTextCol = EditorStyles.label.normal.textColor;

            //this color math is so dumb lol
            selectedBlue = new Color(0.4f, 0.45f, 0.55f) * ((1f - editorTextCol.r + editorTextCol.r) * 2f);
            selectedBlueText = GUI.skin.settings.selectionColor + GUI.skin.settings.selectionColor * editorTextCol * 0.5f;


            bold = new GUIStyle(EditorStyles.boldLabel);
            bold.fontStyle = FontStyle.Bold;
            bold.fontSize = 12;
            bold.normal.textColor = editorTextCol * 1.5f;

            boldFold = new GUIStyle(EditorStyles.foldout);
            boldFold.fontStyle = FontStyle.Bold;
            boldFold.fontSize = 12;
            boldFold.margin = new RectOffset(16, 4, 4, 4);

            center.alignment = TextAnchor.LowerCenter;
            center.fontStyle = FontStyle.Bold;
            center.fontSize = 12;

            label = new GUIStyle(EditorStyles.toolbarButton);
            label.padding = new RectOffset(4, 0, 0, 0);
            label.alignment = TextAnchor.MiddleLeft;
            label.fontSize = 12;

            selectedLabel = new GUIStyle(EditorStyles.miniButton);
            selectedLabel.fontStyle = FontStyle.Bold;
            selectedLabel.alignment = TextAnchor.UpperLeft;
            selectedLabel.padding = new RectOffset(4, 4, 2, 2);
            selectedLabel.fontSize = 12;
            selectedLabel.margin = new RectOffset();
            selectedLabel.normal.textColor = selectedBlueText;

            xBtn = new GUIStyle(EditorStyles.toolbarButton);
            xBtn.margin = new RectOffset(10, 10, 0, 0);

            plusBtn = new GUIStyle(EditorStyles.miniButton) {
                fontSize = 13,
                padding = new RectOffset(),
            };

            errorLabel = new GUIStyle(bold);
            errorLabel.normal.textColor = errorRed;
        }


        public static GrassMesh currentlyDrawnMesh;
        MaterialEditor matEditor;

        [SerializeField] static int mainTabIndex = 0;
        [SerializeField] static int selectedPaintToolIndex = 0;
        [SerializeField] static int selectedBrushIndex = 0;
        [SerializeField] static bool continuousPaint = false;
        [SerializeField] static bool useDeltaTimePaint = true;
        [SerializeField] static LayerMask paintRaycastMask = Physics.AllLayers;
        [SerializeField] static Vector2 clampRange = new Vector2(0, 1);
        [SerializeField] static bool shouldPaint = false;
        [SerializeField] static float paintBrushSize = 0.5f;
        [SerializeField] static float paintBrushStrength = 0.1f;
        [SerializeField] static Color paintBrushColor = new Color(1, 1, 1, 0);
        [SerializeField] static int grassTypeAtlasIdx = 1;
        [SerializeField] static bool useBrushOpacity = true;
        [SerializeField] static PaintToolType paintToolType = PaintToolType.Color;
        [SerializeField] static int splatMapLayerIdx = 0;
        [SerializeField] static float splatMapTolerance = 0;

        [SerializeField] static Vector2 selectMeshScrollPos;
        [SerializeField] static AnimBool meshSelectionExpanded;

        [SerializeField] static int gfPlayCount;
        [SerializeField] static bool showMultiRendererWarning = true;



        [SerializeField] static PaintHistory currentPaintHistory;
        [System.Serializable]
        class PaintUndoRedoController : ScriptableObject {
            public GrassFlowRenderer grass;
            public List<PaintHistory> paintHistory = new List<PaintHistory>();
        }
        [SerializeField] static PaintUndoRedoController paintUndoRedoController;
        static void PaintUndoRedoCallback() {
            HandlePaintUndoRedo();
        }


        const int _MaxTexAtlasSize = 16;
        const float _TexAtlasOff = 1f / 256f;

        static BrushList _BrushList;
        static BrushList brushList {
            get {
                if (_BrushList == null) { _BrushList = new BrushList(); }
                return _BrushList;
            }
            set {
                _BrushList = value;
            }
        }

        public static readonly HashSet<GrassFlowMapEditor.MapType> dirtyTypes = new HashSet<GrassFlowMapEditor.MapType>();


        enum PaintToolType {
            Color = 0,
            Density = 1,
            Height = 2,
            Flat = 3,
            Wind = 4,
            Type = 5,
        }


        GrassFlowRenderer _grassFlow;
        GrassFlowRenderer grassFlow {
            get {
                if (!_grassFlow) {
                    _grassFlow = (GrassFlowRenderer)target;
                }
                return _grassFlow;
            }
        }


        private void OnEnable() {
            LoadInspectorSettings();

            //GrassFlowRenderer.instances.Add(grassFlow);

            if (!paintUndoRedoController || paintUndoRedoController.grass != grassFlow) {
                paintUndoRedoController = CreateInstance<PaintUndoRedoController>();
                paintUndoRedoController.grass = grassFlow;
            }

            meshSelectionExpanded.valueChanged.AddListener(Repaint);

            EditorApplication.playModeStateChanged -= EditorApplication_playModeStateChanged;
            EditorApplication.playModeStateChanged += EditorApplication_playModeStateChanged;

            Undo.undoRedoPerformed += UndoRedoCallback;

            Undo.undoRedoPerformed -= PaintUndoRedoCallback;
            Undo.undoRedoPerformed += PaintUndoRedoCallback;

#if UNITY_2019_1_OR_NEWER
            SceneView.duringSceneGui += SceneGUICallback;
#else
        SceneView.onSceneGUIDelegate += SceneGUICallback;
#endif
        }

        private static void EditorApplication_playModeStateChanged(PlayModeStateChange obj) {
            if (obj == PlayModeStateChange.ExitingEditMode) {
                SaveDatas(prompt: true);
            }
        }

        private void OnDisable() {

            SaveInspectorSettings();
            DisablePaintHighlight(true);

            Undo.undoRedoPerformed -= UndoRedoCallback;

#if UNITY_2019_1_OR_NEWER
            SceneView.duringSceneGui -= SceneGUICallback;
#else
        SceneView.onSceneGUIDelegate -= SceneGUICallback;
#endif

            //need to make sure brush list gets cleared or else it could not load properly later
            brushList = null;

            GrassFlowRenderer.isPaintingOpen = false;
        }

        void CheckURP() {
#if !GRASSFLOW_SRP
            if (PipelineMaterialChecker.CheckURP()) {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.HelpBox(Locale.GetMsg("urpDetected"), MessageType.Warning);
                if (GUILayout.Button(Locale.GetMsg("enableURPSupport"))) {
                    GrassFlowRenderer.ToggleSRP();
                }
                EditorGUILayout.EndHorizontal();
            }
#endif
#if !GRASSFLOW_HDRP
            if (PipelineMaterialChecker.CheckHDRP()) {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.HelpBox(Locale.GetMsg("hdrpDetected"), MessageType.Warning);
                if (GUILayout.Button(Locale.GetMsg("enableHDRPSupport"))) {
                    GrassFlowRenderer.ToggleHDRP();
                }
                EditorGUILayout.EndHorizontal();
            }
#endif
        }

        void DrawWarnings() {

            CheckURP();

            serializedObject.Update();

#if GRASSFLOW_SRP
        EditorGUILayout.LabelField(Locale.GetMsg("urpModeActive"), EditorStyles.helpBox);
#endif
#if GRASSFLOW_HDRP
        EditorGUILayout.LabelField(Locale.GetMsg("hdrpModeActive"), EditorStyles.helpBox);
#endif

            if (gfPlayCount >= 50) {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.LabelField(Locale.GetMsg("enjoyingGrassFlow"), EditorStyles.boldLabel);

                if (GUILayout.Button(Locale.GetMsg("leaveReview"))) {
                    Application.OpenURL("https://assetstore.unity.com/packages/slug/219758#reviews");
                    gfPlayCount = UnityEngine.Random.Range(-500, -1000);
                    EditorPrefs.SetInt("GF_Count", gfPlayCount);
                }
                if (GUILayout.Button(Locale.GetMsg("dismiss"))) {
                    gfPlayCount = UnityEngine.Random.Range(0, 25);
                    EditorPrefs.SetInt("GF_Count", gfPlayCount);
                }
                EditorGUILayout.EndHorizontal();
            }

            if (grassFlow.selectedIndices.Count > 1) return;
            GrassMesh drawGMesh = grassFlow.GetSelectedGrassMesh();

            //if (showMultiRendererWarning) {
            if (renderers != null && renderers.Length > 1) {
                EditorGUILayout.HelpBox(Locale.GetMsg("multipleRenderersWarning"), MessageType.Error);
                //if (GUILayout.Button("Dismiss")) {
                //    EditorPrefs.SetBool("GF_MULTIPLE_RENDERERS_WARNING", false);
                //    showMultiRendererWarning = false;
                //}
            }
            //}

            if (!drawGMesh.terrainTransform) {
                EditorGUILayout.HelpBox(Locale.GetMsg("terrainTransformMissing"), MessageType.Error);
            }

            if (drawGMesh.renderType == GrassFlowRenderer.GrassRenderType.Mesh) {
                if (!drawGMesh.grassMesh) {
                    EditorGUILayout.HelpBox(Locale.GetMsg("grassMeshMissing"), MessageType.Error);
                }
            }
            else {
                if (!drawGMesh.terrainObject) {
                    EditorGUILayout.HelpBox(Locale.GetMsg("terrainMissing"), MessageType.Error);
                }
            }

            if (mainTabIndex == 1 && grassFlow.enableMapPainting) {
                if (!GetPaintMap(paintToolType, drawGMesh)) {
                    EditorGUILayout.HelpBox(Locale.GetMsg("paintTextureMissing"), MessageType.Warning);
                }
            }

            if (!drawGMesh.mainGrassMat) {
                EditorGUILayout.HelpBox(Locale.GetMsg("grassMaterialMissing"), MessageType.Error);
            }

            if (!drawGMesh.customGrassMesh) {
                EditorGUILayout.HelpBox(Locale.GetMsg("customMeshMissing"), MessageType.Error);
            }
        }


        public override void OnInspectorGUI() {


            baseUIColor = GUI.backgroundColor;
            Event e = Event.current;
            HandleHotkeys(e);

            SetStyles();

            DrawWarnings();

            EditorGUILayout.Space();

            if (GUILayout.Button(GetLocalContent("refreshButton"))) {
                SaveData(grassFlow, prompt: true);
                GrassFlowRenderer.instances = new HashSet<GrassFlowRenderer>(FindObjectsOfType<GrassFlowRenderer>());
                grassFlow.Refresh();
                return;
            }

            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            int tabIndex = GUILayout.Toolbar(mainTabIndex, new string[] { Locale.GetName("settingsTab"), Locale.GetName("paintModeTab") });
            if (EditorGUI.EndChangeCheck()) {
                grassFlow.selectedIdx = grassFlow.selectedIdx;
                SaveData(grassFlow, prompt: true);
                mainTabIndex = tabIndex;
            }

            switch (mainTabIndex) {
                case 0:
                    DrawSettingsGUI();
                    break;

                case 1:
                    DrawPaintGUI();
                    break;
            }

            serializedObject.ApplyModifiedProperties();
        }


        public void UndoRedoCallback() {

            foreach (var gMesh in grassFlow.terrainMeshes) {

                gMesh.instanceCount = gMesh.instanceCount;
                gMesh.shouldDraw = false;

                if (gMesh.chunks == null && gMesh.hasRequiredAssets) {
                    gMesh.Reload();
                }

                gMesh.castShadows = gMesh.castShadows;
            }

            grassFlow.enableMapPainting = grassFlow.enableMapPainting;

            brushList.UpdateSelection(selectedBrushIndex);

            Repaint();
        }

        static void HandlePaintUndoRedo() {
            if (!paintUndoRedoController.grass) return;
            paintUndoRedoController.grass.RevertDetailMaps();

            int storeBrushIdx = selectedBrushIndex;

            foreach (PaintHistory history in paintUndoRedoController.paintHistory) {
                if (!history.gMesh) continue;

                var realGMesh = paintUndoRedoController.grass.GetGrassMeshFromTransform(history.gMesh.terrainTransform);
                if (!realGMesh) continue;

                RenderTexture paintMap = GetPaintMapRT(history.paintType, realGMesh);
                if (!paintMap) continue;

                selectedBrushIndex = history.brushIdx;
                GrassFlowRenderer.SetPaintBrushTexture(GetActiveBrushTexture());

                foreach (PaintHistory.PaintAction action in history.paintActions) {
                    action.Dispatch(history, paintMap);
                }
            }

            selectedBrushIndex = storeBrushIdx;
            GrassFlowRenderer.SetPaintBrushTexture(GetActiveBrushTexture());
        }

        static void ClearPaintUndoHistory() {
            Undo.RecordObject(paintUndoRedoController, Locale.GetMsg("undoRevertMaps"));
        }


        Color baseUIColor;

        void DrawUIForGmesh(GrassMesh drawGMesh) {
            EditorGUI.BeginChangeCheck();

            Mesh terrainMesh = drawGMesh.grassMesh;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(GetLocalContent("globalRendererSettings"), bold);
            bool updateBuffers = EditorGUILayout.ToggleLeft(GetContent(() => grassFlow.updateBuffers), grassFlow.updateBuffers);
            bool useMaterialInstance = EditorGUILayout.ToggleLeft(GetContent(() => grassFlow.useMaterialInstance), grassFlow.useMaterialInstance);
            int renderLayer = EditorGUILayout.LayerField(GetContent(() => grassFlow.renderLayer), grassFlow.renderLayer);

            uint renderingLayer = drawGMesh.renderingLayer;
#if UNITY_6000_0_OR_NEWER
            renderingLayer = EditorGUILayout.RenderingLayerMaskField(GetContent(() => drawGMesh.renderingLayer), drawGMesh.renderingLayer);
#else
            renderingLayer = 1;
#endif



            EditorGUILayout.Space();
            EditorGUILayout.LabelField(GetLocalContent("grassRenderProperties"), bold);

            bool asyncInitialization = EditorGUILayout.ToggleLeft(GetContent(() => drawGMesh.asyncInitialization), drawGMesh.asyncInitialization);
            bool bakeDensity = EditorGUILayout.ToggleLeft(GetContent(() => drawGMesh.bakeDensity), drawGMesh.bakeDensity);
            bool bakeData = drawGMesh.bakeData;
            if (!drawGMesh.frustumCull) {
                bakeData = EditorGUILayout.ToggleLeft(GetContent(() => drawGMesh.bakeData), drawGMesh.bakeData);
            }

            int instanceCount = EditorGUILayout.DelayedIntField(GetContent(() => drawGMesh.instanceCount), drawGMesh.instanceCount);
            //int meshSubdivCount = EditorGUILayout.DelayedIntField(GetContent(() => drawGMesh.grassPerTri), drawGMesh.grassPerTri);
            int meshSubdivCount = 1;


            Material grassMaterial = null;
            Mesh frustumMesh = null;
            bool updatedLods = false;
            bool crossFade = drawGMesh.crossFade;
            float crossFadeRange = drawGMesh.crossFadeRange;
            if (drawGMesh.frustumCull) {
                var meshLod = drawGMesh.customGrassMesh;
                if (!drawGMesh.customGrassMesh) {
                    GUI.backgroundColor = errorRed;
                }
                GUIContent lodMeshContent = GetContent(() => drawGMesh.customGrassMesh);
                frustumMesh = EditorGUILayout.ObjectField(lodMeshContent, meshLod, typeof(Mesh), true) as Mesh;
                GUI.backgroundColor = baseUIColor;

                var matLod = drawGMesh.mainGrassMat;
                if (!matLod) {
                    GUI.backgroundColor = errorRed;
                }

                GUIContent lodMatContent = GetContent(() => drawGMesh.mainGrassMat);
                grassMaterial = EditorGUILayout.ObjectField(lodMatContent, matLod, typeof(Material), true) as Material;
                GUI.backgroundColor = baseUIColor;
            }
            else {

                if (!drawGMesh.hasLodAssets()) GUI.backgroundColor = errorRed;

                EditorGUI.BeginChangeCheck();
                var prop = serializedObject.FindProperty(nameof(grassFlow.terrainMeshes))
                    .GetArrayElementAtIndex(drawGMesh.grassIdx).FindPropertyRelative(nameof(drawGMesh.customMeshLods));
                var lodsContent = GetContent(() => drawGMesh.customMeshLods);
                EditorGUILayout.PropertyField(prop, lodsContent);
                if (EditorGUI.EndChangeCheck()) {
                    if (terrainMesh) {
                        updatedLods = true;
                        serializedObject.ApplyModifiedProperties();
                    }
                }
                GUI.backgroundColor = baseUIColor;

                if (drawGMesh.customMeshLods.Length > 1) {
                    crossFade = EditorGUILayout.ToggleLeft(GetContent(() => drawGMesh.crossFade), drawGMesh.crossFade);

                    if (crossFade) {
                        crossFadeRange = EditorGUILayout.FloatField(GetContent(() => drawGMesh.crossFadeRange), drawGMesh.crossFadeRange);
                    }
                }
            }
            GUI.backgroundColor = baseUIColor;

            bool receiveShadows = EditorGUILayout.ToggleLeft(GetContent(() => drawGMesh.receiveShadows), drawGMesh.receiveShadows);
            bool castShadows = EditorGUILayout.ToggleLeft(GetContent(() => drawGMesh.castShadows), drawGMesh.castShadows);


            //----------------Terrain--------------------
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(GetLocalContent("terrainHeader"), bold);

            if (!drawGMesh.terrainTransform) {
                GUI.backgroundColor = errorRed;
            }
            Transform terrainTransform = EditorGUILayout.ObjectField(GetContent(() => drawGMesh.terrainTransform), drawGMesh.terrainTransform, typeof(Transform), true) as Transform;
            GUI.backgroundColor = baseUIColor;

            GrassFlowRenderer.GrassRenderType renderType = (GrassFlowRenderer.GrassRenderType)EditorGUILayout.EnumPopup(GetContent(() => drawGMesh.renderType), drawGMesh.renderType);


            float terrainSlopeThresh = EditorGUILayout.Slider(GetContent(() => drawGMesh.terrainSlopeThresh), drawGMesh.terrainSlopeThresh, -1, 1);
            float terrainSlopeFade = EditorGUILayout.Slider(GetContent(() => drawGMesh.terrainSlopeFade), drawGMesh.terrainSlopeFade, 0, 5);


            Terrain terrainObject = drawGMesh.terrainObject;
            float terrainExpansion = grassFlow.terrainExpansion;
            bool normalizeMeshDensity = grassFlow.normalizeMeshDensity;
            float normalizeMaxRatio = drawGMesh.normalizeMaxRatio;
            switch (renderType) {
                case GrassFlowRenderer.GrassRenderType.Mesh:
                    EditorGUILayout.BeginHorizontal();
                    normalizeMeshDensity = EditorGUILayout.ToggleLeft(GetContent(() => grassFlow.normalizeMeshDensity), grassFlow.normalizeMeshDensity);

                    if (normalizeMeshDensity) {
                        var cont = GetContent(() => drawGMesh.normalizeMaxRatio);
                        cont.text = Locale.GetName("maxRatio");
                        EditorGUILayout.LabelField(cont, GUILayout.Width(80));
                        normalizeMaxRatio = EditorGUILayout.DelayedFloatField(drawGMesh.normalizeMaxRatio);
                    }
                    EditorGUILayout.EndHorizontal();

                    if (!drawGMesh.grassMesh) GUI.backgroundColor = errorRed;
                    terrainMesh = EditorGUILayout.ObjectField(GetContent(() => drawGMesh.grassMesh), drawGMesh.grassMesh, typeof(Mesh), true) as Mesh;
                    GUI.backgroundColor = baseUIColor;

                    break;

                case GrassFlowRenderer.GrassRenderType.Terrain:


                    if (!drawGMesh.terrainObject) GUI.backgroundColor = errorRed;
                    terrainObject = EditorGUILayout.ObjectField(GetContent(() => drawGMesh.terrainObject), drawGMesh.terrainObject, typeof(Terrain), true) as Terrain;
                    GUI.backgroundColor = baseUIColor;

                    terrainExpansion = EditorGUILayout.FloatField(GetContent(() => grassFlow.terrainExpansion), grassFlow.terrainExpansion);
                    break;
            }


            EditorGUILayout.Space();
            EditorGUILayout.LabelField(GetLocalContent("lodHeader"), bold);
            int lodSteps = EditorGUILayout.DelayedIntField(GetContent(() => drawGMesh.lodSteps), drawGMesh.lodSteps);
            float maxRenderDist = EditorGUILayout.FloatField(GetContent(() => drawGMesh.maxRenderDist), drawGMesh.maxRenderDist);
            Vector3 lodParams = EditorGUILayout.Vector3Field(GetContent(() => drawGMesh.lodParams), drawGMesh.lodParams);
            Vector3Int lodChunks = new Vector3Int();

            if (meshSubdivCount <= 0) meshSubdivCount = 1;
            if (lodSteps <= 0) lodSteps = 1;
            if (lodParams.x < 1) lodParams.x = 1;


            GUIContent chunksContent;

            if (renderType == GrassFlowRenderer.GrassRenderType.Mesh) {
                chunksContent = GetLocalContent("meshLodChunks");
            }
            else {
                chunksContent = GetLocalContent("terrainLodChunks");
            }

            EditorGUILayout.BeginHorizontal();
            //let the record show that i hate this and its stupid but theres no DelayedVectorField sooooooooo heck everything
            EditorGUILayout.PrefixLabel(chunksContent);
            CheckMixedFieldValue(() => drawGMesh.chunksX);
            lodChunks.x = EditorGUILayout.DelayedIntField(drawGMesh.chunksX);
            if (renderType == GrassFlowRenderer.GrassRenderType.Mesh) {
                //only render the y field for meshes since it doesnt make sense on terrain
                CheckMixedFieldValue(() => drawGMesh.chunksY);
                lodChunks.y = EditorGUILayout.DelayedIntField(drawGMesh.chunksY);
            }
            CheckMixedFieldValue(() => drawGMesh.chunksZ);
            lodChunks.z = EditorGUILayout.DelayedIntField(drawGMesh.chunksZ);
            EditorGUILayout.EndHorizontal();

            bool visualizeChunkBounds = EditorGUILayout.ToggleLeft(GetContent(() => grassFlow.visualizeChunkBounds), grassFlow.visualizeChunkBounds);

            bool expandBounds = EditorGUILayout.ToggleLeft(GetContent(() => drawGMesh.expandBounds), drawGMesh.expandBounds);

            bool frustumCull = EditorGUILayout.ToggleLeft(GetContent(() => drawGMesh.frustumCull), drawGMesh.frustumCull);

            Vector2 frustumCullThresh = drawGMesh.frustumCullThresh;
            if (drawGMesh.frustumCull) {
                frustumCullThresh = EditorGUILayout.Vector2Field(GetContent(() => drawGMesh.frustumCullThresh), drawGMesh.frustumCullThresh);
            }


            serializedObject.ApplyModifiedProperties();

            if (EditorGUI.EndChangeCheck()) {

                //i wish i had set all this up to use serialized properties instead so all this wasnt necessary....
                //but those kinda suck too i mean you have to freakin use strings and set them up so, idk

                GrassFlowRenderer.processAsync = false;
                Undo.RecordObject(grassFlow, Locale.GetMsg("undoChangeVariable"));


                //Global Vars
                grassFlow.updateBuffers = updateBuffers;

                grassFlow.terrainExpansion = terrainExpansion;
                grassFlow.useMaterialInstance = useMaterialInstance;

                grassFlow.renderLayer = renderLayer;

                grassFlow.normalizeMeshDensity = normalizeMeshDensity;

                grassFlow.visualizeChunkBounds = visualizeChunkBounds;


                //all local stuff is handled in here annoyingly because of multi select nonsense
                //each heckin variable needs to be checked if it changed so that we dont just set EVERY variable to match the source
                Action<GrassMesh> UpdateGMesh = (GrassMesh updateGMesh) => {

                    if (drawGMesh.asyncInitialization != asyncInitialization) {
                        updateGMesh.asyncInitialization = asyncInitialization;
                    }

                    if (drawGMesh.renderingLayer != renderingLayer) {
                        updateGMesh.renderingLayer = renderingLayer;
                    }

                    if (drawGMesh.lodParams != lodParams) {
                        updateGMesh.lodParams = lodParams;
                    }

                    if (drawGMesh.maxRenderDist != maxRenderDist) {
                        updateGMesh.maxRenderDist = maxRenderDist;
                    }

                    if (drawGMesh.castShadows != castShadows) {
                        updateGMesh.castShadows = castShadows;
                    }

                    if (drawGMesh.receiveShadows != receiveShadows) {
                        updateGMesh.receiveShadows = receiveShadows;
                    }

                    if (grassMaterial && drawGMesh.mainGrassMat != grassMaterial) {
                        updateGMesh.mainGrassMat = grassMaterial;
                        updateGMesh.Update(false);
                    }

                    if (drawGMesh.renderType != renderType) {
                        updateGMesh.renderType = renderType;
                    }


                    if (drawGMesh.terrainObject != terrainObject) {
                        updateGMesh.terrainObject = terrainObject;
                    }


                    if (drawGMesh.normalizeMaxRatio != normalizeMaxRatio) {
                        updateGMesh.normalizeMaxRatio = normalizeMaxRatio;
                        updateGMesh.Reload();
                    }

                    if (drawGMesh.terrainSlopeThresh != terrainSlopeThresh) {
                        updateGMesh.terrainSlopeThresh = terrainSlopeThresh;
                        updateGMesh.Reload();
                    }

                    if (drawGMesh.terrainSlopeFade != terrainSlopeFade) {
                        updateGMesh.terrainSlopeFade = terrainSlopeFade;
                        updateGMesh.Reload();
                    }

                    if (drawGMesh.grassMesh != terrainMesh) {
                        updateGMesh.grassMesh = terrainMesh;

                        if (terrainMesh) {
                            updateGMesh.Reload();
                        }
                    }

                    if (frustumMesh && drawGMesh.customGrassMesh != frustumMesh) {
                        updateGMesh.customGrassMesh = frustumMesh;
                    }

                    if (drawGMesh.terrainTransform != terrainTransform) {
                        updateGMesh.terrainTransform = terrainTransform;

                        if (terrainTransform) {
                            updateGMesh.terrainObject = terrainTransform.GetComponent<Terrain>();

                            var filter = terrainTransform.GetComponent<MeshFilter>();
                            updateGMesh.grassMesh = filter ? filter.sharedMesh : null;

                            if (updateGMesh.terrainObject) {
                                updateGMesh.renderType = GrassFlowRenderer.GrassRenderType.Terrain;
                            }
                            else if (filter) {
                                updateGMesh.renderType = GrassFlowRenderer.GrassRenderType.Mesh;
                            }

                            updateGMesh.Reload();
                        }
                    }

                    if (drawGMesh.bakeDensity != bakeDensity) {
                        updateGMesh.bakeDensity = bakeDensity;
                        updateGMesh.Reload();
                    }

                    if (drawGMesh.bakeData != bakeData) {
                        updateGMesh.bakeData = bakeData || updateGMesh.frustumCull;
                        updateGMesh.Reload();
                    }

                    if (drawGMesh.instanceCount != instanceCount) {
                        updateGMesh.instanceCount = instanceCount;
                        CullingZone.ClearCulledChunks();
                    }

                    if (drawGMesh.lodSteps != lodSteps) {
                        updateGMesh.lodSteps = lodSteps;
                        updateGMesh.instanceCount = instanceCount;
                        CullingZone.ClearCulledChunks();
                        updateGMesh.Reload();
                    }

                    if (drawGMesh.grassPerTri != meshSubdivCount) {
                        updateGMesh.grassPerTri = meshSubdivCount;
                        updateGMesh.Reload();
                    }

                    if (drawGMesh.frustumCull != frustumCull) {
                        updateGMesh.frustumCull = frustumCull;
                        if (frustumCull) {
                            updateGMesh.bakeData = true;
                            updateGMesh.EnableKeyword("FRUSTUM_CULLED");
                        }
                        else {
                            updateGMesh.DisableKeyword("FRUSTUM_CULLED");
                        }
                        updateGMesh.Reload();
                    }

                    if (drawGMesh.frustumCullThresh != frustumCullThresh) {
                        updateGMesh.frustumCullThresh = frustumCullThresh;
                    }

                    if (updatedLods) {
                        var lods = drawGMesh.customMeshLods;
                        if (updateGMesh.customMeshLods.Length != lods.Length) {
                            updateGMesh.customMeshLods = new CustomMeshLod[lods.Length];
                        }
                        for (int i = 0; i < updateGMesh.customMeshLods.Length; i++) {
                            var lod = lods[i];
                            updateGMesh.customMeshLods[i] = new CustomMeshLod() {
                                lodMat = lod.lodMat,
                                lodMesh = lod.lodMesh,
                                distance = lod.distance,
                            };
                        }

                        grassFlow.RefreshMaterials();
                        MeshChunker.HandleCustomMesh(updateGMesh, false);
                    }

                    if (drawGMesh.crossFade != crossFade) {
                        updateGMesh.crossFade = crossFade;
                    }

                    if (drawGMesh.crossFadeRange != crossFadeRange) {
                        updateGMesh.crossFadeRange = crossFadeRange;
                    }

                    if (drawGMesh.expandBounds != expandBounds) {
                        updateGMesh.expandBounds = expandBounds;
                    }

                    lodChunks.Clamp(Vector3Int.one, new Vector3Int(500, 500, 500));
                    if (drawGMesh.chunksX != lodChunks.x) {
                        updateGMesh.chunksX = lodChunks.x;
                        updateGMesh.Reload();
                    }
                    if (drawGMesh.chunksY != lodChunks.y) {
                        updateGMesh.chunksY = lodChunks.y;
                        updateGMesh.Reload();
                    }
                    if (drawGMesh.chunksZ != lodChunks.z) {
                        updateGMesh.chunksZ = lodChunks.z;
                        updateGMesh.Reload();
                    }
                };


                foreach (int idx in grassFlow.selectedIndices) {
                    GrassMesh gMesh = grassFlow.GetSelectedGrassMesh(idx);
                    if (gMesh && gMesh != drawGMesh) {
                        UpdateGMesh(gMesh);
                    }
                }

                //needs to be done separately last because the others rely on it not changing
                //this entire multiselect refactor is so fucking bad because this system was never designed for it
                //and everything has already undergone a big refactor plus it was written poorly from the outset because i didnt know any better
                //im so sad
                UpdateGMesh(drawGMesh);

                grassFlow.RefreshMaterials();

                grassFlow.OnValidate();
            }//end setting change check

            DrawMapsInspector();
            EditorGUILayout.Space();

            //make sure to restore ui col
            GUI.backgroundColor = baseUIColor;


            DrawMeshSelectionUI();
            EditorGUILayout.Space();



            //doesnt really make sense to draw mat editor if more than one is selected
            var matEdit = drawGMesh.mainGrassMat;
            if (matEdit && grassFlow.selectedIndices.Count <= 1) {

                currentlyDrawnMesh = drawGMesh;

                if (matEditor == null || matEditor.target != matEdit) {
                    matEditor = (MaterialEditor)CreateEditor(matEdit);
                }

                matEditor.DrawHeader();

                EditorGUI.BeginChangeCheck();
                matEditor.OnInspectorGUI();
                if (EditorGUI.EndChangeCheck()) {
                    //modified material GUI
                    grassFlow.RefreshMaterials();
                }
            }
        }

        void DrawSettingsGUI() {
            GrassMesh drawGMesh = grassFlow.GetSelectedGrassMesh();
            DrawUIForGmesh(drawGMesh);
        }



        void DrawMeshSelectionUI() {


            GrassMesh drawGMesh = grassFlow.GetSelectedGrassMesh();

            GUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();

            if (!grassFlow.hasRequiredAssets) {
                //this is unbelievably stupid
                boldFold.normal.textColor = errorRed;
                boldFold.focused.textColor = errorRed;
                boldFold.hover.textColor = errorRed;
                boldFold.active.textColor = errorRed;
                boldFold.onFocused.textColor = errorRed;
                boldFold.onHover.textColor = errorRed;
                boldFold.onNormal.textColor = errorRed;
                boldFold.onActive.textColor = errorRed;
            }


            meshSelectionExpanded.target = EditorGUILayout.Foldout(meshSelectionExpanded.target, new GUIContent($"{Locale.GetName("grassMeshesHeader")} ({grassFlow.terrainMeshes.Count}) - " + drawGMesh.name,
                ""), true, boldFold);

            //if (GUILayout.Button(new GUIContent("Copy Current To All"))) {

            //}

            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);

            if (EditorGUILayout.BeginFadeGroup(meshSelectionExpanded.faded)) {

                EditorGUI.indentLevel++;
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(GetLocalContent("addCopyGrassMesh"), GUILayout.MaxWidth(160))) {

                    Undo.RecordObject(grassFlow, Locale.GetMsg("undoAddGrassMesh"));
                    var gMesh = drawGMesh.Clone();
                    grassFlow.terrainMeshes.Add(gMesh);

                    grassFlow.SortGrassMeshes();
                }

                if (GUILayout.Button(GetLocalContent("addFromSelected"), GUILayout.MaxWidth(150))) {

                    Undo.RecordObject(grassFlow, Locale.GetMsg("undoAddFromSelection"));

                    var prevSelection = drawGMesh;
                    foreach (var obj in Selection.gameObjects) {

                        var gMesh = drawGMesh.Clone();
                        gMesh.mainGrassMat = drawGMesh.mainGrassMat;

                        var terrObj = obj.GetComponent<Terrain>();
                        gMesh.terrainTransform = obj.transform;
                        gMesh.terrainObject = terrObj;

                        var filter = obj.GetComponent<MeshFilter>();
                        gMesh.grassMesh = filter ? filter.sharedMesh : null;

                        if (terrObj) {
                            gMesh.renderType = GrassFlowRenderer.GrassRenderType.Terrain;
                        }
                        else if (filter) {
                            gMesh.renderType = GrassFlowRenderer.GrassRenderType.Mesh;
                        }

                        if (gMesh.grassMesh || gMesh.terrainObject) {
                            gMesh.Reload();

                            grassFlow.terrainMeshes.Add(gMesh);
                        }
                    }

                    grassFlow.SortGrassMeshes();
                    grassFlow.selectedIdx = grassFlow.terrainMeshes.IndexOf(prevSelection);

                    grassFlow.Refresh();
                }

                EditorGUILayout.EndHorizontal();
                GUILayout.Space(6);

                DrawSeparator(-3);


                int count = grassFlow.terrainMeshes.Count > 15 ? 15 : grassFlow.terrainMeshes.Count;
                selectMeshScrollPos = EditorGUILayout.BeginScrollView(selectMeshScrollPos, GUILayout.MinHeight(count * 25 + 30), GUILayout.ExpandHeight(true));


                bool hovered = false;
                for (int i = 0; i < grassFlow.terrainMeshes.Count; i++) {

                    EditorGUILayout.BeginHorizontal();

                    if (grassFlow.terrainMeshes.Count > 1) {
                        if (GUILayout.Button("X", xBtn, GUILayout.MaxWidth(20))) {
                            Undo.RecordObject(grassFlow, Locale.GetMsg("undoDeleteGrassMesh"));

                            bool prevHasAssets = grassFlow.hasRequiredAssets;

                            if (grassFlow.selectedIndices.Count <= 1) {
                                grassFlow.RemoveGrassMesh(i);
                            }
                            else {
                                foreach (int idx in grassFlow.selectedIndices) {
                                    grassFlow.RemoveGrassMesh(idx);
                                }
                                grassFlow.selectedIdx = 0;
                            }

                            if (grassFlow.hasRequiredAssets && !prevHasAssets) {
                                grassFlow.Refresh();
                            }

                            //we need to end the hori because we're continuing the loop and skipping the end later
                            EditorGUILayout.EndHorizontal();
                            continue;
                        }
                    }

                    var grass = grassFlow.terrainMeshes[i];
                    var style = grassFlow.selectedIndices.Contains(i) ? selectedLabel : label;
                    var prevCol = style.normal.textColor;
                    if (!grass.hasRequiredAssets) style.normal.textColor = errorRed;

                    int totalMem = drawGMesh.bufferMem;
                    if (grass.subGrassMeshes != null) {
                        foreach (var sub in grass.subGrassMeshes) {
                            totalMem += sub.currentMemUsage + sub.frustumMem;
                        }
                    }
                    string memUsage = " | VRAM: " + (totalMem / (1024f * 1024f)).ToString("F1") + "MB";

                    if (GUILayout.Button(grass.name + memUsage, style)) {
                        Undo.RecordObject(this, Locale.GetMsg("undoSelectGrassMesh"));
                        Event e = Event.current;

                        if (e.modifiers == EventModifiers.Shift) {
                            int dir = i < grassFlow.selectedIdx ? -1 : 1;
                            int pos = grassFlow.selectedIdx;
                            while (pos != i && pos >= 0 && pos < grassFlow.terrainMeshes.Count) {
                                pos += dir;
                                grassFlow.selectedIndices.Add(pos);
                            }
                        }
                        else if (e.modifiers == EventModifiers.Control) {
                            if (grassFlow.selectedIndices.Contains(i)) {
                                grassFlow.selectedIndices.Remove(i);
                            }
                            else {
                                grassFlow.selectedIndices.Add(i);
                            }
                        }
                        else {
                            grassFlow.selectedIdx = i;
                        }

                    }

                    style.normal.textColor = prevCol;

                    if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)) {
                        if (!highlightMaterial) {
                            CreateHighlight();
                        }

                        grass.owner.RenderGrassMeshCustomShader(grass, highlightMaterial, highlightAll: true);
                        grass.owner.paintHighlightMesh = grass;
                        grass.owner.paintHighlightMat = highlightMaterial;
                        hovered = true;
                    }

                    EditorGUILayout.EndHorizontal();

                    DrawSeparator(3);
                    GUILayout.Space(6);
                }

                if (Event.current.type == EventType.Repaint && !hovered) {
                    bool hadMat = grassFlow.paintHighlightMat;
                    DisablePaintHighlight();
                    if (hadMat) {
                        //should prevent highlight from sticking on by forcing the editor to update
                        //i guess the render loop is separate from the update loop because it will continue to render the highlight mesh
                        //even though the drawmesh function is no longer being called, like it just remembers the call and isnt cleared until the next editor update
                        //so this should fix that
                        EditorApplication.QueuePlayerLoopUpdate();
                        SceneView.RepaintAll();
                    }
                }


                EditorGUILayout.EndScrollView();
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFadeGroup();

            GUILayout.EndVertical();

        }

        void DrawSeparator(float offsetV = 0, bool wide = false) {

            int wideAdd = wide ? 15 : 0;

            var rect = EditorGUILayout.BeginHorizontal();
            Handles.color = Color.gray;
            Handles.DrawLine(new Vector2(rect.x - wideAdd, rect.y + offsetV), new Vector2(rect.width + wideAdd, rect.y + offsetV));
            EditorGUILayout.EndHorizontal();
        }


        void SelectBrush(int newToolIndex) {
            Undo.RecordObject(this, Locale.GetMsg("undoChangePaintTool"));
            selectedPaintToolIndex = newToolIndex;
            paintToolType = (PaintToolType)selectedPaintToolIndex;
            Repaint();
        }

        void DrawMapsInspector() {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(GetLocalContent("detailMapsHeader"), bold);

            if (addMapIcon == null) {
                _toolIcons = GetToolIcons();
            }

            EditorGUI.BeginChangeCheck();

            bool createButtonPushed = false;

            GrassMesh drawGMesh = grassFlow.GetSelectedGrassMesh();

            Texture colorMap = DrawMapField(drawGMesh.colorMap, GetContent(() => drawGMesh.colorMap), GrassFlowMapEditor.MapType.GrassColor, ref createButtonPushed);
            Texture paramMap = DrawMapField(drawGMesh.paramMap, GetContent(() => drawGMesh.paramMap), GrassFlowMapEditor.MapType.GrassParameters, ref createButtonPushed);
            Texture typeMap = DrawMapField(drawGMesh.typeMap, GetContent(() => drawGMesh.typeMap), GrassFlowMapEditor.MapType.GrassType, ref createButtonPushed);


            if (EditorGUI.EndChangeCheck()) {

                Undo.RecordObject(grassFlow, Locale.GetMsg("undoSetDetailMap"));


                Action<GrassMesh> UpdateGMesh = (GrassMesh gMesh) => {
                    if (!createButtonPushed) {
                        if (drawGMesh.colorMap != colorMap) {
                            gMesh.colorMap = colorMap;
                        }
                        if (drawGMesh.paramMap != paramMap) {
                            gMesh.paramMap = paramMap;
                        }
                        if (drawGMesh.typeMap != typeMap) {
                            gMesh.typeMap = typeMap;
                        }
                    }

                    foreach (var lod in gMesh.customMeshLods) {
                        var grassDrawMat = lod.drawnMat;

                        if (grassDrawMat) {
                            if (!colorMap) {
                                grassDrawMat.SetTexture("colorMap", null);
                            }
                            if (!paramMap) {
                                grassDrawMat.SetTexture("dhfParamMap", null);
                            }
                            if (!typeMap) {
                                grassDrawMat.SetTexture("typeMap", null);
                            }
                        }
                    }
                };


                foreach (int idx in grassFlow.selectedIndices) {
                    GrassMesh gMesh = grassFlow.GetSelectedGrassMesh(idx);
                    if (gMesh && gMesh != drawGMesh) {
                        UpdateGMesh(gMesh);
                    }
                }
                UpdateGMesh(drawGMesh);

                grassFlow.RevertDetailMaps();
                grassFlow.UpdateShaders();
            }
        }

        Texture DrawMapField(Texture srcMap, GUIContent content, GrassFlowMapEditor.MapType mapType, ref bool creatBtn) {

            GrassMesh drawGMesh = grassFlow.GetSelectedGrassMesh();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(content, GUILayout.Width(100));
            GUI.backgroundColor = baseUIColor;
            if (GUILayout.Button(addMapIcon, plusBtn, GUILayout.Width(16), GUILayout.Height(16))) {
                creatBtn = true;
                SaveData(grassFlow, prompt: true);
                GrassFlowMapEditor.Open(drawGMesh, mapType);
            }
            GUI.backgroundColor = selectedBlue;
            Texture map = EditorGUILayout.ObjectField(srcMap, typeof(Texture), true) as Texture;
            EditorGUILayout.EndHorizontal();

            return map;
        }

        void DrawPaintGUI() {

            DrawMapsInspector();

            GrassMesh drawGMesh = grassFlow.GetSelectedGrassMesh();

            EditorGUI.BeginChangeCheck();
            bool tMapPaintingEnabled = EditorGUILayout.ToggleLeft(GetContent(() => grassFlow.enableMapPainting), grassFlow.enableMapPainting);

            if (EditorGUI.EndChangeCheck()) {
                Undo.RecordObject(grassFlow, Locale.GetMsg("undoChangeVariable"));
                grassFlow.enableMapPainting = tMapPaintingEnabled;
            }
            if (!grassFlow.enableMapPainting) return;


            EditorGUI.BeginChangeCheck();
            bool tMapPaintingEnabledRuntime = EditorGUILayout.ToggleLeft(GetContent(() => grassFlow.enableMapPaintingRuntime), grassFlow.enableMapPaintingRuntime);

            if (EditorGUI.EndChangeCheck()) {
                Undo.RecordObject(grassFlow, Locale.GetMsg("undoChangeVariable"));
                grassFlow.enableMapPaintingRuntime = tMapPaintingEnabledRuntime;
            }

            if (GUILayout.Button(GetLocalContent("revertChangesButton"))) {
                RevertDetailMaps(grassFlow);
            }

            //if (drawGMesh.renderType == GrassFlowRenderer.GrassRenderType.Mesh) {
            //    GUILayout.Space(12);

            //    if (GUILayout.Button(GetLocalContent("bakeDensityToMeshButton"))) {

            //        string fileName = EditorUtility.SaveFilePanelInProject("Choose Save Location", "GrassflowDensityMesh", "asset", "");
            //        if (string.IsNullOrEmpty(fileName)) return;

            //        SaveData(grassFlow, prompt: true);
            //        Mesh bakedMesh = MeshChunker.BakeDensityToMesh(drawGMesh.grassMesh, drawGMesh.paramMap);

            //        AssetDatabase.CreateAsset(bakedMesh, fileName);
            //        AssetDatabase.SaveAssets();
            //    }
            //}


            EditorGUILayout.Space();
            EditorGUILayout.LabelField("", new GUIStyle(GUI.skin.horizontalScrollbarThumb) { fixedHeight = 2 }, GUILayout.Height(3));
            EditorGUILayout.Space();

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUI.BeginChangeCheck();
            int newToolIndex = GUILayout.Toolbar(selectedPaintToolIndex, toolIcons, GUILayout.Height(iconSize - 15), GUILayout.Width(iconSize * toolIcons.Length));
            if (EditorGUI.EndChangeCheck()) {
                SelectBrush(newToolIndex);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label(toolInfos[selectedPaintToolIndex].text);
            GUILayout.Label(toolInfos[selectedPaintToolIndex].tooltip, EditorStyles.wordWrappedMiniLabel);
            GUILayout.EndVertical();

            if (brushList.ShowGUI()) {
                Undo.RecordObject(this, Locale.GetMsg("undoChangeBrush"));
                selectedBrushIndex = brushList.selectedIndex;
                GrassFlowRenderer.SetPaintBrushTexture(GetActiveBrushTexture());

            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(GetLocalContent("settingsHeader"), bold);


            Undo.RecordObject(this, Locale.GetMsg("undoChangeVariable"));

            if (paintToolType == PaintToolType.Color) {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(GetLocalContent("brushColor"), GUILayout.Width(100));
                paintBrushColor = EditorGUILayout.ColorField(new GUIContent(), paintBrushColor);
                EditorGUILayout.EndHorizontal();
            }
            else if (paintToolType == PaintToolType.Type) {

                useBrushOpacity = EditorGUILayout.ToggleLeft(GetLocalContent("useBrushOpacity"), useBrushOpacity, GUILayout.Width(125));

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(GetLocalContent("grassTypeIndex"), GUILayout.Width(125));
                grassTypeAtlasIdx = EditorGUILayout.IntSlider(grassTypeAtlasIdx, 1, _MaxTexAtlasSize);
                EditorGUILayout.EndHorizontal();

            }
            else {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(GetLocalContent("clampRange"),
                    GUILayout.Width(100));
                clampRange = EditorGUILayout.Vector2Field("", clampRange, GUILayout.Width(100));
                GUILayout.Space(5);
                EditorGUILayout.MinMaxSlider("", ref clampRange.x, ref clampRange.y, 0, 1, GUILayout.MinWidth(20));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(GetLocalContent("brushSize"), GUILayout.Width(100));
            paintBrushSize = GUILayout.HorizontalSlider(paintBrushSize, 0f, 1f);
            GUILayout.Space(5);
            paintBrushSize = EditorGUILayout.FloatField("", paintBrushSize, GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(GetLocalContent("brushStrength"), GUILayout.Width(100));
            paintBrushStrength = GUILayout.HorizontalSlider(paintBrushStrength, 0f, 1f);
            GUILayout.Space(5);
            paintBrushStrength = EditorGUILayout.FloatField("", paintBrushStrength, GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();

            LayerMask rayCastMask = EditorGUILayout.MaskField(GetLocalContent("raycastLayerMask"),
                InternalEditorUtility.LayerMaskToConcatenatedLayersMask(paintRaycastMask), InternalEditorUtility.layers);
            paintRaycastMask = InternalEditorUtility.ConcatenatedLayersMaskToLayerMask(rayCastMask);

            continuousPaint = EditorGUILayout.ToggleLeft(GetLocalContent("paintContinuously"), continuousPaint);

            useDeltaTimePaint = EditorGUILayout.ToggleLeft(GetLocalContent("useDeltaTimePaint"), useDeltaTimePaint);


            if (drawGMesh.renderType == GrassFlowRenderer.GrassRenderType.Terrain) {
                DrawSplatMapGUI();
            }
        }

        void DrawSplatMapGUI() {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("", new GUIStyle(GUI.skin.horizontalScrollbarThumb) { fixedHeight = 2 }, GUILayout.Height(3));
            EditorGUILayout.Space();

            EditorGUILayout.LabelField(GetLocalContent("splatMapsHeader"), bold);

            GrassMesh drawGMesh = grassFlow.GetSelectedGrassMesh();

            if (drawGMesh.terrainObject) {
                int numSplatLayers = drawGMesh.terrainObject.terrainData.alphamapLayers;

                if (numSplatLayers > 0) {
                    EditorGUI.BeginChangeCheck();

                    int _splatMapLayerIdx = Mathf.Clamp(splatMapLayerIdx, 0, numSplatLayers);
                    float _splatMapTolerance = splatMapTolerance;

                    int[] splatInts = new int[numSplatLayers];
                    GUIContent[] splatStrs = new GUIContent[numSplatLayers];

                    for (int i = 0; i < splatInts.Length; i++) {
                        splatInts[i] = i;
                        //var splat = drawGMesh.terrainObject.terrainData.splatPrototypes[i];
                        //string splatName = splat.texture ? splat.texture.name : "Null";
                        var splat = drawGMesh.terrainObject.terrainData.terrainLayers[i];

                        if (splat == null) {
                            EditorGUILayout.HelpBox(Locale.GetMsg("missingTerrainLayers"), MessageType.Error);
                            return;
                        }

                        string splatName = splat.name;
                        splatStrs[i] = new GUIContent((i + 1).ToString() + " : " + splatName);
                    }

                    _splatMapLayerIdx = EditorGUILayout.IntPopup(GetLocalContent("splatLayer"),
                        _splatMapLayerIdx, splatStrs, splatInts);

                    _splatMapTolerance = EditorGUILayout.Slider(GetLocalContent("tolerance"), splatMapTolerance, 0f, 1f);

                    if (EditorGUI.EndChangeCheck()) {
                        Undo.RecordObject(this, Locale.GetMsg("undoChangePaintTool"));

                        splatMapLayerIdx = _splatMapLayerIdx;
                        splatMapTolerance = _splatMapTolerance;


                    }


                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(GetLocalContent("applyAdditive"))) {
                        grassFlow.ApplySplatTex(drawGMesh, splatMapLayerIdx, 0, splatMapTolerance);
                        SetParametersDirty();
                    }

                    if (GUILayout.Button(GetLocalContent("applySubtractive"))) {
                        grassFlow.ApplySplatTex(drawGMesh, splatMapLayerIdx, 1, 1f - splatMapTolerance);
                        SetParametersDirty();
                    }

                    if (GUILayout.Button(GetLocalContent("applyReplace"))) {
                        grassFlow.ApplySplatTex(drawGMesh, splatMapLayerIdx, 2, splatMapTolerance);
                        SetParametersDirty();
                    }
                    EditorGUILayout.EndHorizontal();

                }
                else {
                    GUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.Label(Locale.GetMsg("noSplatLayers"));
                    GUILayout.EndVertical();
                }

                //grassFlow.terrainObject.terrainData.alphamapTextures[]
            }
            else {
                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(Locale.GetMsg("assignTerrainObject"));
                GUILayout.EndVertical();
            }
        }


        const int iconSize = 40;
        static GUIContent addMapIcon;
        static GUIContent[] _toolIcons;
        static GUIContent[] toolIcons { get { return _toolIcons == null ? (_toolIcons = GetToolIcons()) : _toolIcons; } }
        static GUIContent[] GetToolIcons() {

            //having to manually disable logging so that it doesn't constantly complain about folders it cant find has to be one of the dumbest things
            //right behind having to do this at all because wildcards arent supported for the folder
            //so as far as I can tell theres no way to just fucking search for the Grassflow folder in the project
            //could this be solved by just using resources? probably. But this is a matter of principle.
            Debug.unityLogger.logEnabled = false;
            List<Texture2D> iconTextures = AssetDatabase.FindAssets("t:Texture", new string[] { 
                "Assets/GrassFlow/Editor/InspectorIcons",
                "ThirdParty/GrassFlow/Editor/InspectorIcons",
                "Plugins/GrassFlow/Editor/InspectorIcons",
            }
            ).Select(p => AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(p), typeof(Texture2D)) as Texture2D).Where(b => b != null).ToList();
            Debug.unityLogger.logEnabled = true;

            addMapIcon = new GUIContent(iconTextures.Find(x => x.name == "mapAdd"));

            return new GUIContent[] {
                new GUIContent(iconTextures.Find(x => x.name == "paintClr"), Locale.GetTooltip("paintToolColor")),
                new GUIContent(iconTextures.Find(x => x.name == "paintDensity"), Locale.GetTooltip("paintToolDensity")),
                new GUIContent(iconTextures.Find(x => x.name == "paintHeight"), Locale.GetTooltip("paintToolHeight")),
                new GUIContent(iconTextures.Find(x => x.name == "paintFlat"), Locale.GetTooltip("paintToolFlatness")),
                new GUIContent(iconTextures.Find(x => x.name == "paintWind"), Locale.GetTooltip("paintToolWind")),
                new GUIContent(iconTextures.Find(x => x.name == "paintType"), Locale.GetTooltip("paintToolType")),
            };
        }

        public readonly GUIContent[] toolInfos = new GUIContent[] {
                GetLocalContent("paintGrassColor"),
                GetLocalContent("paintGrassDensity"),
                GetLocalContent("paintGrassHeight"),
                GetLocalContent("paintGrassFlatness"),
                GetLocalContent("paintGrassWindStrength"),
                GetLocalContent("paintGrassType")
    };



        //I don't even remeber why I used reflection for this
        //But I really don't feel like changing it at any rate
        static object[] paramsArr = new object[1];
        static System.Action paintAction;
        static MethodInfo _SetGrassPaintAction_Method;
        static MethodInfo SetGrassPaintAction_Method {
            get {
                if (_SetGrassPaintAction_Method == null) {
                    _SetGrassPaintAction_Method = typeof(GrassFlowRenderer).GetMethod("InspectorSetPaintAction", BindingFlags.NonPublic | BindingFlags.Instance);
                }

                return _SetGrassPaintAction_Method;
            }
        }

        static int paintHighlightBrushTexID = Shader.PropertyToID("paintHighlightBrushTex");
        static int paintHightlightColorID = Shader.PropertyToID("paintHightlightColor");
        static int paintHighlightBrushParamsID = Shader.PropertyToID("paintHighlightBrushParams");

        static Material highlightMaterial;
        void CreateHighlight() {
            highlightMaterial = new Material(Shader.Find("Hidden/GrassFlow/PaintHighlighter"));
            grassFlow.paintHighlightMat = highlightMaterial;
        }

        void SetHighlightTex(Texture tex) {
            highlightMaterial.SetTexture(paintHighlightBrushTexID, tex);
        }

        void SetHighlightColor(Color col) {
            highlightMaterial.SetColor(paintHightlightColorID, col);
        }

        void SetHighlightParams(Vector4 param) {
            highlightMaterial.SetVector(paintHighlightBrushParamsID, param);
        }

        void DisablePaintHighlight(bool destroyHighlighter = false) {

            grassFlow.paintHighlightMat = null;
            grassFlow.paintHighlightMesh = null;

            if (highlightMaterial) {
                if (destroyHighlighter) {
                    DestroyImmediate(highlightMaterial);
                    highlightMaterial = null;
                }
            }
        }

        void SceneGUICallback(SceneView sceneView) {
            Event e = Event.current;

            HandleHotkeys(e);

            if (grassFlow && grassFlow.enableMapPainting && mainTabIndex == 1) {

                GrassFlowRenderer.isPaintingOpen = true;

                RaycastHit hit;
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

                Physics.Raycast(ray, out hit, float.PositiveInfinity, paintRaycastMask);

                bool multiple = grassFlow.MultipleMeshesForTransform(hit.transform);
                GrassMesh hitGMesh = multiple ? grassFlow.GetSelectedGrassMesh() : grassFlow.GetGrassMeshFromTransform(hit.transform);
                bool hitTerrain = hitGMesh != null;

                if (e.type == EventType.MouseDown || e.type == EventType.MouseUp) {
                    if (e.button == 0 && !e.alt) {
                        Selection.activeObject = grassFlow;
                    }
                }

                if (!hitTerrain) {
                    DisablePaintHighlight();
                    if (!shouldPaint) return;
                }


                if (!highlightMaterial) {
                    CreateHighlight();
                }

                SetHighlightTex(GetActiveBrushTexture());
                SetHighlightColor(paintBrushColor);

                if (shouldPaint) {
                    if (paintToolType == PaintToolType.Color) {
                        SetHighlightParams(Vector4.zero);
                    }
                    else {
                        SetHighlightParams(new Vector4(hit.textureCoord.x, hit.textureCoord.y, paintBrushSize * 0.05f, 0.5f));
                    }
                }
                else {
                    SetHighlightParams(new Vector4(hit.textureCoord.x, hit.textureCoord.y, paintBrushSize * 0.05f, 1f));
                }

                grassFlow.paintHighlightMat = highlightMaterial;
                grassFlow.paintHighlightMesh = hitGMesh;


                int id = GUIUtility.GetControlID(grassEditorHash, FocusType.Passive);
                float brushDir = e.modifiers.HasFlag(EventModifiers.Shift) ? -1f : 1f;


                switch (e.GetTypeForControl(id)) {
                    case EventType.Layout:
                        HandleUtility.AddDefaultControl(id);

                        if (continuousPaint && shouldPaint) {
                            //this is really silly but i guess theres a bug with getting a temporary rendertexture during scene gui
                            //not doing this causes the scene gui to do weird things when the painting function asks for a temp RT
                            //weird things like: rendering the scene gui onto the grass O_O
                            //or rendering the gizmo where the shading mode selector is
                            //or blacking out the the entire scene view border frame
                            //this is only needed for repaint/layout events, thus needed for continous paint mode
                            //it doesnt matter on mouse drag events since that isnt during gui drawing stuff
                            paramsArr[0] = paintAction = new System.Action(() => {
                                PaintSwitch(hitGMesh, hit.textureCoord, brushDir);
                            });
                            SetGrassPaintAction_Method.Invoke(grassFlow, paramsArr);
                            EditorUtility.SetDirty(grassFlow);
                        }
                        break;

                    case EventType.MouseMove:
                        HandleUtility.Repaint();
                        break;

                    case EventType.MouseDown:
                    case EventType.MouseDrag: {
                            // Don't do anything at all if someone else owns the hotControl. Fixes case 677541.
                            if (EditorGUIUtility.hotControl != 0 && EditorGUIUtility.hotControl != id)
                                return;

                            // Don't do anything on MouseDrag if we don't own the hotControl.
                            if (e.GetTypeForControl(id) == EventType.MouseDrag && EditorGUIUtility.hotControl != id)
                                return;

                            // If user is ALT-dragging, we want to return to main routine
                            if (Event.current.alt)
                                return;

                            // Allow painting with LMB only
                            if (e.button != 0)
                                return;

                            if (HandleUtility.nearestControl != id)
                                return;

                            if (e.type == EventType.MouseDown) {
                                EditorGUIUtility.hotControl = id;
                                shouldPaint = true;
                                lastPaintTime = Time.realtimeSinceStartup - 0.016f;
                                GrassFlowRenderer.SetPaintBrushTexture(GetActiveBrushTexture());
                                CheckPaintTextureExists(GetPaintMap(paintToolType, hitGMesh));
                            }


                            if (!continuousPaint) {
                                PaintSwitch(hitGMesh, hit.textureCoord, brushDir);
                            }

                            e.Use();
                        }
                        break;

                    case EventType.MouseUp: {

                            if (GUIUtility.hotControl != id) {
                                return;
                            }

                            shouldPaint = false;
                            MarkDirtyMaps();

                            if (currentPaintHistory) {
                                Undo.RecordObject(paintUndoRedoController, Locale.GetMsg("undoPaint"));
                                paintUndoRedoController.paintHistory.Add(currentPaintHistory);
                                currentPaintHistory = null;

                                paramsArr[0] = paintAction = null;
                                SetGrassPaintAction_Method.Invoke(grassFlow, paramsArr);
                            }

                            // Release hot control
                            GUIUtility.hotControl = 0;
                        }
                        break;
                }
            }
            else {
                GrassFlowRenderer.isPaintingOpen = false;
                //DisablePaintHighlight(true);
            }
        }

        void MarkDirtyMaps() {
            switch (paintToolType) {
                case PaintToolType.Color:
                    SetColorMapDirty();
                    break;

                case PaintToolType.Density:
                case PaintToolType.Height:
                case PaintToolType.Flat:
                case PaintToolType.Wind:
                    SetParametersDirty();
                    break;

                case PaintToolType.Type:
                    SetTypeMapDirty();
                    break;
            }
        }
        static Texture GetPaintMap(PaintToolType type, GrassMesh gMesh) {
            switch (type) {
                case PaintToolType.Color: return gMesh.colorMap;

                case PaintToolType.Density:
                case PaintToolType.Height:
                case PaintToolType.Flat:
                case PaintToolType.Wind:
                    return gMesh.paramMap;

                case PaintToolType.Type: return gMesh.typeMap;
            }

            return null;
        }

        static RenderTexture GetPaintMapRT(PaintToolType type, GrassMesh gMesh) {
            switch (type) {
                case PaintToolType.Color: return gMesh.colorMapRT;

                case PaintToolType.Density:
                case PaintToolType.Height:
                case PaintToolType.Flat:
                case PaintToolType.Wind:
                    return gMesh.paramMapRT;

                case PaintToolType.Type: return gMesh.typeMapRT;
            }

            return null;
        }

        public static void SetColorMapDirty() {
            dirtyTypes.Add(GrassFlowMapEditor.MapType.GrassColor);
        }

        public static void SetParametersDirty() {
            dirtyTypes.Add(GrassFlowMapEditor.MapType.GrassParameters);
        }

        public static void SetTypeMapDirty() {
            dirtyTypes.Add(GrassFlowMapEditor.MapType.GrassType);
        }

        static int grassEditorHash = "GrassFlowEditor".GetHashCode();

        void PaintSwitch(GrassMesh gMesh, Vector2 textureCoord, float brushDir) {

            if (!gMesh) return;

            switch (paintToolType) {
                case PaintToolType.Color: //paint color
                    PaintTerrain(gMesh, textureCoord, paintBrushColor, gMesh.colorMapRT, new Vector2(-999f, 999f), paintBrushStrength, 0f);
                    break;

                case PaintToolType.Density: //paint density
                    PaintTerrain(gMesh, textureCoord, new Vector4(brushDir, 0, 0), gMesh.paramMapRT, clampRange, paintBrushStrength, 1f);
                    break;

                case PaintToolType.Height: //paint height
                    PaintTerrain(gMesh, textureCoord, new Vector4(0, brushDir, 0), gMesh.paramMapRT, clampRange, paintBrushStrength, 1f);
                    break;

                case PaintToolType.Flat: //paint flatness
                    PaintTerrain(gMesh, textureCoord, new Vector4(0, 0, -brushDir), gMesh.paramMapRT, clampRange, paintBrushStrength, 1f);
                    break;

                case PaintToolType.Wind: //paint wind affectedness
                    PaintTerrain(gMesh, textureCoord, new Vector4(0, 0, 0, brushDir), gMesh.paramMapRT, clampRange, paintBrushStrength, 1f);
                    break;

                case PaintToolType.Type:
                    int atlasIdx = grassTypeAtlasIdx;
                    if (brushDir == -1) {
                        atlasIdx = 1;
                    }
                    float paintIdx = (atlasIdx - 1) / (float)_MaxTexAtlasSize;
                    float paintPct = paintIdx + (1f / _MaxTexAtlasSize - _TexAtlasOff) * paintBrushStrength;
                    float lowerBound = useBrushOpacity ? paintIdx : paintPct;
                    //Debug.Log(paintIdx + " : " + (paintPct));
                    //PaintTerrain(textureCoord, new Vector4(paintIdx, 0, 0), grassFlow.typeMapRT, new Vector2(paintIdx, paintIdx), 1f, 0f);
                    PaintTerrain(gMesh, textureCoord, new Vector4(paintPct, 0, 0), gMesh.typeMapRT, new Vector2(lowerBound, paintPct), 1f, 0f);
                    break;
            }
        }

        float lastPaintTime = 0;
        void PaintTerrain(GrassMesh gMesh, Vector2 texCoord, Vector4 blendParams, RenderTexture mapRT, Vector2 _clampRange, float strength, float blendMode) {
            if (paintToolType != PaintToolType.Type) {
                strength = useDeltaTimePaint ? strength * (Time.realtimeSinceStartup - lastPaintTime) * 15f : strength;
            }

            if (!currentPaintHistory) {

                currentPaintHistory = new PaintHistory() {
                    gMesh = gMesh,
                    brushSize = paintBrushSize,
                    blendParams = blendParams,
                    paintType = paintToolType,
                    _clampRange = _clampRange,
                    blendMode = blendMode,
                    brushIdx = selectedBrushIndex
                };
            }

            Vector2 halfPixelOff;
            switch (paintToolType) {
                case PaintToolType.Color: halfPixelOff = gMesh.colorMapHalfPixUV; break;
                case PaintToolType.Type: halfPixelOff = gMesh.typeMapHalfPixUV; break;
                default:
                    halfPixelOff = gMesh.paramMapHalfPixUV; break;
            }

            PaintHistory.PaintAction paintAct = new PaintHistory.PaintAction() {
                texCoord = texCoord - halfPixelOff,
                strength = strength
            };
            paintAct.Dispatch(currentPaintHistory, mapRT);
            currentPaintHistory.paintActions.Add(paintAct);

            lastPaintTime = Time.realtimeSinceStartup;
        }

        void CheckPaintTextureExists(Texture tex) {
            if (!tex) {
                Debug.LogError(Locale.GetMsg("errorPaintTextureMissing"));
            }
        }

        [System.Serializable]
        class PaintHistory {

            public List<PaintAction> paintActions = new List<PaintAction>();

            public GrassMesh gMesh;

            public Vector4 blendParams;
            public Vector2 _clampRange;
            public PaintToolType paintType;
            public float blendMode;
            public float brushSize;
            public int brushIdx;

            [System.Serializable]
            public class PaintAction {
                public Vector2 texCoord;
                public float strength;

                public void Dispatch(PaintHistory history, RenderTexture mapRT) {
                    GrassFlowRenderer.PaintDispatch(texCoord, history.brushSize, strength, history.blendParams,
                        mapRT, history._clampRange, history.blendMode);
                }
            }

            public static implicit operator bool(PaintHistory h) { return h != null; }
        }

        //-------------------------------------------------------------
        //------------------utilityyy stuff------------------------
        //-------------------------------------------------------------

        GrassFlowRenderer[] renderers;

        void LoadInspectorSettings() {

            renderers = FindObjectsOfType<GrassFlowRenderer>();

            paintBrushSize = EditorPrefs.GetFloat("GrassFlowBrushSize", paintBrushSize);
            paintBrushStrength = EditorPrefs.GetFloat("GrassFlowBrushStrength", paintBrushStrength);

            paintBrushColor.a = EditorPrefs.GetFloat("GrassFlowBrushColorA", paintBrushColor.a);
            paintBrushColor.r = EditorPrefs.GetFloat("GrassFlowBrushColorR", paintBrushColor.r);
            paintBrushColor.g = EditorPrefs.GetFloat("GrassFlowBrushColorG", paintBrushColor.g);
            paintBrushColor.b = EditorPrefs.GetFloat("GrassFlowBrushColorB", paintBrushColor.b);

            useBrushOpacity = EditorPrefs.GetBool("GrassFlowUseBrushOpacity", useBrushOpacity);
            grassTypeAtlasIdx = EditorPrefs.GetInt("GrassFlowGrassTypeAtlasIdx", grassTypeAtlasIdx);

            mainTabIndex = EditorPrefs.GetInt("GrassFlowMainTab", mainTabIndex);
            selectedPaintToolIndex = EditorPrefs.GetInt("GrassFlowPaintToolIndex", selectedPaintToolIndex);
            brushList.UpdateSelection(EditorPrefs.GetInt("GrassFlowSelectedBrush", 0));
            paintToolType = (PaintToolType)selectedPaintToolIndex;

            continuousPaint = EditorPrefs.GetBool("GrassFlowContinuousPaint", continuousPaint);
            useDeltaTimePaint = EditorPrefs.GetBool("GrassFlowDeltaPaint", useDeltaTimePaint);

            selectedBrushIndex = brushList.selectedIndex;

            splatMapLayerIdx = EditorPrefs.GetInt("GrassFlowSplatMapLayerIdx", splatMapLayerIdx);
            splatMapTolerance = EditorPrefs.GetFloat("GrassFlowSplatMapTolerance", splatMapTolerance);

            grassFlow.selectedIdx = EditorPrefs.GetInt("GrassFlowSelectedGrassMeshIdx", grassFlow.selectedIdx);
            meshSelectionExpanded = new AnimBool(EditorPrefs.GetBool("GrassFlowMeshSelectionExpanded", false));

            gfPlayCount = EditorPrefs.GetInt("GF_Count", UnityEngine.Random.Range(-10, -40));

            showMultiRendererWarning = EditorPrefs.GetBool("GF_MULTIPLE_RENDERERS_WARNING", showMultiRendererWarning);

            Locale.LoadLang();
        }

        void SaveInspectorSettings() {
            EditorPrefs.SetFloat("GrassFlowBrushSize", paintBrushSize);
            EditorPrefs.SetFloat("GrassFlowBrushStrength", paintBrushStrength);

            EditorPrefs.SetFloat("GrassFlowBrushColorA", paintBrushColor.a);
            EditorPrefs.SetFloat("GrassFlowBrushColorR", paintBrushColor.r);
            EditorPrefs.SetFloat("GrassFlowBrushColorG", paintBrushColor.g);
            EditorPrefs.SetFloat("GrassFlowBrushColorB", paintBrushColor.b);

            EditorPrefs.SetBool("GrassFlowUseBrushOpacity", useBrushOpacity);
            EditorPrefs.SetInt("GrassFlowGrassTypeAtlasIdx", grassTypeAtlasIdx);

            EditorPrefs.SetInt("GrassFlowMainTab", mainTabIndex);
            EditorPrefs.SetInt("GrassFlowPaintToolIndex", selectedPaintToolIndex);
            EditorPrefs.SetInt("GrassFlowSelectedBrush", brushList.selectedIndex);

            EditorPrefs.SetBool("GrassFlowContinuousPaint", continuousPaint);
            EditorPrefs.SetBool("GrassFlowDeltaPaint", useDeltaTimePaint);

            EditorPrefs.SetInt("GrassFlowSplatMapLayerIdx", splatMapLayerIdx);
            EditorPrefs.SetFloat("GrassFlowSplatMapTolerance", splatMapTolerance);

            EditorPrefs.SetInt("GrassFlowSelectedGrassMeshIdx", grassFlow.selectedIdx);
            EditorPrefs.SetBool("GrassFlowMeshSelectionExpanded", meshSelectionExpanded.target);
        }

        static bool SavePaintTexture(GrassFlowMapEditor.MapType mapType, Texture original, RenderTexture newTex) {
            if (!original || !newTex) return false;

            string savePath = AssetDatabase.GetAssetPath(original);

            if (string.IsNullOrEmpty(savePath)) {
                Debug.LogError(Locale.GetMsg("errorCantSaveMapNoFile"));
                return false;
            }

            if (Path.GetExtension(savePath).ToLower() != ".png") {
                Debug.LogError(Locale.GetMsg("errorMapNotPng"));
                return false;
            }

            savePath = Path.GetFullPath(Application.dataPath + "/../" + savePath);

            RenderTexture oldRT = RenderTexture.active;
            Texture2D saveTex = new Texture2D(newTex.width, newTex.height, TextureFormat.ARGB32, false, true);

            if (mapType == GrassFlowMapEditor.MapType.GrassType) {
                //this is required for older versions of unity that for one reason or another do not properly
                //read pixels from an R8 rendertexture
                RenderTexture tmp = RenderTexture.GetTemporary(newTex.width, newTex.height, 0);
                if (!tmp.IsCreated()) tmp.Create();
                bool srgb = GL.sRGBWrite;
                GL.sRGBWrite = false;
                Graphics.Blit(newTex, tmp);
                GL.sRGBWrite = srgb;
                newTex = tmp;
            }

            RenderTexture.active = newTex;
            saveTex.ReadPixels(new Rect(0, 0, saveTex.width, saveTex.height), 0, 0, false);
            saveTex.Apply();

            if (mapType == GrassFlowMapEditor.MapType.GrassType) {
                RenderTexture.ReleaseTemporary(newTex);
            }

            RenderTexture.active = oldRT;

            File.WriteAllBytes(savePath, saveTex.EncodeToPNG());
            DestroyImmediate(saveTex);

            return true;
        }

        public static void ClearDirtyMaps() {
            dirtyTypes.Clear();
        }

        public static void SaveDatas(bool refresh = true, bool prompt = false) {
            foreach (var gf in GrassFlowRenderer.instances) {
                SaveData(gf, refresh, prompt);
            }
        }

        public static void SaveData(GrassFlowRenderer grass, bool refresh = true, bool prompt = false) {

            if (!grass) return;

            if (grass.enableMapPainting && dirtyTypes.Count > 0) {
                bool shouldRefresh = false;

                if (prompt) {
                    if (!EditorUtility.DisplayDialog(Locale.GetMsg("saveDialogTitle"), Locale.GetMsg("saveDialogMessage"), Locale.GetMsg("saveDialogYes"), Locale.GetMsg("saveDialogNo"))) {
                        ClearDirtyMaps();
                        grass.RevertDetailMaps();
                    }
                }

                foreach (GrassFlowMapEditor.MapType mapType in dirtyTypes) {
                    foreach (var gMesh in grass.terrainMeshes) {
                        shouldRefresh |= SaveMapSwitch(gMesh, mapType);
                    }
                }

                if (refresh && shouldRefresh) AssetDatabase.Refresh();

                if (paintUndoRedoController) {
                    Undo.ClearUndo(paintUndoRedoController);
                }
                paintUndoRedoController = CreateInstance<PaintUndoRedoController>();
                paintUndoRedoController.grass = grass;


                dirtyTypes.Clear();

                if (shouldRefresh) {
                    grass.Refresh();
                }
            }

            grass.UpdateShaders();
        }

        static bool SaveMapSwitch(GrassMesh gMesh, GrassFlowMapEditor.MapType mapType) {
            switch (mapType) {
                case GrassFlowMapEditor.MapType.GrassColor: return SavePaintTexture(mapType, gMesh.colorMap, gMesh.colorMapRT);
                case GrassFlowMapEditor.MapType.GrassParameters: return SavePaintTexture(mapType, gMesh.paramMap, gMesh.paramMapRT);
                case GrassFlowMapEditor.MapType.GrassType: return SavePaintTexture(mapType, gMesh.typeMap, gMesh.typeMapRT);

                default: return false;
            }
        }



        void HandleHotkeys(Event e) {
            if (e.type != EventType.KeyDown) {
                return;
            }

            bool shifted = e.modifiers.HasFlag(EventModifiers.Shift);
            bool controlled = e.modifiers.HasFlag(EventModifiers.Control);
            bool altd = e.modifiers.HasFlag(EventModifiers.Alt);
            float shiftMult = shifted ? 5f : 1f;
            const float hotKeySpeed = 0.05f;

            switch (e.keyCode) {
                case KeyCode.R:
                    if (!e.control && !e.alt && e.shift) {
                        RevertDetailMaps(grassFlow);
                    }
                    break;


                case KeyCode.LeftBracket:
                    if (altd) grassTypeAtlasIdx--;
                    else if (!controlled) paintBrushSize -= hotKeySpeed * shiftMult;
                    else paintBrushStrength -= hotKeySpeed * shiftMult;
                    Repaint();
                    break;

                case KeyCode.RightBracket:
                    if (altd) grassTypeAtlasIdx++;
                    else if (!controlled) paintBrushSize += hotKeySpeed * shiftMult;
                    else paintBrushStrength += hotKeySpeed * shiftMult;
                    Repaint();
                    break;

                case KeyCode.A:
                    if (controlled) {
                        //select all
                        for (int i = 0; i < grassFlow.terrainMeshes.Count; i++) {
                            grassFlow.selectedIndices.Add(i);
                        }
                    }
                    else {
                        return;
                    }
                    break;


                case KeyCode.F1: SelectBrush(0); break;
                case KeyCode.F2: SelectBrush(1); break;
                case KeyCode.F3: SelectBrush(2); break;
                case KeyCode.F4: SelectBrush(3); break;
                case KeyCode.F5: SelectBrush(4); break;
                case KeyCode.F6: SelectBrush(5); break;

                default:
                    return;
            }

            Repaint();
            e.Use();
        }

        static void RevertDetailMaps(GrassFlowRenderer grass) {
            if (!grass) return;
            ClearPaintUndoHistory();
            grass.RevertDetailMaps();
        }

        private class SaveProcessor : UnityEditor.AssetModificationProcessor {
            static string[] OnWillSaveAssets(string[] paths) {

                GrassFlowRenderer[] grasses = FindObjectsOfType<GrassFlowRenderer>();
                foreach (GrassFlowRenderer grass in grasses) {
                    SaveData(grass);
                }

                return paths;
            }
        }


        void CheckMixedFieldValue<T>(Expression<System.Func<T>> memberExpression) {
            MemberInfo member = ((MemberExpression)memberExpression.Body).Member;
            EditorGUI.showMixedValue = !AllSelectedMeshesSameFieldValue(member);
        }

        bool AllSelectedMeshesSameFieldValue(MemberInfo member) {

            if (GUI.backgroundColor != errorRed) {
                if (member.DeclaringType != typeof(GrassMesh)) {
                    GUI.backgroundColor = baseUIColor;
                    return true;
                }
                else {
                    GUI.backgroundColor = selectedBlue;
                }
            }
            if (grassFlow.selectedIndices.Count <= 1) return true;

            FieldInfo field = member as FieldInfo;
            PropertyInfo prop = member as PropertyInfo;
            if (field == null && prop == null) return false;


            Func<GrassMesh, object> GetMemberValue = (GrassMesh gMesh) => {
                if (field != null) {
                    return field.GetValue(gMesh);
                }
                else {
                    return prop.GetValue(gMesh);
                }
            };

            GrassMesh drawGMesh = grassFlow.GetSelectedGrassMesh();
            object value = GetMemberValue(drawGMesh);

            foreach (int idx in grassFlow.selectedIndices) {
                GrassMesh gMesh = grassFlow.GetSelectedGrassMesh(idx);
                if (gMesh && gMesh != drawGMesh) {
                    if (value == null) {
                        if (GetMemberValue(gMesh) != value) {
                            return false;
                        }
                    }
                    else if (!value.Equals(GetMemberValue(gMesh))) {
                        return false;
                    }
                }
            }

            return true;
        }

        static GUIContent GetLocalContent(string name) {
            return new GUIContent(Locale.GetName(name), Locale.GetTooltip(name));
        }


        GUIContent GetContent<T>(Expression<System.Func<T>> memberExpression) {
            MemberInfo member = ((MemberExpression)memberExpression.Body).Member;
            string fieldName = member.Name;

            EditorGUI.showMixedValue = !AllSelectedMeshesSameFieldValue(member);

            string labelStr = Locale.GetName(fieldName);

            //backup og stuff if not found in locale
            if (string.IsNullOrEmpty(labelStr)) {
                var inspectorName = member.GetCustomAttribute<InspectorNameAttribute>();

                if (inspectorName != null) {
                    labelStr = inspectorName.displayName;
                }
                else {
                    char[] label = fieldName.Replace('_', '\0').ToCharArray();
                    labelStr = label[0].ToString().ToUpper();
                    for (int i = 1; i < fieldName.Length; i++) {
                        if (char.IsUpper(label[i])) {
                            labelStr += " ";
                        }
                        labelStr += label[i];
                    }
                }
            }
            return new GUIContent(labelStr, GetTooltip(fieldName, member.DeclaringType));
        }

        string GetTooltip(string fieldName, System.Type type) {

            string tooltip = Locale.GetTooltip(fieldName);

            if (string.IsNullOrEmpty(tooltip)) {

                var tip = GetTooltipAttribute(fieldName, type);
                if (tip == null) return "";
                tooltip = tip.tooltip;
            }

            return tooltip;
        }


        GFToolTipAttribute GetTooltipAttribute(string fieldName, System.Type type) {
            return type.GetMember(fieldName,
                BindingFlags.GetProperty | BindingFlags.GetField | BindingFlags.NonPublic | BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public
                )[0].GetCustomAttribute<GFToolTipAttribute>();
        }

        static int GetSubPropCount(SerializedProperty property) {
            var copIter = property.Copy().GetEnumerator();

            int i = 0;
            if (copIter.MoveNext()) {
                do {
                    i++;
                } while (copIter.MoveNext());
            }

            return i;
        }

        [CustomPropertyDrawer(typeof(GFInlineAttribute))]
        class InlineDrawer : PropertyDrawer {

            public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {


                GFInlineAttribute attr = attribute as GFInlineAttribute;

                var iterator = property.GetEnumerator();

                //Debug.Log(property.managedReferenceFullTypename);

                float spacing = 10;
                int propCount = GetSubPropCount(property);
                float fullWidth = position.width;

                if (attr.useLabel && !property.isExpanded) {
                    fullWidth -= EditorGUIUtility.labelWidth;
                    position.width = EditorGUIUtility.labelWidth;
                    EditorGUI.LabelField(position, new GUIContent(property.displayName, property.tooltip));
                    position.x += EditorGUIUtility.labelWidth;
                }

                float totalSpacing = (propCount - 1) * spacing;
                float propWidth = (fullWidth - totalSpacing) / propCount;
                position.width = propWidth;

                if (iterator.MoveNext()) {
                    do {
                        if (attr.useLabel) {
                            EditorGUI.PropertyField(position, property, false);
                        }
                        else {
                            EditorGUI.PropertyField(position, property, GUIContent.none, false);
                        }
                        position.x += propWidth + spacing;

                    } while (iterator.MoveNext());
                }
            }
        }




        static Texture2D GetActiveBrushTexture() {
            Brush aBrush = brushList.GetActiveBrush();
            if (!aBrush.m_Mask) {
                _BrushList = new BrushList();
            }

            brushList.UpdateSelection(selectedBrushIndex);
            return brushList.GetActiveBrush().texture;
        }

        class Brush {

            public Texture2D m_Mask;

            Texture2D m_Texture = null;
            Texture2D m_Thumbnail = null;

            bool m_UpdateTexture = true;
            bool m_UpdateThumbnail = true;

            internal static Brush CreateInstance(Texture2D t) {
                var b = new Brush {
                    m_Mask = t
                };
                return b;
            }

            void UpdateTexture() {
                if (m_UpdateTexture || m_Texture == null) {
                    m_Texture = GenerateBrushTexture(m_Mask, m_Mask.width, m_Mask.height);
                    m_UpdateTexture = false;
                }
            }

            void UpdateThumbnail() {
                if (m_UpdateThumbnail || m_Thumbnail == null) {
                    m_Thumbnail = GenerateBrushTexture(m_Mask, 64, 64, true);
                    m_UpdateThumbnail = false;
                }
            }

            public Texture2D texture { get { UpdateTexture(); return m_Texture; } }
            public Texture2D thumbnail { get { UpdateThumbnail(); return m_Thumbnail; } }

            public void SetDirty(bool isDirty) {
                m_UpdateTexture |= isDirty;
                m_UpdateThumbnail |= isDirty;
            }

            static Texture2D GenerateBrushTexture(Texture2D mask, int width, int height, bool isThumbnail = false) {
                RenderTexture oldRT = RenderTexture.active;
                RenderTextureFormat outputRenderFormat = RenderTextureFormat.ARGB32;
                TextureFormat outputTexFormat = TextureFormat.ARGB32;

                // build brush texture
                RenderTexture tempRT = RenderTexture.GetTemporary(width, height, 0, outputRenderFormat, RenderTextureReadWrite.Linear);
                Graphics.Blit(mask, tempRT);

                Texture2D previewTexture = new Texture2D(width, height, outputTexFormat, false, true);

                RenderTexture.active = tempRT;
                previewTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                previewTexture.Apply();

                RenderTexture.ReleaseTemporary(tempRT);
                tempRT = null;

                RenderTexture.active = oldRT;
                return previewTexture;
            }
        }



        class BrushList {
            [SerializeField] int m_SelectedBrush = 0;
            Brush[] m_BrushList = null;
            GUIContent[] m_Thumnails;

            // UI
            Vector2 m_ScrollPos;

            public int selectedIndex { get { return m_SelectedBrush; } }
            static class Styles {
                public static GUIStyle gridList = "GridList";
                public static GUIContent brushes = GetLocalContent("brushesHeader");
            }


            public BrushList() {
                if (m_BrushList == null) {
                    LoadBrushes();
                    UpdateSelection(0);
                }
            }

            public void LoadBrushes() {
                // Load the textures;
                var arr = new List<Brush>();
                int idx = 1;
                Texture2D t = null;

                // Load brushes from editor resources
                do {
                    UnityEngine.Object tBrush = null;

#if UNITY_2019_1_OR_NEWER
                    tBrush = EditorGUIUtility.Load(UnityEditor.Experimental.EditorResources.brushesPath + "builtin_brush_" + idx + ".brush");
#endif

                    if (tBrush) {
                        //this is so freakin stupid but i have to do this now for Unity 2019+ because unity changed
                        //the way built-in brushes are stored and also removed the old brushes and compeletely broke compatibility and its a really bogus situation
                        //so now i have to use reflection methods to worm into the 2019 brush class and call methods and get the texture out of it
                        System.Type tType = tBrush.GetType();
                        var texField = tType.GetField("m_Texture", BindingFlags.Instance | BindingFlags.NonPublic);
                        var updateField = tType.GetField("m_UpdateTexture", BindingFlags.Instance | BindingFlags.NonPublic);
                        var updateMethod = tType.GetMethod("UpdateTexture", BindingFlags.Instance | BindingFlags.NonPublic);
                        updateField.SetValue(tBrush, true);
                        updateMethod.Invoke(tBrush, null);

                        t = (Texture2D)texField.GetValue(tBrush);
                        if (t) {
                            Color32[] pixels = t.GetPixels32();
                            for (int i = 0; i < pixels.Length; i++) {
                                pixels[i].a = pixels[i].r;
                            }
                            t = new Texture2D(t.width, t.height, TextureFormat.Alpha8, false);
                            t.SetPixels32(0, 0, t.width, t.height, pixels, 0);
                            t.Apply();

                            arr.Add(Brush.CreateInstance(t));
                        }
                    }
                    else {
                        t = (Texture2D)EditorGUIUtility.Load("builtin_brush_" + idx + ".png");
                        if (t) {
                            arr.Add(Brush.CreateInstance(t));
                        }
                    }


                    idx++;
                }
                while (t);

                // Load user created brushes from the Assets/Gizmos folder
                idx = 0;
                do {
                    t = EditorGUIUtility.FindTexture("brush_" + idx + ".png");
                    if (t)
                        arr.Add(Brush.CreateInstance(t));
                    idx++;
                }
                while (t);

                m_BrushList = arr.ToArray();
            }

            public void SelectPrevBrush() {
                if (--m_SelectedBrush < 0)
                    m_SelectedBrush = m_BrushList.Length - 1;
                UpdateSelection(m_SelectedBrush);
            }

            public void SelectNextBrush() {
                if (++m_SelectedBrush >= m_BrushList.Length)
                    m_SelectedBrush = 0;
                UpdateSelection(m_SelectedBrush);
            }

            public void UpdateSelection(int newSelectedBrush) {
                m_SelectedBrush = newSelectedBrush;
            }

            public Brush GetCircleBrush() {
                return m_BrushList[0];
            }

            public Brush GetActiveBrush() {
                if (m_SelectedBrush >= m_BrushList.Length)
                    m_SelectedBrush = 0;

                return m_BrushList[m_SelectedBrush];
            }

            public bool ShowGUI() {
                bool repaint = false;

                GUILayout.Label(Styles.brushes, EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                {
                    Rect brushPreviewRect = EditorGUILayout.GetControlRect(true, GUILayout.Width(128), GUILayout.Height(128));
                    if (m_BrushList != null) {
                        EditorGUI.DrawTextureAlpha(brushPreviewRect, GetActiveBrush().thumbnail);

                        bool dummy;
                        m_ScrollPos = EditorGUILayout.BeginScrollView(m_ScrollPos, GUILayout.Height(128));
                        var missingBrush = GetLocalContent("noBrushesDefined");
                        int newBrush = BrushSelectionGrid(m_SelectedBrush, m_BrushList, 32, Styles.gridList, missingBrush, out dummy);
                        if (newBrush != m_SelectedBrush) {
                            UpdateSelection(newBrush);
                            repaint = true;
                        }
                        EditorGUILayout.EndScrollView();
                    }
                }
                EditorGUILayout.EndHorizontal();

                return repaint;
            }

            int BrushSelectionGrid(int selected, Brush[] brushes, int approxSize, GUIStyle style, GUIContent emptyString, out bool doubleClick) {
                GUILayout.BeginVertical("box", GUILayout.MinHeight(approxSize));
                int retval = 0;

                doubleClick = false;

                if (brushes.Length != 0) {
                    int columns = (int)(EditorGUIUtility.currentViewWidth - 150) / approxSize;
                    int rows = (int)Mathf.Ceil((brushes.Length + columns - 1) / columns);
                    Rect r = GUILayoutUtility.GetAspectRect((float)columns / (float)rows);
                    Event evt = Event.current;
                    if (evt.type == EventType.MouseDown && evt.clickCount == 2 && r.Contains(evt.mousePosition)) {
                        doubleClick = true;
                        evt.Use();
                    }

                    if (m_Thumnails == null || m_Thumnails.Length != brushes.Length) {
                        m_Thumnails = GUIContentFromBrush(brushes);
                    }
                    retval = GUI.SelectionGrid(r, System.Math.Min(selected, brushes.Length - 1), m_Thumnails, (int)columns, style);
                }
                else
                    GUILayout.Label(emptyString);

                GUILayout.EndVertical();
                return retval;
            }

        }

        static GUIContent[] GUIContentFromBrush(Brush[] brushes) {
            GUIContent[] retval = new GUIContent[brushes.Length];

            for (int i = 0; i < brushes.Length; i++)
                retval[i] = new GUIContent(brushes[i].thumbnail);

            return retval;
        }

    }
}



#endif