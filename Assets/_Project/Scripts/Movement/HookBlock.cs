using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Tip block: pointed hook. Light + sharp — low mass means modest
    /// kinetic energy per swing, but the angular silhouette reads as
    /// "this thing pierces."
    /// </summary>
    /// <remarks>
    /// Damage shape comes from the base <see cref="TipBlock"/>; the
    /// only Hook-specific concerns are the visual mesh and the contact
    /// collider's geometry. Per-block mass lives on the
    /// <see cref="BlockDefinition"/> (set by the wizard at scaffold
    /// time) — TipBlock reads it via <see cref="TipBlock.Mass"/>.
    /// </remarks>
    public sealed class HookBlock : TipBlock
    {
        // Slightly off-charcoal so the hook reads against the rope's
        // dark-slate segment colour without disappearing into it.
        private static readonly Color s_hookColor = new Color(0.45f, 0.32f, 0.18f);

        protected override void BuildTipVisual()
        {
            // Curved-hook stand-in: a narrow cube angled forward + down.
            // Visual is intentionally simple — once the user calls a
            // Phase-2 art pass we can swap in an authored mesh.
            Transform shaft = BlockVisuals.GetOrCreatePrimitiveChild(
                transform, "HookShaft", PrimitiveType.Cube, stripCollider: true);
            shaft.localScale    = new Vector3(0.20f, 0.55f, 0.20f);
            shaft.localPosition = new Vector3(0f, -0.10f, 0f);
            shaft.localRotation = Quaternion.identity;

            Transform barb = BlockVisuals.GetOrCreatePrimitiveChild(
                transform, "HookBarb", PrimitiveType.Cube, stripCollider: true);
            barb.localScale    = new Vector3(0.20f, 0.20f, 0.45f);
            barb.localPosition = new Vector3(0f, -0.40f, 0.18f);
            barb.localRotation = Quaternion.Euler(0f, 0f, 0f);

            Tint(shaft.GetComponent<Renderer>(), s_hookColor);
            Tint(barb.GetComponent<Renderer>(),  s_hookColor);

            // Contact collider: a tight box covering shaft + barb. The
            // box approximates "anything inside this volume gets hooked."
            BoxCollider bc = GetComponent<BoxCollider>();
            if (bc == null) bc = gameObject.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, -0.25f, 0.10f);
            bc.size = new Vector3(0.25f, 0.65f, 0.50f);
            bc.isTrigger = false;
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
