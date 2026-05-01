using GrassFlow;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using static GrassFlow.GrassMesh;

namespace GrassFlow {
    public class CullingZone {

        public const int MAX_BATCH_LENGTH = 4;

        static CameraCullResults cullResults;
        static Dictionary<Camera, CameraCullResults> cameraCulls = new Dictionary<Camera, CameraCullResults>();
        public static ComputeKernel cullKernel;


        public static void ReleaseResourcesForSubMesh(SubGrassMesh sub) {
            foreach (var cull in cameraCulls) {

                if (cull.Value.frustumGrassMeshes.Count == 0) continue;

                cull.Value.frustumGrassMeshDict.TryGetValue(sub, out CullGrassMesh cgm);
                cgm?.ReleaseFrustumCullResources();
            }
        }
        public static int GetFrustumMemForSubMesh(SubGrassMesh sub) {
            int mem = 0;
            foreach (var cull in cameraCulls) {

                if (cull.Value.frustumGrassMeshes.Count == 0) continue;

                cull.Value.frustumGrassMeshDict.TryGetValue(sub, out CullGrassMesh cgm);
                if (cgm != null) {
                    if (cgm.posIdBuffer != null) mem += cgm.posIdBuffer.stride * cgm.posIdBuffer.count;
                    if (cgm.chunkDataBuffer != null) mem += cgm.chunkDataBuffer.stride * cgm.chunkDataBuffer.count;
                }
            }
            return mem;
        }

        public class CameraCullResults {

            public Camera cam;
            public Vector3 pos;
            public Matrix4x4 camMatrix;
            public Matrix4x4 projMatrix;
            public Matrix4x4 vpMatrix;
            public Plane[] frustumPlanes;


            public int batchCount;
            public List<Batch> batches = new List<Batch>();
            List<Batch> batchesDblBfr = new List<Batch>();

            public Dictionary<SubGrassMesh, CullGrassMesh> frustumGrassMeshDict;
            public List<CullGrassMesh> frustumGrassMeshes;


            public Task asyncCullTask;
            public bool needsRunning;
            public bool needsLoadUnload;

            public class Batch {
                public readonly Vector4[] batchData = new Vector4[MAX_BATCH_LENGTH];
                public GrassMesh.CustomMeshLod lod;
                public SubGrassMesh subMesh;
                public Bounds visibleBounds;
                public int totalInstances;
                public float alpha;
            }

            public CameraCullResults(Camera inCam) {
                cam = inCam;
                frustumPlanes = new Plane[6];
                needsRunning = true;

                frustumGrassMeshDict = new Dictionary<SubGrassMesh, CullGrassMesh>(10);
                frustumGrassMeshes = new List<CullGrassMesh>(10);

                if (!cullKernel) {
                    cullKernel = new ComputeKernel(GrassFlowRenderer.gfComputeShader, "GrassCull");
                }
            }

            public void UpdatePos() {
                pos = cam.transform.position;
                camMatrix = cam.worldToCameraMatrix;
                projMatrix = cam.projectionMatrix;
            }

            void SwapBatchBuffers() {
                var tmp = batches;
                batches = batchesDblBfr;
                batchesDblBfr = tmp;
            }

            public void UpdateVP() {
                UpdatePos();
                vpMatrix = projMatrix * camMatrix;
            }

