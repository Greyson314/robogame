using Robogame.Block;
using Robogame.Core;
using UnityEngine;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Garage panel that lets the player choose which active-module ability
    /// their chassis runs. Writes the selection onto
    /// <see cref="ChassisBlueprint.ActiveModuleKind"/> — frozen at match
    /// start (invariant #2). Renders nothing unless the current build
    /// actually contains an <see cref="BlockIds.ActiveModule"/> block.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ModuleSelectHud : MonoBehaviour
    {
        [SerializeField, Min(4f)] private float _margin = 18f;
        [SerializeField, Min(120f)] private float _panelWidth = 200f;
        [SerializeField, Min(8)] private int _fontSize = 14;

        private static readonly ModuleKind[] s_kinds =
            { ModuleKind.EmpBurst, ModuleKind.Blink, ModuleKind.DiscShield };

        private GUIStyle _titleStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _selectedStyle;

        private void OnGUI()
        {
            GameStateController state = GameStateController.Instance;
            ChassisBlueprint bp = state != null ? state.CurrentBlueprint : null;
            if (bp == null || !HasModuleBlock(bp)) return;

            _titleStyle ??= HudStyles.Bold(_fontSize, HudStyles.TextMuted);
            _buttonStyle ??= HudStyles.Label(_fontSize, HudStyles.TextPrimary, TextAnchor.MiddleCenter);
            _selectedStyle ??= HudStyles.Bold(_fontSize, HudStyles.Accent, TextAnchor.MiddleCenter);

            float rowH = 26f;
            float x = Screen.width - _panelWidth - _margin;
            float y = _margin;
            float panelH = rowH * (s_kinds.Length + 1) + 12f;

            Color prev = GUI.color;
            GUI.color = HudStyles.PanelBg;
            GUI.DrawTexture(new Rect(x, y, _panelWidth, panelH), HudStyles.Pixel);
            GUI.color = HudStyles.PanelEdge;
            GUI.DrawTexture(new Rect(x, y, _panelWidth, 2f), HudStyles.Pixel);
            GUI.color = prev;

            GUI.Label(new Rect(x + 10f, y + 6f, _panelWidth - 20f, rowH), "ACTIVE MODULE", _titleStyle);

            for (int i = 0; i < s_kinds.Length; i++)
            {
                ModuleKind kind = s_kinds[i];
                bool selected = bp.ActiveModuleKind == kind;
                var rect = new Rect(x + 10f, y + 6f + rowH * (i + 1), _panelWidth - 20f, rowH - 4f);
                if (GUI.Button(rect, Label(kind), selected ? _selectedStyle : _buttonStyle))
                {
                    bp.ActiveModuleKind = kind;
                    AudioRouter.PlayUI(AudioCue.UiClick);
                }
            }
        }

        private static bool HasModuleBlock(ChassisBlueprint bp)
        {
            ChassisBlueprint.Entry[] entries = bp.Entries;
            for (int i = 0; i < entries.Length; i++)
                if (entries[i].BlockId == BlockIds.ActiveModule) return true;
            return false;
        }

        private static string Label(ModuleKind kind) => kind switch
        {
            ModuleKind.EmpBurst => "EMP Burst",
            ModuleKind.Blink => "Blink",
            ModuleKind.DiscShield => "Disc Shield",
            _ => kind.ToString(),
        };
    }
}
