using Robogame.Block;
using UnityEngine;

namespace Robogame.Movement
{
    /// <summary>
    /// Tip block: heavy spiked ball. Sized at ~1 m diameter so it reads
    /// as a "wrecking ball at the end of the rope" against gameplay-
    /// scale targets. Mass is the gameplay differentiator vs the hook —
    /// a default-spec mace at 5 kg vs a 1.5 kg hook gives ~3.3× the
    /// kinetic energy at the same swing speed, so the mace hits harder
    /// per impact even with identical <c>Combat.RopeDamagePerKj</c>.
    /// </summary>
    public sealed class MaceBlock : TipBlock
    {
        // Cool grey ball with darker spikes — separates from the hook's
        // warm rust tone so the player can tell which tip is on which
        // rope at a glance.
        private static readonly Color s_maceColor = new Color(0.55f, 0.58f, 0.62f);

        // Geometry constants centralise the visual + collider sizing.
        private const float BallDiameter  = 1.00f;
        private const float SpikeLength   = 0.55f;
        private const float SpikeThick    = 0.20f;
        private const float ColliderRadius = 0.65f; // covers ball + ~half of each spike

        protected override void BuildTipVisual()
        {
            // Idempotent: clear any prior SphereColliders before adding
            // the new one (Awake / EnsureRig may re-fire on reimport).
            ClearSphereColliders();

            // Central ball.
            Transform ball = BlockVisuals.GetOrCreatePrimitiveChild(
                transform, "MaceBall", PrimitiveType.Sphere, stripCollider: true);
            ball.localScale    = new Vector3(BallDiameter, BallDiameter, BallDiameter);
            ball.localPosition = Vector3.zero;
            ball.localRotation = Quaternion.identity;
            Tint(ball.GetComponent<Renderer>(), s_maceColor);

            // Six axial spikes for the studded silhouette. Cube primitives
            // give angular shadows; the spike's long axis is its local +Z
            // after a LookRotation toward the spike's outward direction.
            Vector3[] axes =
            {
                new Vector3( 1, 0, 0), new Vector3(-1, 0, 0),
                new Vector3( 0, 1, 0), new Vector3( 0,-1, 0),
                new Vector3( 0, 0, 1), new Vector3( 0, 0,-1),
            };
            float spikeOffset = BallDiameter * 0.5f + SpikeLength * 0.5f - 0.05f;
            for (int i = 0; i < axes.Length; i++)
            {
                Transform spike = BlockVisuals.GetOrCreatePrimitiveChild(
                    transform, $"MaceSpike_{i}", PrimitiveType.Cube, stripCollider: true);
                spike.localScale    = new Vector3(SpikeThick, SpikeThick, SpikeLength);
                spike.localPosition = axes[i] * spikeOffset;
                // Pick an arbitrary up that is NOT parallel to the axis,
                // so LookRotation can compute a stable basis.
                Vector3 upHint = (Mathf.Abs(axes[i].y) > 0.5f) ? Vector3.right : Vector3.up;
                spike.localRotation = Quaternion.LookRotation(axes[i], upHint);
                Tint(spike.GetComponent<Renderer>(), s_maceColor);
            }

            // Compound collider: a single sphere big enough to enclose
            // the ball and most of the spike length. Approximates the
            // hit volume well enough for contact damage; precise
            // per-spike colliders are overkill for cosmetic-spike
            // silhouette work.
            SphereCollider sc = gameObject.AddComponent<SphereCollider>();
            sc.radius = ColliderRadius;
            sc.center = Vector3.zero;
            sc.isTrigger = false;
        }

        private void ClearSphereColliders()
        {
            SphereCollider[] existing = GetComponents<SphereCollider>();
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