            public void RunCulling() {

                int cullBatchCount = 0;
                Batch batch = null;

                try {

                    vpMatrix = projMatrix * camMatrix;
                    CalculateFrustumPlanes(vpMatrix, frustumPlanes);

                    needsLoadUnload = false;

                    foreach (var gf in GrassFlowRenderer.instances) {
                        foreach (var gMesh in gf.terrainMeshes) {

                            if (gMesh.chunks == null) continue;
                            if (gMesh.subGrassMeshes == null) continue;
                            if (!gMesh.customMeshLods[0].hasMesh) continue;
                            if (!gMesh.customMeshLods[0].hasMat) continue;
                            if (!gMesh.customMeshLods[0].hasDrawnMesh) continue;

                            float subMeshLoadDist = Mathf.Pow(gMesh.maxRenderDist + 50, 2);
                            float subMeshUnLoadDist = Mathf.Pow(gMesh.maxRenderDist + 100, 2);

                            float lodStartDistSqr = gMesh.customMeshLods[0].distance;
                            lodStartDistSqr *= lodStartDistSqr;

                            foreach (var subMesh in gMesh.subGrassMeshes) {

                                if (subMesh.totalInstances == 0) continue;

                                float subDist = subMesh.bounds.SqrDistance(pos);
                                if (subDist < subMeshLoadDist) {

                                    subMesh.framesInvisible = 0;

                                    if (!subMesh.loaded) {
                                        subMesh.shouldLoad = true;
                                        needsLoadUnload = true;
                                        subMesh.shouldUnload = false;
                                    }
                                }
                                else if (subDist > subMeshUnLoadDist) {

                                    subMesh.framesInvisible++;

                                    if (subMesh.loaded && subMesh.framesInvisible > 100) {
                                        subMesh.shouldUnload = true;
                                        needsLoadUnload = true;
                                    }
                                }

                                //rapidly triggers load/unload of all chunks every frame, useful for testing load performance
                                //but in my tests theres no hitching and its actually quite performant
                                //subMesh.shouldLoad = !subMesh.shouldLoad;
                                //subMesh.shouldUnload = !subMesh.shouldLoad;


                                frustumGrassMeshDict.TryGetValue(subMesh, out CullGrassMesh cullMesh);

                                if (gMesh.frustumCull) {

                                    if (cullMesh == null) {
                                        cullMesh = new CullGrassMesh(subMesh);
                                        frustumGrassMeshDict.Add(subMesh, cullMesh);
                                        frustumGrassMeshes.Add(cullMesh);
                                    }

                                    cullMesh.ProcessVisibleBatches(this);
                                }
                                else {

                                    bool useCrossfade = gMesh.crossFade && gMesh.customMeshLods.Length > 1;

                                    if (cullMesh != null) {
                                        frustumGrassMeshDict.Remove(subMesh);
                                        frustumGrassMeshes.Remove(cullMesh);
                                        cullMesh.ReleaseFrustumCullResources();
                                    }

                                    CullChunk prevCullChunk = default;
                                    MeshChunker.MeshChunk prevChunk = null;
                                    int batchIdx = int.MaxValue;
                                    foreach (var grassChunk in subMesh.chunks) {

                                        if (grassChunk.instCount == 0) continue;

                                        float camDist = grassChunk.worldBounds.SqrDistance(pos);
                                        if (camDist > gMesh.maxRenderDistSqr) {
                                            continue;
                                        }

                                        if (camDist < lodStartDistSqr) {
                                            continue;
                                        }

                                        var chunk = new CullChunk(grassChunk, camDist, this);

                                        void BeginNewBatch() {
                                            if (cullBatchCount >= batchesDblBfr.Count) {
                                                batch = new Batch();
                                                batchesDblBfr.Add(batch);
                                            }
                                            else {
                                                batch = batchesDblBfr[cullBatchCount];
                                            }
                                            cullBatchCount++;
                                            batch.subMesh = subMesh;
                                            batch.lod = gMesh.customMeshLods[chunk.lodIdx];
                                            batch.visibleBounds = grassChunk.worldBounds;
                                            batch.totalInstances = 0;
                                            batch.alpha = 1;
                                            batchIdx = 0;
                                            prevChunk = null;
                                        }

                                        void SetBatchData() {
                                            batch.totalInstances += chunk.instancesToRender;
                                            batch.batchData[batchIdx] = new Vector4(
                                                grassChunk.posBufferOffset,
                                                grassChunk.invLodStepMult,
                                                chunk.instanceLod,
                                                chunk.instancesToRender
                                            );

                                            batchIdx++;
                                        }

                                        void NormalBatch() {
                                            if (batchIdx >= MAX_BATCH_LENGTH) {
                                                BeginNewBatch();
                                            }
                                            else if (prevChunk != null) {
                                                //make sure each batch uses the same lod mesh
                                                //and split batches by frustum so unity can cull batches that are out of view and dont cast shadow
                                                //otherwise something that would be culled might get roped into a batch that is visible
                                                if (prevCullChunk.lodIdx != chunk.lodIdx
                                                || prevCullChunk.isInFrustum != chunk.isInFrustum) {
                                                    BeginNewBatch();
                                                }
                                                else {
                                                    batch.visibleBounds.Encapsulate(grassChunk.worldBounds);
                                                }
                                            }

                                            SetBatchData();
                                        }

                                        if (useCrossfade && chunk.lodIdx < gMesh.customMeshLods.Length - 1) {
                                            var nextLod = gMesh.customMeshLods[chunk.lodIdx + 1];
                                            float crossDist = nextLod.distance - chunk.distance;

                                            if (crossDist < gMesh.crossFadeRange) {

                                                if (crossDist < 0) crossDist = 0;
                                                float pctFade = crossDist / gMesh.crossFadeRange;

                                                BeginNewBatch();
                                                SetBatchData();
                                                batch.alpha = Mathf.Clamp01(pctFade * 2);

                                                chunk.lodIdx++;
                                                BeginNewBatch();
                                                SetBatchData();
                                                batch.alpha = Mathf.Clamp01((1 - pctFade) * 2);

                                                batchIdx = int.MaxValue;
                                            }
                                            else {
                                                NormalBatch();
                                            }
                                        }
                                        else {
                                            NormalBatch();
                                        }

                                        prevChunk = grassChunk;
                                        prevCullChunk = chunk;
                                    }
                                }
                            }
                            gMesh.shouldDraw = true;
                        }
                    }
                } catch (Exception ex) {
                    Debug.LogError(ex.Message);
                }


                SwapBatchBuffers();
                batchCount = cullBatchCount;
                needsRunning = true;
            }

