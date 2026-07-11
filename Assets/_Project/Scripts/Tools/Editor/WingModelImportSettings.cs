using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Import settings for the rigged Wing FBX (the project's first
    /// skinned-mesh asset). Enforced as an <see cref="AssetPostprocessor"/>
    /// rather than hand-set in the Inspector so every re-export from
    /// Blender (<c>artgen/inv_export.py export_wing_anim</c>) reimports
    /// correctly with zero manual steps — the idempotent-scaffolder
    /// convention.
    /// </summary>
    /// <remarks>
    /// Legacy <see cref="Animation"/> (not Animator): one always-on loop
    /// with no blending needs no controller asset, and enable/Play/Stop
    /// is the whole runtime API <c>WingFlapAnimator</c> uses.
    /// <c>playAutomatically</c> is forced OFF so a garage-placed wing
    /// holds its rest pose until the animator component decides —
    /// invariant #2's "what you build is what freezes" readability.
    /// </remarks>
    public sealed class WingModelImportSettings : AssetPostprocessor
    {
        private const string WingFbxPath =
            "Assets/_Project/Art/Models/Blocks/Inv/Wing_Inv.fbx";

        private bool IsWing => assetPath == WingFbxPath;

        private void OnPreprocessModel()
        {
            if (!IsWing) return;
            var mi = (ModelImporter)assetImporter;
            mi.animationType = ModelImporterAnimationType.Legacy;
            mi.importAnimation = true;
        }

        private void OnPreprocessAnimation()
        {
            if (!IsWing) return;
            var mi = (ModelImporter)assetImporter;
            // The baked action covers frames 1..49 with 49 == 1, so the
            // clip loops seamlessly when wrapped.
            ModelImporterClipAnimation[] clips = mi.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
            {
                clips[i].loopTime = true;
                clips[i].wrapMode = WrapMode.Loop;
            }
            mi.clipAnimations = clips;
        }

        private void OnPostprocessModel(GameObject go)
        {
            if (!IsWing) return;
            var anim = go.GetComponent<Animation>();
            if (anim != null)
            {
                anim.playAutomatically = false;
                anim.wrapMode = WrapMode.Loop;
            }
        }
    }
}
