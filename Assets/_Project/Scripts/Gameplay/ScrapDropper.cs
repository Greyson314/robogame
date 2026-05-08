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
        [Tooltip("Total scrap value to drop, expressed as a fraction of the chassis's " +
                 "at-spawn block count. Default 1.0 = '1 scrap per starting block', " +
                 "rounded down. Floored to MinTotalValue so a tiny chassis still drops something.")]
        [SerializeField, Min(0f)] private float _scrapPerBlock = 1f;

        [Tooltip("Floor on total scrap dropped, regardless of chassis size.")]
        [SerializeField, Min(1)] private int _minTotalValue = 2;

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

            int totalValue = ComputeTotalValue(robot);
            int pickupCount = Mathf.Clamp(robot.InitialBlockCount / 6, _minPickupCount, _maxPickupCount);
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

        private int ComputeTotalValue(Robot robot)
        {
            int blocks = Mathf.Max(1, robot.InitialBlockCount);
            int value = Mathf.RoundToInt(blocks * _scrapPerBlock);
            return Mathf.Max(_minTotalValue, value);
        }
    }
}
