
#if UNITY_EDITOR

using System.IO;
using System.Linq;
using System.Collections;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using SVPreprocessor;
using System;

namespace GrassFlow {
    public class ShaderVariantHelper {


        public const float shaderVersionNum = 23f;

        public static bool HandleVariantCompilation(Material mat, HashSet<string> keywords) {

            TextInfo tI = new CultureInfo("en-US", false).TextInfo;
            string sName = tI.ToTitleCase(string.Join(" ", keywords).ToLower());

            ShaderVariantPreprocessor.resourceFolder = "GrassFlow/";
            string shaderText = ShaderVariantPreprocessor.CompileShader("GrassFlowShaderTemplate", sName, keywords);
            //Debug.Log(shaderText);


            string shaderVariantFolder = FindFolderInProject("Assets", "GrassFlow/Shaders/Variants");
            if (shaderVariantFolder == null) {
                Debug.LogError("GrassFlow Shader variant folder could not be found. Make sure you haven't moved the path for GrassFlow/Shaders/Variants");
                return false;
            }

            string fullShaderName = "GrassFlow/" + sName;
            string fullShaderPath = shaderVariantFolder + "GF " + sName + ".shader";


            var existingShader = File.Exists(fullShaderPath);
            bool shaderExists = false;
            if (existingShader) {
                string existingText = File.ReadAllText(fullShaderPath);
                if (existingText == shaderText) {
                    //same shader so don't bother
                    //Debug.Log("same shader text");
                    shaderExists = true;
                }
                else {
                    //shader diferrs somehow, probably due to GrassFlow update changing template files
                    //so just continue and overwrite the file
                    //Debug.Log("diff shader text");
                }
            }

            if (!shaderExists) {
                File.WriteAllText(fullShaderPath, shaderText);
                AssetDatabase.ImportAsset(fullShaderPath);
            }

            Shader newShader = Shader.Find(fullShaderName);
            if (newShader) {
                //Debug.Log(mat.shader == newShader);
                if (mat.shader != newShader) {
                    mat.shader = newShader;
                    return true;
                }
            }
            else {
                Debug.LogError("Shader Variant Compilation Error. Shader Name: " + fullShaderName);
            }

            return false;
        }


