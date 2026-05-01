using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GrassFlow.Examples {
    [ExecuteInEditMode]
    public class StickToTerrain : MonoBehaviour {

        public float castOffset = 10f;
        public float heightOffset = 0;


        Ray ray;
        RaycastHit hit;

        private void LateUpdate() {

            ray.origin = transform.position + transform.up * castOffset;
            ray.direction = Vector3.down;
            if (Physics.Raycast(ray, out hit)) {
                Vector3 pos = transform.position;
                pos.y = hit.point.y + heightOffset;
                transform.position = pos;
            }

        }

    }
}
