using Robogame.Robots;
using UnityEngine;

namespace Robogame.Player
{
    /// <summary>
    /// Arena-scoped coordinator that keeps each chassis's
    /// <see cref="ChassisOutlineController"/> in sync with relevance:
    /// the local player's chassis is always outlined; the player's
    /// current aim target is outlined; everything else runs the plain
    /// (no-outline-pass) materials. This is the draw-call win — the
    /// per-renderer MK Toon outline pass only runs for ~2 chassis
    /// instead of every robot in frame.
    /// </summary>
    /// <remarks>
    /// Lazily self-creates. <see cref="Gameplay"/>'s assembler calls
    /// <see cref="RegisterAsLocalPlayer"/> / <see cref="RegisterAsBot"/>
    /// after a chassis finishes building. Subscribes to
    /// <see cref="TargetTracker.TargetChanged"/> (the SP relevance
    /// signal); the <see cref="IRelevanceSource"/> seam lets MP replace
    /// the target signal with a server hint later.
    /// </remarks>
    public sealed class OutlineRelevanceManager : MonoBehaviour, IRelevanceSource
    {
        private static OutlineRelevanceManager s_instance;

        private Robot _player;
        private Robot _target;
        private TargetTracker _tracker;
        private bool _subscribed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_instance = null;

        public static OutlineRelevanceManager Get()
        {
            if (s_instance != null) return s_instance;
            var go = new GameObject("OutlineRelevanceManager") { hideFlags = HideFlags.DontSave };
            s_instance = go.AddComponent<OutlineRelevanceManager>();
            return s_instance;
        }

        bool IRelevanceSource.IsRelevant(Robot robot)
            => robot != null && (robot == _player || robot == _target);

        /// <summary>Assembler entry point: dispatches to local-player or bot.</summary>
        public void RegisterChassis(Robot robot, bool isLocalPlayer)
        {
            if (isLocalPlayer) RegisterAsLocalPlayer(robot);
            else RegisterAsBot(robot);
        }

        public void RegisterAsLocalPlayer(Robot robot)
        {
            if (robot == null) return;
            _player = robot;
            EnsureSubscribed();
            Apply(robot, true);
        }

        public void RegisterAsBot(Robot robot)
        {
            if (robot == null) return;
            EnsureSubscribed();
            // Relevant only if it happens to already be the target.
            Apply(robot, ((IRelevanceSource)this).IsRelevant(robot));
        }

        private void EnsureSubscribed()
        {
            if (_subscribed) return;
            // The tracker lives on the camera; it may not exist yet when
            // the first chassis registers, so resolve lazily.
            if (_tracker == null)
            {
                Camera cam = Camera.main;
                if (cam != null) _tracker = cam.GetComponent<TargetTracker>();
                if (_tracker == null) _tracker = FindAnyObjectByType<TargetTracker>();
            }
            if (_tracker != null)
            {
                _tracker.TargetChanged += OnTargetChanged;
                _subscribed = true;
            }
        }

        private void Update()
        {
            // Retry the subscription until the camera/tracker exists
            // (chassis can spawn before the camera rig in some scenes).
            if (!_subscribed) EnsureSubscribed();
        }

        private void OnTargetChanged(Robot previous, Robot next)
        {
            _target = next;
            // Drop the outline on the old target unless it's the player.
            if (previous != null && previous != _player)
                Apply(previous, false);
            // Raise it on the new target (player is already outlined).
            if (next != null && next != _player)
                Apply(next, true);
        }

        private static void Apply(Robot robot, bool outlined)
        {
            if (robot == null) return; // Unity-null safe (destroyed chassis)
            var ctrl = robot.GetComponent<ChassisOutlineController>();
            if (ctrl != null) ctrl.SetOutlined(outlined);
        }

        private void OnDestroy()
        {
            if (_subscribed && _tracker != null)
                _tracker.TargetChanged -= OnTargetChanged;
            if (s_instance == this) s_instance = null;
        }
    }
}
