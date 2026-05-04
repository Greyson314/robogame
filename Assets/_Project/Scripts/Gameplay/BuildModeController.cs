using System;
using Robogame.Block;
using Robogame.Player;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Owns the Garage's Build Mode toggle. When entered, freezes the
    /// chassis Rigidbody (kinematic, zero velocity), disables the chase
    /// camera + player input, and enables the build-mode free-fly camera
    /// + block editor + hotbar HUD. Exit reverses every step and
    /// triggers a chassis Respawn so subsystems re-bind cleanly to the
    /// (possibly edited) blueprint.
    /// </summary>
    /// <remarks>
    /// Session 23: replaced the chassis-locked <see cref="OrbitCamera"/>
    /// with a Robocraft-style <see cref="BuildFreeCam"/>. The orbit
    /// camera component stays on the camera GameObject (legacy code
    /// paths still reference it) but never gets enabled in build mode.
    /// </remarks>
    /// <remarks>
    /// Lives on the same GameObject as <see cref="GarageController"/> and
    /// is created/wired by it. All build-mode work goes through here so
    /// there's exactly one place that knows how to pause the world for
    /// editing.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BuildModeController : MonoBehaviour
    {
        [Tooltip("Robot transform to target. Picked up from GarageController.Chassis on enter.")]
        [SerializeField] private Transform _chassis;

        public bool IsActive { get; private set; }
        public Transform Chassis => _chassis;

        /// <summary>Raised after entering build mode (subsystems can show their UI etc.).</summary>
        public event Action Entered;
        /// <summary>Raised after leaving build mode (before the chassis Respawn is requested).</summary>
        public event Action Exited;

        // Saved state so Exit can restore exactly what Enter changed.
        private FollowCamera _follow;
        private BuildFreeCam _freeCam;
        private MonoBehaviour _playerInput; // kept loose-typed to avoid pulling Player.PlayerInputHandler into the public surface

        public void SetChassis(Transform chassis) => _chassis = chassis;

        public void Enter()
        {
            if (IsActive) return;
            if (_chassis == null)
            {
                Debug.LogWarning("[Robogame] BuildModeController.Enter: no chassis bound; ignoring.", this);
                return;
            }

            // Note: the chassis is ALREADY parked by GarageController
            // (kinematic + FreezeAll). We don't touch the Rigidbody here.

            // 1. Disable player input — stops the cursor capture / aim
            //    updates / weapon-fire from running while editing.
            _playerInput = _chassis.GetComponent("PlayerInputHandler") as MonoBehaviour;
            if (_playerInput != null) _playerInput.enabled = false;

            // 2. Camera swap. FollowCamera off, BuildFreeCam on (created
            //    lazily). Free-fly is a true Robocraft-style cam — WASD
            //    to translate, Q/E or Space/Ctrl for vertical, hold
            //    right-mouse to look. Cursor stays free so the player
            //    can still click hotbar buttons + place blocks.
            Camera cam = Camera.main;
            if (cam != null)
            {
                _follow = cam.GetComponent<FollowCamera>();
                if (_follow != null) _follow.enabled = false;

                _freeCam = cam.GetComponent<BuildFreeCam>();
                if (_freeCam == null) _freeCam = cam.gameObject.AddComponent<BuildFreeCam>();
                _freeCam.enabled = true;
                // Position the free-cam looking at the chassis from a
                // sensible starting offset so the player isn't dropped
                // mid-bot. Camera's transform is what BuildFreeCam reads
                // on enable to seed yaw/pitch.
                Vector3 chassisPos = _chassis.position;
                cam.transform.position = chassisPos + new Vector3(0f, 6f, -12f);
                cam.transform.LookAt(chassisPos);
            }

            IsActive = true;
            Entered?.Invoke();
        }

        public void Exit(bool requestRespawn = true)
        {
            if (!IsActive) return;
            IsActive = false;

            // 1. Camera swap back.
            if (_freeCam != null) _freeCam.enabled = false;
            if (_follow != null) _follow.enabled = true;

            // 2. Re-enable player input.
            if (_playerInput != null) _playerInput.enabled = true;

            // Note: chassis stays parked — GarageController owns that state
            // and Respawn() below will rebuild + re-park anyway.

            Exited?.Invoke();

            // 3. Rebuild the chassis from the (possibly edited) blueprint
            //    so subsystem composition (drive subsystems, weapon binders)
            //    reflects the final block list.
            if (requestRespawn)
            {
                GarageController garage = FindAnyObjectByType<GarageController>();
                if (garage != null) garage.Respawn();
            }
        }

        /// <summary>Toggle convenience for hotkey hookups.</summary>
        public void Toggle()
        {
            if (IsActive) Exit();
            else Enter();
        }
    }
}
