using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace StillAwake
{
    /// <summary>Единая точка чтения ввода (Input System), чтобы не размазывать Mouse.current по коду.</summary>
    public static class GameInput
    {
        public static Vector2 PointerPosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;

        /// <summary>Курсор в долях экрана: -1..1 по обеим осям, центр экрана — 0.</summary>
        public static Vector2 PointerNormalized
        {
            get
            {
                Vector2 p = PointerPosition;
                return new Vector2(
                    Mathf.Clamp(p.x / Mathf.Max(1, Screen.width) * 2f - 1f, -1f, 1f),
                    Mathf.Clamp(p.y / Mathf.Max(1, Screen.height) * 2f - 1f, -1f, 1f));
            }
        }

        public static bool ClickPressed => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

        /// <summary>Шаг колеса мыши за кадр: +1 — от себя (приблизить), -1 — к себе.</summary>
        public static int ScrollSteps
        {
            get
            {
                if (Mouse.current == null) return 0;
                float y = Mouse.current.scroll.ReadValue().y;
                return y > 0.01f ? 1 : y < -0.01f ? -1 : 0;
            }
        }

        public static bool ResetZoomPressed => Pressed(GameAction.ResetZoom);

        public static bool RightClickPressed => Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;

        /// <summary>Действие с переназначаемыми клавишами (см. KeyBindings).</summary>
        public static bool Pressed(GameAction action) => KeyBindings.Pressed(action);

        public static bool KeyPressed(Key key) => Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;

        /// <summary>Пока печатают в поле ввода, клавиши действий не срабатывают.</summary>
        public static bool Typing
        {
            get
            {
                GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                var field = selected != null ? selected.GetComponent<UnityEngine.UI.InputField>() : null;
                return field != null && field.isFocused;
            }
        }

        public static bool PointerOverUi => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}
