using Robogame.Core;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Marks a horizontal water surface in a scene. The surface plane is
    /// implicit at <c>transform.position.y</c>, extending infinitely in
    /// X and Z — buoyancy clients (see <see cref="BuoyancyController"/>)
    /// don't bother checking lateral bounds because the water-arena scene
    /// is enclosed by walls anyway.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lookup model: there is at most one <see cref="WaterVolume"/> per
    /// scene. The active instance is registered in <see cref="OnEnable"/>
    /// and accessible via <see cref="Active"/> so per-chassis buoyancy
    /// components can resolve the water without a serialized reference.
    /// </para>
    /// <para>
    /// Density is the only physically-meaningful knob. Drag values are
    /// stylised: real water drag is velocity-squared, but a linear/angular
    /// scalar matches Unity's <see cref="Rigidbody"/> drag fields and is
    /// stable enough for a sandbox. A fully-submerged chassis gets the
    /// configured drag scaled by its average submerged fraction; surface-
    /// floating chassis feel snappier because only some blocks are wet.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class WaterVolume : MonoBehaviour
    {
        // Water parameters live in Tweakables so the in-game settings
        // sliders drive them at runtime. The scene authoring stage (the
        // EnvironmentBuilder pass that creates this volume) doesn't need
        // to know any numbers — defaults come from the Tweakables registry.

        public float SurfaceY    => transform.position.y;
        public float Density     => Tweakables.Get(Tweakables.WaterDensity);
        public float Displacement => Tweakables.Get(Tweakables.WaterDisplacement);
        public float LinearDrag  => Tweakables.Get(Tweakables.WaterLinearDrag);
        public float AngularDrag => Tweakables.Get(Tweakables.WaterAngularDrag);
        public float Gravity     => Tweakables.Get(Tweakables.WaterGravity);

        /// <summary>The currently-active water volume in the scene, or null.</summary>
        public static WaterVolume Active { get; private set; }

        private void OnEnable()
        {
            // First-wins; subsequent volumes log a warning and stay
            // dormant. Scenes are expected to author exactly one.
            if (Active != null && Active != this)
            {
                Debug.LogWarning(
                    $"[Robogame] WaterVolume: a second instance was enabled on " +
                    $"'{name}'; ignoring and keeping '{Active.name}' as Active.",
                    this);
                return;
            }
            Active = this;
        }

        private void OnDisable()
        {
            if (Active == this) Active = null;
        }
    }
}
