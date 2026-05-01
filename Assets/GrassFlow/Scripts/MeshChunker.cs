using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;
using System.Threading.Tasks;
using UnityEngine.Rendering;
using Unity.Collections;

using SubGrassMesh = GrassFlow.GrassMesh.SubGrassMesh;
using System.Drawing;

namespace GrassFlow {

    public class MeshChunker {

        class MeshChunkData {
            public List<int> tris = new List<int>();
            public Bounds bounds;

            public void CalculateBounds(List<Vector3> verts) {
                bounds = new Bounds(verts[tris[0]], Vector3.zero);

                bounds.Encapsulate(verts[tris[1]]);
                bounds.Encapsulate(verts[tris[2]]);

                for (int i = 3; i < tris.Count; i += 3) {
                    bounds.Encapsulate(verts[tris[i + 0]]);
                    bounds.Encapsulate(verts[tris[i + 1]]);
                    bounds.Encapsulate(verts[tris[i + 2]]);
                }
            }

            public static implicit operator bool(MeshChunkData data) => data != null;
        }


        public class MeshChunk {
            public GrassMesh parentMesh;
            public int chunkIdx;
            public int subIdx;
            public Bounds worldBounds;
            public Bounds meshBounds;
            public Vector4 chunkPos;

            public int posBufferOffset;
            public float invLodStepMult;
            public int terrainTriCount;
            public int instMult = 1;
            public int instCount;
            public int cullBatchID = -1;

            [NonSerialized] internal List<int> tmpTris;
            [NonSerialized] internal ComputeBuffer triBuff;

            [NonSerialized] public MaterialPropertyBlock pBlock;
            public MeshChunk(GrassMesh gMesh) {
                parentMesh = gMesh;
                pBlock = new MaterialPropertyBlock();
            }
        }

        static float map(float value, float from1, float to1, float from2, float to2) {
            if (to1 == from1) return 0;

            return Mathf.Clamp((value - from1) / (to1 - from1), from2, to2);
        }


        static float minArea = float.MaxValue;
        static float maxArea = 0;

        public static void ResetTriangleMinMax() {
            minArea = float.MaxValue;
            maxArea = 0;
        }

        static void NormalizeMeshDensity(GrassMesh gF, List<int> tris, List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs) {


            ResetTriangleMinMax();

            const float densityPredictionMultVerts = 6;
            const float baseAreaSubdiv = 2.25f;

            float[] areas = new float[tris.Count / 3];

            for (int i = 0; i < areas.Length; i++) {

                int triIdx = i * 3;
                Vector3 p1 = verts[tris[triIdx + 0]];
                Vector3 p2 = verts[tris[triIdx + 1]];
                Vector3 p3 = verts[tris[triIdx + 2]];

                float a = Vector3.Distance(p1, p2);
                float b = Vector3.Distance(p2, p3);
                float c = Vector3.Distance(p3, p1);
                float s = (a + b + c) * 0.5f;
                float area = Mathf.Sqrt(s * (s - a) * (s - b) * (s - c));

                if (area < minArea) {
                    minArea = area;
                }
                if (area > maxArea) {
                    maxArea = area;
                }

                areas[i] = area;
            }

            minArea /= baseAreaSubdiv;
            if (maxArea / minArea > gF.normalizeMaxRatio) {
                minArea = maxArea / gF.normalizeMaxRatio;
            }



            tris.Capacity = (int)(tris.Capacity * baseAreaSubdiv);
            verts.Capacity = (int)(verts.Capacity * densityPredictionMultVerts);
            norms.Capacity = (int)(norms.Capacity * densityPredictionMultVerts);
            uvs.Capacity = (int)(uvs.Capacity * densityPredictionMultVerts);

            float triCount = tris.Count;
            float vertCount = verts.Count;

            for (int i = 0; i < areas.Length; i++) {

                int triIdx = i * 3;
                int t1 = tris[triIdx + 0];
                int t2 = tris[triIdx + 1];
                int t3 = tris[triIdx + 2];

                float area = areas[i];

                int subDivs = Mathf.RoundToInt(area / minArea);
                float step = 1f / subDivs;
                int prevIdx = triIdx;

                for (int s = 1; s < subDivs; s++) {

                    float t = s * step;

                    int newIdx = verts.Count;
                    verts.Add(Vector3.Lerp(verts[t1], verts[t3], t));
                    norms.Add(Vector3.Lerp(norms[t1], norms[t3], t));
                    uvs.Add(Vector2.Lerp(uvs[t1], uvs[t3], t));

                    if (s == 1) {
                        tris[triIdx + 2] = newIdx;

                    }
                    else {
                        tris.Add(prevIdx);
                        tris.Add(t2);
                        tris.Add(newIdx);
                    }

                    if (s == subDivs - 1) {
                        tris.Add(newIdx);
                        tris.Add(t2);
                        tris.Add(t3);
                    }

                    prevIdx = newIdx;
                }
            }

            //Debug.Log("Tris - " + triCount + " : " + tris.Count + " : " + (tris.Count / triCount));
            //Debug.Log("Verts - " + vertCount + " : " + verts.Count + " : " + (verts.Count / vertCount));

        }

        static int lodMultID = Shader.PropertyToID("lodMult");
        static int posBufferOffsetID = Shader.PropertyToID("posBufferOffset");
        static int meshInvVertCountID = Shader.PropertyToID("meshInvVertCount");


        struct GrassPos {

            public Vector3 pos;
            public Vector3 norm;
            public Vector2 uv;
            public Vector4 paramo; // chunk id, height, flatness, wind
            public Vector4 col;
            public float type;

            public const int stride = (sizeof(float) * 3) +
                                      (sizeof(float) * 3) +
                                      (sizeof(float) * 2) +
                                      (sizeof(float) * 4) +
                                      (sizeof(float) * 4) +
                                      (sizeof(float) * 1);
        }


        struct GrassPosCompressed {

            public Vector3 pos;
            public Vector3Int data1;
            public Vector2Int data2;

            public static int GetStride(bool baked) => baked ? strideBaked : strideNoBake;

            public const int strideBaked = (sizeof(uint) * 3) +
                                      (sizeof(uint) * 3) +
                                      (sizeof(uint) * 2);

            public const int strideNoBake = (sizeof(uint) * 3) +
                                            (sizeof(uint) * 2);
        }


        public struct VertexData {
            public Vector3 pos;
            public Vector3 norm;
            public Vector2 uv;

            public const int stride = (sizeof(float) * 3) +
                                      (sizeof(float) * 3) +
                                      (sizeof(float) * 2);
        }


        static int posBufferID = Shader.PropertyToID("posBuffer");
        static int countBuffID = Shader.PropertyToID("countBuff");
        static int vertsID = Shader.PropertyToID("verts");

