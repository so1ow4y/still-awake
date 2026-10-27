using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StillAwake
{
    /// <summary>Слот хотбара: клик — использовать предмет, перетаскивание — применить к объекту.</summary>
    public sealed class InventorySlot : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        public InventoryView View;
        public int Index;
        public Image Border, Icon;
        public Text Label;

        string Item => View.ItemAt(Index);

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left && !e.dragging && Item != null) View.Use(Item);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (Item != null) View.BeginDrag(Item, Icon.sprite, e.position);
        }

        public void OnDrag(PointerEventData e) => View.MoveDrag(e.position);

        public void OnEndDrag(PointerEventData e) => View.EndDrag(e.position);

        public void OnPointerEnter(PointerEventData e) => View.SetHoverBorder(this, true);

        public void OnPointerExit(PointerEventData e) => View.SetHoverBorder(this, false);
    }
}
