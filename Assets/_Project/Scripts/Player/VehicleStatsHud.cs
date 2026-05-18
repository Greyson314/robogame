using System.Text;
using Robogame.Block;
using Robogame.Combat;
using Robogame.Core;
using Robogame.Robots;
using UnityEngine;

namespace Robogame.Player
{
    /// <summary>
    /// Bottom-right corner readout of the local chassis: ground speed,
    /// altitude, and live block count. Speed is XZ-plane magnitude (driving
    /// or flying both read sensibly); altitude is metres above world Y=0;
    /// block count drops as the chassis takes damage so the player has a
    /// visible health surrogate.
    /// </summary>
    /// <remarks>
    /// Pulls the chassis from the sibling <see cref="FollowCamera"/>'s
    /// target, so respawns rebind for free without any extra wiring.
    /// IMGUI keeps it consistent with <see cref="AimReticle"/> /
    /// <see cref="HitMarkerOverlay"/>; UI Toolkit is the longer-term
    /// destination for all in-game HUD.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class VehicleStatsHud : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Pixels from the screen edge to the panel.")]
        [SerializeField, Min(4f)] private float _margin = 18f;

        [Tooltip("Panel width in pixels.")]
        [SerializeField, Min(80f)] private float _panelWidth = 220f;

        [Tooltip("Panel height in pixels.")]
        [SerializeField, Min(40f)] private float _panelHeight = 152f;

        [Header("Look")]
        [Tooltip("Background colour. Defaults wired from HudStyles.PanelBg at first OnGUI.")]
        [SerializeField] private Color _bgColor = new Color(0f, 0f, 0f, 0.55f);
        [SerializeField, Min(8)] private int _fontSize = 17;

        [Header("Filtering")]
        [Tooltip("Smoothing window (s) on the speed readout. 0 = raw, ~0.1 = stable while still responsive.")]
        [SerializeField, Range(0f, 0.5f)] private float _speedSmoothing = 0.08f;

        private FollowCamera _follow;
        private Rigidbody _rb;
        private Robot _robot;
        private WeaponAmmoState _ammo;
        private Transform _target;
        private int _maxBlockCount;
        private float _displaySpeed;
        private float _smoothVel;
        private readonly StringBuilder _ammoLine = new();

        // Cached display strings + style-selection flags, rebuilt once per
        // frame in Update so OnGUI (2-6 events/frame) only renders. The
        // int-driven rows are change-gated; speed/alt are formatted to one
        // decimal so their quantised value is tracked, not the raw float.
        private string _speedText = "SPD  --";
        private string _altText = "ALT  --";
        private string _blocksText = "BLK  --";
        private string _scrapText = "SCR  --";
        private string _ammoText = "AMO  —";
        private int _lastSpeedDeci = int.MinValue;
        private int _lastAltDeci = int.MinValue;
        private int _lastBlocks = int.MinValue;
        private int _lastMaxBlocks = int.MinValue;
        private int _lastScrap = int.MinValue;
        private int _lastScrapCap = int.MinValue;
        private bool _damaged;
        private bool _scrapFull;

        // Reuse GUIStyles so OnGUI doesn't allocate per draw.
        private GUIStyle _labelStyle;
        private GUIStyle _damageLabelStyle;

        private void Awake()
        {
            _follow = GetComponent<FollowCamera>();
        }

        private void Update()
        {
            // Rebind when the chassis swaps. _target is identity-compared so
            // a brand-new GameObject (e.g. respawn) triggers re-resolution.
            Transform t = _follow != null ? _follow.Target : null;
            if (t != _target)
            {
                _target = t;
                _rb = _target != null ? _target.GetComponent<Rigidbody>() : null;
                _robot = _target != null ? _target.GetComponent<Robot>() : null;
                _ammo = _target != null ? _target.GetComponent<WeaponAmmoState>() : null;
                // Capture the chassis's full block count on respawn so the
                // BLOCKS line can render N/Max. Counts include every cell in
                // the BlockGrid; foils adopted under a kinematic rotor hub
                // still belong to the grid logically, so they're counted.
                _maxBlockCount = _robot != null ? _robot.BlockCount : 0;
            }

            if (_rb == null) return;
            Vector3 v = _rb.linearVelocity;
            v.y = 0f; // ground speed (XZ magnitude); altitude carries the vertical info.
            float instant = v.magnitude;
            if (_speedSmoothing <= 0f)
            {
                _displaySpeed = instant;
                _smoothVel = 0f;
            }
            else
            {
                _displaySpeed = Mathf.SmoothDamp(_displaySpeed, instant, ref _smoothVel, _speedSmoothing, Mathf.Infinity, Time.unscaledDeltaTime);
            }

            BuildDisplayStrings();
        }

