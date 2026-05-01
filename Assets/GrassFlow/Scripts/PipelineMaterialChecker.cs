
#if UNITY_EDITOR

using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

using static GrassFlow.ShaderVariantHelper;
using UnityEngine.SceneManagement;

namespace GrassFlow {

    [ExecuteInEditMode]
    public class PipelineMaterialChecker : MonoBehaviour {

        static RenderPipelineAsset GetRenderPipelineAsset() {
#if UNITY_2019_3_OR_NEWER
            return GraphicsSettings.currentRenderPipeline;
#else
            return GraphicsSettings.renderPipelineAsset;
#endif
        }

        public static bool CheckURP() {
            if (GetRenderPipelineAsset()) {
                if (GetRenderPipelineAsset().GetType().ToString().Contains("Universal")) {
                    return true;
                }
            }
            return false;
        }
        public static bool CheckHDRP() {
            if (GetRenderPipelineAsset()) {
                if (GetRenderPipelineAsset().GetType().ToString().Contains("HighDefinition")) {
                    return true;
                }
            }
            return false;
        }


        private void Start() {

            //make sure we don't run this on non example scenes just in case someone mistakenly copies it into their project
            if (!SceneManager.GetActiveScene().path.Contains("GrassFlow/Example Scenes/")) {
                Debug.Log("GrassFlow Material Helper exists in your scene, this script is not required or useful outside of the example scenes.");
                return;
            }


            int pipeIdx = 0;
            string shader = "Standard";

            bool urp = CheckURP();
            bool hdrp = CheckHDRP();

            if (urp) {
                pipeIdx = 1;
                shader = "Universal Render Pipeline/Simple Lit";
            }
            else if (hdrp) {
                pipeIdx = 2;
                shader = "HDRP/Lit";
            }

            void FixMat(Material mat) {
                if (mat && mat.shader.name != shader) {
                    var mainTex = mat.GetTexture("_MainTex");
                    var mainScale = mat.GetTextureScale("_MainTex");
                    var mainOffset = mat.GetTextureOffset("_MainTex");
                    var mainCol = mat.GetColor("_Color");
                    mat.shader = Shader.Find(shader);
                    if (urp) {
                        mat.SetTexture("_BaseMap", mainTex);
                        mat.SetTextureScale("_BaseMap", mainScale);
                        mat.SetTextureOffset("_BaseMap", mainOffset);
                    }
                    if (hdrp) {
                        mat.SetTexture("_BaseColorMap", mainTex);
                        mat.SetTextureScale("_BaseColorMap", mainScale);
                        mat.SetTextureOffset("_BaseColorMap", mainOffset);
                    }
                    //both
                    mat.SetColor("_BaseColor", mainCol);
                }
            }

            Renderer[] rends = FindObjectsOfType<MeshRenderer>();
            foreach (var rend in rends) {
                foreach (var mat in rend.sharedMaterials) {
                    FixMat(mat);
                }
            }

            rends = FindObjectsOfType<SkinnedMeshRenderer>();
            foreach (var rend in rends) {
                foreach (var mat in rend.sharedMaterials) {
                    FixMat(mat);
                }
            }
            var grasses = FindObjectsOfType<GrassFlowRenderer>();
            foreach (var gf in grasses) {
                foreach (var gMesh in gf.terrainMeshes) {

                    if (gMesh.renderType == GrassFlowRenderer.GrassRenderType.Terrain) {
                        Material mat = gMesh.terrainObject.materialTemplate;
                        if (mat.HasProperty("_Detail")) {
                            Texture tex = mat.GetTexture("_Detail");
                            FixMat(mat);
                            mat.mainTexture = tex;
                        }
                    }

                    foreach (var lod in gMesh.customMeshLods) {
                        var grassMat = lod.lodMat;
                        if (grassMat) {
                            bool needsComp = false;
                            if (hdrp && grassMat.GetInt(renderPathID) != 1) {
                                grassMat.SetInt(renderPathID, 1);
                                needsComp = true;
                            }
                            if (grassMat.GetInt(pipeTypeID) != pipeIdx) {
                                grassMat.SetInt(pipeTypeID, pipeIdx);
                                needsComp = true;
                            }

                            if (needsComp) {
                                ShaderVariantHelper.HandleVariantGuiAndCompilation(gf, grassMat, 0, true, false);
                            }

                            if (hdrp) {
                                grassMat.SetFloat("GF_SELF_SHADOW", 0);
                                grassMat.SetFloat("specularMult", 1);
                                grassMat.SetFloat("_Gloss", grassMat.GetFloat("specSmooth"));
                            }
                        }
                    }
                }
            }
        }



    }
}

#endif