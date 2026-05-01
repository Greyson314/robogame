using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;
using Marshal = System.Runtime.InteropServices.Marshal;
using UnityEngine.Rendering;

namespace GrassFlow {
    public class ComputeKernel {

        public int kernelId { get; private set; } = -1;
        public ComputeShader shader { get; private set; }
        public int numThreadsX { get; private set; } = 1;
        public int numThreadsY { get; private set; } = 1;
        public int numThreadsZ { get; private set; } = 1;
        public int resX { get; private set; } = 1;
        public int resY { get; private set; } = 1;
        public int resZ { get; private set; } = 1;

        public int tGroupsX { get; private set; } = 1;
        public int tGroupsY { get; private set; } = 1;
        public int tGroupsZ { get; private set; } = 1;



        void BaseConstructor(ComputeShader shader_, string kernelName) {
            shader = shader_;
            kernelId = shader.FindKernel(kernelName);

            uint udimX, udimY, udimZ;
            shader.GetKernelThreadGroupSizes(kernelId, out udimX, out udimY, out udimZ);

            numThreadsX = (int)udimX;
            numThreadsY = (int)udimY;
            numThreadsZ = (int)udimZ;
        }

        /// <summary>
        /// Auto sets num threads
        /// </summary>
        public ComputeKernel(ComputeShader shader_, string kernelName) {
            BaseConstructor(shader_, kernelName);
        }

        /// <summary>
        /// Auto sets thread group sizes.
        /// </summary>
        public ComputeKernel(ComputeShader shader_, string kernelName, int width, int height, int depth) {
            BaseConstructor(shader_, kernelName);
            SetResolution(width, height, depth);
        }

        /// <summary>
        /// Auto sets thread group sizes based on the dimensions of the texture
        /// </summary>
        public ComputeKernel(ComputeShader shader_, string kernelName, RenderTexture rt) {
            BaseConstructor(shader_, kernelName);
            SetResolution(rt.width, rt.height, rt.dimension == TextureDimension.Tex3D ? rt.volumeDepth : 1);
        }

        public ComputeBuffer CreateBufferWithData<T>(string name, List<T> data, int count, int stride) where T : struct {
            ComputeBuffer buff = new ComputeBuffer(count, stride);
            buff.SetData(data);
            SetBuffer(name, buff);

            return buff;
        }

        public ComputeBuffer CreateBufferWithData(string name, Array data, int count, int stride) {
            ComputeBuffer buff = new ComputeBuffer(count, stride);
            buff.SetData(data);
            SetBuffer(name, buff);

            return buff;
        }

        public void SetResolution(RenderTexture rt) {
            SetResolution(rt.width, rt.height, rt.dimension == TextureDimension.Tex3D ? rt.volumeDepth : 1);
        }
        public void SetResolution(Texture2D tex) {
            SetResolution(tex.width, tex.height, 1);
        }

        /// <summary>
        /// Divides the resolution by number of threads to set total threadgroups that will be used for dispatching
        /// </summary>
        public void SetResolution(int width, int height, int depth) {
            tGroupsX = Mathf.CeilToInt((float)(resX = width) / numThreadsX);
            tGroupsY = Mathf.CeilToInt((float)(resY = height) / numThreadsY);
            tGroupsZ = Mathf.CeilToInt((float)(resZ = depth) / numThreadsZ);
        }

        /// <summary>
        /// Sets the thread group counts directly. Total threads = threadgroups * numthreads(in the kernel)
        /// </summary>
        public void SetThreadGroups(int width, int height, int depth) {
            tGroupsX = width;
            tGroupsY = height;
            tGroupsZ = depth;
        }

        public void DispatchByCount(int countX, int countY = 1, int countZ = 1) {
            SetResolution(countX, countY, countZ);
            Dispatch();
        }

        public void Dispatch(int threadGroupsX, int threadGroupsY = 1, int threadGroupsZ = 1) {
            shader.Dispatch(kernelId, threadGroupsX, threadGroupsY, threadGroupsZ);
        }

        /// <summary>
        /// Uses pre-set group sizes
        /// </summary>
        public void Dispatch() {
            shader.Dispatch(kernelId, tGroupsX, tGroupsY, tGroupsZ);
        }

        public void SetBuffer(string name, ComputeBuffer buff) { shader.SetBuffer(kernelId, name, buff); }
        public void SetBuffer(int nameID, ComputeBuffer buff) { shader.SetBuffer(kernelId, nameID, buff); }