            public void ReleaseFrustumCullResources() {
                foreach (var fMesh in frustumGrassMeshes) {
                    fMesh.ReleaseFrustumCullResources();
                }
            }
        }

        public static void InitializeCamera(Camera camera) {
            if (camera && !cameraCulls.ContainsKey(camera)) {
                cullResults = new CameraCullResults(camera);
                cameraCulls.Add(camera, cullResults);
            }
        }

        public static async Task GetWaitForCullingTask() {
            foreach (var cull in cameraCulls) {
                var cTask = cull.Value.asyncCullTask;
                if (cTask != null && cTask.Status != TaskStatus.Faulted) await cTask;
            }
        }


        public static CameraCullResults GetCullResult(Camera cam) {
            if (!cameraCulls.TryGetValue(cam, out cullResults)) {
                cullResults = new CameraCullResults(cam);
                cameraCulls.Add(cam, cullResults);
            }

            return cullResults;
        }




        static void ClearAllCulledChunks() {
            foreach (var cull in cameraCulls) {
                cull.Value.batchCount = 0;

                cull.Value.ReleaseFrustumCullResources();
                cull.Value.frustumGrassMeshes.Clear();
                cull.Value.frustumGrassMeshDict.Clear();

                cull.Value.needsRunning = true;
            }

            cameraCulls.Clear();
        }

        public static async void ClearCulledChunks() {

            await GetWaitForCullingTask();

            ClearAllCulledChunks();
        }

        //struct for use in culling
        public struct CullChunk {
            public MeshChunker.MeshChunk parentChunk;
            public int instancesToRender;
            public float bladePct;
            public float instanceLod;
            public int lodIdx;
            public bool isInFrustum;
            public float distance;

