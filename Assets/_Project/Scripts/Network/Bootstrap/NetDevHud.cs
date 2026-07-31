#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Robogame.Network.Diagnostics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Robogame.Network.Bootstrap
{
    /// <summary>
    /// Throwaway dev-only Host / Join control for Phase 1 MPPM loopback
    /// testing (NETCODE_PLAN §15 Phase 1) + Phase 3.6 latency-injection
    /// cycle (§15 / §16). Compiled out of release builds.
    /// </summary>
    /// <remarks>
    /// Actions are <b>hotkeys, not IMGUI buttons</b>: in the arena the
    /// cursor is locked, and FollowCamera's click-to-recapture path
    /// consumes clicks before IMGUI sees them, so an IMGUI button is a
    /// dead button (documented gotcha in architecture.md — same reason
    /// StartMatchHud uses a hotkey). Display is a single status line
    /// docked under the FPS counter — the old 260×170 left-edge panel
    /// sat vertically centred and buried the garage's UGUI button stack
    /// on shorter game views (IMGUI draws over UGUI).
    /// F9 = Host, F10 = Join, F8 = Server
    /// (Phase 6 dedicated, no local player), F11 = Stop, F5 = cycle the
    /// <see cref="NetcodeFakeLatencyController"/> preset.
    /// </remarks>
    // TRACE[LOG-128]: docked layout — the old panel buried garage UGUI buttons.
    // TRACE[DOC:hud-layout]: placement follows the HUD layout doc.
    [DisallowMultipleComponent]
    public sealed class NetDevHud : MonoBehaviour
    {
        private const string Ip = "127.0.0.1";

        private static GameObject s_root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_root = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrap()
        {
            if (s_root != null) return;
            s_root = new GameObject("[NetDevHud]");
            DontDestroyOnLoad(s_root);
            s_root.AddComponent<NetDevHud>();
            // Co-locate the fake-latency controller on the same DontDestroyOnLoad
            // root so a single GameObject carries every dev-only netcode helper.
            // NetworkSimulator (the underlying Multiplayer Tools component) binds
            // via the global NetworkAdapters registry, so co-location with the
            // transport is not required — sharing the HUD root keeps F5 cycling
            // and the IMGUI status line one component apart.
            NetcodeFakeLatencyController.EnsureAttached(s_root);
        }

        private void Update()
        {
            NetworkBootstrap nb = NetworkBootstrap.Instance;
            Keyboard kb = Keyboard.current;
            if (nb == null || kb == null) return;

            if (kb.f9Key.wasPressedThisFrame && !nb.IsOnline)
                nb.StartHost(NetworkBootstrap.DefaultPort);
            else if (kb.f10Key.wasPressedThisFrame && !nb.IsOnline)
                nb.StartClient(Ip, NetworkBootstrap.DefaultPort);
            else if (kb.f8Key.wasPressedThisFrame && !nb.IsOnline)
                nb.StartServer(NetworkBootstrap.DefaultPort);
            else if (kb.f11Key.wasPressedThisFrame && nb.IsOnline)
                nb.StopSession();
            else if (kb.f5Key.wasPressedThisFrame)
                NetcodeFakeLatencyController.Instance?.CyclePreset();
        }

        // Cached status line — rebuilt only when the underlying state
        // changes so held-open OnGUI repaints stay allocation-free.
        // TRACE[INV-6]
        private string _line = string.Empty;
        private bool _lastOnline;
        private bool _lastServer;
        private bool _lastClient;
        private bool _lastHost;
        private string _lastPreset;
        private GUIStyle _style;

        private void OnGUI()
        {
            NetworkBootstrap nb = NetworkBootstrap.Instance;
            if (nb == null) return;

            NetcodeFakeLatencyController lat = NetcodeFakeLatencyController.Instance;
            string preset = lat != null ? lat.ActivePresetName : null;
            if (_line.Length == 0
                || nb.IsOnline != _lastOnline || nb.IsServer != _lastServer
                || nb.IsClient != _lastClient || nb.IsHost != _lastHost
                || preset != _lastPreset)
            {
                _lastOnline = nb.IsOnline;
                _lastServer = nb.IsServer;
                _lastClient = nb.IsClient;
                _lastHost = nb.IsHost;
                _lastPreset = preset;
                string latPart = preset != null ? $"  ·  F5 lat: {preset}" : string.Empty;
                _line = nb.IsOnline
                    ? $"NET online  srv:{(nb.IsServer ? 1 : 0)} cli:{(nb.IsClient ? 1 : 0)} host:{(nb.IsHost ? 1 : 0)}  ·  F11 stop{latPart}"
                    : $"NET offline  ·  F9 host  ·  F10 join {Ip}  ·  F8 server  @{NetworkBootstrap.DefaultPort}{latPart}";
            }

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                };
            }

            // One line docked under the top-left FPS counter (y 8–32).
            // Drop shadow for readability against bright skies — same
            // treatment as FpsCounter.
            Rect shadow = new Rect(9f, 35f, 720f, 20f);
            Rect main = new Rect(8f, 34f, 720f, 20f);
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(shadow, _line, _style);
            GUI.color = new Color(0.75f, 0.85f, 0.95f, 0.9f);
            GUI.Label(main, _line, _style);
            GUI.color = prev;
        }
    }
}
#endif
