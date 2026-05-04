using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Tip block: large J-shaped grappling hook. Sized to enclose a
    /// chassis cell (~1 m × 1 m × 1 m) inside its trap zone so the
    /// player can swing the rope under a target's exposed bar / handle
    /// and catch it on the way back up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Visual is three cubes — vertical shaft, horizontal barb arm,
    /// vertical barb tip — laid out in the rope segment's local frame
    /// (segment-local +Z = world down, segment-local +Y = chassis
    /// forward). Compound BoxCollider on the host approximates the
    /// J's hit volume so the trap reads physically, not just visually.
    /// </para>
    /// <para>
    /// Damage on contact comes from the base <see cref="TipBlock"/>;
    /// per-block mass lives on the <see cref="BlockDefinition"/> (set
    /// by <c>BlockDefinitionWizard</c>) and feeds the kinetic-energy
    /// formula through <see cref="TipBlock.Mass"/>. The mass differs
    /// from the mace's so identical swing speeds yield different KE.
    /// </para>
    /// </remarks>
    public sealed class HookBlock : TipBlock
    {
        // Rust / iron tone, separate from the rope's slate so the hook
        // reads against the chain at gameplay distance.
        private static readonly Color s_hookColor = new Color(0.45f, 0.32f, 0.18f);

        // Geometry (in segment-local space). Centralised so the visual
        // cubes and the matching BoxColliders stay in sync.
        // Coordinate system: +Z = down the rope (world down at rest);
        //                    +Y = chassis-forward direction;
        //                    +X = chassis-right.
        private const float ThicknessX  = 0.45f; // hook's narrow side
        private const float ThicknessYZ = 0.40f; // bar / arm thickness
        private const float ShaftLength = 1.70f; // vertical shaft Z extent
        private const float ArmLength   = 1.70f; // horizontal barb arm Y extent
        private const float TipLength   = 1.50f; // upturned barb tip Z extent

        protected override void BuildTipVisual()
        {
            // Idempotent: clear any prior BoxColliders before adding the
            // new compound. Awake / EnsureRig may run twice in
            // pathological cases (asset reimport, scene reload).
            ClearBoxColliders();

            // Shaft — vertical, going down the rope. Starts at the hook
            // origin (segment centre) and extends +Z by ShaftLength.
            Vector3 shaftCentre = new Vector3(0f, 0f, ShaftLength * 0.5f);
            Vector3 shaftSize   = new Vector3(ThicknessX, ThicknessYZ, ShaftLength);
            BuildVisualCube("HookShaft", shaftCentre, shaftSize);
            AddBoxCollider(shaftCentre, shaftSize);

            // Barb arm — horizontal, sitting under the shaft, extending
            // forward (+Y) by ArmLength. Top face flush with shaft bottom.
            Vector3 armCentre = new Vector3(0f, ArmLength * 0.5f, ShaftLength + ThicknessYZ * 0.5f);
            Vector3 armSize   = new Vector3(ThicknessX, ArmLength, ThicknessYZ);
            BuildVisualCube("HookBarbArm", armCentre, armSize);
            AddBoxCollider(armCentre, armSize);

            // Barb tip — vertical, going back up from the end of the arm.
            // Sits at Y = ArmLength, spans Z from (top of shaft + a sliver)
            // down to flush with the arm's top face, leaving a clear
            // mouth opening at the top of the J.
            float tipCentreZ = ShaftLength - TipLength * 0.5f;
            Vector3 tipCentre = new Vector3(0f, ArmLength, tipCentreZ);
            Vector3 tipSize   = new Vector3(ThicknessX, ThicknessYZ, TipLength);
            BuildVisualCube("HookBarbTip", tipCentre, tipSize);
            AddBoxCollider(tipCentre, tipSize);
        }

        private void BuildVisualCube(string name, Vector3 centre, Vector3 size)
        {
            Transform t = BlockVisuals.GetOrCreatePrimitiveChild(
                transform, name, PrimitiveType.Cube, stripCollider: true);
            t.localPosition = centre;
            t.localRotation = Quaternion.identity;
            t.localScale    = size;
            Tint(t.GetComponent<Renderer>(), s_hookColor);
        }

        private void AddBoxCollider(Vector3 centre, Vector3 size)
        {
            BoxCollider bc = gameObject.AddComponent<BoxCollider>();
            bc.center = centre;
            bc.size = size;
            bc.isTrigger = false;
        }

        private void ClearBoxColliders()
        {
            BoxCollider[] existing = GetComponents<BoxCollider>();
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] == null) continue;
                if (Application.isPlaying) Destroy(existing[i]);
                else                       DestroyImmediate(existing[i]);
            }
        }

        private static void Tint(Renderer r, Color color)
        {
            if (r == null) return;
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(Shader.PropertyToID("_AlbedoColor"), color);
            mpb.SetColor(Shader.PropertyToID("_BaseColor"),   color);
            mpb.SetColor(Shader.PropertyToID("_Color"),       color);
            r.SetPropertyBlock(mpb);
        }
    }
}