        static int terrainHeightMapID = Shader.PropertyToID("terrainHeightMap");
        static int terrainHoleMapID = Shader.PropertyToID("terrainHoleMap");
        static int terrainNormalMapID = Shader.PropertyToID("terrainNormalMap");
        static int terrainSizeID = Shader.PropertyToID("terrainSize");
        static int invTerrainSizeID = Shader.PropertyToID("invTerrainSize");
        static int terrainChunkSizeID = Shader.PropertyToID("terrainChunkSize");
        static int terrainExpansionID = Shader.PropertyToID("terrainExpansion");
        static int terrainMapOffsetID = Shader.PropertyToID("terrainMapOffset");
        static int _chunkPosID = Shader.PropertyToID("_chunkPos");
        static int terrainMatrixID = Shader.PropertyToID("terrainMatrix");
        static int worldToTerrainMatrixID = Shader.PropertyToID("worldToTerrainMatrix");


        static int triCountID = Shader.PropertyToID("triCount");
        static int trisID = Shader.PropertyToID("tris");
        static int dispatchCountID = Shader.PropertyToID("dispatchCount");
        static int chunkID = Shader.PropertyToID("chunkID");
        static int subID = Shader.PropertyToID("subID");
        static int bakeDensityID = Shader.PropertyToID("bakeDensity");
        static int terrainSlopeThreshID = Shader.PropertyToID("terrainSlopeThresh");
        static int terrainSlopeFadeID = Shader.PropertyToID("terrainSlopeFade");


        static int typeMapID = Shader.PropertyToID("typeMap");
        static int colorMapID = Shader.PropertyToID("colorMap");
        static int dhfParamMapID = Shader.PropertyToID("dhfParamMap");

        static void SetKernelParamMaps(ComputeKernel kernel, GrassMesh gMesh) {

            Texture colMap = gMesh.colorMapRT ? gMesh.colorMapRT : (Texture)gMesh.colorMap;
            Texture paramMap = gMesh.paramMapRT ? gMesh.paramMapRT : (Texture)gMesh.paramMap;
            Texture typeMap = gMesh.typeMapRT ? gMesh.typeMapRT : (Texture)gMesh.typeMap;

            kernel.SetTexture(typeMapID, (typeMap ? typeMap : Texture2D.blackTexture));
            kernel.SetTexture(colorMapID, (colMap ? colMap : Texture2D.whiteTexture));
            kernel.SetTexture(dhfParamMapID, (paramMap ? paramMap : Texture2D.whiteTexture));
        }

        static ComputeBuffer GetPosBuffer(SubGrassMesh subMesh, int totalCount) {
            subMesh.posBuffer.ReleaseIfValid();
            subMesh.posBuffer = new ComputeBuffer(totalCount, GrassPosCompressed.GetStride(subMesh.gMesh.bakeData), ComputeBufferType.Append);

            return subMesh.posBuffer;
        }


