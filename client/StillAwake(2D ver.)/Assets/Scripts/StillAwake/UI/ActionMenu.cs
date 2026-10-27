using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace StillAwake
{
    public sealed class RoomAction
    {
        public readonly string Title;

        /// <summary>Возвращает мысль героини (субтитр) или null, если сказать нечего.</summary>
        public readonly Func<string> Run;

        public RoomAction(string title, Func<string> run)
        {
            Title = title;
            Run = run;
        }
    }

    /// <summary>
    /// Маленькое меню действий у курсора после клика по объекту. Живёт, пока курсор над этим объектом
    /// или над самим меню: увёл курсор с объекта — меню исчезло.
    /// </summary>
    public sealed class ActionMenu : MonoBehaviour
    {
        const float Width = 300f;
        const float RowHeight = 40f;
        /// <summary>Сколько можно «промахнуться» мимо объекта и меню, прежде чем меню закроется.</summary>
        const float GraceSeconds = 0.25f;

        static readonly Color PanelColor = new Color(0.05f, 0.05f, 0.07f, 0.92f);
        static readonly Color BorderColor = new Color(1f, 1f, 1f, 0.18f);
        static readonly Color RowColor = new Color(1f, 1f, 1f, 0f);
        static readonly Color TitleColor = new Color(1f, 0.85f, 0.55f);

        public bool IsOpen => panel.gameObject.activeSelf;
        public Hotspot Target { get; private set; }

        /// <summary>Действие выполнено: его мысль — в субтитры.</summary>
        public event Action<string> Result;

        RectTransform canvas, panel, list;
        Text title;
        Func<bool> pointerOnTarget;
        float outsideFor;
        /// <summary>Точка объекта в мире: меню «приколото» к ней и не уезжает вместе с камерой.</summary>
        Vector3 anchor;

        public static ActionMenu Create(RectTransform canvas)
        {
            RectTransform root = Ui.Stretch(Ui.Node("Action menu", canvas));
            var menu = root.gameObject.AddComponent<ActionMenu>();
            menu.canvas = canvas;

            Image border = Ui.Image("Panel", root, BorderColor, null, raycast: true);
            menu.panel = border.rectTransform;
            menu.panel.anchorMin = menu.panel.anchorMax = new Vector2(0.5f, 0.5f);
            menu.panel.pivot = new Vector2(0f, 1f);
            var fit = border.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var outer = border.gameObject.AddComponent<VerticalLayoutGroup>();
            outer.padding = new RectOffset(1, 1, 1, 1);
            outer.childControlWidth = outer.childControlHeight = true;
            outer.childForceExpandWidth = true;
            outer.childForceExpandHeight = false;

            Image inner = Ui.Image("Inner", border.transform, PanelColor, null, raycast: true);
            menu.list = inner.rectTransform;
            var layout = inner.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 2f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            menu.title = Ui.Size(Ui.Text("Title", inner.transform, "", 18, TitleColor, TextAnchor.MiddleLeft, FontStyle.Bold), 30f);
            menu.panel.sizeDelta = new Vector2(Width, 0f);
            menu.panel.gameObject.SetActive(false);
            return menu;
        }

        /// <summary>Открыть меню у экранной точки. pointerOnObject — курсор всё ещё над объектом.</summary>
        public void Open(Hotspot target, string heading, List<RoomAction> actions, Vector2 screenPoint, Func<bool> pointerOnObject)
        {
            Target = target;
            pointerOnTarget = pointerOnObject;
            title.text = heading.ToUpperInvariant();

            for (int i = list.childCount - 1; i >= 1; i--)
            {
                GameObject row = list.GetChild(i).gameObject;
                row.SetActive(false);
                Destroy(row);
            }
            foreach (RoomAction action in actions)
            {
                RoomAction captured = action;
                Button row = Ui.Size(Ui.Button(action.Title, list, action.Title, () => Run(captured), 21, RowColor), RowHeight);
                ColorBlock colors = row.colors;
                colors.highlightedColor = new Color(1f, 1f, 1f, 0.12f);
                colors.pressedColor = new Color(1f, 1f, 1f, 0.2f);
                row.colors = colors;
                Text label = row.GetComponentInChildren<Text>();
                label.alignment = TextAnchor.MiddleLeft;
                label.rectTransform.offsetMin = new Vector2(12f, label.rectTransform.offsetMin.y);
            }

            panel.gameObject.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            Camera cam = Camera.main;
            anchor = cam != null ? cam.ScreenToWorldPoint(screenPoint) : (Vector3)screenPoint;
            Place(screenPoint);
            outsideFor = 0f;
        }

        public void Close()
        {
            Target = null;
            pointerOnTarget = null;
            panel.gameObject.SetActive(false);
        }

        /// <summary>Курсор над меню (для камеры и выбора объектов).</summary>
        public bool PointerOver => IsOpen && RectTransformUtility.RectangleContainsScreenPoint(panel, GameInput.PointerPosition, null);

        void Place(Vector2 screenPoint)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screenPoint, null, out Vector2 local);
            Vector2 size = panel.rect.size;
            Vector2 half = canvas.rect.size / 2f;
            // Чуть правее и ниже курсора, но целиком на экране.
            var position = new Vector2(local.x + 14f, local.y - 6f);
            if (position.x + size.x > half.x - 10f) position.x = local.x - 14f - size.x;
            if (position.y - size.y < -half.y + 10f) position.y = -half.y + 10f + size.y;
            panel.anchoredPosition = position;
        }

        // После камеры: позиция меню пересчитывается из мировой точки объекта.
        void LateUpdate()
        {
            if (!IsOpen) return;
            Camera cam = Camera.main;
            if (cam != null) Place(cam.WorldToScreenPoint(anchor));
        }

        void Update()
        {
            if (!IsOpen) return;
            bool inside = PointerOver || (pointerOnTarget != null && pointerOnTarget());
            outsideFor = inside ? 0f : outsideFor + Time.unscaledDeltaTime;
            if (outsideFor > GraceSeconds) Close();
        }

        void Run(RoomAction action)
        {
            Close();
            Sfx.Click();
            string result = action.Run();
            if (!string.IsNullOrEmpty(result)) Result?.Invoke(result);
        }
    }
}