        // Rebuild only the rows whose displayed value actually changed —
        // same dirty-string discipline as FpsCounter. Speed/alt are tracked
        // at the 0.1-unit resolution they're rendered at.
        private void BuildDisplayStrings()
        {
            int speedDeci = Mathf.RoundToInt(_displaySpeed * 10f);
            if (speedDeci != _lastSpeedDeci)
            {
                _lastSpeedDeci = speedDeci;
                _speedText = $"SPD  {_displaySpeed:F1} m/s";
            }

            int altDeci = Mathf.RoundToInt((_target != null ? _target.position.y : 0f) * 10f);
            if (altDeci != _lastAltDeci)
            {
                _lastAltDeci = altDeci;
                _altText = $"ALT  {altDeci / 10f:F1} m";
            }

            int blocks = _robot != null ? _robot.BlockCount : 0;
            if (blocks != _lastBlocks || _maxBlockCount != _lastMaxBlocks)
            {
                _lastBlocks = blocks;
                _lastMaxBlocks = _maxBlockCount;
                _blocksText = _maxBlockCount > 0
                    ? $"BLK  {blocks} / {_maxBlockCount}"
                    : $"BLK  {blocks}";
            }
            _damaged = _maxBlockCount > 0 && blocks < _maxBlockCount;

            int scrap = _robot != null ? _robot.ScrapHeld : 0;
            int scrapCap = _robot != null ? _robot.ScrapCarryCapacity : 0;
            _scrapFull = scrapCap > 0 && scrap >= scrapCap;
            if (scrap != _lastScrap || scrapCap != _lastScrapCap)
            {
                _lastScrap = scrap;
                _lastScrapCap = scrapCap;
                _scrapText = scrapCap > 0
                    ? (_scrapFull ? $"SCR  {scrap} / {scrapCap}  FULL" : $"SCR  {scrap} / {scrapCap}")
                    : $"SCR  {scrap}";
            }

            BuildAmmoLine();
            _ammoText = _ammoLine.ToString();
        }

        private void OnGUI()
        {
            if (_target == null || _rb == null) return;

            if (_labelStyle == null)
            {
                _labelStyle = HudStyles.Bold(_fontSize, HudStyles.Accent, TextAnchor.MiddleLeft);
                _damageLabelStyle = HudStyles.Bold(_fontSize, HudStyles.Danger, TextAnchor.MiddleLeft);
            }

            float x = Screen.width - _panelWidth - _margin;
            float y = Screen.height - _panelHeight - _margin;
            Rect bg = new Rect(x, y, _panelWidth, _panelHeight);
            Color prev = GUI.color;
            GUI.color = _bgColor;
            GUI.DrawTexture(bg, HudStyles.Pixel);
            // Accent top edge — matches the scoreboard chrome so the
            // bottom-right corner reads as the same UI family.
            GUI.color = HudStyles.PanelEdge;
            GUI.DrawTexture(new Rect(x, y, _panelWidth, 2f), HudStyles.Pixel);
            GUI.color = prev;

            float rowH = _panelHeight / 5f;
            // Style selection only — all text is built once per frame in
            // Update (BuildDisplayStrings). BLOCKS row reds out on integrity
            // loss; SCRAP row reds out at carry cap.
            GUIStyle blockStyle = _damaged ? _damageLabelStyle : _labelStyle;
            GUIStyle scrapStyle = _scrapFull ? _damageLabelStyle : _labelStyle;
            GUIStyle ammoStyle = _ammoAnyEmpty ? _damageLabelStyle : _labelStyle;

            Rect speedRect  = new Rect(x + 12f, y + 2f,             _panelWidth - 24f, rowH);
            Rect altRect    = new Rect(x + 12f, y + rowH,           _panelWidth - 24f, rowH);
            Rect blocksRect = new Rect(x + 12f, y + rowH * 2f - 2f, _panelWidth - 24f, rowH);
            Rect scrapRect  = new Rect(x + 12f, y + rowH * 3f - 4f, _panelWidth - 24f, rowH);
            Rect ammoRect   = new Rect(x + 12f, y + rowH * 4f - 6f, _panelWidth - 24f, rowH);
            GUI.Label(speedRect,  _speedText,  _labelStyle);
            GUI.Label(altRect,    _altText,    _labelStyle);
            GUI.Label(blocksRect, _blocksText, blockStyle);
            GUI.Label(scrapRect,  _scrapText,  scrapStyle);
            GUI.Label(ammoRect,   _ammoText,   ammoStyle);
        }

        private bool _ammoAnyEmpty;

        private void BuildAmmoLine()
        {
            _ammoLine.Clear();
            _ammoLine.Append("AMO  ");
            _ammoAnyEmpty = false;
            if (_ammo == null)
            {
                _ammoLine.Append("—");
                return;
            }
            bool first = true;
            foreach (var kvp in _ammo.EnumeratePools())
            {
                if (!first) _ammoLine.Append(" · ");
                first = false;
                string shortName = ShortenWeaponId(kvp.Key);
                int cur = kvp.Value.current;
                int max = kvp.Value.max;
                if (cur <= 0) _ammoAnyEmpty = true;
                if (kvp.Value.reloading)
                {
                    _ammoLine.Append(shortName).Append(' ').Append('R');
                }
                else
                {
                    _ammoLine.Append(shortName).Append(' ').Append(cur).Append('/').Append(max);
                }
            }
            if (first) _ammoLine.Append("—");
        }

        // Display abbreviations — keeps the ammo row narrow when the
        // chassis sports multiple weapon types.
        private static string ShortenWeaponId(string blockId) => blockId switch
        {
            BlockIds.Weapon  => "SMG",
            BlockIds.Cannon  => "CAN",
            BlockIds.BombBay => "BMB",
            _ => blockId,
        };
    }
}
