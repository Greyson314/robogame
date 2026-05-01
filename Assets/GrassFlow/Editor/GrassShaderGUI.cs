using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.AnimatedValues;

using static GrassFlow.ShaderVariantHelper;
using GrassFlow.Localization;

using PropType = UnityEngine.Rendering.ShaderPropertyType;
using PropFlags = UnityEngine.Rendering.ShaderPropertyFlags;

namespace GrassFlow {
    public class GrassShaderGUI : ShaderGUI {

        bool m_FirstTimeApply = true;



        static Dictionary<string, AnimBool> foldoutDict;
        static Stack<bool> nestedFoldouts;
        static Stack<string> nestedFoldoutProps;

        static GUIStyle foldoutStyle;

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader) {
            base.AssignNewShaderToMaterial(material, oldShader, newShader);

            //this is really stupid but by default shaders are created in the standard shader
            //which sets this to 0.5, which we dont want
            material.SetFloat("_Cutoff", 0);
        }


        void CreateStyles() {
            foldoutStyle = new GUIStyle(EditorStyles.foldout) {
                fontStyle = FontStyle.Bold, fontSize = 12
            };
        }

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] props) {

            CreateStyles();

            EditorGUIUtility.fieldWidth = 50;
            EditorGUIUtility.labelWidth = EditorGUIUtility.currentViewWidth * 0.75f;

            Material mat = materialEditor.target as Material;

            if (m_FirstTimeApply) {
                //UpdateMaterial(mat);
                UpdateBools(materialEditor, props);
                m_FirstTimeApply = false;
            }

#if GRASSFLOW_SRP
            if (mat.GetFloat(pipeTypeID) != 1 && PipelineMaterialChecker.CheckURP()) {
                EditorGUILayout.HelpBox("URP detected but material not set to URP.", MessageType.Warning);
            }
#endif
#if GRASSFLOW_HDRP
            if (mat.GetFloat(pipeTypeID) != 2 && PipelineMaterialChecker.CheckHDRP()) {
                EditorGUILayout.HelpBox("HDRP detected but material not set to HDRP.", MessageType.Warning);
            }
#endif

            bool shaderChanged = DrawShaderVariantUI(mat, materialEditor);
            if (shaderChanged) {
                //i don't even know, this makes no fucking sense
                //but if try to return instead of this unity throws a weird ui error that doesnt matter so
                //w/e
                props = new MaterialProperty[0];
            }


            MaterialProperty mainTexProp = null;
            if (props.Length > 0) {
                mainTexProp = FindProperty("_MainTex", props);
            }

            EditorGUILayout.HelpBox("Check the tooltips or documentation for information on material settings", MessageType.Info, true);




            bool hideIf = false;

            int propIdx = -1;
            foreach (MaterialProperty prop in props) {
                propIdx++;

                bool hideProp = prop.propertyFlags.HasFlag(PropFlags.HideInInspector);

                if (prop.name == "_IncIndent") {
                    EditorGUI.indentLevel++;
                    continue;
                }

                if (prop.name == "_DecIndent") {
                    EditorGUI.indentLevel--;
                    continue;
                }


                if (prop.name.StartsWith("_EndHideIf")) {
                    hideIf = false;
                }

                if (hideIf) {
                    continue;
                }

                if (prop.name.StartsWith("_CollapseEnd") &&
                    nestedFoldoutProps.Peek() == prop.displayName) {

                    if (prop.name == "_CollapseEnd_Maps" && nestedFoldouts.Peek()) {
                        materialEditor.TextureScaleOffsetProperty(mainTexProp);
                    }

                    EditorGUILayout.EndFadeGroup();
                    //Debug.Log("pop: " + prop.name);
                    nestedFoldouts.Pop();
                    nestedFoldoutProps.Pop();
                    EditorGUI.indentLevel--;
                    GUILayout.Space(4);
                    continue;
                }

                if (nestedFoldouts.Count != 0 && !nestedFoldouts.Peek()) {
                    continue;
                }

                GUIContent content = new GUIContent(Locale.GetName(prop.name), Locale.GetMatTooltip(prop.name));
                if (string.IsNullOrEmpty(content.text)) {
                    content.text = prop.displayName;
                }

                if (prop.propertyType == PropType.Texture) {

                    var nextProp1 = (propIdx + 1 < props.Length) ? props[propIdx + 1] : null;
                    bool nextPropHidden = (nextProp1 != null) && (nextProp1.propertyType != PropType.Texture) &&
                        nextProp1.propertyFlags.HasFlag(PropFlags.HideInInspector);

                    EditorGUI.BeginChangeCheck();
                    if (nextPropHidden) {
                        var nextProp2 = (propIdx + 2 < props.Length) ? props[propIdx + 2] : null;
                        bool nextPropHidden2 = (nextProp2 != null) && (nextProp2.propertyType != PropType.Texture) &&
                            nextProp2.propertyFlags.HasFlag(PropFlags.HideInInspector);

                        if (nextPropHidden2) {
                            materialEditor.TexturePropertySingleLine(content,
                                prop, nextProp1, nextProp2);
                        }
                        else {
                            materialEditor.TexturePropertySingleLine(content,
                                prop, nextProp1);
                        }
                    }
                    else {
                        if (!hideProp) {
                            if (prop.propertyFlags.HasFlag(PropFlags.NoScaleOffset)) {
                                materialEditor.TexturePropertySingleLine(content, prop);
                            }
                            else {
                                EditorGUILayout.Space();
                                var rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
                                materialEditor.TexturePropertyMiniThumbnail(rect, prop, content.text, content.tooltip);

                                const float texWidth = 200;
                                rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
                                rect.x += texWidth;
                                rect.width -= texWidth;
                                materialEditor.TextureScaleOffsetProperty(rect, prop);
                            }
                        }
                    }

                    if (EditorGUI.EndChangeCheck()) {
                        HandleTexFeatureKeyword(mat, prop);
                    }

                    if (prop.name == "_EmissionMap") {
                        //materialEditor.LightmapEmissionProperty(2);
                        MaterialEditor.FixupEmissiveFlag(mat);
                        materialEditor.LightmapEmissionFlagsProperty(2, true);
                        //mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.
                    }

                    continue;
                }



                if (prop.name.StartsWith("_CollapseStart")) {
                    //drawProps = GetPropDefined(prop);

                    //GUILayout.Space(15);
                    EditorGUILayout.BeginHorizontal();

                    AnimBool animBool = CheckFoldoutDict(prop.displayName);
                    if (animBool == null) {
                        UpdateBools(materialEditor, props);
                        animBool = CheckFoldoutDict(prop.displayName);
                    }

                    EditorGUI.BeginChangeCheck();

                    animBool.target = EditorGUILayout.Foldout(
                        animBool.target,
                        prop.displayName,
                        true,
                        foldoutStyle
                    );

                    if (EditorGUI.EndChangeCheck()) {
                        SetPref(prop, animBool.target);
                    }

                    //GUILayout.Label(prop.displayName, EditorStyles.boldLabel);

                    if (!hideProp) {
                        materialEditor.ShaderProperty(prop, content);
                    }


                    EditorGUILayout.EndHorizontal();


                    nestedFoldouts.Push(EditorGUILayout.BeginFadeGroup(animBool.faded));
                    nestedFoldoutProps.Push(prop.displayName);
                    EditorGUI.indentLevel++;
                    //Debug.Log("push: " + prop.name + " : " + nestedFoldouts.Peek().ToString());

                    continue;
                }

                if (prop.name.StartsWith("_HideIf")) {
                    hideIf = !prop.GetPropSetOrEnabled();
                }

                if (prop.name.StartsWith("_Space")) {
                    GUILayout.Space(15);
                    continue;
                }


                if (prop.name.StartsWith("_header")) {
                    DrawHeader(prop.displayName);
                    continue;
                }



                //Just a normal prop

                if (hideProp) {
                    continue;
                }




                switch (prop.propertyType) {

                    case PropType.Texture:
                        materialEditor.TexturePropertySingleLine(content, prop);
                        if (!prop.propertyFlags.HasFlag(PropFlags.NoScaleOffset)) {
                            materialEditor.TextureScaleOffsetProperty(prop);
                        }
                        break;


                    default:

                        if (prop.propertyType == PropType.Vector) {
                            GUILayout.Space(5);
                            EditorGUILayout.PrefixLabel(content);
                            content.text = "";
                        }

                        materialEditor.ShaderProperty(prop, content);
                        break;
                }
            }

            GUILayout.Space(10);
            DrawHeader("Other");
            materialEditor.RenderQueueField();
            materialEditor.EnableInstancingField();
            //materialEditor.DoubleSidedGIField();

            wasOpen = true;
        }

        static AnimBool CheckFoldoutDict(string key) {
            if (foldoutDict.ContainsKey(key)) {
                return foldoutDict[key];
            }
            else {
                return null;
            }
        }

        static void UpdateBools(MaterialEditor matEdit, MaterialProperty[] props) {

            foldoutDict = new Dictionary<string, AnimBool>();
            nestedFoldouts = new Stack<bool>();
            nestedFoldoutProps = new Stack<string>();

            foreach (MaterialProperty prop in props) {
                if (prop.name.StartsWith("_CollapseStart")) {

                    AnimBool aBool = new AnimBool(GetPref(prop));
                    aBool.valueChanged.AddListener(matEdit.Repaint);
                    aBool.speed *= 2;
                    foldoutDict.Add(prop.displayName, aBool);
                }
            }


            if (sVariantFoldout == null) {
                sVariantFoldout = new AnimBool(GetPref("ShaderVariants"));
                sVariantFoldout.speed *= 2;
            }
            else sVariantFoldout.valueChanged.RemoveAllListeners();
            sVariantFoldout.valueChanged.AddListener(matEdit.Repaint);
        }



        bool wasOpen = false;
        static AnimBool sVariantFoldout;

        //returns whether or not the shader changed
        bool DrawShaderVariantUI(Material mat, MaterialEditor matEdit) {


            AnimBool aBool = sVariantFoldout;
            bool fadeTarget = aBool.target;
            aBool.target = EditorGUILayout.Foldout(
                aBool.target,
                "Shader Variants",
                true,
                foldoutStyle
            );
            if (fadeTarget != aBool.target) {
                SetPref("ShaderVariants", aBool.target);
            }

            bool forceCompile = false;
            if (!wasOpen) {
                //handle checks that might require to recompile the shader
                if (!mat.HasProperty(VERSIONID)) {
                    ShaderVariantHelper.PortOldShader(mat);
                    return true;
                }
                else {
                    forceCompile = ShaderVariantHelper.CheckShaderNeedsRecompilation(mat);
                }
            }

            if (aBool.faded == 0 && !forceCompile) {
                return false;
            }
            GrassFlowRenderer gf = GrassFlowInspector.currentlyDrawnMesh?.owner;
            return ShaderVariantHelper.HandleVariantGuiAndCompilation(gf, mat, aBool.faded, forceCompile);
        }



        //
        //UTILITY
        //

        const string prefsfix = "GFGUI_";

        static bool GetPref(string name) { return EditorPrefs.GetBool(prefsfix + name, true); }

        static void SetPref(string name, bool val) { EditorPrefs.SetBool(prefsfix + name, val); }

        static bool GetPref(MaterialProperty prop) { return GetPref(prop.displayName); }

        static void SetPref(MaterialProperty prop, bool val) { SetPref(prop.displayName, val); }


        void DrawHeader(string text) {
            GUILayout.Space(10);
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        }



        void HandleTexFeatureKeyword(Material mat, MaterialProperty prop) {
            mat.SetKeyword(prop.name.ToUpper(), prop.GetPropSetOrEnabled());
        }



    }

    static class GrassGUIExtensions {
        public static void SetKeyword(this Material m, string keyword, bool state) {
            if (state)
                m.EnableKeyword(keyword);
            else
                m.DisableKeyword(keyword);
        }
        public static int TryGetInt(this Material m, int id, int defaultValue = 0) {
            int result = defaultValue;
            if (m.HasProperty(id)) {
                result = m.GetInt(id);
            }
            return result;
        }
        public static bool TryGetBool(this Material m, int id) {
            return m.GetFloat(id) == 1;
        }

        public static bool TryGetBool(this Material m, int id, bool defaultValue) {
            bool result = defaultValue;
            if (m.HasProperty(id)) {
                result = m.GetFloat(id) == 1;
            }
            return result;
        }

        public static void TrySetInt(this Material m, int id, int value) {
            if (m.HasProperty(id)) {
                m.SetInt(id, value);
            }
        }
        public static void TrySetBool(this Material m, int id, bool value) {
            if (m.HasProperty(id)) {
                m.SetFloat(id, value ? 1 : 0);
            }
        }

        public static bool GetPropSetOrEnabled(this MaterialProperty prop) {
            switch (prop.propertyType) {

                case PropType.Range:
                case PropType.Float:
                    return prop.floatValue != 0;

                case PropType.Texture:
                    return prop.textureValue;

                case PropType.Vector:
                    return prop.vectorValue != Vector4.zero;

                case PropType.Color:
                    return prop.colorValue != Color.clear;

                default:
                    return true;
            }
        }
    }

}