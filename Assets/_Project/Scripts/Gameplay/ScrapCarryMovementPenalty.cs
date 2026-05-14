using Robogame.Movement;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Bridge component that listens for <see cref="Robot.ScrapAwarded"/>
    /// and pushes the chassis's current carry-weight multiplier onto its
    /// <see cref="RobotDrive.CarrySpeedMultiplier"/>. Implements Phase 2
    /// of <c>docs/SCRAP_LOOP_PLAN.md</c> — a chassis hauling scrap moves
    /// slower so the player has to commit to a depot run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a separate component, not direct read in RobotDrive?</b>
    /// <see cref="RobotDrive"/> lives in <c>Robogame.Movement</c>, which
    /// is referenced by <c>Robogame.Robots</c> (the home of
    /// <see cref="Robot"/>). Reversing the reference would create a
    /// circular asmdef edge, so the multiplier is pushed in from
    /// Gameplay tier — same shape as <see cref="ScrapDropper"/>.
    /// </para>
    /// <para>
    /// <b>Event-driven.</b> No per-frame Update: the multiplier is only
    /// recomputed when <see cref="Robot.ScrapHeld"/> actually changes.
    /// One push at OnEnable seeds the initial value (default 1).
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Robot))]
    [RequireComponent(typeof(RobotDrive))]
    public sealed class ScrapCarryMovementPenalty : MonoBehaviour
    {
        private Robot _robot;
        private RobotDrive _drive;

        private void OnEnable()
        {
            _robot = GetComponent<Robot>();
            _drive = GetComponent<RobotDrive>();
            if (_robot != null) _robot.ScrapAwarded += HandleScrapChanged;
            Apply();
        }

        private void OnDisable()
        {
            if (_robot != null) _robot.ScrapAwarded -= HandleScrapChanged;
        }

        private void HandleScrapChanged(Robot _, int __) => Apply();

        private void Apply()
        {
            if (_drive == null || _robot == null) return;
            _drive.CarrySpeedMultiplier = _robot.CarryWeightMoveMultiplier;
        }
    }
}
