using System.Collections.Generic;
using UnityEngine;

namespace Robogame.Core
{
    /// <summary>
    /// Runtime perf-bisect switchboard. Non-destructive and scene-only:
    /// nothing is deleted, no asset or production code path is changed.
    /// Each switch disables a whole subsystem so the F3 perf HUD shows
    /// its real cost, then restores it. Driven from the Esc settings
    /// panel (<c>SettingsHud</c> → "Perf Bisect" section).
    /// </summary>
    /// <remarks>
    /// Built to answer "the default Arena is slower than the PlanetArena —
    /// which Arena-only system is it?" empirically. The concrete
    /// Arena↔PlanetArena difference is the ~3–4 AI bots (0 on the
    /// planet); grass and dig are the other Arena-only GPU costs.
    /// <list type="bullet">
    ///   <item><description><b>Bots</b> — every chassis carrying a
    ///   Ground/AirBotInputSource. The local player has none, so it is
    ///   never touched.</description></item>
    ///   <item><description><b>Grass</b> — renderers using a Fluff
    ///   material (PERFORMANCE.md §5.3, the documented #1 GPU cost).</description></item>
    ///   <item><description><b>Dig</b> — the 36 <c>Chunk_*</c> mesh
    ///   renderers. DigZone may re-enable dug chunks on its next remesh,
    ///   so treat this as a coarse probe.</description></item>
    /// </list>
    /// Type-name / material-name matching keeps this decoupled from the
    /// gameplay asmdefs — it is a diagnostic, not a dependency. Auto-
    /// bootstraps so it is present in every scene with zero authoring.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PerfBisect : MonoBehaviour
    {
        public static PerfBisect Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("PerfBisect");
            DontDestroyOnLoad(go);
            go.AddComponent<PerfBisect>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // What we disabled, so restore touches exactly what we touched
        // (inactive objects/renderers can't be re-found by a fresh scan).
        private readonly List<GameObject> _hiddenBots = new();
        private readonly List<Renderer> _hiddenGrass = new();
        private readonly List<Renderer> _hiddenDig = new();

        public bool BotsOff { get; private set; }
        public bool GrassOff { get; private set; }
        public bool DigOff { get; private set; }

        // -----------------------------------------------------------------
        // Bots — chassis carrying a *BotInputSource (player has none).
        // -----------------------------------------------------------------

        public void SetBotsOff(bool off)
        {
            if (off == BotsOff) return;
            if (!off)
            {
                for (int i = 0; i < _hiddenBots.Count; i++)
                    if (_hiddenBots[i] != null) _hiddenBots[i].SetActive(true);
                _hiddenBots.Clear();
                BotsOff = false;
                Debug.Log("[PerfBisect] AI bots RESTORED.");
                return;
            }

            _hiddenBots.Clear();
            MonoBehaviour[] all = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                MonoBehaviour mb = all[i];
                if (mb == null) continue;
                string n = mb.GetType().Name;
                if (n != "GroundBotInputSource" && n != "AirBotInputSource") continue;
                GameObject root = mb.gameObject; // *BotInputSource sits on the Robot root
                if (!_hiddenBots.Contains(root)) _hiddenBots.Add(root);
            }
            for (int i = 0; i < _hiddenBots.Count; i++) _hiddenBots[i].SetActive(false);
            BotsOff = true;
            Debug.Log($"[PerfBisect] AI bots HIDDEN: {_hiddenBots.Count} chassis disabled.");
        }

        // -----------------------------------------------------------------
        // Fluff grass — renderers using a Fluff-shader/material.
        // -----------------------------------------------------------------

        public void SetGrassOff(bool off)
        {
            if (off == GrassOff) return;
            if (!off)
            {
                for (int i = 0; i < _hiddenGrass.Count; i++)
                    if (_hiddenGrass[i] != null) _hiddenGrass[i].enabled = true;
                _hiddenGrass.Clear();
                GrassOff = false;
                Debug.Log("[PerfBisect] Fluff grass RESTORED.");
                return;
            }

            _hiddenGrass.Clear();
            Renderer[] rs = FindObjectsByType<Renderer>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r == null || !r.enabled) continue;
                Material m = r.sharedMaterial;
                if (m == null) continue;
                bool isFluff =
                    (m.name != null && m.name.IndexOf("Fluff", System.StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (m.shader != null && m.shader.name != null &&
                     m.shader.name.IndexOf("Fluff", System.StringComparison.OrdinalIgnoreCase) >= 0);
                if (!isFluff) continue;
                r.enabled = false;
                _hiddenGrass.Add(r);
            }
            GrassOff = true;
            Debug.Log($"[PerfBisect] Fluff grass HIDDEN: {_hiddenGrass.Count} renderer(s) disabled.");
        }

        // -----------------------------------------------------------------
        // Dig chunks — MeshRenderers on "Chunk_*" objects (DigZone names
        // them Chunk_{x}_{y}_{z}).
        // -----------------------------------------------------------------

        public void SetDigOff(bool off)
        {
            if (off == DigOff) return;
            if (!off)
            {
                for (int i = 0; i < _hiddenDig.Count; i++)
                    if (_hiddenDig[i] != null) _hiddenDig[i].enabled = true;
                _hiddenDig.Clear();
                DigOff = false;
                Debug.Log("[PerfBisect] Dig chunk renderers RESTORED.");
                return;
            }

            _hiddenDig.Clear();
            Renderer[] rs = FindObjectsByType<Renderer>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r == null || !r.enabled) continue;
                if (!r.gameObject.name.StartsWith("Chunk_")) continue;
                r.enabled = false;
                _hiddenDig.Add(r);
            }
            DigOff = true;
            Debug.Log($"[PerfBisect] Dig chunk renderers HIDDEN: {_hiddenDig.Count} disabled.");
        }

        public void RestoreAll()
        {
            SetBotsOff(false);
            SetGrassOff(false);
            SetDigOff(false);
            Debug.Log("[PerfBisect] All systems RESTORED.");
        }
    }
}
