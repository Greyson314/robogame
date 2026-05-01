using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GrassFlow;

public class RemoveGrassFromBuffer : MonoBehaviour {

    public GrassFlowRenderer gf;
    public float radius = 1;

    void RemoveGrass() {
        //gf.terrainMeshes[0].subGrassMeshes[0].posBuffer

    }

    private void Update() {
        if (Input.GetKey(KeyCode.T)) {
            MeshChunker.RemoveGrass(gf.terrainMeshes[0], transform.position, radius);
        }
    }

}
