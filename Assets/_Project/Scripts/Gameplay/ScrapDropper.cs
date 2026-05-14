using Robogame.Robots;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Sibling component on a chassis Rigidbody. Subscribes to
    /// <see cref="Robot.Destroyed"/> and, on death, spawns a small ring
    /// of <see cref="ScrapPickup"/> instances around the chassis centre.
    /// Pickup count + total value scale with the chassis's at-spawn
    /// block count so big bots are worth more than scout chassis.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a separate component, not Robot.MarkDestroyed?</b> Robot
    /// lives in <c>Robogame.Robots</c> (a low asmdef tier so the
    /// Movement/Combat/Gameplay layers can reference it). Scrap pickups
    /// live in <c>Robogame.Gameplay</c>. Putting drop logic on a Gameplay
    /// component keeps the asmdef tier clean and gives future drop-rule
    /// systems (faction-based, mode-based, score-multiplier perks) a
    /// natural home.
    /// </para>
    /// <para>
    /// <b>Tuning.</b> All values are <c>SerializeField</c> on the
    /// component, NOT Tweakables — drop rates affect gameplay outcomes
    /// (PHYSICS_PLAN § 1.5). Designer-authored per-chassis or per-arena
    /// when those scopes land.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Robot))]
    public sealed class ScrapDropper : MonoBehaviour
    {
        [Tooltip("Flat scrap dropped on death. Override defaults to 3 unless MatchConfig.BaseDeathDrop is wired in, which it is by ArenaController.")]
        [SerializeField, Min(0)] private int _baseDeathDrop = 3;

        [Tooltip("Minimum number of pickup instances to scatter. The total value is split " +
                 "across instances (with the remainder added to the first).")]
        [SerializeField, Min(1)] private int _minPickupCount = 2;

        [Tooltip("Maximum number of pickup instances. Capped so a 100-block titan doesn't " +
                 "spawn 100 pickups and bog the trigger system.")]
        [SerializeField, Min(1)] private int _maxPickupCount = 6;

        [Tooltip("Approximate cells of horizontal spread around the chassis centre. The actual " +
                 "scatter is randomised inside this radius so pickups don't pile on top of each other.")]
        [SerializeField, Min(0f)] private float _scatterRadius = 1.6f;

        [Tooltip("Vertical bias above the chassis centre — keeps spawned pickups from clipping into " +
                 "the ground or the still-falling debris cloud.")]
        [SerializeField, Min(0f)] private float _scatterUpward = 1.0f;

        [Tooltip("If true, the chassis owner doesn't get to collect their own scrap on respawn or " +
                 "via repair-pad rebuild. Currently a no-op (no faction system) — left as a hook " +
                 "for future faction filters.")]
        [SerializeField] private bool _ownerCannotCollect = false;

        private Robot _robot;
        private bool _dropped;

        public bool OwnerCannotCollect => _ownerCannotCollect;

        /// <summary>
        /// Override the base death drop at runtime — called by ArenaController
        /// after spawn so the value comes from MatchConfig instead of each
        /// chassis's SerializeField. Lets a designer balance drop rates in one
        /// place per match.
        /// </summary>
        public void ConfigureDrop(int baseDeathDrop)
        {
            if (baseDeathDrop >= 0) _baseDeathDrop = baseDeathDrop;
        }

        private void OnEnable()
        {
            _robot = GetComponent<Robot>();
            if (_robot != null) _robot.Destroyed += HandleDestroyed;
        }

        private void OnDisable()
        {
            if (_robot != null) _robot.Destroyed -= HandleDestroyed;
        }

        private void HandleDestroyed(Robot robot)
        {
            if (_dropped) return;
            _dropped = true;

            // Total dropped = base flat amount + whatever scrap the victim
            // was carrying. The carried-scrap drop is what makes hoarding
            // risky — a robot loaded with 7 scrap is a 10-scrap kill target,
            // not a 3-scrap one.
            int totalValue = _baseDeathDrop + Mathf.Max(0, robot.ScrapHeld);
            if (totalValue <= 0) return;

            // Grinder kill bonus (SCRAP_LOOP_PLAN § 4). A chassis that
            // died inside a depot's volume drops bonus-multiplied scrap.
            // The bonus is sourced from the depot itself so designers
            // can tune the multiplier per-depot if asymmetric arenas
            // ever need it.
            ScrapDepot grinder = ScrapDepot.FindDepotContaining(robot);
            if (grinder != null && grinder.GrinderKillBonus > 1f)
            {
                totalValue = Mathf.RoundToInt(totalValue * grinder.GrinderKillBonus);
            }

            int pickupCount = Mathf.Clamp(totalValue, _minPickupCount, _maxPickupCount);
            int valuePerPickup = Mathf.Max(1, totalValue / pickupCount);
            int remainder = totalValue - valuePerPickup * pickupCount;

            Rigidbody rb = robot.Rigidbody;
            Vector3 origin = rb != null ? rb.worldCenterOfMass : transform.position;

            for (int i = 0; i < pickupCount; i++)
            {
                Vector3 offset = Random.insideUnitSphere * _scatterRadius;
                offset.y = Mathf.Abs(offset.y) + _scatterUpward;
                int v = valuePerPickup + (i == 0 ? remainder : 0);
                ScrapPickup.Spawn(origin + offset, v);
            }
        }
    }
}