        static async Task<NativeArray<int>> CountBuffReadback(GrassMesh gMesh) {
            var countBuff = gMesh.chunkCountBuff;

            NativeArray<int> countArr;
            if (SystemInfo.supportsAsyncGPUReadback) {
                countArr = new NativeArray<int>(countBuff.count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                var req = AsyncGPUReadback.RequestIntoNativeArray(ref countArr, countBuff);

                if (GrassFlowRenderer.processAsync) {
                    while (!req.done) {
                        await Task.Delay(15);
                    }
                }
                else {
                    req.WaitForCompletion();
                }
            }
            else {
                int[] tC = new int[countBuff.count];
                countBuff.GetData(tC);
                countArr = new NativeArray<int>(tC, Allocator.Persistent);
            }

            return countArr;
        }

        static async Task HandleCounter(GrassMesh gMesh) {

            var countArr = await CountBuffReadback(gMesh);

            foreach (var sub in gMesh.subGrassMeshes) {

                int currentCount = 0;
                foreach (var chunk in sub.chunks) {

                    int count = countArr[chunk.chunkIdx];
                    //Debug.Log(count);

                    chunk.posBufferOffset = currentCount;
                    chunk.invLodStepMult = 1f / (count / gMesh.lodSteps);
                    chunk.pBlock.SetFloat(lodMultID, chunk.invLodStepMult);
                    chunk.pBlock.SetInt(posBufferOffsetID, chunk.posBufferOffset);

                    chunk.instCount = count;
                    chunk.instMult = Mathf.FloorToInt(chunk.instCount / (float)chunk.parentMesh.lodSteps / (float)chunk.parentMesh.grassPerTri);
                    if (chunk.instMult <= 0) chunk.instMult = 1;

                    currentCount += count;
                }

                sub.totalInstances = currentCount;
            }

            countArr.Dispose();
        }

        static int trimWidthID = Shader.PropertyToID("trimWidth");
        static int removePosID = Shader.PropertyToID("removePos");
        static int trimmedPosBufferID = Shader.PropertyToID("trimmedPosBuffer");
        static int posBufferSourceID = Shader.PropertyToID("posBufferSource");

        static ComputeKernel trimKernel;
        static ComputeKernel removeKernel;
        static void TrimPosBuffer(SubGrassMesh subMesh) {

            int totalCount = subMesh.totalInstances;
            if (totalCount <= 0) {
                subMesh.posBuffer.Release();
                subMesh.posBuffer = null;
                return;
            }

            if (!trimKernel) trimKernel = new ComputeKernel(GrassFlowRenderer.gfComputeShader, "TrimPosBuffer");
            trimKernel.shader.SetInt(trimWidthID, totalCount);

            //use half stride because data will be compressed
            var trimmedBuff = new ComputeBuffer(totalCount, GrassPosCompressed.GetStride(subMesh.gMesh.bakeData), ComputeBufferType.Append);
            trimmedBuff.SetCounterValue(0);
            trimKernel.SetBuffer(trimmedPosBufferID, trimmedBuff);
            trimKernel.SetBuffer(posBufferSourceID, subMesh.posBuffer);
            trimKernel.DispatchByCount(totalCount);

            subMesh.posBuffer.Release();
            subMesh.posBuffer = trimmedBuff;
        }

        static readonly int[] dummyArray = new int[1] { 0 };

        /// <summary>
        /// This method removes the grass directly from the buffer so it wont be considered for render at all.
        /// It's more efficient but is not persistent if the chunk get unloaded and then reloaded.
        /// Also it's buggy and grass might do weird stuff.
        /// </summary>
        public static async void RemoveGrass(GrassMesh gMesh, Vector4 pos, float radius) {
            if (!removeKernel) removeKernel = new ComputeKernel(GrassFlowRenderer.gfComputeShader, "RemoveGrassFromBuffer");

            removeKernel.SetBuffer(countBuffID, gMesh.chunkCountBuff);

            pos.w = radius * radius;
            removeKernel.shader.SetVector(removePosID, pos);
            removeKernel.shader.SetMatrix(terrainMatrixID, gMesh.terrainTransform.localToWorldMatrix);


            foreach (var sub in gMesh.subGrassMeshes) {

                if(!sub.loaded) continue;
                if (sub.bounds.SqrDistance(pos) > pos.w) continue;

                //var trimmedBuff = new ComputeBuffer(sub.posBuffer.count, sub.posBuffer.stride, ComputeBufferType.Append);
                //trimmedBuff.SetCounterValue(0);
                removeKernel.SetBuffer(posBufferSourceID, sub.posBuffer);
                removeKernel.SetBuffer(trimmedPosBufferID, sub.posBuffer);
                //removeKernel.SetBuffer(trimmedPosBufferID, trimmedBuff);
                //removeKernel.SetBuffer(posBufferID, trimmedBuff);
                foreach (var chunk in sub.chunks) {

                    if (chunk.instCount <= 0) continue;
                    if (chunk.worldBounds.SqrDistance(pos) > pos.w) continue;

                    gMesh.chunkCountBuff.SetData(dummyArray, 0, chunk.chunkIdx, 1);

                    removeKernel.shader.SetInt(trimWidthID, chunk.instCount);
                    removeKernel.shader.SetInt(chunkID, chunk.chunkIdx);
                    removeKernel.shader.SetInt(posBufferOffsetID, chunk.posBufferOffset);

                    removeKernel.Dispatch(chunk.instCount);
                }

                //sub.posBuffer.Release();
                //sub.posBuffer = trimmedBuff;
            }

            var countArr = await CountBuffReadback(gMesh);
            foreach (var sub in gMesh.subGrassMeshes) {
                int currentCount = 0;
                foreach (var chunk in sub.chunks) {

                    int count = countArr[chunk.chunkIdx];
                    chunk.invLodStepMult = 1f / (count / gMesh.lodSteps);
                    chunk.pBlock.SetFloat(lodMultID, chunk.invLodStepMult);

                    chunk.instCount = count;
                    chunk.instMult = Mathf.FloorToInt(chunk.instCount / (float)chunk.parentMesh.lodSteps / (float)chunk.parentMesh.grassPerTri);
                    if (chunk.instMult <= 0) chunk.instMult = 1;

                    currentCount += count;
                }

                sub.totalInstances = currentCount;
            }
            countArr.Dispose();
        }

        static ComputeBuffer GetCountBuffer(int length) {
            //Debug.Log(length);
            var buff = new ComputeBuffer(length, sizeof(int));

            //some graphics apis dont clear the memory i guess so we need to make sure these are zerod out
            var tmpCountfill = new int[buff.count];
            buff.SetData(tmpCountfill);

            return buff;
        }

        static ComputeKernel normalKernel;

        public static void ClearKernels() {

        }

        static async Task RefreshMeshPosBuffer(SubGrassMesh subMesh, bool onlyCount = false) {

            var gMesh = subMesh.gMesh;
            var chunkMesh = gMesh.chunkedMesh;

            if (gMesh.vertexDataBuffer.NullOrInvalid()) {

                //we dont really have access to the newer graphicsbuffer stuff in unity 2019
                //but also it's really annoying to figure out so idk.
                int vertCount = chunkMesh.vertexCount;
                var verts = new List<Vector3>(vertCount);
                var uvs = new List<Vector2>(vertCount);
                var norms = new List<Vector3>(vertCount);
                chunkMesh.GetVertices(verts);
                chunkMesh.GetUVs(0, uvs);
                chunkMesh.GetNormals(norms);


                var vertData = new VertexData[vertCount];

                Action asyncAction = new Action(() => {
                    for (int i = 0; i < vertCount; i++) {
                        vertData[i] = new VertexData() {
                            pos = verts[i],
                            norm = norms[i],
                            uv = uvs[i],
                        };
                    }
                });
                if (GrassFlowRenderer.processAsync) await Task.Run(asyncAction); else asyncAction();

                gMesh.vertexDataBuffer = new ComputeBuffer(vertCount, VertexData.stride);
                gMesh.vertexDataBuffer.SetData(vertData);
            }

            var compute = GrassFlowRenderer.gfComputeShader;
            if (!gMesh.meshPosKernel) gMesh.meshPosKernel = new ComputeKernel(compute, "FillMeshPosBuffer");

            var meshPosKernel = gMesh.meshPosKernel;
            meshPosKernel.SetBuffer(vertsID, gMesh.vertexDataBuffer);
            compute.SetMatrix(terrainMatrixID, gMesh.terrainTransform.localToWorldMatrix);
            compute.SetMatrix(worldToTerrainMatrixID, gMesh.terrainTransform.worldToLocalMatrix);

            SetKernelParamMaps(meshPosKernel, gMesh);

            if (subMesh.totalInstances < 0) {
                subMesh.totalInstances = 0;
                foreach (var chunk in subMesh.chunks) {
                    subMesh.totalInstances += ((int)chunkMesh.GetIndexCount(chunk.chunkIdx) / 3) * gMesh.instanceCount;
                }
            }

            //Debug.Log("total instance count: " + totalInstanceCount);

            ComputeBuffer posBuffer;
            if (onlyCount) {
                if (gMesh.dummyPosBuffer.NullOrInvalid()) gMesh.dummyPosBuffer = new ComputeBuffer(1, 4, ComputeBufferType.Append);
                posBuffer = gMesh.dummyPosBuffer;
            }
            else {
                if (subMesh.posBuffer == null || subMesh.totalInstances != subMesh.posBuffer.count) {
                    posBuffer = GetPosBuffer(subMesh, subMesh.totalInstances);
                }
                else {
                    posBuffer = subMesh.posBuffer;
                }
            }

            posBuffer.SetCounterValue(0);
            meshPosKernel.SetBuffer(posBufferID, posBuffer);

            if (gMesh.chunkCountBuff.NullOrInvalid()) gMesh.chunkCountBuff = GetCountBuffer(gMesh.chunks.Length);
            meshPosKernel.SetBuffer(countBuffID, gMesh.chunkCountBuff);

            foreach (var chunk in subMesh.chunks) {


                if (chunk.triBuff.NullOrInvalid()) {
                    int i = chunk.chunkIdx;
                    var tmpTris = new List<int>((int)chunkMesh.GetIndexCount(i));
                    chunkMesh.GetTriangles(tmpTris, i);
                    chunk.terrainTriCount = tmpTris.Count / 3;

                    chunk.triBuff = new ComputeBuffer(tmpTris.Count, sizeof(uint));
                    chunk.triBuff.SetData(tmpTris);
                }

                int dispatchCount = chunk.terrainTriCount * gMesh.instanceCount;


                meshPosKernel.SetBuffer(trisID, chunk.triBuff);
                compute.SetInt(triCountID, chunk.terrainTriCount);
                compute.SetInt(dispatchCountID, dispatchCount);
                compute.SetInt(chunkID, chunk.chunkIdx);
                compute.SetInt(subID, chunk.subIdx);
                meshPosKernel.DispatchByCount(dispatchCount);
            }
        }


        static void RefreshTerrainPosBuffer(SubGrassMesh subMesh, bool onlyCount = false) {

            var gMesh = subMesh.gMesh;

            var compute = GrassFlowRenderer.gfComputeShader;
            if (!gMesh.terrPosKernel) gMesh.terrPosKernel = new ComputeKernel(compute, "FillTerrainPosBuffer");
            if (!normalKernel) normalKernel = new ComputeKernel(compute, "NormalsMain");

            var terrPosKernel = gMesh.terrPosKernel;

            RenderTexture terrainNormalMap = null;

            try {
                terrainNormalMap = TextureCreator.GetTerrainNormalMap(gMesh.terrainObject, compute, gMesh.terrainHeightmap, normalKernel);

                terrPosKernel.SetTexture(terrainHeightMapID, gMesh.terrainHeightmap);
                terrPosKernel.SetTexture(terrainNormalMapID, terrainNormalMap);
                terrPosKernel.SetTexture(terrainHoleMapID, gMesh.terrainHoleMap);

                Vector3 terrainScale = gMesh.terrainObject.terrainData.size;
                compute.SetVector(terrainSizeID, new Vector4(terrainScale.x, terrainScale.y, terrainScale.z));
                compute.SetVector(invTerrainSizeID, new Vector4(1f / terrainScale.x, 1f / terrainScale.y, 1f / terrainScale.z));
                compute.SetVector(terrainChunkSizeID, new Vector4(terrainScale.x / gMesh.chunksX, terrainScale.z / gMesh.chunksZ));
                compute.SetFloat(terrainExpansionID, gMesh.owner.terrainExpansion);
                compute.SetFloat(terrainMapOffsetID, 1f / gMesh.terrainHeightmap.width * 0.5f);
                compute.SetMatrix(terrainMatrixID, gMesh.terrainTransform.localToWorldMatrix);
                compute.SetMatrix(worldToTerrainMatrixID, gMesh.terrainTransform.worldToLocalMatrix);

                SetKernelParamMaps(terrPosKernel, gMesh);

                if (subMesh.totalInstances < 0) {
                    subMesh.totalInstances = 0;
                    foreach (var chunk in subMesh.chunks) {
                        subMesh.totalInstances += chunk.terrainTriCount * gMesh.instanceCount;
                    }
                }
                //Debug.Log("total instance count: " + totalInstanceCount);


                ComputeBuffer posBuffer;
                if (onlyCount) {
                    if (gMesh.dummyPosBuffer.NullOrInvalid()) gMesh.dummyPosBuffer = new ComputeBuffer(1, 4, ComputeBufferType.Append);
                    posBuffer = gMesh.dummyPosBuffer;
                }
                else {
                    if (subMesh.posBuffer == null || subMesh.totalInstances != subMesh.posBuffer.count) {
                        posBuffer = GetPosBuffer(subMesh, subMesh.totalInstances);
                    }
                    else {
                        posBuffer = subMesh.posBuffer;
                    }
                }

                posBuffer.SetCounterValue(0);
                terrPosKernel.SetBuffer(posBufferID, posBuffer);

                if (gMesh.chunkCountBuff.NullOrInvalid()) gMesh.chunkCountBuff = GetCountBuffer(gMesh.chunks.Length);
                terrPosKernel.SetBuffer(countBuffID, gMesh.chunkCountBuff);

                foreach (var chunk in subMesh.chunks) {

                    int dispatchCount = chunk.terrainTriCount * gMesh.instanceCount;

                    compute.SetVector(_chunkPosID, chunk.chunkPos);
                    compute.SetInt(chunkID, chunk.chunkIdx);
                    compute.SetInt(subID, chunk.subIdx);
                    compute.SetInt(dispatchCountID, dispatchCount);

                    terrPosKernel.DispatchByCount(dispatchCount);
                }
            } catch (Exception ex) {
                Debug.LogException(ex);
            } finally {
                RenderTexture.ReleaseTemporary(terrainNormalMap);
            }            
        }


        public static async Task RefreshPosBuffer(SubGrassMesh subMesh, bool onlyCount = false) {

            var gMesh = subMesh.gMesh;
            bool needsTrim = subMesh.totalInstances < 0;
            GrassFlowRenderer.gfComputeShader.SetBool(bakeDensityID, gMesh.bakeDensity && !GrassFlowRenderer.isPaintingOpen);
            GrassFlowRenderer.gfComputeShader.SetFloat(terrainSlopeThreshID, gMesh.terrainSlopeThresh);
            GrassFlowRenderer.gfComputeShader.SetFloat(terrainSlopeFadeID, 1f / (gMesh.terrainSlopeFade + 0.0001f));

            if (!gMesh.bakeData) {
                GrassFlowRenderer.gfComputeShader.EnableKeyword("NOBAKE");
            }
            else {
                GrassFlowRenderer.gfComputeShader.DisableKeyword("NOBAKE");
            }

            if (gMesh.renderType == GrassFlowRenderer.GrassRenderType.Mesh) {
                await RefreshMeshPosBuffer(subMesh, onlyCount);
            }
            else {
                RefreshTerrainPosBuffer(subMesh, onlyCount);
            }
        }

        public static async Task RefreshPosBufferInitialize(GrassMesh gMesh) {

            CullingZone.ClearCulledChunks();

            gMesh.chunkCountBuff = gMesh.chunkCountBuff.ReleaseIfValid();

            foreach (var sub in gMesh.subGrassMeshes) {

                sub.totalInstances = -1; //force counts to be regenerated

                //if the submesh is not intended to be loaded, we only want to count it instead of creating the full buffer
                await RefreshPosBuffer(sub, !sub.loaded);
            }

            await HandleCounter(gMesh);

            if (gMesh.bakeDensity && !GrassFlowRenderer.isPaintingOpen) {
                foreach (var sub in gMesh.subGrassMeshes) {
                    if (sub.posBuffer != null) {
                        TrimPosBuffer(sub);
                    }
                }
            }

            gMesh.bufferMem = 0;
            if (gMesh.chunkCountBuff != null) gMesh.bufferMem += gMesh.chunkCountBuff.stride * gMesh.chunkCountBuff.count;
            if (gMesh.vertexDataBuffer != null) gMesh.bufferMem += gMesh.vertexDataBuffer.stride * gMesh.vertexDataBuffer.count;
            if (gMesh.renderType == GrassFlowRenderer.GrassRenderType.Mesh) {
                foreach (var chunk in gMesh.chunks) {
                    if (chunk.triBuff != null) gMesh.bufferMem += chunk.triBuff.stride * chunk.triBuff.count;
                }
            }
        }

        public static async Task HandleCustomMesh(GrassMesh gMesh, bool refreshPosBuffer = true) {

            if (!gMesh.customGrassMesh) return;

            //manually disabling the grassPerTri feature :)
            gMesh.grassPerTri = 1;

            foreach (var lod in gMesh.customMeshLods) {
                lod.invVertCount = 1f / (lod.lodMesh.vertexCount);
                lod.drawnMesh = gMesh.grassPerTri > 1 ? await DuplicateMesh(lod.lodMesh, gMesh.grassPerTri) : lod.lodMesh;
                lod.hasGeneratedMesh = gMesh.grassPerTri > 1;
                lod.hasDrawnMesh = true;
            }

            if (refreshPosBuffer) {
                await RefreshPosBufferInitialize(gMesh);
            }

            gMesh.UpdateMaxVertHeight();
        }


        static List<MeshChunk[,,]> Split3DArrayIntoChunks(MeshChunk[,,] inputArray, int chunkWidth, int chunkHeight, int chunkDepth) {
            List<MeshChunk[,,]> outputChunks = new List<MeshChunk[,,]>();

            // Calculate the number of chunks in each dimension
            int numChunksX = (int)Math.Ceiling((double)inputArray.GetLength(0) / chunkWidth);
            int numChunksY = (int)Math.Ceiling((double)inputArray.GetLength(1) / chunkHeight);
            int numChunksZ = (int)Math.Ceiling((double)inputArray.GetLength(2) / chunkDepth);

            // Loop through each chunk and create a new 3D array
            for (int i = 0; i < numChunksX; i++) {
                for (int j = 0; j < numChunksY; j++) {
                    for (int k = 0; k < numChunksZ; k++) {
                        // Calculate the dimensions of the chunk
                        int chunkX = Math.Min(chunkWidth, inputArray.GetLength(0) - i * chunkWidth);
                        int chunkY = Math.Min(chunkHeight, inputArray.GetLength(1) - j * chunkHeight);
                        int chunkZ = Math.Min(chunkDepth, inputArray.GetLength(2) - k * chunkDepth);

                        // Create the new chunk array and copy the values from the input array
                        MeshChunk[,,] chunkArray = new MeshChunk[chunkX, chunkY, chunkZ];
                        for (int x = 0; x < chunkX; x++) {
                            for (int y = 0; y < chunkY; y++) {
                                for (int z = 0; z < chunkZ; z++) {
                                    chunkArray[x, y, z] = inputArray[i * chunkWidth + x, j * chunkHeight + y, k * chunkDepth + z];
                                }
                            }
                        }

                        // Add the chunk array to the output list
                        outputChunks.Add(chunkArray);
                    }
                }
            }

            return outputChunks;
        }

        static int GetBetterSubMeshGrouping(int dimSize) {

            int start = Mathf.Min(subMeshGroup, dimSize);
            if (start < 5) {
                return start;
            }

            for (int i = start; i >= 5; i--) {
                if (dimSize % i == 0) return i;
            }
            for (int i = start; i <= subMeshGroup * 2; i++) {
                if (dimSize % i == 0) return i;
            }

            return start;
        }

        const int subMeshGroup = 8;
        public static async Task SplitToSubMeshes(GrassMesh gMesh, Vector3 camPos) {
            Action asyncAction = new Action(() => {


                int sWidth = GetBetterSubMeshGrouping(gMesh.chunksX);
                int sHeight = GetBetterSubMeshGrouping(gMesh.chunksY);
                int sDepth = GetBetterSubMeshGrouping(gMesh.chunksZ);

                var splitChunks = Split3DArrayIntoChunks(gMesh.chunks3D, sWidth, sHeight, sDepth);

                var subMeshes = new List<SubGrassMesh>(splitChunks.Count);

                for (int i = 0; i < splitChunks.Count; i++) {

                    var chunks = splitChunks[i];
                    var sub = new SubGrassMesh() {
                        gMesh = gMesh,
                    };


                    List<MeshChunk> realChunks = new List<MeshChunk>(chunks.Length);

                    foreach (var chunk in chunks) {

                        if (chunk == null) continue;

                        chunk.subIdx = realChunks.Count;
                        realChunks.Add(chunk);

                        if (sub.bounds.extents.x != 0) {
                            sub.bounds.Encapsulate(chunk.worldBounds);
                        }
                        else {
                            sub.bounds = chunk.worldBounds;
                        }
                    }

                    if (realChunks.Count > 0) {

                        sub.chunks = realChunks.ToArray();

                        sub.loaded = sub.bounds.SqrDistance(camPos) <= gMesh.maxRenderDistSqr;

                        subMeshes.Add(sub);
                    }
                }

                gMesh.subGrassMeshes = subMeshes.ToArray();

            });
            if (GrassFlowRenderer.processAsync) await Task.Run(asyncAction); else asyncAction();

            foreach (var sub in gMesh.subGrassMeshes) {
                sub.pBlock = new MaterialPropertyBlock();
            }
        }




        //thanks chatGPT i guess
        public static async Task<Mesh> DuplicateMesh(Mesh originalMesh, int numberOfCopies) {

            Mesh newMesh = new Mesh();

            Vector3[] originalVertices = originalMesh.vertices;
            Vector3[] originalNormals = originalMesh.normals;
            Vector2[] originalUVs = originalMesh.uv;
            int[] originalTriangles = originalMesh.triangles;

            int numVerts = originalVertices.Length;
            int numTris = originalTriangles.Length;
            int totalTris = numTris * numberOfCopies;

            if (totalTris / 3 >= 65535) {
                newMesh.indexFormat = IndexFormat.UInt32;
            }

            Vector3[] vertices = new Vector3[numVerts * numberOfCopies];
            Vector3[] normals = new Vector3[numVerts * numberOfCopies];
            Vector2[] uvs = new Vector2[numVerts * numberOfCopies];
            int[] triangles = new int[numTris * numberOfCopies];

            var asyncAction = new Action(() => {
                for (int i = 0; i < numberOfCopies; i++) {
                    int offset = i * numVerts;

                    // Copy the vertices, normals, and UVs
                    for (int j = 0; j < numVerts; j++) {
                        vertices[offset + j] = originalVertices[j];
                        normals[offset + j] = originalNormals[j];
                        uvs[offset + j] = originalUVs[j];
                    }

                    // Copy the triangles and adjust the indices
                    int triOff = i * numTris;
                    for (int j = 0; j < numTris; j += 3) {
                        triangles[triOff + j + 0] = originalTriangles[j + 0] + offset;
                        triangles[triOff + j + 1] = originalTriangles[j + 1] + offset;
                        triangles[triOff + j + 2] = originalTriangles[j + 2] + offset;
                    }
                }
            });
            if (GrassFlowRenderer.processAsync) await Task.Run(asyncAction); else asyncAction();

            newMesh.vertices = vertices;
            newMesh.normals = normals;
            newMesh.uv = uvs;
            newMesh.triangles = triangles;

            newMesh.OptimizeIndexBuffers();
            newMesh.UploadMeshData(false);

            return newMesh;
        }




        public static async Task<MeshChunk[]> ChunkMesh(GrassMesh gMesh, bool normalize) {

            MeshChunk[] finalChunks = null;

            try {

                Mesh chunkedMesh = new Mesh();
                gMesh.chunkedMesh = chunkedMesh;

                Mesh meshToChunk = gMesh.grassMesh;
                Bounds meshBounds = meshToChunk.bounds;

                int vertCount = meshToChunk.vertexCount;
                int triCount = (int)meshToChunk.GetIndexCount(0);

                int xChunks = gMesh.chunksX;
                int yChunks = gMesh.chunksY;
                int zChunks = gMesh.chunksZ;

                List<int> tris = null;
                List<Vector3> verts = null;
                List<Vector3> norms = null;
                List<Vector2> uvs = null;

                List<MeshChunk> resultChunks = null;

                Action asyncAction = new Action(() => {

                    tris = new List<int>(triCount);
                    verts = new List<Vector3>(vertCount);
                    norms = new List<Vector3>(vertCount);
                    uvs = new List<Vector2>(vertCount);
                });
                if (GrassFlowRenderer.processAsync) await Task.Run(asyncAction); else asyncAction();



                meshToChunk.GetTriangles(tris, 0);
                meshToChunk.GetVertices(verts);
                meshToChunk.GetNormals(norms);
                meshToChunk.GetUVs(0, uvs);



                MeshChunkData[,,] meshChunks = null;
                int meshCount = 0;
                int totalTris = 0;

                //Color32[] pixels = null;
                //int pMapW = 0, pMapH = 0;
                //if (Application.isPlaying && gMesh.owner.discardEmptyTriangles && gMesh.paramMap) {
                //    //need to do this stuff early because it cant be done on another thread
                //    pixels = gMesh.paramMap.GetPixels32();
                //    pMapW = gMesh.paramMap.width;
                //    pMapH = gMesh.paramMap.height;
                //}

                asyncAction = new Action(() => {
                    if (normalize) {
                        NormalizeMeshDensity(gMesh, tris, verts, norms, uvs);
                    }

                    //if (gMesh.owner.discardEmptyTriangles) {
                    //    BakeDensityToMesh(gMesh, pMapW, pMapH, pixels, tris, verts, norms, uvs);
                    //}

                    meshChunks = new MeshChunkData[xChunks, yChunks, zChunks];
                    resultChunks = new List<MeshChunk>(meshChunks.Length);

                    int[] thisTris = new int[3];
                    for (int i = 0; i < tris.Count; i += 3) {

                        int t1 = tris[i]; int t2 = tris[i + 1]; int t3 = tris[i + 2];
                        Vector3 checkVert = verts[t3];

                        int xIdx = (int)(map(checkVert.x, meshBounds.min.x, meshBounds.max.x, 0f, 0.99999f) * xChunks);
                        int yIdx = (int)(map(checkVert.y, meshBounds.min.y, meshBounds.max.y, 0f, 0.99999f) * yChunks);
                        int zIdx = (int)(map(checkVert.z, meshBounds.min.z, meshBounds.max.z, 0f, 0.99999f) * zChunks);

                        MeshChunkData cData = meshChunks[xIdx, yIdx, zIdx];
                        if (cData == null) meshChunks[xIdx, yIdx, zIdx] = (cData = new MeshChunkData());

                        thisTris[0] = t1;
                        thisTris[1] = t2;
                        thisTris[2] = t3;

                        cData.tris.AddRange(thisTris);
                    }

                    foreach (var chunk in meshChunks) {
                        if (chunk?.tris.Count > 0) {
                            meshCount++;
                            chunk.CalculateBounds(verts);
                            totalTris += chunk.tris.Count;
                        }
                    }
                });
                if (GrassFlowRenderer.processAsync) await Task.Run(asyncAction); else asyncAction();

#if UNITY_2017_3_OR_NEWER
                if (totalTris / 3 >= 65535) {
                    chunkedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                }
#endif

                chunkedMesh.SetVertices(verts);
                chunkedMesh.SetNormals(norms);
                chunkedMesh.SetUVs(0, uvs);

                chunkedMesh.subMeshCount = meshCount;

                gMesh.chunks3D = new MeshChunk[xChunks, yChunks, zChunks];
                for (int cx = 0; cx < xChunks; cx++) {
                    for (int cy = 0; cy < yChunks; cy++) {
                        for (int cz = 0; cz < zChunks; cz++) {

                            MeshChunkData cData = meshChunks[cx, cy, cz];

                            if (cData?.tris.Count > 0) {

                                int subIdx = resultChunks.Count;
                                chunkedMesh.SetTriangles(cData.tris, subIdx, false);

                                var chunk = new MeshChunk(gMesh) {
                                    meshBounds = cData.bounds,
                                    chunkIdx = subIdx,
                                };
                                resultChunks.Add(chunk);

                                gMesh.chunks3D[cx, cy, cz] = chunk;
                            }
                        }
                    }
                }


                chunkedMesh.Optimize();
                chunkedMesh.UploadMeshData(false);

                float bH = gMesh.mainGrassMat.GetFloat("bladeHeight");
                float bW = gMesh.mainGrassMat.GetFloat("bladeWidth");

                asyncAction = new Action(() => {
                    finalChunks = resultChunks.ToArray();

                    ExpandChunks(finalChunks, gMesh, bH, bW);
                });
                if (GrassFlowRenderer.processAsync) await Task.Run(asyncAction); else asyncAction();

            } catch (Exception ex) {
                Debug.LogException(ex);
                return null;
            }

            return finalChunks;
        }



        public static async Task<Mesh> CreatePlaneMesh(GrassMesh gMesh, Vector3 chunkSize) {


            Vector3[] verts = null;
            int[] tris = null;

            Action asyncAction = new Action(() => {
                int subDiv = GrassMesh.terrainGrassDensity;
                float spacing = 1f / subDiv * 100f;

                Vector2Int tileCount = new Vector2Int(Mathf.CeilToInt(chunkSize.x / spacing), Mathf.CeilToInt(chunkSize.z / spacing));
                Vector2 normSpacing = new Vector2(1f / tileCount.x, 1f / tileCount.y);
                int vertWidth = tileCount.x + 1;
                Func<int, int, int> GetVIDX = (int x, int y) => y * vertWidth + x;

                int vertCount = (tileCount.x + 1) * (tileCount.y + 1);
                int triCount = tileCount.x * tileCount.y * 2;
                verts = new Vector3[vertCount];
                tris = new int[triCount * 3];

                int i = 0;
                for (int y = 0; y <= tileCount.y; y++) {
                    for (int x = 0; x <= tileCount.x; x++) {
                        verts[GetVIDX(x, y)] = new Vector3(x * normSpacing.x, 0, y * normSpacing.y);



                        if (x > 0 && y > 0) {
                            tris[i++] = GetVIDX(x - 1, y - 1);
                            tris[i++] = GetVIDX(x - 1, y - 0);
                            tris[i++] = GetVIDX(x - 0, y - 1);

                            tris[i++] = GetVIDX(x - 1, y - 0);
                            tris[i++] = GetVIDX(x - 0, y - 0);
                            tris[i++] = GetVIDX(x - 0, y - 1);
                        }
                    }
                }
            });
            if (GrassFlowRenderer.processAsync) await Task.Run(asyncAction); else asyncAction();


            Mesh chunkPlane = new Mesh();
            chunkPlane.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            chunkPlane.vertices = verts;

            chunkPlane.triangles = tris;

            //chunkPlane.subMeshCount = chunkCount;
            //for (int i = 0; i < chunkCount; i++) {
            //    chunkPlane.SetTriangles(tris, i);
            //}

            //chunkPlane.Optimize();
            //chunkPlane.UploadMeshData(false);

            return chunkPlane;
        }

        public static async Task<MeshChunk[]> ChunkTerrain(GrassMesh gMesh) {

            MeshChunk[] chunks = null;

            TerrainData terrain = gMesh.terrainObject.terrainData;

            Vector3 terrainScale = terrain.size;

            int xChunks = gMesh.chunksX;
            int yChunks = gMesh.chunksY;
            int zChunks = gMesh.chunksZ;
            int chunkCount = xChunks * zChunks;

            Vector3 chunkSize = new Vector3(terrainScale.x / xChunks, terrainScale.y * 0.5f, terrainScale.z / zChunks);
            Vector3 halfChunkSize = chunkSize * 0.5f;

            //calc amount of instances to generate based on physical chunk size
            int terrainTriCount = Mathf.CeilToInt(chunkSize.x * chunkSize.z * 0.025f);

            //gMesh.chunkedMesh = await CreatePlaneMesh(gMesh, chunkSize, chunkCount);

            //Color32[] pixels = null;
            //int pMapW = 0, pMapH = 0;
            //Vector3[] verts = null;
            //int[] tris = null;
            //int mapWidth = gMesh.terrainHeightmap.width;
            //if (Application.isPlaying && gMesh.owner.discardEmptyTriangles && gMesh.paramMap) {
            //    //need to do this stuff early because it cant be done on another thread
            //    pixels = gMesh.paramMap.GetPixels32();
            //    pMapW = gMesh.paramMap.width;
            //    pMapH = gMesh.paramMap.height;
            //    verts = gMesh.chunkedMesh.vertices;
            //    tris = gMesh.chunkedMesh.GetTriangles(0);
            //}


            chunks = new MeshChunk[chunkCount];
            for (int i = 0; i < chunkCount; i++) {
                chunks[i] = new MeshChunk(gMesh);
            }

            int w = terrain.heightmapResolution - 1;
            int h = terrain.heightmapResolution - 1;
            float cWf = w / (float)xChunks;
            float cHf = h / (float)zChunks;
            int cW = (int)cWf;
            int cH = (int)cHf;

            float[,] tHeights = terrain.GetHeights(0, 0, terrain.heightmapResolution, terrain.heightmapResolution);

            float bH = gMesh.mainGrassMat.GetFloat("bladeHeight");
            float bW = gMesh.mainGrassMat.GetFloat("bladeWidth");

            Action asyncAction = new Action(() => {

                gMesh.chunks3D = new MeshChunk[xChunks, 1, zChunks];

                Parallel.For(0, zChunks, z => {
                    //for (int z = 0; z < zChunks; z++) {
                    int index = z * xChunks;
                    for (int x = 0; x < xChunks; x++) {

                        float maxHeight = 0;
                        float minHeight = 1;

                        int cXS = (int)(cWf * x);
                        int cZS = (int)(cHf * z);

                        for (int cX = cXS; cX < cXS + cW; cX++) {
                            for (int cZ = cZS; cZ < cZS + cH; cZ++) {

                                //still not entirely sure why this needs to be sampled backwards
                                //prob just the heightmap is in a different orientation..
                                float tH = tHeights[cZ, cX];

                                if (tH > maxHeight)
                                    maxHeight = tH;
                                if (tH < minHeight)
                                    minHeight = tH;
                            }
                        }

                        Vector3 chunkPos = Vector3.Scale(chunkSize, new Vector3(x, 0, z));
                        Vector3 mapChunkPos = new Vector4(chunkPos.x, chunkPos.z);

                        chunkPos += halfChunkSize;
                        chunkPos.y = chunkSize.y * (maxHeight + minHeight);

                        halfChunkSize.y = chunkSize.y * (maxHeight - minHeight);

                        var chunk = chunks[index];
                        chunk.meshBounds = new Bounds() {
                            center = chunkPos,
                            extents = halfChunkSize
                        };
                        chunk.worldBounds = new Bounds() {
                            extents = halfChunkSize
                        };
                        chunk.chunkPos = mapChunkPos;
                        chunk.chunkIdx = index;
                        chunk.terrainTriCount = terrainTriCount;

                        gMesh.chunks3D[x, 0, z] = chunk;

                        index++;
                    }
                    //}
                });

                //if (gMesh.owner.discardEmptyTriangles) {
                //    Vector2 invChunkCount = new Vector2(1f / gMesh.chunksX, 1f / gMesh.chunksZ);
                //    BakeDensityToTerrainMesh(chunks, pMapW, pMapH, pixels, chunkSize, invChunkCount,
                //        terrainScale, mapWidth, verts, tris);
                //}

                ExpandChunks(chunks, gMesh, bH, bW);
            });
            if (GrassFlowRenderer.processAsync) await Task.Run(asyncAction); else asyncAction();


            foreach (var chunk in chunks) {

                //this only matters for deprecated stuff that isnt used anymore
                if (chunk.tmpTris != null) {
                    //we still need to create individual submeshes per chunk because we need to be able to discard triangles
                    gMesh.chunkedMesh.SetTriangles(chunk.tmpTris, chunk.chunkIdx);
                    chunk.tmpTris = null;
                }

                chunk.pBlock.SetVector(_chunkPosID, chunk.chunkPos);
            }

            return chunks;
        }

        static void ExpandChunks(MeshChunk[] chunks, GrassMesh gMesh, float bH, float bW) {


            bW *= 0.5f;

            Vector3 bladeBoundsExpand;
            if (gMesh.expandBounds) {
                bladeBoundsExpand = new Vector3(bH, bH, bH);
            }
            else {
                bladeBoundsExpand = new Vector3(bW, bH, bW);
            }

            Vector3 scaler = Vector3.one;
            if (gMesh.renderType == GrassFlowRenderer.GrassRenderType.Terrain) {
                scaler.x = 1 + gMesh.owner.terrainExpansion;
                scaler.z = 1 + gMesh.owner.terrainExpansion;
            }

            foreach (MeshChunk chunk in chunks) {
                Vector3 extents = chunk.meshBounds.extents;
                extents.Scale(scaler);
                extents += bladeBoundsExpand;
                chunk.meshBounds.extents = extents;
                chunk.worldBounds.extents = extents;
            }
        }

        static int UVtoIdx(int w, int h, Vector2 uv) {
            return Mathf.Clamp(Mathf.RoundToInt(uv.x * w) + Mathf.RoundToInt(uv.y * h) * w, 0, w * h - 1);
        }

        const float densityThresh = 0.02f;
        const float byte255to01 = 0.0039215686274509803921568627451f;



        static void BakeDensityToTerrainMesh(MeshChunk[] chunks, int width, int height, Color32[] pixels,
            Vector3 chunkSize, Vector2 invChunkCount, Vector3 terrainScale, float terrainMapW, Vector3[] verts, int[] tris) {


            if (pixels == null) return;

            float terrainMapOffset = 1f / terrainMapW * 0.5f;

            Parallel.For(0, chunks.Length, (c) => {
                var chunk = chunks[c];

                int numPixChecked = 0;
                float densityAcc = 0f;
                Action<int> CheckPixel = (pIdx) => {
                    numPixChecked++;
                    densityAcc += pixels[pIdx].r;
                };

                Action<Vector2, Vector2> CheckSide = (uv1, uv2) => {
                    for (float t = 0; t <= 1; t += 0.1f) {
                        CheckPixel(UVtoIdx(width, height, Vector3.Lerp(uv1, uv2, t)));
                    }
                };

                List<int> filledTris = new List<int>(tris.Length);

                Vector2 chunkUV = chunk.chunkPos;
                chunkUV.x /= terrainScale.x;
                chunkUV.y /= terrainScale.z;
                chunkUV = chunkUV * (1f - terrainMapOffset * 2f) + new Vector2(terrainMapOffset, terrainMapOffset);


                Vector2 GetUV(int idx) {
                    Vector3 chunkVert = verts[idx];
                    return chunkUV + Vector2.Scale(new Vector2(chunkVert.x, chunkVert.z), invChunkCount);
                }

                for (int i = 0; i < tris.Length; i += 3) {
                    int[] thisTri = new int[] { tris[i], tris[i + 1], tris[i + 2] };
                    Vector2 uv1 = GetUV(thisTri[0]); Vector2 uv2 = GetUV(thisTri[1]); Vector2 uv3 = GetUV(thisTri[2]);

                    densityAcc = 0f;
                    numPixChecked = 0;

                    CheckSide(uv1, uv2);
                    CheckSide(uv3, uv2);
                    CheckSide(uv1, uv3);

                    uv1 = Vector2.LerpUnclamped(uv1, uv2, 0.5f);
                    Vector3 mid = Vector2.Lerp(uv1, uv3, 0.5f);
                    CheckPixel(UVtoIdx(width, height, mid));

                    uv2 = Vector2.LerpUnclamped(uv2, uv3, 0.5f);
                    uv3 = Vector2.LerpUnclamped(uv1, uv3, 0.5f);

                    CheckSide(uv1, uv2);
                    CheckSide(uv3, uv2);
                    CheckSide(uv1, uv3);

                    densityAcc = densityAcc / numPixChecked * byte255to01;
                    if (densityAcc > densityThresh) {
                        filledTris.AddRange(thisTri);
                    }
                }

                chunk.tmpTris = filledTris;
            });
        }

        public static void BakeDensityToMesh(GrassMesh gF, int width, int height, Color32[] pixels,
            List<int> baseTris, List<Vector3> baseVerts, List<Vector3> baseNorms, List<Vector2> baseUvs) {

            if (pixels == null) return;

            if (baseUvs.Count == 0) {
                Debug.LogError("GrassFlow:DiscardEmptyTriangles: Base mesh does not have uvs!");
                return;
            }

            List<int> filledTris = new List<int>();

            int numPixChecked = 0;
            float densityAcc = 0f;
            Action<int> CheckPixel = (pIdx) => {
                numPixChecked++;
                densityAcc += pixels[pIdx].r;
            };

            Action<Vector2, Vector2> CheckSide = (uv1, uv2) => {
                for (float t = 0; t <= 1; t += 0.1f) {
                    CheckPixel(UVtoIdx(width, height, Vector3.Lerp(uv1, uv2, t)));
                }
            };

            for (int i = 0; i < baseTris.Count; i += 3) {
                int[] thisTri = new int[] { baseTris[i], baseTris[i + 1], baseTris[i + 2] };
                Vector2 uv1 = baseUvs[thisTri[0]]; Vector2 uv2 = baseUvs[thisTri[1]]; Vector2 uv3 = baseUvs[thisTri[2]];

                densityAcc = 0f;
                numPixChecked = 0;

                CheckSide(uv1, uv2);
                CheckSide(uv3, uv2);
                CheckSide(uv1, uv3);

                uv1 = Vector2.LerpUnclamped(uv1, uv2, 0.5f);
                Vector3 mid = Vector2.Lerp(uv1, uv3, 0.5f);
                CheckPixel(UVtoIdx(width, height, mid));

                uv2 = Vector2.LerpUnclamped(uv2, uv3, 0.5f);
                uv3 = Vector2.LerpUnclamped(uv1, uv3, 0.5f);

                CheckSide(uv1, uv2);
                CheckSide(uv3, uv2);
                CheckSide(uv1, uv3);

                densityAcc = densityAcc / numPixChecked * byte255to01;
                if (densityAcc > densityThresh) {
                    filledTris.AddRange(thisTri);
                }
            }

            var distinctTriIndexes = filledTris.Distinct().ToArray();

            Vector3[] verts = new Vector3[distinctTriIndexes.Length];
            Vector3[] norms = new Vector3[distinctTriIndexes.Length];
            Vector2[] uvs = new Vector2[distinctTriIndexes.Length];

            Dictionary<int, int> triMap = new Dictionary<int, int>();
            for (int i = 0; i < distinctTriIndexes.Length; i++) {
                int distinctTriIdx = distinctTriIndexes[i];
                triMap.Add(distinctTriIdx, i);

                verts[i] = baseVerts[distinctTriIdx];
                norms[i] = baseNorms[distinctTriIdx];
                uvs[i] = baseUvs[distinctTriIdx];
            }

            int[] remappedTris = new int[filledTris.Count];
            for (int i = 0; i < remappedTris.Length; i++) {
                remappedTris[i] = triMap[filledTris[i]];
            }

            baseTris.Clear();
            baseVerts.Clear();
            baseNorms.Clear();
            baseUvs.Clear();

            baseTris.AddRange(remappedTris);
            baseVerts.AddRange(verts);
            baseNorms.AddRange(norms);
            baseUvs.AddRange(uvs);
        }


    }//class
}//namespace