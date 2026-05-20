#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Multiplayer.Tools.NetworkSimulator.Runtime;
using UnityEngine;

namespace Robogame.Network.Diagnostics
{
    /// <summary>
    /// Phase-3.6 latency / jitter / loss injection (NETCODE_PLAN §15 / §16) —
    /// wraps Multiplayer Tools' <see cref="NetworkSimulator"/> behind a tiny
    /// preset-cycle API so a single hotkey on <c>NetDevHud</c> can pin the
    /// transport to LAN / 100ms / 200ms / 200ms+jitter+loss for qualitative
    /// MPPM testing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists.</b> UTP 2.x's <c>SetDebugSimulatorParameters</c> is
    /// <c>[Obsolete]</c>; the supported replacement is Multiplayer Tools'
    /// <c>NetworkSimulator</c> component, which discovers any registered
    /// <c>IHandleNetworkParameters</c> adapter (UTP2 ships one) via the global
    /// <c>NetworkAdapters</c> registry — so this controller does not need to
    /// sit on the same GameObject as <c>UnityTransport</c>. It rides
    /// <c>[NetDevHud]</c>'s persistent DontDestroyOnLoad root.
    /// </para>
    /// <para>
    /// <b>Preset matrix.</b> The four entries below match §16's qualitative
    /// matrix: 0 ms baseline, 100 ms RTT, 200 ms RTT, and a worst-case combo
    /// with jitter + ~5% loss. NetworkSimulator uses one-way delay, so RTT is
    /// 2 × <c>PacketDelayMs</c>: 50 ms one-way = 100 ms RTT.
    /// </para>
    /// <para>
    /// <b>Compiled out of release builds</b> — same gate as <c>NetDevHud</c>.
    /// Editor + DEVELOPMENT_BUILD only; the package + this file disappear in
    /// shipping configurations.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class NetcodeFakeLatencyController : MonoBehaviour
    {
        /// <summary>Preset matrix from NETCODE_PLAN §16. Index 0 = off / LAN;
        /// the cycle hotkey on <see cref="Bootstrap.NetDevHud"/> walks the
        /// array forward.</summary>
        public static readonly NetworkSimulatorPreset[] Presets =
        {
            NetworkSimulatorPreset.Create(
                "LAN (loopback)",
                "Zero injected latency — Phase-1 / Phase-3 lite baseline."),
            NetworkSimulatorPreset.Create(
                "Test 100 ms RTT",
                "50 ms one-way (= 100 ms RTT), no jitter, no loss.",
                packetDelayMs: 50),
            NetworkSimulatorPreset.Create(
                "Test 200 ms RTT",
                "100 ms one-way (= 200 ms RTT), no jitter, no loss.",
                packetDelayMs: 100),
            NetworkSimulatorPreset.Create(
                "Test 200 ms + jitter + 5% loss",
                "Worst-case from §16 matrix. RTT 200 ms ±60 ms, ~5% loss.",
                packetDelayMs: 100,
                packetJitterMs: 30,
                packetLossPercent: 5),
        };

        private static NetcodeFakeLatencyController s_instance;
        private NetworkSimulator _sim;
        private int _activeIndex;

        public static NetcodeFakeLatencyController Instance => s_instance;

        /// <summary>Index into <see cref="Presets"/> for the currently-applied
        /// preset. 0 = LAN baseline.</summary>
        public int ActivePresetIndex => _activeIndex;

        /// <summary>Display name of the currently-applied preset.</summary>
        public string ActivePresetName => Presets[_activeIndex].Name;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_instance = null;

        /// <summary>Idempotent — attaches the controller (and an underlying
        /// <see cref="NetworkSimulator"/>) to <paramref name="host"/> if it
        /// isn't already, and returns the singleton.</summary>
        public static NetcodeFakeLatencyController EnsureAttached(GameObject host)
        {
            if (s_instance != null) return s_instance;
            if (host == null) return null;
            s_instance = host.GetComponent<NetcodeFakeLatencyController>()
                      ?? host.AddComponent<NetcodeFakeLatencyController>();
            return s_instance;
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(this);
                return;
            }
            s_instance = this;

            // Place the simulator on the same GameObject so it shares the
            // DontDestroyOnLoad lifetime. NetworkSimulator binds to network
            // adapters via the global NetworkAdapters registry, so it does
            // not need to co-locate with UnityTransport.
            _sim = gameObject.GetComponent<NetworkSimulator>()
                ?? gameObject.AddComponent<NetworkSimulator>();
            _sim.ConnectionPreset = Presets[_activeIndex];
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }

        public void SetPreset(int index)
        {
            if (index < 0 || index >= Presets.Length) return;
            _activeIndex = index;
            if (_sim != null) _sim.ConnectionPreset = Presets[index];
        }

        /// <summary>Advance to the next preset in the matrix (wraps).</summary>
        public void CyclePreset() => SetPreset((_activeIndex + 1) % Presets.Length);
    }
}
#endif
