using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GrassFlow.Examples {
    public class ContactFlatten : MonoBehaviour {

        public Collider grassCol;
        public int grassLayer = -1;
        public Texture2D flatTex;
        public GrassFlowRenderer grassFlow;
        public float flatStrength;
        public float flatSize;

        //private void Update() {
        //    grassFlow.UpdateTransform();
        //}

        Ray ray;
        RaycastHit hit;
        ContactPoint contact;
        private void OnCollisionStay(Collision collision) {
            if (enabled) {
                if (collision.transform == grassCol.transform || collision.gameObject.layer == grassLayer) {

#if UNITY_2018_3_OR_NEWER
                    contact = collision.GetContact(0);
#else
                    contact = collision.contacts[0];
#endif


                    ray = new Ray(contact.point + contact.normal * 0.1f, -contact.normal);
                    if (contact.otherCollider.Raycast(ray, out hit, 0.5f)) {
                        var gMesh = grassFlow.GetGrassMeshFromTransform(hit.transform);

                        GrassFlowRenderer.SetPaintBrushTexture(flatTex);
                        grassFlow.PaintParameters(gMesh, hit.textureCoord, flatSize, flatStrength, 0, 0, -1, 0, new Vector2(0f, 0.9f));
                    }
                }
            }
        }

    }
}