        public void SetTexture(string name, Texture tex) { shader.SetTexture(kernelId, name, tex); }
        public void SetTexture(int nameID, Texture tex) { shader.SetTexture(kernelId, nameID, tex); }




        public static implicit operator int(ComputeKernel kernel) {
            return kernel.kernelId;
        }

        public static implicit operator ComputeShader(ComputeKernel kernel) {
            return kernel.shader;
        }

        public static implicit operator bool(ComputeKernel kernel) {
            return kernel != null;
        }
    }

    public static class CSExtensions {

        ///// <summary>
        ///// Auto sets numthreads
        ///// </summary>
        //public static ComputeKernel GetComputeKernel(this ComputeShader shader, string kernelName) {
        //    return new ComputeKernel(shader, kernelName);
        //}

        ///// <summary>
        ///// Auto sets threadgroup sizes based on given resolution
        ///// </summary>
        //public static ComputeKernel GetComputeKernel(this ComputeShader shader, string kernelName, int resX, int resY, int resZ) {
        //    return new ComputeKernel(shader, kernelName, resX, resY, resZ);
        //}




        //private static Dictionary<Type, int> typeSizes = new Dictionary<Type, int> {
        //    {typeof(float), 4}, {typeof(int), 4}, {typeof(bool), 4}, {typeof(Color), 16}, {typeof(Vector2), 8}, {typeof(Vector3), 12}, {typeof(Vector4), 16}
        //};
        public static int Size(this Type obj) {
            return Marshal.SizeOf(obj);
            //var properties = obj.GetFields();
            //int count = 0;
            //
            //foreach (var prop in properties)
            //    count += SizeOf(prop.FieldType);
            //
            //return count;
        }

        public static int Size(this object c) {
            return Marshal.SizeOf(c.GetType());
        }


        public static string ListFields(this object obj) {
            string rstr = "";
            string sep = "   ";
            foreach (var prop in obj.GetType().GetFields()) {
                rstr += prop.Name + ": " + prop.GetValue(obj) + sep;
            }
            foreach (var prop in obj.GetType().GetProperties()) {
                rstr += prop.Name + ": " + prop.GetValue(obj, null) + sep;
            }

            return rstr;
        }

        private enum ShaderVarSetType { Vector, Int, Float, Bool, Color };
        private static Dictionary<Type, ShaderVarSetType> typeVarSets = new Dictionary<Type, ShaderVarSetType> {
        {typeof(float), ShaderVarSetType.Float}, {typeof(int), ShaderVarSetType.Int}, {typeof(bool), ShaderVarSetType.Bool},
        {typeof(Vector2), ShaderVarSetType.Vector}, {typeof(Vector3), ShaderVarSetType.Vector}, {typeof(Color), ShaderVarSetType.Color}
    };

        /// <summary>
        /// Uses reflection and string names
        /// <para>Not recommended for performance use cases</para>
        /// </summary>
        public static void SetComputeVars(this ComputeShader shader, string[] varNamesToBeSet, object hostClass) {

            foreach (string name in varNamesToBeSet) {
                System.Reflection.FieldInfo field = hostClass.GetType().GetField(name);
                if (field == null) {
                    //Debug.Log("nf: " + name);
                    continue;
                }


                //Debug.Log(field.Name + " : " + field.GetValue(hostClass));

                switch (typeVarSets[field.FieldType]) {
                    case ShaderVarSetType.Bool:
                        shader.SetInt(name, System.Convert.ToInt32((bool)field.GetValue(hostClass)));
                        break;
                    case ShaderVarSetType.Int:
                        shader.SetInt(name, (int)field.GetValue(hostClass));
                        break;
                    case ShaderVarSetType.Float:
                        shader.SetFloat(name, (float)field.GetValue(hostClass));
                        break;
                    case ShaderVarSetType.Vector:
                        shader.SetVector(name, (Vector3)field.GetValue(hostClass));
                        break;
                    case ShaderVarSetType.Color:
                        shader.SetVector(name, (Color)field.GetValue(hostClass));
                        break;
                }
            }
        }

        public static bool NullOrInvalid(this ComputeBuffer buff) => buff == null || !buff.IsValid();
        public static ComputeBuffer ReleaseIfValid(this ComputeBuffer buff) {
            if(buff != null && buff.IsValid()){
                buff.Release(); 
            }
            return null; 
        }


    }
}