        //It's a bit silly that this also draws the UI but it was easier than refactoring to split this all up into two functions
        public static bool HandleVariantGuiAndCompilation(GrassFlowRenderer gf, Material mat, float fade, bool forceCompile, bool drawUI = true) {
            int renderPathIdx = mat.GetInt(renderPathID);
            int pipeTypeIdx = mat.GetInt(pipeTypeID);

            ShaderFeatures features = 0;
            if (mat.TryGetMaterialToggle(depthPassID)) {
                features |= ShaderFeatures.Depth_Pass;
            }
            if (mat.TryGetMaterialToggle(noTransparencyID)) {
                features |= ShaderFeatures.No_Transparency;
            }
            if (mat.TryGetMaterialToggle(lowerQualityID)) {
                features |= ShaderFeatures.Lower_Quality;
            }

#if !GRASSFLOW_SRP && !GRASSFLOW_HDRP
            if (mat.TryGetMaterialToggle(forwardAddID)) {
                features |= ShaderFeatures.Forward_Add;
            }
#endif


            EditorGUI.BeginChangeCheck();
            if (drawUI && EditorGUILayout.BeginFadeGroup(fade)) {

                Func<GUIContent, Rect> GetInlineRect = (GUIContent content) => {
                    var rct = GUILayoutUtility.GetRect(content, EditorStyles.label);
                    rct.width += 5;
                    return rct;
                };

                //EditorGUILayout.BeginHorizontal();
                //var renderModeContent = new GUIContent("Render Mode", "Which shader mode to use.");
                //EditorGUI.PrefixLabel(GetInlineRect(renderModeContent), renderModeContent);
                //renderTypeIdx = EditorGUILayout.Popup(renderTypeIdx, renderTypeOpts);
                //EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                var pipeContent = new GUIContent("Render Pipeline", "Set this to match your render pipeline. Probably standard if you don't know.");
                EditorGUI.PrefixLabel(GetInlineRect(pipeContent), pipeContent);
                pipeTypeIdx = EditorGUILayout.Popup(pipeTypeIdx, pipelineOpts);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                var pathContent = new GUIContent("Render Path", "Can be used in deferred mode if your scene it set up for it. Deferred can improve lighting perforamance but cannot have transparency and will use dithering instead which can be undesirable.");
                EditorGUI.PrefixLabel(GetInlineRect(pathContent), pathContent);
                renderPathIdx = EditorGUILayout.Popup(renderPathIdx, pathOpts);
                EditorGUILayout.EndHorizontal();

#if GRASSFLOW_HDRP
                if (renderPathIdx == 0) {
                    renderPathIdx = 1;
                    mat.SetInt(renderPathID, renderPathIdx);
                    Debug.LogError("GRASSFLOW: Forward rendering not supported in HDRP.");
                }
#endif

                EditorGUILayout.BeginHorizontal();
                var featureContent = new GUIContent("Features", "Select which features to use.\n" +
                    "Depth Pass: Required for casting/receiving shadows.\n" +
                    "Forward Add: Required for receiving additional lighting in standard pipeline. Not necessary in URP.\n" +
                    "No Transparency: Disables transparent and cutout capabilities. This also means no dithering. May perform drastically better especially on Mobile.\n" +
                    "Lower Quality: Disables many superficial calculations in the shader to optimize performance. Useful for mobile platforms.");
                EditorGUI.PrefixLabel(GetInlineRect(featureContent), featureContent);
                features = (ShaderFeatures)EditorGUILayout.EnumFlagsField(features);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndFadeGroup();


            bool changed = false;

            if (EditorGUI.EndChangeCheck() || forceCompile) {

                HashSet<string> keywords = new HashSet<string>();
                keywords.Add(pathDict.ElementAt(renderPathIdx).Value);
                keywords.Add(pipelineDict.ElementAt(pipeTypeIdx).Value);

                if (keywords.Contains(URP) || keywords.Contains(HDRP)) {
                    keywords.Add(SRP);
                }

                var featureNames = Enum.GetNames(typeof(ShaderFeatures));
                foreach (var name in featureNames) {
                    if (features.HasFlag(Enum.Parse(typeof(ShaderFeatures), name) as Enum)) {
                        keywords.Add(name.ToUpper());
                    }
                }


                Undo.RecordObject(mat, "GrassFlow Shader Wizard");

#if !GRASSFLOW_SRP && !GRASSFLOW_HDRP
                if ((keywords.Contains(DEFERRED) || keywords.Contains(SRP)) && keywords.Contains(FORWARD_ADD)) {
                    keywords.Remove(FORWARD_ADD);
                    mat.SetFloat(forwardAddID, 0);
                }
                else {
                    mat.SetFloat(forwardAddID, features.HasFlag(ShaderFeatures.Forward_Add) ? 1 : 0);
                }
#endif

                changed = ShaderVariantHelper.HandleVariantCompilation(mat, keywords);

                mat.SetInt(renderPathID, renderPathIdx);
                mat.SetInt(pipeTypeID, pipeTypeIdx);

                mat.SetFloat(depthPassID, features.HasFlag(ShaderFeatures.Depth_Pass) ? 1 : 0);
                mat.SetFloat(noTransparencyID, features.HasFlag(ShaderFeatures.No_Transparency) ? 1 : 0);
                mat.SetFloat(lowerQualityID, features.HasFlag(ShaderFeatures.Lower_Quality) ? 1 : 0);

                mat.SetFloat(VERSIONID, shaderVersionNum);
            }

            return changed;
        }

        public static bool CheckShaderNeedsRecompilation(Material mat) {

            if (!mat.shader.isSupported) {
                mat.shader = Shader.Find("GrassFlow/Grass Material Repair");
            }

            if (!mat.HasProperty(VERSIONID)) {
                ShaderVariantHelper.PortOldShader(mat);
                return false;
            }
            else if (mat.GetFloat(VERSIONID) != shaderVersionNum) {
                return true;
                //Debug.Log("vDiff");
            }
            else if (mat.shader.name.EndsWith("Repair")) {
                return true;
            }

            return false;
        }


        /// <summary>
        /// Looks in the project folders to find a specfic folder structure.
        /// Useful for finding a folder that may be moved around in different subfolders
        /// </summary>
        static string FindFolderInProject(string startPath, string searchPath) {

            var folders = AssetDatabase.GetSubFolders(startPath);

            foreach (var f in folders) {
                if (f.EndsWith(searchPath)) {
                    return f + "/";
                }
                else {
                    string result = FindFolderInProject(f, searchPath);
                    if (result != null) return result;
                }
            }

            return null;
        }

        public static Dictionary<string, string> pipelineDict = new Dictionary<string, string>() {
            {"Standard", "STANDARD"},
            {"URP", "URP"},
            {"HDRP", "HDRP"},
        };
#if GRASSFLOW_SRP || GRASSFLOW_HDRP
        public static string[] pipelineOpts = pipelineDict.Select(x => x.Key).ToArray();
#else
        public static string[] pipelineOpts = new string[] { "Standard" };
#endif

        public static Dictionary<string, string> pathDict = new Dictionary<string, string>() {
            {"Forward", "FORWARD"},
            {"Deferred", "DEFERRED"},
        };
        public static string[] pathOpts = pathDict.Select(x => x.Key).ToArray();

        public enum ShaderFeatures {
            //None = 0,
#if !GRASSFLOW_SRP && !GRASSFLOW_HDRP
            Forward_Add = 1,
#endif
            Depth_Pass = 2,
            No_Transparency = 4,
            Lower_Quality = 8,
            //All = ~0,
        };


        public static int pipeTypeID = Shader.PropertyToID("Pipe_Type");
        public static int renderTypeID = Shader.PropertyToID("Render_Type");
        public static int renderPathID = Shader.PropertyToID("Render_Path");
        public static int depthPassID = Shader.PropertyToID("Depth_Pass");
        public static int forwardAddID = Shader.PropertyToID("Forward_Add");
        public static int noTransparencyID = Shader.PropertyToID("No_Transparency");
        public static int lowerQualityID = Shader.PropertyToID("Lower_Quality");
        public static int VERSIONID = Shader.PropertyToID("VERSION");

        public const string DEPTH_PASS = "DEPTH_PASS";
        public const string FORWARD_ADD = "FORWARD_ADD";
        public const string DEFERRED = "DEFERRED";
        public const string FORWARD = "FORWARD";
        public const string STANDARD = "STANDARD";
        public const string SRP = "SRP";
        public const string URP = "URP";
        public const string HDRP = "HDRP";

        public static void PortOldShader(Material mat) {

            HashSet<string> keywords = new HashSet<string>();

            string sName = mat.shader.name;

            if (sName.EndsWith("Grass Material Shader With Depth Pass")) {
                keywords.Add(FORWARD, STANDARD, DEPTH_PASS);
                HandleVariantCompilation(mat, keywords);
            }
            else if (sName.EndsWith("Grass Material Shader With Depth&Add Passes")) {
                keywords.Add(FORWARD, STANDARD, FORWARD_ADD, DEPTH_PASS);
                HandleVariantCompilation(mat, keywords);
                mat.SetFloat(forwardAddID, 1);
            }
            else if (sName.EndsWith("Deferred Grass Material Shader")) {
                keywords.Add(DEFERRED, STANDARD, DEPTH_PASS);
                HandleVariantCompilation(mat, keywords);
                mat.SetFloat(renderPathID, 1);
            }
            else if (sName.EndsWith("Grass Material Shader URP")) {
                keywords.Add(FORWARD, URP, SRP, DEPTH_PASS);
                HandleVariantCompilation(mat, keywords);
                mat.SetFloat(pipeTypeID, 1);
            }
            else if (sName.EndsWith("Grass Material Shader With Surface Tesselation")) {
                keywords.Add(FORWARD, STANDARD, DEPTH_PASS);
                HandleVariantCompilation(mat, keywords);
                mat.SetFloat(renderTypeID, 1);
            }
            else if (sName.EndsWith("Grass Material Shader")) {
                keywords.Add(FORWARD, STANDARD);
                HandleVariantCompilation(mat, keywords);
                mat.SetFloat(depthPassID, 0);
            }
        }
    }



    public static class SVHExt {
        public static void Add<T>(this HashSet<T> set, params T[] items) {
            foreach (T item in items) {
                set.Add(item);
            }
        }

        public static bool TryGetMaterialToggle(this Material mat, int toggleID) {
            return mat.HasProperty(toggleID) && mat.GetFloat(toggleID) == 1;
        }
    }
}

#endif