using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace OccaSoftware.Fluff.Demo
{
    [RequireComponent(typeof(Rigidbody))]
    [AddComponentMenu("OccaSoftware/Fluff/Demo/Sphere Controller")]
    public class SphereController : MonoBehaviour
    {
        Rigidbody rb;

        private void Start()
        {
            rb = GetComponent<Rigidbody>();
        }

        void Update()
        {
            Vector3 input = Vector3.zero;
            Vector3 velocity = rb.linearVelocity;
            if (Input.GetKey(KeyCode.W))
            {
                input += Vector3.forward;
            }
            if (Input.GetKey(KeyCode.A))
            {
                input -= Vector3.right;
            }
            if (Input.GetKey(KeyCode.S))
            {
                input -= Vector3.forward;
            }
            if (Input.GetKey(KeyCode.D))
            {
                input += Vector3.right;
            }
            input *= 10f;
            input = Vector3.ClampMagnitude(input, 1f);
            velocity.x = Mathf.MoveTowards(velocity.x, input.x, 10f * Time.deltaTime);
            velocity.z = Mathf.MoveTowards(velocity.z, input.z, 10f * Time.deltaTime);
            rb.linearVelocity = velocity;
        }
    }

}
