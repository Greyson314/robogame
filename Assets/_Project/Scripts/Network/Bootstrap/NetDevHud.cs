#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.InputSystem;

namespace Robogame.Network.Bootstrap
{
    /// <summary>
    /// Throwaway dev-only Host / Join control for Phase 1 MPPM loopback
    /// testing (NETCODE_PLAN §15 Phase 1). Compiled out of release builds.
    /// </summary>
    /// <remarks>
    /// Actions are <b>hotkeys, not IMGUI buttons</b>: in the arena the
    /// cursor is locked, and FollowCamera's click-to-recapture path
    /// consumes clicks before IMGUI sees them, so an IMGUI button is a
    /// dead button (documented gotcha in architecture.md — same reason
    /// StartMatchHud uses a hotkey). The IMGUI panel is status / hint
    /// display only. F9 = Host, F10 = Join, F11 = Stop.
    /// </remarks>
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
            else if (kb.f11Key.wasPressedThisFrame && nb.IsOnline)
                nb.StopSession();
        }

        private void OnGUI()
        {
            NetworkBootstrap nb = NetworkBootstrap.Instance;
            if (nb == null) return;

            // Left edge, vertically centred — clear of the top-left FPS
            // counter and the top-right PerformanceHud (F3).
            const float w = 240f;
            const float h = 120f;
            GUILayout.BeginArea(new Rect(8f, Screen.height * 0.5f - h * 0.5f, w, h), GUI.skin.box);
            GUILayout.Label("<b>Netcode Dev (Phase 1)</b>",
                new GUIStyle(GUI.skin.label) { richText = true });

            if (nb.IsOnline)
            {
                GUILayout.Label($"Online — server:{nb.IsServer} " +
                                $"client:{nb.IsClient} host:{nb.IsHost}");
                GUILayout.Label("[F11] Stop session");
            }
            else
            {
                GUILayout.Label("Offline");
                GUILayout.Label($"[F9]  Host on {NetworkBootstrap.DefaultPort}");
                GUILayout.Label($"[F10] Join {Ip}:{NetworkBootstrap.DefaultPort}");
            }

            GUILayout.EndArea();
        }
    }
}
#endif
