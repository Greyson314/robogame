using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Robogame.Gameplay
{
    /// <summary>
    /// Minimal pointer-hover relay for build-HUD help affordances (the
    /// "?" chips next to variant sliders). The owner wires
    /// <see cref="Show"/> / <see cref="Hide"/> to its shared tooltip
    /// surface; this component only reports enter/exit. Needs a
    /// raycast-target Graphic on the same GameObject to receive events.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HoverTip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public string Tip;
        public Action<string> Show;
        public Action Hide;

        public void OnPointerEnter(PointerEventData eventData) => Show?.Invoke(Tip);
        public void OnPointerExit(PointerEventData eventData) => Hide?.Invoke();

        private void OnDisable() => Hide?.Invoke();
    }
}