            public CullChunk(MeshChunker.MeshChunk chunk, float sqrDist, CameraCullResults cullResults = null) {

                Vector3 lodParams = chunk.parentMesh.lodParams;
                float instanceMult = chunk.parentMesh.lodSteps;

                if (cullResults != null) {
                    isInFrustum = TestPlanesAABB(cullResults.frustumPlanes, ref chunk.worldBounds);
                }
                else {
                    isInFrustum = false;
                }

                parentChunk = chunk;

                var camDist = Mathf.Sqrt(sqrDist) - lodParams.z;
                if (camDist <= 0f) camDist = 0.0001f;
                distance = camDist;

                lodIdx = 0;
                for (int i = chunk.parentMesh.customMeshLods.Length - 1; i > 0; i--) {
                    var lod = chunk.parentMesh.customMeshLods[i];
                    if (camDist > lod.distance) {
                        lodIdx = i;
                        break;
                    }
                }

                camDist = 1.0f / camDist;

                bladePct = Mathf.Clamp01(Mathf.Pow(camDist * lodParams.x, lodParams.y));
                instanceLod = bladePct * instanceMult;

                if (instanceLod > instanceMult) instanceLod = instanceMult;

                instancesToRender = Mathf.CeilToInt(instanceLod) * chunk.instMult;
            }
        }

        public class CullGrassMesh {

            public struct CullingDispatch {
                public int startOffset;
                public int dispatchLength;
            }

            public Bounds visibleBounds;
            public List<CullChunk> visibleChunks;
            public List<CullingDispatch> cullBatches;
            List<CullingDispatch> cullBatchesDblBfr;
            public GrassMesh.CustomMeshLod lod;

            public GrassMesh grassMesh;
            public SubGrassMesh subMesh;
            MeshChunker.MeshChunk[] chunks => subMesh.chunks;

            public ComputeBuffer posIdBuffer;
            public ComputeBuffer indirectArgs;
            public ComputeBuffer chunkDataBuffer;
            public Vector4[] chunkDataArr;

            public MaterialPropertyBlock pBlock;


            public void SetIndirectArgs(Mesh mesh) {

                if (indirectArgs != null) return;

                //documentation for indirect args is seriously lacking so a lot of this doesnt make a ton of sense
                //but may as well set it up properly for future reference
                var indirectArgsArr = new uint[] {
                mesh.GetIndexCount(0), //index count per instance
                0, //instance count, placeholder for now
                mesh.GetIndexStart(0), //start index location
                mesh.GetBaseVertex(0), //base vertex location
                0 //start instance location
            };

                indirectArgs = new ComputeBuffer(indirectArgsArr.Length, sizeof(uint), ComputeBufferType.IndirectArguments);
                indirectArgs.SetData(indirectArgsArr);
            }

            public CullGrassMesh(SubGrassMesh sub) {
                subMesh = sub;
                grassMesh = sub.gMesh;
                chunkDataArr = new Vector4[chunks.Length];
                visibleChunks = new List<CullChunk>(chunks.Length);
                cullBatches = new List<CullingDispatch>(chunks.Length);
                cullBatchesDblBfr = new List<CullingDispatch>(chunks.Length);
            }

