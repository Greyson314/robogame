using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Tip block: heavy spiked ball. Mass is the gameplay differentiator
    /// — at default 2.0 kg vs the hook's 0.5 kg, the same swing speed
    /// yields ~4× the kinetic energy. The damage formula scales linearly
    /// with energy, so the mace hits harder per impact.
    /// </summary>
    public sealed class MaceBlock : TipBlock
    {
        // Cool grey — separates from the hook's warm brown so a player
        // can tell which tip is on which rope at a glance.
        private static readonly Color s_maceColor = new Color(0.55f, 0.58f, 0.62f);

        protected override void BuildTipVisual()
        {
            // Central ball — generous radius so the mace reads as
            // chunky compared to the hook's slim profile.
            Transform ball = BlockVisuals.GetOrCreatePrimitiveChild(
                transform, "MaceBall", PrimitiveType.Sphere, stripCollider: true);
            ball.localScale    = new Vector3(0.55f, 0.55f, 0.55f);
            ball.localPosition = Vector3.zero;
            ball.localRotation = Quaternion.identity;

            // Six axial spikes for the studded silhouette. Cube primitives
            // work fine — we want angular shadows, not smooth pyramids.
            Vector3[] axes =
            {
                new Vector3( 1, 0, 0), new Vector3(-1, 0, 0),
                new Vector3( 0, 1, 0), new Vector3( 0,-1, 0),
                new Vector3( 0, 0, 1), new Vector3( 0, 0,-1),
            };
            for (int i = 0; i < axes.Length; i++)
            {
                Transform spike = BlockVisuals.GetOrCreatePrimitiveChild(
                    transform, $"MaceSpike_{i}", PrimitiveType.Cube, stripCollider: true);
                spike.localScale    = new Vector3(0.10f, 0.10f, 0.30f);
                spike.localPosition = axes[i] * 0.32f;
                spike.localRotation = Quaternion.LookRotation(axes[i], Vector3.up);
                Tint(spike.GetComponent<Renderer>(), s_maceColor);
            }
            Tint(ball.GetComponent<Renderer>(), s_maceColor);

            // Sphere collider sized to enclose the ball + most of the
            // spikes — tight enough that the mace doesn't snag on
            // every passing block, generous enough to register hits.
            SphereCollider sc = GetComponent<SphereCollider>();
            if (sc == null) sc = gameObject.AddComponent<SphereCollider>();
            sc.radius = 0.40f;
            sc.center = Vector3.zero;
            sc.isTrigger = false;
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
