using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace StillAwake
{
    /// <summary>Действия игрока, которые можно переназначить в «Настройки → Управление».</summary>
    public enum GameAction
    {
        TurnLeft,
        TurnRight,
        Sofa,
        Window,
        Phone,
        PhoneHide,
        /// <summary>Спрятать телефон целиком кнопкой, пока курсор над ним (по умолчанию M3).</summary>
        PhoneHideOnHover,
        /// <summary>Опустить телефон до четверти кнопкой, пока курсор над ним (по умолчанию ПКМ).</summary>
        PhonePeekOnHover,
        Inventory,
        CameraLock,
        Unhide,
        ResetZoom,
        DebugAreas,
        DebugStalker,
    }

    /// <summary>
    /// Клавиши действий: сколько угодно на действие, хранятся в PlayerPrefs (sa.keys.*). Кроме клавиатуры можно
    /// назначить кнопки мыши (кроме левой — она для кликов). Esc не переназначается — это всегда «назад» и пауза.
    /// Одна клавиша — одно действие: при назначении она снимается с другого. Исключение — действия «над телефоном»:
    /// они срабатывают только с курсором над телефоном и могут делить клавишу с обычными (M3 — и камера, и телефон).
    /// </summary>
    public static class KeyBindings
    {
        const string Prefix = "sa.keys.";

        // Кнопки мыши живут в том же Key, но за пределами значений клавиатуры.
        public const Key MouseMiddle = (Key)1001;
        public const Key MouseRight = (Key)1002;
        public const Key MouseBack = (Key)1003;
        public const Key MouseForward = (Key)1004;

        static bool IsMouse(Key key) => key >= MouseMiddle && key <= MouseForward;

        static ButtonControl MouseButton(Key key)
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return null;
            switch (key)
            {
                case MouseMiddle: return mouse.middleButton;
                case MouseRight: return mouse.rightButton;
                case MouseBack: return mouse.backButton;
                case MouseForward: return mouse.forwardButton;
                default: return null;
            }
        }

        public sealed class Definition
        {
            public readonly GameAction Action;
            public readonly string Title;
            public readonly Key[] Defaults;
            /// <summary>Срабатывает только с курсором над телефоном.</summary>
            public readonly bool OverPhone;

            public Definition(GameAction action, string title, params Key[] defaults) : this(action, title, false, defaults) { }

            public Definition(GameAction action, string title, bool overPhone, params Key[] defaults)
            {
                Action = action;
                Title = title;
                OverPhone = overPhone;
                Defaults = defaults;
            }
        }

        public static readonly Definition[] All =
        {
            new Definition(GameAction.TurnLeft, "Повернуться влево", Key.A, Key.LeftArrow),
            new Definition(GameAction.TurnRight, "Повернуться вправо", Key.D, Key.RightArrow),
            new Definition(GameAction.Sofa, "Вернуться на диван", Key.S, Key.Backspace),
            new Definition(GameAction.Window, "Подойти к окну", Key.W),
            new Definition(GameAction.Phone, "Телефон: открыть / свернуть", Key.Tab, Key.P),
            new Definition(GameAction.PhoneHide, "Телефон: спрятать / показать", Key.H),
            new Definition(GameAction.PhoneHideOnHover, "Телефон: спрятать целиком (курсор над ним)", true, MouseMiddle),
            new Definition(GameAction.PhonePeekOnHover, "Телефон: опустить до 25% (курсор над ним)", true, MouseRight),
            new Definition(GameAction.Inventory, "Инвентарь: показать / скрыть", Key.I),
            new Definition(GameAction.CameraLock, "Закрепить камеру", MouseMiddle, Key.C),
            new Definition(GameAction.Unhide, "Вылезти из укрытия", Key.Space),
            new Definition(GameAction.ResetZoom, "Общий вид (сброс приближения)", Key.Z),
            new Definition(GameAction.DebugAreas, "Отладка: области", Key.F1),
            new Definition(GameAction.DebugStalker, "Отладка: сталкер", Key.F2),
        };

        public static event Action Changed;

        static readonly Dictionary<GameAction, List<Key>> current = new Dictionary<GameAction, List<Key>>();

        public static Definition Get(GameAction action) => Array.Find(All, d => d.Action == action);

        /// <summary>Все клавиши действия по порядку.</summary>
        public static IReadOnlyList<Key> GetKeys(GameAction action) => Keys(action);

        public static bool Pressed(GameAction action)
        {
            foreach (Key key in Keys(action))
                if (WasPressed(key)) return true;
            return false;
        }

        static bool WasPressed(Key key)
        {
            if (key == Key.None) return false;
            if (IsMouse(key)) return MouseButton(key)?.wasPressedThisFrame ?? false;
            return Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;
        }

        /// <summary>«A / ←» — для подсказок в интерфейсе.</summary>
        public static string Label(GameAction action)
        {
            var names = new List<string>();
            foreach (Key key in Keys(action))
                if (key != Key.None) names.Add(KeyName(key));
            return names.Count > 0 ? string.Join(" / ", names) : "—";
        }

        /// <summary>Первая клавиша действия — для подписей на кнопках («Инвентарь [I]»).</summary>
        public static string Short(GameAction action)
        {
            foreach (Key key in Keys(action))
                if (key != Key.None) return KeyName(key);
            return "—";
        }

        public static bool CanBind(Key key) => key != Key.None && key != Key.Escape && (IsMouse(key) || Enum.IsDefined(typeof(Key), key));

        /// <summary>
        /// Назначить клавишу в позицию slot: slot = числу клавиш — добавить новую, Key.None — убрать эту.
        /// Та же клавиша снимается с других действий того же вида (обычных или «над телефоном»).
        /// </summary>
        public static void Set(GameAction action, int slot, Key key)
        {
            List<Key> keys = Keys(action);
            if (key == Key.None)
            {
                if (slot >= 0 && slot < keys.Count) keys.RemoveAt(slot);
                Save();
                return;
            }
            if (!CanBind(key)) return;

            bool overPhone = Get(action)?.OverPhone ?? false;
            foreach (Definition def in All)
                if (def.Action != action && def.OverPhone == overPhone) Keys(def.Action).Remove(key);

            int existing = keys.IndexOf(key);
            if (slot >= keys.Count)
            {
                if (existing < 0) keys.Add(key);
            }
            else if (existing < 0 || existing == slot)
            {
                keys[slot] = key;
            }
            else
            {
                // Клавиша уже есть у этого действия в другой позиции — просто убираем заменяемую.
                keys.RemoveAt(slot);
            }
            Save();
        }

        public static void ResetToDefaults()
        {
            current.Clear();
            foreach (Definition def in All) PlayerPrefs.DeleteKey(Prefix + def.Action);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Нажатая в этом кадре клавиша или кнопка мыши (для экрана переназначения) или Key.None.</summary>
        public static Key PressedThisFrame()
        {
            foreach (Key mouse in new[] { MouseMiddle, MouseRight, MouseBack, MouseForward })
                if (WasPressed(mouse)) return mouse;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return Key.None;
            foreach (KeyControl control in keyboard.allKeys)
                if (control != null && control.wasPressedThisFrame) return control.keyCode;
            return Key.None;
        }

        public static string KeyName(Key key)
        {
            switch (key)
            {
                case Key.None: return "—";
                case MouseMiddle: return "M3";
                case MouseRight: return "ПКМ";
                case MouseBack: return "M4";
                case MouseForward: return "M5";
                case Key.LeftArrow: return "←";
                case Key.RightArrow: return "→";
                case Key.UpArrow: return "↑";
                case Key.DownArrow: return "↓";
                case Key.Space: return "Пробел";
                case Key.Enter: return "Enter";
                case Key.Escape: return "Esc";
                case Key.LeftShift: return "Shift";
                case Key.RightShift: return "Правый Shift";
                case Key.LeftCtrl: return "Ctrl";
                case Key.RightCtrl: return "Правый Ctrl";
                case Key.LeftAlt: return "Alt";
                case Key.RightAlt: return "Правый Alt";
            }
            string name = key.ToString();
            if (name.StartsWith("Digit")) return name.Substring(5);
            if (name.StartsWith("Numpad")) return "Num " + name.Substring(6);
            return name;
        }

        static List<Key> Keys(GameAction action)
        {
            if (current.TryGetValue(action, out List<Key> keys)) return keys;

            keys = new List<Key>();
            Definition def = Get(action);
            // GetString без ключа возвращает "", а не default, — поэтому HasKey.
            if (PlayerPrefs.HasKey(Prefix + action))
            {
                foreach (string part in PlayerPrefs.GetString(Prefix + action).Split(','))
                    if (Enum.TryParse(part, out Key key) && CanBind(key) && !keys.Contains(key)) keys.Add(key);
            }
            else if (def != null)
            {
                keys.AddRange(def.Defaults);
            }
            current[action] = keys;
            return keys;
        }

        static void Save()
        {
            foreach (Definition def in All)
            {
                List<Key> keys = Keys(def.Action);
                PlayerPrefs.SetString(Prefix + def.Action, string.Join(",", keys.ConvertAll(k => k.ToString())));
            }
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            current.Clear();
            Changed = null;
        }
    }
}
