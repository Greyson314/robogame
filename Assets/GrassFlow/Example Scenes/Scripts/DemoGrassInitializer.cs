using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GrassFlow {
    public class DemoGrassInitializer : MonoBehaviour {

        public MeshRenderer terrain;

        GrassFlowRenderer grassFlow;

        private void Awake() {

            grassFlow = GetComponent<GrassFlowRenderer>();
            grassFlow.initOnStart = false;

            //enable this so that the color map isnt limited to 0-1, allows the demo to have good bloom in the render
            //but this wont really matter without post proecessing set up in the example scene
            GrassFlowRenderer.useFloatFormatColorMap = true;
        }

        private async void Start() {

            //init manually and wait for it to finish so we can guarantee the color map render texture has been created
            await grassFlow.Refresh();

            Material tMat = Instantiate(terrain.sharedMaterial);
            terrain.sharedMaterial = tMat;

            //set the terrains detail texture to the grass color map just for some extra flare because we can
            tMat.SetTexture("_DetailAlbedoMap", grassFlow.GetGrassMeshFromTransform(terrain.transform).colorMapRT);
            tMat.EnableKeyword("_DETAIL_MULX2");

            //lower the color since the material detail albedo is multiplied by 2
            tMat.color *= 0.5f;

        }
    }
}