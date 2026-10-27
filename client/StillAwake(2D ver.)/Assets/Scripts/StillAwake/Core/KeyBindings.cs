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
        Phone,
        PhoneHide,
        Inventory,
        CameraLock,
        Unhide,
        ResetZoom,
        DebugAreas,
        DebugStalker,
    }

    /// <summary>
    /// Клавиши действий: по две на действие, хранятся в PlayerPrefs (sa.keys.*). Кроме клавиатуры можно назначить
    /// кнопки мыши (кроме левой — она для кликов). Esc не переназначается — это всегда «назад» и пауза.
    /// Одна клавиша — одно действие: при назначении она снимается с другого.
    /// </summary>
    public static class KeyBindings
    {
        public const int Slots = 2;
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

            public Definition(GameAction action, string title, params Key[] defaults)
            {
                Action = action;
                Title = title;
                Defaults = defaults;
            }
        }

        public static readonly Definition[] All =
        {
            new Definition(GameAction.TurnLeft, "Повернуться влево", Key.A, Key.LeftArrow),
            new Definition(GameAction.TurnRight, "Повернуться вправо", Key.D, Key.RightArrow),
            new Definition(GameAction.Sofa, "Вернуться на диван", Key.S, Key.Backspace),
            new Definition(GameAction.Phone, "Телефон: открыть / свернуть", Key.Tab, Key.P),
            new Definition(GameAction.PhoneHide, "Телефон: спрятать / показать", Key.H),
            new Definition(GameAction.Inventory, "Инвентарь: показать / скрыть", Key.I),
            new Definition(GameAction.CameraLock, "Закрепить камеру", MouseMiddle, Key.C),
            new Definition(GameAction.Unhide, "Вылезти из укрытия", Key.Space),
            new Definition(GameAction.ResetZoom, "Общий вид (сброс приближения)", Key.Z),
            new Definition(GameAction.DebugAreas, "Отладка: области", Key.F1),
            new Definition(GameAction.DebugStalker, "Отладка: сталкер", Key.F2),
        };

        public static event Action Changed;

        static readonly Dictionary<GameAction, Key[]> current = new Dictionary<GameAction, Key[]>();

        public static Definition Get(GameAction action) => Array.Find(All, d => d.Action == action);

        public static Key GetKey(GameAction action, int slot) => Keys(action)[slot];

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

        /// <summary>Назначить клавишу (Key.None — очистить). Та же клавиша снимается с других действий.</summary>
        public static void Set(GameAction action, int slot, Key key)
        {
            if (key != Key.None && !CanBind(key)) return;
            if (key != Key.None)
            {
                foreach (Definition def in All)
                {
                    Key[] keys = Keys(def.Action);
                    for (int i = 0; i < Slots; i++)
                        if (keys[i] == key && (def.Action != action || i != slot)) keys[i] = Key.None;
                }
            }
            Keys(action)[slot] = key;
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

        static Key[] Keys(GameAction action)
        {
            if (current.TryGetValue(action, out Key[] keys)) return keys;

            keys = new Key[Slots];
            Definition def = Get(action);
            // GetString без ключа возвращает "", а не default, — поэтому HasKey.
            if (PlayerPrefs.HasKey(Prefix + action))
            {
                string[] parts = PlayerPrefs.GetString(Prefix + action).Split(',');
                for (int i = 0; i < Slots && i < parts.Length; i++)
                    keys[i] = Enum.TryParse(parts[i], out Key key) && CanBind(key) ? key : Key.None;
            }
            else if (def != null)
            {
                for (int i = 0; i < Slots && i < def.Defaults.Length; i++) keys[i] = def.Defaults[i];
            }
            current[action] = keys;
            return keys;
        }

        static void Save()
        {
            foreach (Definition def in All)
            {
                Key[] keys = Keys(def.Action);
                PlayerPrefs.SetString(Prefix + def.Action, string.Join(",", Array.ConvertAll(keys, k => k.ToString())));
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
