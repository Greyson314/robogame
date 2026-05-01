using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace GrassFlow {
    public class RuntimeAddMeshExample : MonoBehaviour {


        public GrassFlowRenderer grass;
        public Material grassMat;
        public Mesh mesh;

        GrassFlow.GrassMesh addedMesh;

        void Awake() {

            grass.OnInititialized += Add;

            Invoke(nameof(Remove), 5);
        }

        async void Add() {
            addedMesh = await grass.AddMesh(mesh, transform, grassMat);
        }

        void Remove() {
            grass.RemoveGrassMesh(addedMesh);
        }

    }
}