using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SVPreprocessor;

namespace GrassFlow {
    public class CompilerTest : MonoBehaviour {

        void Start() {

            string[] keywords = new string[] { "FORWARD", "DEPTH_PASS", "GEOMETRY", "URP", "SRP" };
            string sName = string.Join(" ", keywords);

            ShaderVariantPreprocessor.resourceFolder = "GrassFlow/";
            ShaderVariantPreprocessor.CompileShader("GrassFlowShaderTemplate", sName, keywords);
        }
    }
}