            public void ProcessVisibleBatches(CameraCullResults cullResults) {

                grassMesh.EnsureMeshLods();
                lod = grassMesh.customMeshLods[0];

                //cull for visible chunks

                Bounds visBound = new Bounds();
                visibleChunks.Clear();
                for (int i = 0; i < chunks.Length; i++) {

                    var chunk = chunks[i];
                    if (chunk.instCount == 0) continue;

                    chunk.cullBatchID = -1;
                    float sqrDist = chunk.worldBounds.SqrDistance(cullResults.pos);

                    if (sqrDist > grassMesh.maxRenderDistSqr) continue;
                    if (!TestPlanesAABB(cullResults.frustumPlanes, ref chunk.worldBounds)) continue;

                    if (visibleChunks.Count == 0) {
                        visBound = chunk.worldBounds;
                    }
                    else {
                        visBound.Encapsulate(chunk.worldBounds);
                    }
                    visibleChunks.Add(new CullChunk(chunk, sqrDist));
                }

                visibleBounds = visBound;


                cullBatchesDblBfr.Clear();

                for (int i = 0; i < visibleChunks.Count; i++) {

                    var chunk = visibleChunks[i];
                    int subIdx = chunk.parentChunk.subIdx;
                    var batch = new CullingDispatch();
                    batch.startOffset = chunk.parentChunk.posBufferOffset;


                    void HandleChunkData(int idx, int length, ref CullChunk bc) {
                        chunks[idx].cullBatchID = cullBatchesDblBfr.Count;
                        chunkDataArr[idx] = new Vector4(length, bc.instancesToRender, bc.instanceLod, bc.parentChunk.invLodStepMult);
                    }

                    int dispatchLength = chunk.parentChunk.instCount;

                    HandleChunkData(subIdx, 0, ref chunk);
                    while (i < visibleChunks.Count - 1) {
                        var nextVisible = visibleChunks[i + 1].parentChunk;
                        var thisChunk = visibleChunks[i].parentChunk;

                        if (nextVisible.subIdx != thisChunk.subIdx + 1) break;

                        var vcc = visibleChunks[++i];
                        var vc = vcc.parentChunk;
                        HandleChunkData(vc.subIdx, dispatchLength, ref vcc);
                        dispatchLength += vc.instCount;
                    }

                    batch.dispatchLength = dispatchLength;
                    cullBatchesDblBfr.Add(batch);
                }

                SwapChunkBuffers();

                //Debug.Log("batches: " + cullBatches.Count);
                //Debug.Log("saved by batching: " + (visibleChunks.Count - cullBatches.Count));
            }


            void SwapChunkBuffers() {
                var tmp = cullBatches;
                cullBatches = cullBatchesDblBfr;
                cullBatchesDblBfr = tmp;
            }

            static readonly int[] zeroArr = new int[] { 0 };
            public void DispatchFrustumCullShader(CameraCullResults cullResults) {

                if (cullBatches.Count == 0) return;
                if (subMesh.posBuffer == null) return;

                SetupFrustumCulling();

                cullKernel.shader.SetMatrix(VPMatrixID, cullResults.vpMatrix);
                cullKernel.shader.SetMatrix(objMatrixID, grassMesh.terrainTransform.localToWorldMatrix);
                cullKernel.shader.SetFloat(maxDrawDistanceID, grassMesh.maxRenderDist);
                cullKernel.shader.SetVector(cullThreshID, grassMesh.frustumCullThresh);
                cullKernel.shader.SetInt(grassPerTriID, grassMesh.grassPerTri);

                posIdBuffer.SetCounterValue(0);
                chunkDataBuffer.SetData(chunkDataArr);

                cullKernel.SetBuffer(grassPosBufferID, subMesh.posBuffer);
                cullKernel.SetBuffer(culledGrassIdxBuffID, posIdBuffer);
                cullKernel.SetBuffer(chunkLodDataID, chunkDataBuffer);
                cullKernel.SetBuffer(indirectArgsID, indirectArgs);

                indirectArgs.SetData(zeroArr, 0, 1, 1);

                pBlock.SetBuffer(grassPosBufferID, subMesh.posBuffer);

                foreach (var batch in cullBatches) {
                    cullKernel.shader.SetInt(startOffsetID, batch.startOffset);
                    //cullKernel.shader.SetInt("cullLength", batch.dispatchLength);
                    cullKernel.DispatchByCount(batch.dispatchLength);
                }

                //uint[] uints = new uint[5];
                //indirectArgs.GetData(uints);
                //Debug.Log(uints[1]);
            }



            public void ReleaseFrustumCullResources() {
                if (indirectArgs != null) {
                    indirectArgs.Release();
                    indirectArgs = null;
                }
                if (posIdBuffer != null) {
                    posIdBuffer.Release();
                    posIdBuffer = null;
                }
                if (chunkDataBuffer != null) {
                    chunkDataBuffer.Release();
                    chunkDataBuffer = null;
                }

#if UNITY_EDITOR
                subMesh.frustumMem = GetFrustumMemForSubMesh(subMesh);
#endif
            }

