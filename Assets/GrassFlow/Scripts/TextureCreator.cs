using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace GrassFlow {
    public class TextureCreator : MonoBehaviour {

        public static void CreateColorMap(Texture2D inTex, int width, int height, float noiseScale,
            float normalization, bool useNoise, Color color) {
            if (inTex.width != width || inTex.height != height) {
                inTex.Reinitialize(width, height);
            }

            Color[] pixels = new Color[width * height];
            float normMult = 1f - normalization;

            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    float u = x / (float)width;
                    float v = y / (float)height;

                    float pNoise = 1;
                    if (useNoise) {
                        pNoise = Mathf.PerlinNoise(u * noiseScale, v * noiseScale) * normMult + normalization;
                    }

                    pixels[width * y + x] = color * pNoise;
                    pixels[width * y + x].a = 1;
                }
            }

            inTex.SetPixels(pixels);
            inTex.Apply();
        }

        public static void CreateParamMap(Texture2D inTex, int width = 128, int height = 128, float heightMult = 0.1f,
            float noiseScaleDensity = 10f, float noiseScaleHeight = 50f, float noiseScaleWind = 8f,
            float normalizationDensity = 0.85f, float normalizationHeight = 0.5f, float normalizationWind = 0.8f) {
            if (inTex.width != width || inTex.height != height) {
                inTex.Reinitialize(width, height);
            }

            Color[] pixels = new Color[width * height];
            float normMultD = 1f - normalizationDensity;
            float normMultH = 1f - normalizationHeight;
            float normMultW = 1f - normalizationWind;

            for (int y = 0; y < height; y++) {
                for (int x = 0; x < width; x++) {
                    float u = x / (float)width;
                    float v = y / (float)height;

                    float pNoiseD = Mathf.PerlinNoise(u * noiseScaleDensity + 10f, v * noiseScaleDensity + 10f) * normMultD + normalizationDensity;
                    float pNoiseH = Mathf.PerlinNoise(u * noiseScaleHeight + 20f, v * noiseScaleHeight + 20f) * normMultH + normalizationHeight;
                    float pnoiseW = Mathf.PerlinNoise(u * noiseScaleWind + 30f, v * noiseScaleHeight + 30f) * normMultW + normalizationWind;

                    pixels[width * y + x] = new Vector4(pNoiseD, pNoiseH * heightMult, 1f, pnoiseW);
                }
            }

            inTex.SetPixels(pixels);
            inTex.Apply();
        }


        static int resolutionID = Shader.PropertyToID("resolution");
        static int HeightMapInputID = Shader.PropertyToID("HeightMapInput");
        static int NormalResultID = Shader.PropertyToID("NormalResult");
        public static RenderTexture GetTerrainNormalMap(Terrain terrainObj, ComputeShader normalShader, RenderTexture heightmap, int normalKernel) {

            TerrainData terrain = terrainObj.terrainData;
            int w = terrain.heightmapResolution - 1;
            int h = terrain.heightmapResolution - 1;

            RenderTextureFormat rtFormat = RenderTextureFormat.ARGBHalf;
            RenderTextureDescriptor desc = new RenderTextureDescriptor(w, h, rtFormat, 0) {
                enableRandomWrite = true, 
            };

            RenderTexture terrainNormalMap = RenderTexture.GetTemporary(desc);
            terrainNormalMap.wrapMode = TextureWrapMode.Clamp;
            //RenderTexture terrainNormalMap = new RenderTexture(w, h, 0, rtFormat, RenderTextureReadWrite.Linear) {
            //    enableRandomWrite = true, wrapMode = TextureWrapMode.Clamp
            //};
            //terrainNormalMap.Create();

            normalShader.SetInt(resolutionID, w);
            //normalShader.SetBuffer(normalKernel, "inHeights", heightBuffer);
            normalShader.SetTexture(normalKernel, HeightMapInputID, heightmap);
            normalShader.SetTexture(normalKernel, NormalResultID, terrainNormalMap);
            normalShader.Dispatch(normalKernel, Mathf.CeilToInt(w / 8f), Mathf.CeilToInt(h / 8f), 1);

            
            
            return terrainNormalMap;
        }

        public static Color[] GetTerrainHeightMapData(Terrain terrainObj) {
            TerrainData terrain = terrainObj.terrainData;

            int w = terrain.heightmapResolution;
            int h = terrain.heightmapResolution;
            float[,] heightData = terrain.GetHeights(0, 0, w, h);

            Color[] rawClrs = new Color[w * h];
            int index = 0;
            for (int x = 0; x < w; x++) {
                for (int y = 0; y < h; y++) {
                    rawClrs[index++] = new Color(heightData[x, y], 0, 0);
                }
            }

            return rawClrs;
        }

        public static Texture2D GetTerrainHeightMap(Terrain terrainObj) {
            TerrainData terrain = terrainObj.terrainData;

            int w = terrain.heightmapResolution;
            int h = terrain.heightmapResolution;


            Color[] rawClrs = GetTerrainHeightMapData(terrainObj);


            Texture2D heightMap = new Texture2D(w, h, TextureFormat.RFloat, false, true) {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };

            heightMap.SetPixels(rawClrs);

            heightMap.Apply();

            return heightMap;
        }




        //weird array for noise stuff
        //if i put it as static in the shader itself it crashes on mobile
        //so this is lame...
        public static int[] noisePerm = { 169, 129, 19, 102, 202, 100, 213, 222, 42, 20, 213, 224, 232, 66, 222, 131, 101, 56, 44, 1, 60, 254, 110, 31, 29, 95, 28, 56,
            40, 96, 1, 216, 127, 177, 49, 59, 0, 33, 109, 87, 146, 220, 64, 54, 31, 147, 59, 24, 239, 45, 213, 49, 252, 8, 154, 71, 57, 249, 194, 54, 211, 11, 165,
            71, 216, 44, 192, 118, 118, 151, 146, 114, 92, 108, 190, 137, 100, 106, 73, 184, 170, 86, 101, 39, 69, 167, 32, 231, 98, 77, 174, 148, 122, 93, 211, 183,
            106, 49, 147, 55, 134, 229, 252, 120, 57, 74, 184, 197, 109, 150, 216, 65, 1, 66, 231, 109, 35, 161, 105, 151, 55, 40, 218, 179, 57, 12, 82, 172, 46, 28,
            166, 231, 134, 253, 77, 141, 203, 74, 120, 160, 22, 1, 113, 253, 251, 13, 221, 207, 214, 31, 19, 159, 207, 178, 112, 155, 252, 4, 213, 227, 111, 37, 225,
            88, 63, 206, 110, 230, 222, 104, 12, 36, 221, 62, 164, 149, 124, 209, 45, 48, 113, 55, 214, 2, 216, 21, 19, 79, 18, 90, 76, 145, 52, 27, 184, 30, 233, 49,
            140, 210, 72, 41, 25, 246, 119, 68, 86, 38, 152, 177, 220, 159, 187, 14, 64, 89, 36, 95, 167, 220, 6, 214, 86, 192, 14, 22, 253, 52, 17, 174, 76, 175, 215,
            57, 217, 28, 143, 16, 251, 173, 168, 149, 52, 75, 83, 29, 212, 71, 115, 59, 3, 146, 86, 244, 157, 37, 169, 129, 19, 102, 202, 100, 213, 222, 42, 20, 213, 224,
            232, 66, 222, 131, 101, 56, 44, 1, 60, 254, 110, 31, 29, 95, 28, 56, 40, 96, 1, 216, 127, 177, 49, 59, 0, 33, 109, 87, 146, 220, 64, 54, 31, 147, 59, 24, 239,
            45, 213, 49, 252, 8, 154, 71, 57, 249, 194, 54, 211, 11, 165, 71, 216, 44, 192, 118, 118, 151, 146, 114, 92, 108, 190, 137, 100, 106, 73, 184, 170, 86, 101, 39,
            69, 167, 32, 231, 98, 77, 174, 148, 122, 93, 211, 183, 106, 49, 147, 55, 134, 229, 252, 120, 57, 74, 184, 197, 109, 150, 216, 65, 1, 66, 231, 109, 35, 161, 105,
            151, 55, 40, 218, 179, 57, 12, 82, 172, 46, 28, 166, 231, 134, 253, 77, 141, 203, 74, 120, 160, 22, 1, 113, 253, 251, 13, 221, 207, 214, 31, 19, 159, 207, 178,
            112, 155, 252, 4, 213, 227, 111, 37, 225, 88, 63, 206, 110, 230, 222, 104, 12, 36, 221, 62, 164, 149, 124, 209, 45, 48, 113, 55, 214, 2, 216, 21, 19, 79, 18, 90,
            76, 145, 52, 27, 184, 30, 233, 49, 140, 210, 72, 41, 25, 246, 119, 68, 86, 38, 152, 177, 220, 159, 187, 14, 64, 89, 36, 95, 167, 220, 6, 214, 86, 192, 14, 22, 253,
            52, 17, 174, 76, 175, 215, 57, 217, 28, 143, 16, 251, 173, 168, 149, 52, 75, 83, 29, 212, 71, 115, 59, 3, 146, 86, 244, 157, 37 };


    } //class
} //namespace