using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;


namespace GrassFlow {
    [CustomEditor(typeof(ObstructionEraser))]
    public class ObstructionEraserEditor : Editor {


        ObstructionEraser _eraser;
        ObstructionEraser eraser {
            get {
                if (!_eraser) {
                    _eraser = (ObstructionEraser)target;
                }
                return _eraser;
            }
        }

        static bool isRunning = false;
        static float progress = 0;

        public override void OnInspectorGUI() {

            DrawDefaultInspector();

            EditorGUI.BeginChangeCheck();

            int terrainLayer = EditorGUILayout.LayerField(new GUIContent("Terrain Layer",
                "Layer that your terrain collider is on."),
                eraser.terrainLayer);

            LayerMask rayCastMask = EditorGUILayout.MaskField(new GUIContent("Obstacle Layer Mask",
                "Layers that contain the obstacles you want to check against."),
                InternalEditorUtility.LayerMaskToConcatenatedLayersMask(eraser.obstacleMask), InternalEditorUtility.layers);
            int paintRaycastMask = InternalEditorUtility.ConcatenatedLayersMaskToLayerMask(rayCastMask);


            if (EditorGUI.EndChangeCheck()) {
                Undo.RecordObject(eraser, "Change Inspector");

                paintRaycastMask &= ~(1 << terrainLayer);

                eraser.terrainLayer = terrainLayer;
                eraser.obstacleMask = paintRaycastMask;
            }


            GUILayout.Space(20);

            if (GUILayout.Button("Refresh Map")) {
                eraser.grassFlow?.RevertDetailMaps();
                GrassFlowInspector.ClearDirtyMaps();
            }

            EditorGUILayout.Space();

            if (isRunning) {
                Rect progressRect = GUILayoutUtility.GetRect(new GUIContent(), EditorStyles.helpBox);
                EditorGUI.ProgressBar(progressRect, progress, "Running...");
            }
            else {
                if (GUILayout.Button("Check Obstructions")) {
                    Run();
                }
            }

            if (GrassFlowInspector.dirtyTypes.Count > 0) {

                EditorGUILayout.Space();

                if (GUILayout.Button("Save Map")) {
                    GrassFlowRenderer.instances.Add(eraser.grassFlow);
                    GrassFlowInspector.SaveDatas(true, true);
                }
            }
        }

        async void Run() {
            isRunning = true;
            var enume = eraser.RunObstacleChecks();
            while (enume.MoveNext()) {
                progress = enume.Current;
                Repaint();
                await Task.Delay(16);
            }

            Repaint();
            GrassFlowInspector.SetParametersDirty();
            isRunning = false;
            progress = 0;
        }

    }
}