            public void SetupFrustumCulling() {
                if (indirectArgs == null) {
                    SetIndirectArgs(grassMesh.EnsureMeshLods()[0].drawnMesh);
                }
                if (pBlock == null) {
                    pBlock = new MaterialPropertyBlock();
                    pBlock.SetFloat(meshInvVertCountID, grassMesh.customMeshLods[0].invVertCount);
                }
                if (posIdBuffer == null) {
                    posIdBuffer = new ComputeBuffer(subMesh.posBuffer.count, sizeof(uint) * 2, ComputeBufferType.Append);
                    pBlock.SetBuffer(posIdBufferID, posIdBuffer);
                }
                if (chunkDataBuffer == null) {
                    chunkDataBuffer = new ComputeBuffer(chunks.Length, sizeof(float) * 4);
                }



#if UNITY_EDITOR
                subMesh.frustumMem = GetFrustumMemForSubMesh(subMesh);
#endif
            }
        }

        static bool TestPlanesAABB(Plane[] planes, ref Bounds bounds) {
            for (int i = 0; i < planes.Length; i++) {
                Plane plane = planes[i];
                Vector3 normal_sign = new Vector3(Mathf.Sign(plane.normal.x), Mathf.Sign(plane.normal.y), Mathf.Sign(plane.normal.z));
                Vector3 test_point = (bounds.center) + Vector3.Scale(bounds.extents, normal_sign);

                float dot = Vector3.Dot(test_point, plane.normal);
                if (dot + plane.distance < 0)
                    return false;
            }

            return true;
        }


        static void CalculateFrustumPlanes(Matrix4x4 mat, Plane[] planes) {
            // left
            planes[0].normal = new Vector3(mat.m30 + mat.m00, mat.m31 + mat.m01, mat.m32 + mat.m02);
            planes[0].distance = mat.m33 + mat.m03;

            // right
            planes[1].normal = new Vector3(mat.m30 - mat.m00, mat.m31 - mat.m01, mat.m32 - mat.m02);
            planes[1].distance = mat.m33 - mat.m03;

            // bottom
            planes[2].normal = new Vector3(mat.m30 + mat.m10, mat.m31 + mat.m11, mat.m32 + mat.m12);
            planes[2].distance = mat.m33 + mat.m13;

            // top
            planes[3].normal = new Vector3(mat.m30 - mat.m10, mat.m31 - mat.m11, mat.m32 - mat.m12);
            planes[3].distance = mat.m33 - mat.m13;

            // near
            planes[4].normal = new Vector3(mat.m30 + mat.m20, mat.m31 + mat.m21, mat.m32 + mat.m22);
            planes[4].distance = mat.m33 + mat.m23;

            // far
            planes[5].normal = new Vector3(mat.m30 - mat.m20, mat.m31 - mat.m21, mat.m32 - mat.m22);
            planes[5].distance = mat.m33 - mat.m23;

            // normalize
            for (uint i = 0; i < 6; i++) {
                float length = planes[i].normal.magnitude;
                planes[i].normal /= length;
                planes[i].distance /= length;
            }
        }

        static int culledGrassIdxBuffID = Shader.PropertyToID("culledGrassIdxBuff");
        static int posBufferID = Shader.PropertyToID("posBuffer");
        static int grassPosBufferID = Shader.PropertyToID("grassPosBuffer");
        static int chunkLodDataID = Shader.PropertyToID("chunkLodData");
        static int maxDrawDistanceID = Shader.PropertyToID("maxDrawDistance");
        static int VPMatrixID = Shader.PropertyToID("VPMatrix");
        static int objMatrixID = Shader.PropertyToID("objMatrix");
        static int startOffsetID = Shader.PropertyToID("startOffset");
        static int cullThreshID = Shader.PropertyToID("cullThresh");

        static int indirectArgsID = Shader.PropertyToID("indirectArgs");
        static int grassPerTriID = Shader.PropertyToID("grassPerTri");

        static int posIdBufferID = Shader.PropertyToID("posIdBuffer");
        static int meshInvVertCountID = Shader.PropertyToID("meshInvVertCount");

    }
}