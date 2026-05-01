using UnityEngine;

namespace OccaSoftware.Fluff.Runtime
{
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public sealed class RenderInteractiveGrass : MonoBehaviour
    {
        #region Properties
        [Tooltip("You can define a target that the interactivity renderer will follow. If left null, the camera will remain in place.")]
        public Transform target;

        [Tooltip(
            "When a target is assigned, the camera will follow the world position of the target. If the target is null, this value will be ignored. The camera will also be offset by this amount on the y axis to ensure the target is inside of the view frustum. Formula: Camera Position = Target Position + Vector3(0, yOffset, 0)."
        )]
        public float yOffset;

        /// <summary>
        /// The camera used for the grass interactivity renderer. This must be set and is required for the script as part of a [RequireComponent] attribute.
        /// </summary>
        private Camera cam;

        /// <summary>
        /// The camera should always aim down.
        /// </summary>
        private static Quaternion rotation = Quaternion.Euler(90, 0, 0);

        /// <summary>
        /// This static class stores the shader property ID's for each property that will be set by the Interactivity Renderer.
        /// Using the ID directly is more performant than using the Property.
        /// </summary>
        private static class ShaderParams
        {
            public static int _GrassInteractivityCameraPosition = Shader.PropertyToID("_GrassInteractivityCameraPosition");
            public static int _GrassInteractivityCameraSize = Shader.PropertyToID("_GrassInteractivityCameraSize");
            public static int _GrassInteractivityEnabled = Shader.PropertyToID("_GrassInteractivityEnabled");
            public static int _GrassInteractivityMap = Shader.PropertyToID("_GrassInteractivityMap");
        }

        /// <summary>
        /// The camera must be set up correctly before the Interactivity Renderer will execute.
        /// Warnings will be logged if the camera isn't set up correctly.
        /// This setup should occur in editor.
        /// </summary>
        private bool IsValid => cam.targetTexture != null ? true : false;
        #endregion

        #region Unity Event Functions
        private void OnValidate()
        {
            ValidateConfig();
        }

        private void Start()
        {
            cam = GetComponent<Camera>();
            ValidateConfig();
        }

        private void LateUpdate()
        {
            if (IsValid)
            {
                UpdateRotation();
                UpdatePosition();
                UpdateShaderProperties();
            }
        }
        #endregion

        #region Methods
        private void ValidateConfig()
        {
            cam = GetComponent<Camera>();
            ValidateCamera();
            ValidateRenderTexture();
        }

        private void ValidateCamera()
        {
            if (!cam.orthographic)
            {
                cam.orthographic = true;
                cam.orthographicSize = 20;
                Debug.Log(
                    "Your Grass Interactivity Renderer was using a camera with perspective projection. This sytem requires an orthographic camera. The camera has been automatically changed to orthographic.",
                    cam
                );
            }
        }

        private void ValidateRenderTexture()
        {
            if (cam.targetTexture == null)
            {
                Debug.Log(
                    "Your Grass Interactivity Renderer's Camera is missing a target render texture. You must assign a target render texture.",
                    cam
                );
            }
        }

        private void UpdateRotation()
        {
            transform.rotation = rotation;
        }

        private void UpdatePosition()
        {
            if (target != null)
            {
                transform.position = new Vector3(target.position.x, target.position.y + yOffset, target.position.z);
            }
        }

        private void UpdateShaderProperties()
        {
            Shader.SetGlobalInt(ShaderParams._GrassInteractivityEnabled, 1);
            Shader.SetGlobalVector(ShaderParams._GrassInteractivityCameraPosition, transform.position);
            Shader.SetGlobalFloat(ShaderParams._GrassInteractivityCameraSize, cam.orthographicSize);
            Shader.SetGlobalTexture(ShaderParams._GrassInteractivityMap, cam.targetTexture);
        }
        #endregion
    }
}
