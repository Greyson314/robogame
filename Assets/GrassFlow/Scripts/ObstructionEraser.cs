using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GrassFlow;
using System.Threading.Tasks;

namespace GrassFlow {
    /// <summary>
    /// A utility for removing or applying effects to grass where it intersects with obstacles in the scene.
    /// </summary>
    public class ObstructionEraser : MonoBehaviour {


        [Header("Settings")]
        [Tooltip("Size to paint on the parameter map each time a spot is obstructed. Defaults to the size of one pixel on the map.")]
        public float eraseSize = 1f;

        [Tooltip("Density of the raycasts used to check for obstructions. You'll want to set this as low as possible while still getting good coverage.")]
        public float testDensity = 1f;
        public bool visualizeDensity = false;
        public bool drawObstructionDebugRays = false;

        [Tooltip("Casts an additional ray to check if the the obstruction is too far from the surface to actually obstruct.")]
        public bool checkOverhangs = false;
        public float overhangTolerance = 0.5f;

        [Tooltip("Defines what to paint when an obstruction is detected. Should be 0-1 value setting percentage to use. " +
            "These values could be negative if you wanted to add grass back, useful if you want to delete the obstacles afterwards.\n" +
            "X = Density.\n" +
            "Y = Height.\n" +
            "z = Flatness.\n" +
            "W = Wind.")]
        public Vector4 paintParams = new Vector4(0.5f, 0.5f, 0.5f, 0.25f);

        public Texture2D brushTex;


        [Header("Assign")]
        public GrassFlowRenderer grassFlow;

        [HideInInspector]
        public int terrainLayer = 0;
        [HideInInspector]
        public int obstacleMask = 0;

        [Tooltip("How many raycasts to process per frame.")]
        public int raysPerFrame = 10000;


        private void Start() {
            Task.Run(() => {
                bool rayHit = Physics.Raycast(Vector3.zero, Vector3.down);
                print(rayHit);
            });
        }

        const float baseDensity = 0.5f;


        //Realistically it'd be better to implement this using RaycastCommand to allow parallel async raycasting
        //But it's such a hassle with that for me since I can't rely on people having the unity plugins for it
        //It's a major hassle and just another reason I hate their shift to the package manager stuff but w/e
        //Point is you might wanna consider reimplimenting this using that if you need performance for a runtime application
        public IEnumerator<float> RunObstacleChecks() {

            if (!grassFlow) {
                Debug.LogError("Obstruction Eraser: GrassFlow not set!");
                yield break;
            }

            Ray ray = new Ray();
            RaycastHit hit, overHit;

            int count = 0;
            int totalCount = 0;
            int terrainMask = 1 << terrainLayer;

            float halfTolerance = overhangTolerance * 0.5f;
            Vector3 topPos = transform.localToWorldMatrix.MultiplyPoint3x4(new Vector3(-0.5f, 0.5f, -0.5f));
            Vector3 size = transform.lossyScale;
            Vector3 scaledDensity = Vector3.one * baseDensity / testDensity;
            float totalCasts = (size.x / scaledDensity.x) * (size.z / scaledDensity.z);

            grassFlow.SetBrushTexture(brushTex);

            bool preBackfaces = Physics.queriesHitBackfaces;
            //the overhang check would break if this was enabled
            Physics.queriesHitBackfaces = false;



            for (float x = 0; x < size.x; x += scaledDensity.x) {
                for (float z = 0; z < size.z; z += scaledDensity.z) {

                    ray.origin = topPos + (transform.right * x) + (transform.forward * z);
                    ray.direction = -transform.up;
                    //Debug.DrawRay(ray.origin, ray.direction, Color.yellow, 1);

                    if (Physics.Raycast(ray, out hit, size.y, obstacleMask)) {

                        if (drawObstructionDebugRays) {
                            Debug.DrawRay(hit.point, hit.normal, Color.blue, 1);
                        }

                        Vector3 obstructionPoint = hit.point;

                        if (Physics.Raycast(ray, out hit, size.y, terrainMask)) {

                            GrassMesh gMesh = grassFlow.GetGrassMeshFromTransform(hit.transform);
                            if (gMesh) {


                                //check if the obstruction hit under the terrain
                                if (Vector3.Dot(-ray.direction, obstructionPoint - hit.point) < 0) {
                                    continue;
                                }

                                Vector2 brushPos = hit.textureCoord;

                                if (checkOverhangs) {

                                    ray.origin = hit.point + ray.direction * halfTolerance;
                                    ray.direction = -ray.direction;

                                    if (Physics.Raycast(ray, out overHit, size.y + halfTolerance, obstacleMask)) {


                                        if (overHit.distance > overhangTolerance * 2) {

                                            if (drawObstructionDebugRays) {
                                                Debug.DrawLine(ray.origin, overHit.point, Color.red, 1);
                                            }

                                            //skip this ray since we want to keep the grass under this overhang
                                            continue;
                                        }
                                    }

                                    //if the ray doesnt hit anything we just assume theres no overhang
                                }

                                float brushSize = eraseSize * gMesh.paramMapHalfPixUV.x * 16;
                                grassFlow.PaintParameters(gMesh, brushPos, brushSize, 1,
                                    -paintParams.x, -paintParams.y, -paintParams.z, -paintParams.w, Vector2.up);

                                if (drawObstructionDebugRays) {
                                    Debug.DrawRay(hit.point, hit.normal, Color.yellow, 1);
                                }
                            }
                        }
                    }

                    if (++count > raysPerFrame) {
                        count = 0;
                        yield return totalCount / totalCasts;
                        grassFlow.SetBrushTexture(brushTex);
                    }
                    totalCount++;
                }
            }

            Physics.queriesHitBackfaces = preBackfaces;
            yield break;
        }






        private void Reset() {


            grassFlow = FindObjectOfType<GrassFlowRenderer>();
            if (grassFlow) {

            }
        }


        float GetDensity() {
            if (testDensity < 0.1f) {
                testDensity = 0.1f;
            }

            return testDensity;
        }

        private void OnDrawGizmosSelected() {

            Vector3 size = Vector3.one;
            Vector3 pos = Vector3.zero;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(pos, size);


            if (visualizeDensity) {

                Vector3 down = Vector3.down * size.y;
                Vector3 scaledDensity = transform.lossyScale;
                if (scaledDensity.x < 0.1f) scaledDensity.x = 0.1f;
                if (scaledDensity.z < 0.1f) scaledDensity.z = 0.1f;
                scaledDensity.x = baseDensity / scaledDensity.x / GetDensity();
                scaledDensity.z = baseDensity / scaledDensity.z / GetDensity();

                Action DrawLine = () => {
                    Gizmos.DrawRay(pos, down);
                };


                pos = new Vector3(-0.5f, 0.5f, 0f);
                for (float x = -0.5f; x < 0.5f; x += scaledDensity.x) {
                    pos.x = x;
                    DrawLine();
                }
                pos = new Vector3(0f, 0.5f, -0.5f);
                for (float z = -0.5f; z < 0.5f; z += scaledDensity.z) {
                    pos.z = z;
                    DrawLine();
                }
            }

            Gizmos.color = Color.green;
            Gizmos.DrawRay(Vector3.zero, Vector3.down);

        }

    }
}