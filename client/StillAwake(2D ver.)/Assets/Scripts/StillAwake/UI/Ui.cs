using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StillAwake
{
    /// <summary>Мини-фабрика uGUI: весь интерфейс прототипа собирается кодом, без префабов.</summary>
    public static class Ui
    {
        public static readonly Color Ink = new Color(0.08f, 0.08f, 0.1f);
        public static readonly Color Paper = new Color(0.96f, 0.96f, 0.94f);
        public static readonly Color Accent = new Color(0.95f, 0.3f, 0.3f);
        public static readonly Color ButtonColor = new Color(0.16f, 0.17f, 0.2f, 0.92f);
        public static readonly Color PrimaryButton = new Color(0.55f, 0.08f, 0.1f, 0.95f);
        public static readonly Color Muted = new Color(1f, 1f, 1f, 0.55f);
        public static readonly Color ErrorColor = new Color(1f, 0.48f, 0.42f);
        public static readonly Color OkColor = new Color(0.55f, 0.9f, 0.6f);
        static readonly Color FieldColor = new Color(1f, 1f, 1f, 0.07f);

        static Font font;
        public static Font Font => font != null ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static RectTransform CreateCanvas(Transform parent)
        {
            var go = new GameObject("UI", typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return (RectTransform)go.transform;
        }

        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        public static RectTransform Stretch(RectTransform rect, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        public static RectTransform Anchors(RectTransform rect, float minX, float minY, float maxX, float maxY)
        {
            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        /// <summary>Растянуть по прямоугольнику в пикселях картинки-подложки (как на заглушке художника).</summary>
        public static RectTransform AnchorsPx(RectTransform rect, PxRect area, float imageWidth, float imageHeight) =>
            Anchors(rect,
                area.X / imageWidth, 1f - (area.Y + area.H) / imageHeight,
                (area.X + area.W) / imageWidth, 1f - area.Y / imageHeight);

        public static RectTransform Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        public static Image Image(string name, Transform parent, Color color, Sprite sprite = null, bool raycast = false)
        {
            var image = Node(name, parent).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        public static Text Text(string name, Transform parent, string value, int size, Color color,
            TextAnchor alignment = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var text = Node(name, parent).gameObject.AddComponent<Text>();
            text.font = Font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.fontStyle = style;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;
            text.raycastTarget = false;
            return text;
        }

        public static Button Button(string name, Transform parent, string label, UnityAction onClick,
            int fontSize = 24, Color? background = null, Sprite sprite = null)
        {
            Color color = background ?? ButtonColor;
            Image image = Image(name, parent, Color.white, sprite, raycast: true);

            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = color;
            colors.selectedColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.25f);
            colors.disabledColor = new Color(color.r, color.g, color.b, color.a * 0.4f);
            button.colors = colors;

            Text text = Text("Label", image.transform, label, fontSize, Color.white);
            Stretch(text.rectTransform, 10f, 4f, 10f, 4f);

            // Снимаем выделение после клика, иначе Пробел/Enter повторно «нажмут» эту кнопку.
            button.onClick.AddListener(ClearSelection);
            if (onClick != null) button.onClick.AddListener(onClick);
            return button;
        }

        /// <summary>Узел с вертикальной раскладкой: дети по ширине, высота — из их LayoutElement (см. Size).</summary>
        public static RectTransform Column(string name, Transform parent, float spacing = 12f, int padding = 0)
        {
            RectTransform rect = Node(name, parent);
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return rect;
        }

        /// <summary>Узел с горизонтальной раскладкой: ширина детей — из их LayoutElement.</summary>
        public static RectTransform Row(string name, Transform parent, float spacing = 12f)
        {
            RectTransform rect = Node(name, parent);
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            return rect;
        }

        /// <summary>Размер элемента внутри Column/Row; width ≤ 0 — тянуться по свободному месту.</summary>
        public static T Size<T>(T component, float height, float width = 0f) where T : Component
        {
            var element = component.GetComponent<LayoutElement>();
            if (element == null) element = component.gameObject.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            if (width > 0f)
            {
                element.minWidth = width;
                element.preferredWidth = width;
                element.flexibleWidth = 0f;
            }
            else
            {
                element.flexibleWidth = 1f;
            }
            return component;
        }

        /// <summary>Галочка с подписью (кликабельна и подпись). Высоту задаёт Size снаружи.</summary>
        public static UnityEngine.UI.Toggle Checkbox(string name, Transform parent, string label, bool isOn, UnityAction<bool> onChange, int fontSize = 24)
        {
            RectTransform row = Row(name, parent, 14f);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            var toggle = row.gameObject.AddComponent<UnityEngine.UI.Toggle>();
            Image box = Image("Box", row, Color.white, null, raycast: true);
            Size(box, 36f, 36f);
            Image check = Image("Check", box.transform, Accent);
            Stretch(check.rectTransform, 8f, 8f, 8f, 8f);
            Text text = Text("Label", row, label, fontSize, Color.white, TextAnchor.MiddleLeft);
            text.raycastTarget = true;
            Size(text, 36f);

            toggle.targetGraphic = box;
            toggle.graphic = check;
            ColorBlock colors = toggle.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0.18f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.3f);
            colors.selectedColor = colors.normalColor;
            colors.pressedColor = colors.highlightedColor;
            toggle.colors = colors;
            toggle.isOn = isOn;
            if (onChange != null) toggle.onValueChanged.AddListener(onChange);
            return toggle;
        }

        /// <summary>Однострочное поле ввода (legacy InputField) с подсказкой внутри.</summary>
        public static InputField Input(string name, Transform parent, string placeholder, int fontSize = 28,
            InputField.ContentType contentType = InputField.ContentType.Standard)
        {
            Image background = Image(name, parent, Color.white, null, raycast: true);
            var field = background.gameObject.AddComponent<InputField>();
            field.targetGraphic = background;
            ColorBlock colors = field.colors;
            colors.normalColor = FieldColor;
            colors.highlightedColor = Color.Lerp(FieldColor, Color.white, 0.08f);
            colors.selectedColor = Color.Lerp(FieldColor, Color.white, 0.12f);
            colors.pressedColor = colors.selectedColor;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.03f);
            field.colors = colors;

            Text hint = Text("Placeholder", background.transform, placeholder, fontSize, new Color(1f, 1f, 1f, 0.32f), TextAnchor.MiddleLeft, FontStyle.Italic);
            Stretch(hint.rectTransform, 18f, 4f, 18f, 4f);

            Text text = Text("Text", background.transform, "", fontSize, Color.white, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform, 18f, 4f, 18f, 4f);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;

            field.textComponent = text;
            field.placeholder = hint;
            field.contentType = contentType;
            field.lineType = InputField.LineType.SingleLine;
            field.caretWidth = 2;
            field.customCaretColor = true;
            field.caretColor = Accent;
            field.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);
            // Tab переключает поля, а не печатается.
            field.onValidateInput += (value, index, c) => c == '\t' ? '\0' : c;
            return field;
        }

        static void ClearSelection()
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        public static void SetLabel(Button button, string label)
        {
            var text = button.GetComponentInChildren<Text>(true);
            if (text != null) text.text = label;
        }

        /// <summary>Плашка, подстраивающая размер под однострочный текст (подсказка у курсора, тосты).</summary>
        public static Text AutoSizeBox(string name, Transform parent, Color background, int fontSize, out RectTransform box)
        {
            Image image = Image(name, parent, background);
            box = image.rectTransform;

            var layout = image.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 10, 10);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = image.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Text text = Text("Text", box, "", fontSize, Color.white);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            return text;
        }

        /// <summary>Удалить детей. Сначала выключаем, чтобы layout-группы не учитывали их до конца кадра.</summary>
        public static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                Object.Destroy(child);
            }
        }
    }
}
