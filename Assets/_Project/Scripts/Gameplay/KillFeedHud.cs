using System.Collections.Generic;
using Robogame.Core;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Persistent kill-feed strip — the running log of recent kills,
    /// counterpart to <see cref="KillAnnouncer"/>'s splashy centre-screen
    /// banner. The announcer is fire-and-forget juice; this is the
    /// readable history a player can glance at to know who just died
    /// (and to whom).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Subscribes to <see cref="MatchController.KillRegistered"/> and
    /// keeps a fixed-capacity ring of recent entries. Each entry stays
    /// at full opacity for <see cref="_holdSeconds"/> then fades out
    /// over <see cref="_fadeSeconds"/>; fully-faded entries are dropped
    /// from the ring on the next render pass.
    /// </para>
    /// <para>
    /// Auto-attached to the camera by <see cref="ArenaController.ConfigureCamera"/>
    /// alongside the other HUDs. SP-only today (reads MatchController
    /// directly). The networked counterpart lives behind a future
    /// <c>NetworkKillFeed</c> sibling under <c>Assets/_Project/Scripts/Network</c>
    /// that drives a server-authoritative event list — Phase 7 QoL.
    /// Designing against <see cref="MatchController.KillRegistered"/>
    /// keeps the HUD itself unchanged when the network sibling lands.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class KillFeedHud : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Pixels from the right edge to the panel.")]
        [SerializeField, Min(4f)] private float _rightMargin = 18f;

        [Tooltip("Pixels from the top edge to the topmost entry.")]
        [SerializeField, Min(4f)] private float _topOffset = 160f;

        [Tooltip("Width of each entry's pill in pixels.")]
        [SerializeField, Min(80f)] private float _entryWidth = 240f;

        [Tooltip("Height of each entry's pill in pixels.")]
        [SerializeField, Min(16f)] private float _entryHeight = 26f;

        [Tooltip("Vertical gap between stacked entries.")]
        [SerializeField, Min(0f)] private float _entrySpacing = 4f;

        [Tooltip("Maximum simultaneous entries shown. Older entries scroll off.")]
        [SerializeField, Min(2)] private int _maxEntries = 6;

        [Header("Lifetime")]
        [Tooltip("Seconds an entry stays at full opacity.")]
        [SerializeField, Min(0.5f)] private float _holdSeconds = 5.0f;

        [Tooltip("Seconds an entry takes to fade out after its hold expires.")]
        [SerializeField, Min(0.1f)] private float _fadeSeconds = 0.8f;

        [Header("Look")]
        [SerializeField, Min(8)] private int _fontSize = 14;

        // Subscribe state.
        private MatchController _match;
        private GUIStyle _style;
        // Ring-style buffer. We pre-size to _maxEntries so steady-state
        // logging is allocation-free; on overflow we drop the oldest
        // (List.RemoveAt(0) is O(N) but N <= ~8 so this is irrelevant).
        private readonly List<Entry> _entries = new(8);

        private struct Entry
        {
            public string Text;
            public Color Color;
            public float SpawnTime;
        }

        public void BindMatch(MatchController match)
        {
            if (_match != null) _match.KillRegistered -= HandleKill;
            _match = match;
            if (_match != null) _match.KillRegistered += HandleKill;
            _entries.Clear();
        }

        /// <summary>
        /// Switch to named-entry mode: the feed stops listening to
        /// <see cref="MatchController.KillRegistered"/> (side-level only)
        /// and instead renders whatever <see cref="PushKill"/> /
        /// <see cref="PushDeath"/> hand it. ArenaController uses this when
        /// a per-combatant stats tracker exists, so rows read
        /// "YOU → BOT 2" instead of "YOU → ENEMY". The side-subscription
        /// path stays for sandbox arenas with no tracker.
        /// </summary>
        public void BindNamedFeed()
        {
            if (_match != null) _match.KillRegistered -= HandleKill;
            _match = null;
            _entries.Clear();
        }

        /// <summary>Named kill entry — colour keyed to the killer's side.</summary>
        public void PushKill(string killerName, string victimName, MatchSide killerSide)
            => Push($"{killerName}  →  {victimName}", ColorFor(killerSide));

        /// <summary>Named unattributed death ("BOT 2 †") — environment / stale damage.</summary>
        public void PushDeath(string victimName)
            => Push($"{victimName}  †", HudStyles.TextMuted);

        private void OnDisable()
        {
            if (_match != null) _match.KillRegistered -= HandleKill;
        }

        private void HandleKill(MatchSide killer, MatchSide victim)
            => Push(FormatEntry(killer, victim), ColorFor(killer));

        private void Push(string text, Color color)
        {
            if (_entries.Count >= _maxEntries) _entries.RemoveAt(0);
            _entries.Add(new Entry
            {
                Text = text,
                Color = color,
                SpawnTime = Time.unscaledTime,
            });
        }

        private void OnGUI()
        {
            if (_entries.Count == 0) return;
            EnsureStyle();

            // Sweep faded-out entries before render. Walk back-to-front
            // so RemoveAt indices stay valid.
            float now = Time.unscaledTime;
            float totalLife = _holdSeconds + _fadeSeconds;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (now - _entries[i].SpawnTime > totalLife) _entries.RemoveAt(i);
            }
            if (_entries.Count == 0) return;

            float x = Screen.width - _entryWidth - _rightMargin;
            float y = _topOffset;
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                float age = now - e.SpawnTime;
                float alpha = age < _holdSeconds
                    ? 1f
                    : Mathf.Clamp01(1f - (age - _holdSeconds) / Mathf.Max(0.01f, _fadeSeconds));

                Rect bg = new Rect(x, y, _entryWidth, _entryHeight);
                Color saved = GUI.color;

                // Subtle panel background — matches HudStyles density so
                // the feed reads as a chrome strip not floating text.
                Color bgCol = HudStyles.PanelBg;
                bgCol.a *= alpha;
                GUI.color = bgCol;
                GUI.DrawTexture(bg, HudStyles.Pixel);

                // Accent left edge keyed to killer side — green for
                // player kills, red for enemy kills.
                Color edge = e.Color;
                edge.a *= alpha;
                GUI.color = edge;
                GUI.DrawTexture(new Rect(x, y, 3f, _entryHeight), HudStyles.Pixel);

                // Text. Same tint as the edge so player-kill rows read
                // green-on-dark, enemy-kill rows red-on-dark.
                Color text = e.Color;
                text.a *= alpha;
                _style.normal.textColor = text;
                Rect r = new Rect(x + 12f, y, _entryWidth - 16f, _entryHeight);
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Label(r, e.Text, _style);

                GUI.color = saved;
                y += _entryHeight + _entrySpacing;
            }
        }

        private void EnsureStyle()
        {
            if (_style != null) return;
            _style = HudStyles.Bold(_fontSize, HudStyles.TextPrimary, TextAnchor.MiddleLeft);
        }

        private static string FormatEntry(MatchSide killer, MatchSide victim)
        {
            string k = SideLabel(killer);
            string v = SideLabel(victim);
            // Environment / suicide reads cleanly as "VICTIM †" — no
            // arrow needed when there's no real killer.
            if (killer == MatchSide.None || killer == victim)
                return $"{v}  †";
            return $"{k}  →  {v}";
        }

        private static string SideLabel(MatchSide side) => side switch
        {
            MatchSide.Player => "You",
            MatchSide.Enemy => "Enemy",
            _ => "—",
        };

        private static Color ColorFor(MatchSide killer)
        {
            // Player kills tint green; enemy kills tint red; ambient
            // (environment) tints muted so it reads as background event.
            if (killer == MatchSide.Player) return HudStyles.Healthy;
            if (killer == MatchSide.Enemy) return HudStyles.Danger;
            return HudStyles.TextMuted;
        }
    }
}
