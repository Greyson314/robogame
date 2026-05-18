#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace Robogame.Network.Bootstrap
{
    /// <summary>
    /// Throwaway dev-only Host / Join overlay for Phase 1 MPPM loopback
    /// testing (NETCODE_PLAN §15 Phase 1). Compiled out of release builds.
    /// Deliberately separate from the gameplay F1 <c>DevHud</c> — this only
    /// drives <see cref="NetworkBootstrap"/> session start/stop and does not
    /// disturb existing dev tooling.
    /// </summary>
    /// <remarks>
    /// IMGUI string churn is irrelevant here (dev-only, not shipped, not a
    /// steady-state gameplay HUD — the PHYSICS_PLAN §1 no-alloc rule targets
    /// gameplay hot paths). Auto-bootstraps so no scene authoring is needed.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class NetDevHud : MonoBehaviour
    {
        private static GameObject s_root;

        private string _ip = "127.0.0.1";
        private string _port = NetworkBootstrap.DefaultPort.ToString();

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

        private void OnGUI()
        {
            NetworkBootstrap nb = NetworkBootstrap.Instance;
            if (nb == null) return;

            // Left edge, vertically centred — clear of the top-left FPS
            // counter and the top-right PerformanceHud (F3) so the
            // Host/Join buttons are always clickable during MPPM testing.
            const float w = 220f;
            const float h = 160f;
            GUILayout.BeginArea(new Rect(8f, Screen.height * 0.5f - h * 0.5f, w, h), GUI.skin.box);
            GUILayout.Label("<b>Netcode Dev (Phase 1)</b>", new GUIStyle(GUI.skin.label) { richText = true });

            if (nb.IsOnline)
            {
                GUILayout.Label($"Online — server:{nb.IsServer} client:{nb.IsClient} host:{nb.IsHost}");
                if (GUILayout.Button("Stop session")) nb.StopSession();
            }
            else
            {
                if (GUILayout.Button($"Host on {_port}"))
                    nb.StartHost(ParsePort());

                GUILayout.BeginHorizontal();
                _ip = GUILayout.TextField(_ip, GUILayout.Width(110f));
                _port = GUILayout.TextField(_port, GUILayout.Width(50f));
                GUILayout.EndHorizontal();

                if (GUILayout.Button($"Join {_ip}:{_port}"))
                    nb.StartClient(_ip, ParsePort());
            }

            GUILayout.EndArea();
        }

        private ushort ParsePort()
            => ushort.TryParse(_port, out ushort p) ? p : NetworkBootstrap.DefaultPort;
    }
}
#endif
