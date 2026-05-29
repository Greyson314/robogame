using Robogame.Combat;
using Robogame.Core;
using UnityEngine;

namespace Robogame.Player
{
    /// <summary>
    /// Small in-arena indicator for the chassis's active-module ability:
    /// the ability name + a cooldown fill bar that empties as the ability
    /// recharges. Hidden entirely when the chassis carries no (live) module.
    /// </summary>
    /// <remarks>
    /// Sits on the main-camera GameObject next to <see cref="VehicleStatsHud"/>
    /// and resolves its chassis the same way — through the shared
    /// <see cref="FollowCamera"/> target — so a respawn re-binds for free.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ModuleHud : MonoBehaviour
    {
        [SerializeField, Min(4f)] private float _margin = 18f;
        [SerializeField, Min(80f)] private float _panelWidth = 220f;
        [SerializeField, Min(20f)] private float _panelHeight = 40f;
        [SerializeField, Min(8)] private int _fontSize = 15;

        // Pixels above the bottom edge — sits clear of the stats panel which
        // hugs the bottom-left. This one rides the bottom-centre.
        [SerializeField, Min(0f)] private float _bottomOffset = 18f;

        private FollowCamera _follow;
        private Transform _target;
        private ActiveModuleSystem _module;
        private GUIStyle _labelStyle;

        private void Awake()
        {
            _follow = GetComponent<FollowCamera>();
        }

        private void Update()
        {
            Transform t = _follow != null ? _follow.Target : null;
            if (t == _target) return;
            _target = t;
            _module = _target != null ? _target.GetComponent<ActiveModuleSystem>() : null;
        }

        private void OnGUI()
        {
            if (_module == null || !_module.HasModule) return;

            _labelStyle ??= HudStyles.Bold(_fontSize, HudStyles.TextPrimary, TextAnchor.MiddleCenter);

            float x = (Screen.width - _panelWidth) * 0.5f;
            float y = Screen.height - _bottomOffset - _panelHeight;

            // Panel chrome.
            Color prev = GUI.color;
            GUI.color = HudStyles.PanelBg;
            GUI.DrawTexture(new Rect(x, y, _panelWidth, _panelHeight), HudStyles.Pixel);
            GUI.color = HudStyles.PanelEdge;
            GUI.DrawTexture(new Rect(x, y, _panelWidth, 2f), HudStyles.Pixel);

            // Cooldown fill.
            float frac = _module.ReadyFraction;
            float barH = 8f;
            float barY = y + _panelHeight - barH - 4f;
            float barW = _panelWidth - 16f;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x + 8f, barY, barW, barH), HudStyles.Pixel);
            GUI.color = frac >= 1f ? HudStyles.Healthy : HudStyles.Accent;
            GUI.DrawTexture(new Rect(x + 8f, barY, barW * frac, barH), HudStyles.Pixel);
            GUI.color = prev;

            string name = ModuleLabel(_module.ModuleKindOrNull);
            string status = frac >= 1f ? "READY [Q]" : $"{Mathf.CeilToInt((1f - frac) * 100f)}%";
            GUI.Label(new Rect(x + 8f, y + 2f, _panelWidth - 16f, 20f), $"{name}   {status}", _labelStyle);
        }

        private static string ModuleLabel(Block.ModuleKind? kind)
        {
            return kind switch
            {
                Block.ModuleKind.EmpBurst => "EMP BURST",
                Block.ModuleKind.Blink => "BLINK",
                Block.ModuleKind.DiscShield => "DISC SHIELD",
                _ => "MODULE",
            };
        }
    }
}
