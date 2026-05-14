#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Robogame.Robots;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Editor / development-build cheat keys for fast iteration on the
    /// scrap loop. Compile-guarded behind <c>UNITY_EDITOR</c> /
    /// <c>DEVELOPMENT_BUILD</c> so a shipped player build can never
    /// trigger them.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>F2</b>: grant the local player 5 scrap. Tests the
    ///         carried-scrap → deposit → team-total path without
    ///         needing a fresh kill cycle.</item>
    ///   <item><b>F3</b>: teleport the local player to the team's
    ///         scrap depot. Saves a 30-second drive every test cycle.</item>
    ///   <item><b>F4</b>: push the player team to one scrap below the
    ///         target. Lets you walk one deposit onto the pad and see
    ///         the end-of-match overlay fire without grinding through
    ///         a full round.</item>
    /// </list>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ScrapDevCheats : MonoBehaviour
    {
        [Tooltip("Hotkey to grant the local player 5 scrap.")]
        [SerializeField] private Key _grantKey = Key.F2;

        [Tooltip("Hotkey to teleport the local player to the team scrap depot.")]
        [SerializeField] private Key _teleportKey = Key.F3;

        [Tooltip("Hotkey to push the player team score to (target - 1).")]
        [SerializeField] private Key _endgameKey = Key.F4;

        [Tooltip("Scrap awarded per F2 press.")]
        [SerializeField, Min(1)] private int _grantAmount = 5;

        private ArenaController _arena;

        public void Bind(ArenaController arena) => _arena = arena;

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || _arena == null) return;

            // Gate on locked cursor so cheats don't fire while the user
            // is interacting with a HUD panel (Settings, Build, etc.).
            if (Cursor.lockState != CursorLockMode.Locked) return;

            if (kb[_grantKey].wasPressedThisFrame) GrantScrap();
            if (kb[_teleportKey].wasPressedThisFrame) TeleportToDepot();
            if (kb[_endgameKey].wasPressedThisFrame) PushToEndgame();
        }

        private void GrantScrap()
        {
            Robot r = ResolvePlayerRobot();
            if (r == null) return;
            int awarded = r.TryAwardScrap(_grantAmount);
            Debug.Log($"[Robogame] DevCheat F2: granted {awarded} scrap (now {r.ScrapHeld}/{r.ScrapCarryCapacity}).", r);
        }

        private void TeleportToDepot()
        {
            Robot r = ResolvePlayerRobot();
            if (r == null) return;
            ScrapDepot depot = _arena.PlayerDepot;
            if (depot == null) { Debug.LogWarning("[Robogame] DevCheat F3: no player depot to teleport to."); return; }
            Vector3 dst = depot.transform.position + Vector3.up * 2f;
            Rigidbody rb = r.Rigidbody;
            if (rb != null)
            {
                rb.position = dst;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            else
            {
                r.transform.position = dst;
            }
            Debug.Log($"[Robogame] DevCheat F3: teleported player to depot at {dst}.", r);
        }

        private void PushToEndgame()
        {
            MatchController match = _arena.Match;
            if (match == null) { Debug.LogWarning("[Robogame] DevCheat F4: no match controller."); return; }
            int target = match.TargetTeamScrap;
            int current = match.ScoreForSide(MatchSide.Player);
            int delta = Mathf.Max(0, target - 1 - current);
            if (delta <= 0) { Debug.Log("[Robogame] DevCheat F4: already at endgame threshold."); return; }
            // Push directly into the team total. Won't trigger win — the
            // player still has to walk a 1-scrap deposit onto the depot
            // to test the victory path end-to-end.
            match.DepositScrap(MatchSide.Player, delta);
            Debug.Log($"[Robogame] DevCheat F4: pushed player to {target - 1}/{target}. Deposit any scrap to win.");
        }

        private Robot ResolvePlayerRobot()
        {
            if (_arena == null || _arena.Chassis == null) return null;
            return _arena.Chassis.GetComponent<Robot>();
        }
    }
}
#endif
