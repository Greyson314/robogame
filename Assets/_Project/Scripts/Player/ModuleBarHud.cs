using Robogame.Block;
using Robogame.Combat;
using Robogame.Core;
using UnityEngine;

namespace Robogame.Player
{
    /// <summary>
    /// In-arena MOBA-style ability bar: a bottom-centre row of tiles, one per
    /// module slot the chassis carries (up to <see cref="ModuleBudget.MaxModules"/>).
    /// Each tile shows the ability name, its 1/2/3/4 keybind, a cooldown fill
    /// that recharges from the bottom, and a greyed state when the ability
    /// can't fire — on cooldown, contextually blocked (spring while airborne),
    /// or the carrier block destroyed. Hidden entirely when the chassis has no
    /// live module.
    /// </summary>
    /// <remarks>
    /// Sits on the main-camera GameObject next to <see cref="VehicleStatsHud"/>
    /// and resolves its chassis the same way — through the shared
    /// <see cref="FollowCamera"/> target — so a respawn re-binds for free.
    /// IMGUI keeps it consistent with the other Player HUDs.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ModuleBarHud : MonoBehaviour
    {
        [SerializeField, Min(32f)] private float _tileSize = 60f;
        [SerializeField, Min(0f)] private float _tileGap = 8f;
        [SerializeField, Min(0f)] private float _bottomOffset = 18f;
        [SerializeField, Min(8)] private int _nameFontSize = 13;
        [SerializeField, Min(8)] private int _keyFontSize = 12;

        private FollowCamera _follow;
        private Transform _target;
        private ModuleSystem _module;

        private GUIStyle _nameStyle;
        private GUIStyle _nameDimStyle;
        private GUIStyle _keyStyle;
        private GUIStyle _cdStyle;

        private void Awake()
        {
            _follow = GetComponent<FollowCamera>();
        }

        private void Update()
        {
            Transform t = _follow != null ? _follow.Target : null;
            if (t == _target) return;
            _target = t;
            _module = _target != null ? _target.GetComponent<ModuleSystem>() : null;
        }

        private void OnGUI()
        {
            if (_module == null || !_module.HasAnyModule) return;

            EnsureStyles();

            var slots = _module.Slots;
            int n = slots.Count;
            float totalW = n * _tileSize + (n - 1) * _tileGap;
            float startX = (Screen.width - totalW) * 0.5f;
            float y = Screen.height - _bottomOffset - _tileSize;

            Color prev = GUI.color;
            for (int i = 0; i < n; i++)
            {
                ModuleSystem.Slot slot = slots[i];
                float x = startX + i * (_tileSize + _tileGap);
                DrawTile(new Rect(x, y, _tileSize, _tileSize), slot, i);
            }
            GUI.color = prev;
        }

        private void DrawTile(Rect r, ModuleSystem.Slot slot, int index)
        {
            bool hasBlock = slot.HasBlock;
            float frac = slot.ReadyFraction;
            bool ready = slot.IsAvailable;
            // Off cooldown but still not usable = contextually blocked
            // (spring while airborne). Distinguish from a cooling tile.
            bool contextBlocked = hasBlock && frac >= 1f && !ready;

            // Tile background.
            GUI.color = HudStyles.PanelBg;
            GUI.DrawTexture(r, HudStyles.Pixel);

            // Cooldown fill: a bright bar rising from the bottom as it
            // recharges. Empty (dark) tile = just fired.
            if (hasBlock && frac < 1f)
            {
                float fillH = r.height * frac;
                GUI.color = new Color(HudStyles.Accent.r, HudStyles.Accent.g, HudStyles.Accent.b, 0.22f);
                GUI.DrawTexture(new Rect(r.x, r.yMax - fillH, r.width, fillH), HudStyles.Pixel);
            }

            // Border: bright accent when ready, caution when context-blocked,
            // dim otherwise.
            Color border = !hasBlock ? HudStyles.TextMuted
                : ready ? HudStyles.Healthy
                : contextBlocked ? HudStyles.Warning
                : HudStyles.PanelEdge;
            DrawBorder(r, border, 2f);

            // Keybind chip (top-left).
            GUI.color = Color.white;
            GUI.Label(new Rect(r.x + 4f, r.y + 2f, 18f, 16f), ModuleSystem.KeyLabel(index), _keyStyle);

            // Ability name (centre).
            string name = hasBlock ? ModuleKinds.Label(slot.Kind) : "—";
            GUIStyle nameStyle = ready ? _nameStyle : _nameDimStyle;
            GUI.Label(new Rect(r.x, r.y + r.height * 0.32f, r.width, r.height * 0.4f), name, nameStyle);

            // Cooldown seconds (bottom) while recharging.
            if (hasBlock && frac < 1f)
            {
                int secs = Mathf.CeilToInt((1f - frac) * slot.CooldownDuration);
                GUI.color = Color.white;
                GUI.Label(new Rect(r.x, r.yMax - 18f, r.width, 16f), secs.ToString(), _cdStyle);
            }
            else if (contextBlocked)
            {
                GUI.color = Color.white;
                GUI.Label(new Rect(r.x, r.yMax - 18f, r.width, 16f), "AIR", _cdStyle);
            }
        }

        private static void DrawBorder(Rect r, Color color, float t)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), HudStyles.Pixel);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), HudStyles.Pixel);
            GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), HudStyles.Pixel);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), HudStyles.Pixel);
            GUI.color = prev;
        }

        private void EnsureStyles()
        {
            _nameStyle ??= HudStyles.Bold(_nameFontSize, HudStyles.TextPrimary, TextAnchor.MiddleCenter);
            _nameDimStyle ??= HudStyles.Bold(_nameFontSize, HudStyles.TextMuted, TextAnchor.MiddleCenter);
            _keyStyle ??= HudStyles.Bold(_keyFontSize, HudStyles.Accent, TextAnchor.MiddleLeft);
            _cdStyle ??= HudStyles.Bold(_keyFontSize, HudStyles.TextPrimary, TextAnchor.MiddleCenter);
        }
    }
}
