using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GrassFlow {
    public class DeerGrassinteractions : MonoBehaviour {

        [Header("Assignments")]
        public Transform nosePoint;
        public Transform bodyPoint;
        public GrassFlowRenderer grassFlow;
        public Texture2D brushTex;

        [Header("Ripples")]
        public float startSize = 0f;
        public Vector4 pushParams;
        public Vector4 shockwaveParams;
        public Vector4 eatParams;


        [Header("Color Anim")]
#if UNITY_2019_1_OR_NEWER
        [ColorUsage(true, true)]
#endif
        public Color paintCol;
        public Color afterPaintCol;
        public Vector3 paintParams;
        public AnimationCurve paintSpreadCurve;
        public float paintAnimSpeed = 14;

        public bool test;


        Ray ray;
        RaycastHit rayHit;

        private void Update() {
            if (test) {
                test = false;
                AnimatedPaint();
            }
        }

        public void AnimatedPaint() {
            StartCoroutine(AnimatedPaintRoutine());
        }

        IEnumerator AnimatedPaintRoutine() {


            ray = new Ray(bodyPoint.position + bodyPoint.up, Vector3.down);
            if (!Physics.Raycast(ray, out rayHit)) {
                yield break;
            }

            ShockWave();

            var paintMesh = grassFlow.GetGrassMeshFromTransform(rayHit.transform);
            Vector2 paintCoord = rayHit.textureCoord;

            Action<float> HandlePaint = (float time) => {
                float size = paintParams.x * paintSpreadCurve.Evaluate(time * 4);
                float strength = paintParams.y;
                Color col = Color.Lerp(paintCol, afterPaintCol, time * 2);
                float blend = col.a;
                col.a = 0;

                grassFlow.SetBrushTexture(brushTex);
                grassFlow.PaintColor(paintMesh, paintCoord, size, strength, col, new Vector2(0, 1), blend);
            };

            float t = 0;
            while (t < 1) {
                t += Time.deltaTime * paintAnimSpeed;
                HandlePaint(t);
                yield return null;
            }

            HandlePaint(1);
        }



        public void DoPush(Vector3 pushPos, Vector3 pushDir, Vector4 rippleParams) {

            ray = new Ray(pushPos, pushDir);
            if (Physics.Raycast(ray, out rayHit)) {

                Vector3 point = rayHit.point + rayHit.normal * rippleParams.w;
                GrassFlowRenderer.AddRipple(point, rippleParams.x, rippleParams.y, rippleParams.z, startSize);
            }
        }

        public void ShockWave() {
            DoPush(bodyPoint.position + bodyPoint.up, Vector3.down, shockwaveParams);
        }

        public void NosePush() {
            DoPush(nosePoint.position, nosePoint.forward, pushParams);

            var paintMesh = grassFlow.GetGrassMeshFromTransform(rayHit.transform);
            if (paintMesh) {
                grassFlow.SetBrushTexture(brushTex);
                grassFlow.PaintParameters(paintMesh, rayHit.textureCoord,
                    eatParams.x, eatParams.y, 0, eatParams.z, eatParams.w, 0, Vector2.up);
            }
        }


    }
}