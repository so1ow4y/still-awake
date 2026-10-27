using System;
using UnityEngine;
using UnityEngine.UI;

namespace StillAwake
{
    /// <summary>
    /// Инвентарь-хотбар, как в Minecraft: ряд небольших слотов внизу по центру. Клик по предмету — использовать,
    /// перетащить на объект в комнате — применить к нему. Наведение — название и описание.
    /// Иконки — Items/&lt;id&gt;.png; без них — название в слоте.
    /// </summary>
    public sealed class InventoryView : MonoBehaviour
    {
        public const int SlotCount = 6;
        const float SlotSize = 84f;
        const float Gap = 6f;

        static readonly Color SlotColor = new Color(0.08f, 0.08f, 0.1f, 0.78f);
        static readonly Color BorderColor = new Color(1f, 1f, 1f, 0.22f);
        static readonly Color HoverBorderColor = new Color(1f, 0.85f, 0.45f, 0.9f);
        static readonly Color MissingIconColor = new Color(0.75f, 0.75f, 0.78f);

        public bool IsVisible => bar.gameObject.activeSelf;
        public bool IsDragging => dragging != null;
        /// <summary>Курсор над хотбаром — камера не двигается.</summary>
        public bool PointerOver => IsVisible && RectTransformUtility.RectangleContainsScreenPoint(bar, GameInput.PointerPosition, null);

        public event Action<string> UseRequested;
        /// <summary>Предмет отпустили над экранной точкой (на объекте комнаты или мимо).</summary>
        public event Action<string, Vector2> Dropped;
        public event Action<string> DragStarted;
        /// <summary>Каждый кадр перетаскивания — чтобы подсветить объект под предметом.</summary>
        public event Action<Vector2> Dragging;
        public event Action DragEnded;

        Inventory inventory;
        RectTransform canvas, bar, tooltip;
        Text tooltipText;
        Image ghost;
        readonly InventorySlot[] slots = new InventorySlot[SlotCount];
        string dragging;

        internal string ItemAt(int index) => index < inventory.Items.Count ? inventory.Items[index] : null;

        internal void Use(string id) => UseRequested?.Invoke(id);

        internal void SetHoverBorder(InventorySlot slot, bool hovered)
        {
            slot.Border.color = hovered ? HoverBorderColor : BorderColor;
            string id = ItemAt(slot.Index);
            if (hovered && id != null) ShowTooltip(id, (RectTransform)slot.transform);
            else tooltip.gameObject.SetActive(false);
        }

        public static InventoryView Create(RectTransform canvas, Inventory inventory)
        {
            RectTransform root = Ui.Stretch(Ui.Node("Inventory", canvas));
            var view = root.gameObject.AddComponent<InventoryView>();
            view.inventory = inventory;
            view.canvas = canvas;
            view.Build(root);
            inventory.Changed += view.Refresh;
            view.Refresh();
            return view;
        }

        public void Toggle() => SetVisible(!IsVisible);

        public void SetVisible(bool visible)
        {
            if (!visible) CancelDrag();
            bar.gameObject.SetActive(visible);
            tooltip.gameObject.SetActive(false);
        }

        void Build(RectTransform root)
        {
            float width = SlotCount * SlotSize + (SlotCount - 1) * Gap;
            bar = Ui.Node("Hotbar", root);
            Ui.Place(bar, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(width, SlotSize));

            for (int i = 0; i < SlotCount; i++)
            {
                Image border = Ui.Image("Slot " + (i + 1), bar, BorderColor, null, raycast: true);
                Ui.Place(border.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(i * (SlotSize + Gap), 0f), new Vector2(SlotSize, SlotSize));
                Image fill = Ui.Image("Fill", border.transform, SlotColor);
                Ui.Stretch(fill.rectTransform, 2f, 2f, 2f, 2f);

                Image icon = Ui.Image("Icon", border.transform, Color.white);
                icon.preserveAspect = true;
                Ui.Stretch(icon.rectTransform, 10f, 10f, 10f, 10f);

                Text label = Ui.Text("Name", border.transform, "", 14, Color.white);
                Ui.Stretch(label.rectTransform, 4f, 4f, 4f, 4f);

                Text number = Ui.Text("Number", border.transform, (i + 1).ToString(), 13, new Color(1f, 1f, 1f, 0.35f), TextAnchor.UpperLeft);
                Ui.Stretch(number.rectTransform, 6f, 4f, 4f, 2f);

                var slot = border.gameObject.AddComponent<InventorySlot>();
                slot.View = this;
                slot.Index = i;
                slot.Border = border;
                slot.Icon = icon;
                slot.Label = label;
                slots[i] = slot;
            }

            tooltipText = Ui.AutoSizeBox("Item tooltip", root, new Color(0f, 0f, 0f, 0.85f), 20, out tooltip);
            tooltipText.horizontalOverflow = HorizontalWrapMode.Wrap;
            tooltip.pivot = new Vector2(0.5f, 0f);
            tooltip.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            tooltip.sizeDelta = new Vector2(420f, 0f);
            tooltip.gameObject.SetActive(false);

            ghost = Ui.Image("Dragged item", canvas, new Color(1f, 1f, 1f, 0.85f));
            ghost.preserveAspect = true;
            ghost.rectTransform.sizeDelta = new Vector2(SlotSize, SlotSize);
            ghost.gameObject.SetActive(false);
        }

        void ShowTooltip(string id, RectTransform slot)
        {
            if (IsDragging) return;
            ItemDef item = ItemCatalog.Get(id);
            tooltipText.text = "<b>" + item.Title + "</b>\n" + item.Description + "\n<size=16><color=#ffffff88>клик — использовать · перетащить на объект — применить</color></size>";
            tooltip.gameObject.SetActive(true);
            tooltip.position = slot.position + new Vector3(0f, slot.rect.height * slot.lossyScale.y / 2f + 10f, 0f);
        }

        internal void BeginDrag(string id, Sprite icon, Vector2 screen)
        {
            dragging = id;
            tooltip.gameObject.SetActive(false);
            ghost.sprite = icon;
            ghost.color = icon != null ? new Color(1f, 1f, 1f, 0.85f) : MissingIconColor;
            ghost.gameObject.SetActive(true);
            ghost.transform.SetAsLastSibling();
            ghost.rectTransform.position = screen;
            DragStarted?.Invoke(id);
        }

        internal void MoveDrag(Vector2 screen)
        {
            if (dragging == null) return;
            ghost.rectTransform.position = screen;
            Dragging?.Invoke(screen);
        }

        internal void EndDrag(Vector2 screen)
        {
            if (dragging == null) return;
            string id = dragging;
            CancelDrag();
            Dropped?.Invoke(id, screen);
        }

        void CancelDrag()
        {
            if (dragging == null) return;
            dragging = null;
            ghost.gameObject.SetActive(false);
            DragEnded?.Invoke();
        }

        void Refresh()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                string id = i < inventory.Items.Count ? inventory.Items[i] : null;
                Sprite icon = id != null ? ArtLibrary.Load(ArtPaths.Item(id)) : null;
                slots[i].Icon.enabled = icon != null;
                slots[i].Icon.sprite = icon;
                slots[i].Label.text = id != null && icon == null ? ItemCatalog.Get(id).Title : "";
            }
        }
    }
}
