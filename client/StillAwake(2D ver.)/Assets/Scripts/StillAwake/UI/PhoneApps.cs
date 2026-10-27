using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace StillAwake
{
    /// <summary>Приложение на экране телефона. Живёт внутри PhoneView, кадр получает через Tick.</summary>
    public abstract class PhoneApp
    {
        public RectTransform Root { get; protected set; }
        public bool Visible => Root.gameObject.activeSelf;

        public void Show()
        {
            Root.gameObject.SetActive(true);
            OnShow();
        }

        public void Hide()
        {
            if (!Visible) return;
            OnHide();
            Root.gameObject.SetActive(false);
        }

        protected virtual void OnShow() { }
        protected virtual void OnHide() { }
        public virtual void Tick(float dt) { }

        protected static RectTransform Page(string name, RectTransform area, Color background)
        {
            Image page = Ui.Image(name, area, background, null, raycast: true);
            Ui.Stretch(page.rectTransform);
            page.gameObject.SetActive(false);
            return page.rectTransform;
        }

        /// <summary>Шапка приложения: «‹ назад» и заголовок.</summary>
        protected static RectTransform Header(RectTransform page, string title, UnityAction back, Color color, float height = 56f)
        {
            Image header = Ui.Image("Header", page, color, null, raycast: true);
            header.rectTransform.anchorMin = new Vector2(0f, 1f);
            header.rectTransform.anchorMax = Vector2.one;
            header.rectTransform.pivot = new Vector2(0.5f, 1f);
            header.rectTransform.sizeDelta = new Vector2(0f, height);
            Button backButton = Ui.Button("Back", header.transform, "‹", back, 34, new Color(1f, 1f, 1f, 0f));
            Ui.Place((RectTransform)backButton.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(44f, height));
            if (title != null)
            {
                Text text = Ui.Text("Title", header.transform, title, 20, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
                Ui.Stretch(text.rectTransform, 52f, 0f, 12f, 0f);
            }
            return header.rectTransform;
        }

        protected static Button Key(Transform parent, string label, UnityAction onClick, Color color, int size = 26) =>
            Ui.Button(label, parent, label, () =>
            {
                Sfx.Click();
                onClick();
            }, size, color);

        protected static RectTransform Grid(string name, RectTransform parent, int columns, Vector2 cell, Vector2 spacing)
        {
            RectTransform rect = Ui.Node(name, parent);
            var grid = rect.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = cell;
            grid.spacing = spacing;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.childAlignment = TextAnchor.MiddleCenter;
            return rect;
        }
    }

    /// <summary>Мессенджер в духе Telegram: шапка с «печатает…», пузыри, подсказки ответов и своё сообщение.</summary>
    public sealed class MessengerApp : PhoneApp
    {
        const int VisibleMessages = 30;
        static readonly Color HeaderColor = new Color(0.09f, 0.13f, 0.17f);
        static readonly Color ChatColor = new Color(0.055f, 0.086f, 0.13f);
        static readonly Color IncomingColor = new Color(0.094f, 0.145f, 0.2f);
        static readonly Color OutgoingColor = new Color(0.17f, 0.32f, 0.47f);
        static readonly Color AccentColor = new Color(0.32f, 0.53f, 0.76f);
        static readonly Color MutedColor = new Color(1f, 1f, 1f, 0.5f);

        readonly Phone phone;
        readonly Action<int> replyChosen;
        readonly Action<string> submitted;
        readonly RectTransform messages, replies;
        readonly Text status, typingBubble;
        readonly InputField input;
        float dots;

        /// <summary>Поле ввода в фокусе: клавиши игры не срабатывают.</summary>
        public bool InputFocused => input.isFocused;

        public MessengerApp(RectTransform area, Phone phone, Action back, Action<int> replyChosen, Action<string> submitted)
        {
            this.phone = phone;
            this.replyChosen = replyChosen;
            this.submitted = submitted;
            Root = Page("Messenger", area, ChatColor);

            RectTransform header = Header(Root, null, () => back(), HeaderColor, 64f);
            Image avatar = Ui.Image("Avatar", header, new Color(0.35f, 0.36f, 0.4f), ArtLibrary.Circle);
            Ui.Place(avatar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(48f, 0f), new Vector2(42f, 42f));
            Text initial = Ui.Text("Initial", avatar.transform, "?", 22, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(initial.rectTransform);
            Text name = Ui.Text("Name", header, Phone.ContactName, 19, Color.white, TextAnchor.LowerLeft, FontStyle.Bold);
            Ui.Stretch(name.rectTransform, 100f, 32f, 8f, 8f);
            status = Ui.Text("Status", header, "", 15, MutedColor, TextAnchor.UpperLeft);
            Ui.Stretch(status.rectTransform, 100f, 8f, 8f, 34f);

            messages = Ui.Node("Messages", Root);
            Ui.Stretch(messages, 0f, 60f, 0f, 64f);
            messages.gameObject.AddComponent<RectMask2D>();
            var layout = messages.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.LowerLeft;   // новые снизу, старые уходят вверх за маску
            layout.padding = new RectOffset(8, 8, 6, 6);
            layout.spacing = 6f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            typingBubble = Ui.Text("Typing", Root, "", 16, MutedColor, TextAnchor.MiddleLeft, FontStyle.Italic);
            typingBubble.gameObject.SetActive(false);

            replies = Ui.Column("Replies", Root, 6f);
            replies.anchorMin = new Vector2(0f, 0f);
            replies.anchorMax = new Vector2(1f, 0f);
            replies.pivot = new Vector2(0.5f, 0f);
            replies.anchoredPosition = new Vector2(0f, 60f);
            replies.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(10, 10, 4, 4);

            Image bar = Ui.Image("Input bar", Root, HeaderColor, null, raycast: true);
            bar.rectTransform.anchorMin = Vector2.zero;
            bar.rectTransform.anchorMax = new Vector2(1f, 0f);
            bar.rectTransform.pivot = new Vector2(0.5f, 0f);
            bar.rectTransform.sizeDelta = new Vector2(0f, 56f);
            input = Ui.Input("Message", bar.transform, "Сообщение", 18);
            Ui.Stretch(input.GetComponent<RectTransform>(), 8f, 8f, 60f, 8f);
            input.characterLimit = 300;
            input.onEndEdit.AddListener(_ =>
            {
                if (GameInput.KeyPressed(UnityEngine.InputSystem.Key.Enter) || GameInput.KeyPressed(UnityEngine.InputSystem.Key.NumpadEnter)) Submit();
            });
            Button send = Ui.Button("Send", bar.transform, "→", Submit, 26, AccentColor);
            RectTransform sendRect = Ui.Anchors((RectTransform)send.transform, 1f, 0f, 1f, 1f);
            sendRect.pivot = new Vector2(1f, 0.5f);
            sendRect.sizeDelta = new Vector2(48f, -16f);
            sendRect.anchoredPosition = new Vector2(-6f, 0f);

            phone.Changed += Refresh;
            Refresh();
        }

        protected override void OnHide()
        {
            if (input.isFocused) input.DeactivateInputField();
        }

        void Submit()
        {
            string text = input.text.Trim();
            if (text.Length == 0) return;
            input.text = "";
            submitted(text);
            input.ActivateInputField();
        }

        // Прочитанными сообщения отмечает PhoneView: только когда телефон в руке и чат открыт.
        public void Refresh()
        {
            Ui.Clear(messages);
            for (int i = Mathf.Max(0, phone.Messages.Count - VisibleMessages); i < phone.Messages.Count; i++)
                AddBubble(phone.Messages[i]);

            Ui.Clear(replies);
            for (int i = 0; i < phone.Replies.Count; i++)
            {
                int index = i;
                Button chip = Ui.Size(Ui.Button("Reply", replies, phone.Replies[i], () => replyChosen(index), 16, new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.35f)), 32f);
                chip.GetComponentInChildren<Text>().fontStyle = FontStyle.Italic;
            }
            float repliesHeight = phone.Replies.Count > 0 ? phone.Replies.Count * 38f + 8f : 0f;
            messages.offsetMin = new Vector2(0f, 60f + repliesHeight + (phone.Typing ? 26f : 0f));

            typingBubble.gameObject.SetActive(phone.Typing);
            Ui.Place(typingBubble.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 60f + repliesHeight), new Vector2(300f, 26f));
            UpdateStatus();
        }

        public override void Tick(float dt)
        {
            if (!phone.Typing) return;
            dots += dt;
            UpdateStatus();
        }

        void UpdateStatus()
        {
            if (phone.Typing)
            {
                string animated = new string('.', 1 + (int)(dots * 3f) % 3);
                status.text = "<color=#6aa6e8>печатает" + animated + "</color>";
                typingBubble.text = Phone.ContactName + " печатает" + animated;
            }
            else
            {
                status.text = phone.Messages.Count > 0 ? "был(а) недавно" : "в сети давно не был(а)";
            }
        }

        void AddBubble(PhoneMessage message)
        {
            RectTransform row = Ui.Node("Row", messages);
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.childAlignment = message.FromPlayer ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            rowLayout.childControlWidth = rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = rowLayout.childForceExpandHeight = false;

            Image bubble = Ui.Image("Bubble", row, message.FromPlayer ? OutgoingColor : IncomingColor);
            var bubbleLayout = bubble.gameObject.AddComponent<VerticalLayoutGroup>();
            bubbleLayout.padding = new RectOffset(10, 10, 6, 6);
            bubbleLayout.childControlWidth = bubbleLayout.childControlHeight = true;
            bubbleLayout.childForceExpandWidth = true;
            bubbleLayout.childForceExpandHeight = false;
            Text text = Ui.Text("Text", bubble.transform,
                message.Text + "  <size=12><color=#ffffff77>" + message.Time + "</color></size>", 17, Color.white, TextAnchor.UpperLeft);

            // Пузырь по ширине текста, но не шире трёх четвертей экрана.
            var size = bubble.gameObject.AddComponent<LayoutElement>();
            size.preferredWidth = Mathf.Min(300f, text.preferredWidth + bubbleLayout.padding.horizontal + 2f);
            size.flexibleWidth = 0f;
        }
    }

    /// <summary>Набор номера и звонок. Пока никто не отвечает — только гудки.</summary>
    public sealed class DialerApp : PhoneApp
    {
        const float RingSeconds = 9f;
        static readonly Color Background = new Color(0.04f, 0.04f, 0.05f);
        static readonly Color KeyColor = new Color(0.17f, 0.17f, 0.19f);
        static readonly Color CallColor = new Color(0.16f, 0.62f, 0.3f);
        static readonly Color HangUpColor = new Color(0.82f, 0.18f, 0.16f);

        readonly Action<string> callStarted;
        readonly Text display, callTitle, callState;
        readonly RectTransform keypad, callScreen;
        string number = "";
        float callTime = -1f;
        bool failed;

        public bool InCall => callTime >= 0f;

        public DialerApp(RectTransform area, Action back, Action<string> callStarted)
        {
            this.callStarted = callStarted;
            Root = Page("Dialer", area, Background);

            keypad = Ui.Stretch(Ui.Node("Keypad", Root));
            Header(keypad, "Телефон", () => back(), Background);
            display = Ui.Text("Number", keypad, "", 34, Color.white, TextAnchor.MiddleCenter);
            Ui.Place(display.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(380f, 60f));
            display.horizontalOverflow = HorizontalWrapMode.Overflow;

            RectTransform grid = Grid("Keys", keypad, 3, new Vector2(96f, 72f), new Vector2(16f, 12f));
            Ui.Place(grid, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(330f, 330f));
            foreach (string key in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "*", "0", "#" })
            {
                string digit = key;
                Key(grid, key, () => Press(digit), KeyColor, 30);
            }

            Button call = Ui.Button("Call", keypad, "Вызов", Call, 24, CallColor);
            Ui.Place((RectTransform)call.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(180f, 64f));
            Button erase = Ui.Button("Erase", keypad, "←", Erase, 26, new Color(1f, 1f, 1f, 0f));
            Ui.Place((RectTransform)erase.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(140f, 40f), new Vector2(64f, 64f));

            callScreen = Ui.Stretch(Ui.Node("Call", Root));
            callTitle = Ui.Text("Number", callScreen, "", 32, Color.white, TextAnchor.MiddleCenter);
            Ui.Place(callTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(380f, 50f));
            callState = Ui.Text("State", callScreen, "", 20, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter);
            Ui.Place(callState.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(380f, 40f));
            Button hangUp = Ui.Button("Hang up", callScreen, "Завершить", HangUp, 22, HangUpColor);
            Ui.Place((RectTransform)hangUp.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(200f, 64f));
            callScreen.gameObject.SetActive(false);
        }

        void Press(string digit)
        {
            if (number.Length >= 16) return;
            number += digit;
            display.text = number;
            Sfx.Dtmf(digit);
        }

        void Erase()
        {
            if (number.Length == 0) return;
            number = number.Substring(0, number.Length - 1);
            display.text = number;
        }

        void Call()
        {
            if (number.Length == 0) return;
            keypad.gameObject.SetActive(false);
            callScreen.gameObject.SetActive(true);
            callTitle.text = number;
            callTime = 0f;
            failed = number == "112" || number == "102" || number == "911" || number == "02";
            callState.text = "Вызов…";
            callStarted(number);
            if (!failed) Sfx.StartRingback();
        }

        void HangUp()
        {
            Sfx.StopRingback();
            callTime = -1f;
            callScreen.gameObject.SetActive(false);
            keypad.gameObject.SetActive(true);
        }

        protected override void OnHide()
        {
            if (InCall) HangUp();
        }

        public override void Tick(float dt)
        {
            if (!InCall) return;
            callTime += dt;
            if (failed)
            {
                callState.text = callTime < 2f ? "Вызов…" : "Вызов не удался. Нет сети.";
                if (callTime > 4f) HangUp();
                return;
            }
            if (callTime < RingSeconds) callState.text = "Гудки… " + (int)callTime + " с";
            else if (callTime < RingSeconds + 2.5f)
            {
                Sfx.StopRingback();
                callState.text = "Абонент не отвечает";
            }
            else HangUp();
        }
    }

    /// <summary>Простой калькулятор.</summary>
    public sealed class CalculatorApp : PhoneApp
    {
        static readonly Color Background = new Color(0.02f, 0.02f, 0.03f);
        static readonly Color DigitColor = new Color(0.2f, 0.2f, 0.22f);
        static readonly Color OperatorColor = new Color(0.95f, 0.6f, 0.1f);
        static readonly Color FunctionColor = new Color(0.55f, 0.55f, 0.57f);

        readonly Text display;
        string entry = "0";
        double stored;
        string pending;
        bool fresh = true;

        public CalculatorApp(RectTransform area, Action back)
        {
            Root = Page("Calculator", area, Background);
            Header(Root, "Калькулятор", () => back(), Background);
            display = Ui.Text("Display", Root, "0", 52, Color.white, TextAnchor.LowerRight);
            Ui.Place(display.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(380f, 110f));
            display.horizontalOverflow = HorizontalWrapMode.Overflow;

            RectTransform grid = Grid("Keys", Root, 4, new Vector2(86f, 74f), new Vector2(10f, 10f));
            Ui.Place(grid, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(380f, 410f));
            string[] keys = { "C", "±", "%", "÷", "7", "8", "9", "×", "4", "5", "6", "−", "1", "2", "3", "+", "0", ",", "←", "=" };
            foreach (string key in keys)
            {
                string k = key;
                bool op = k == "÷" || k == "×" || k == "−" || k == "+" || k == "=";
                bool fn = k == "C" || k == "±" || k == "%" || k == "←";
                Key(grid, k, () => Press(k), op ? OperatorColor : fn ? FunctionColor : DigitColor, 30);
            }
        }

        void Press(string key)
        {
            switch (key)
            {
                case "C": entry = "0"; stored = 0; pending = null; fresh = true; break;
                case "±": entry = entry.StartsWith("-") ? entry.Substring(1) : entry == "0" ? "0" : "-" + entry; break;
                case "%": entry = Format(Value(entry) / 100.0); break;
                case "←": entry = entry.Length > 1 ? entry.Substring(0, entry.Length - 1) : "0"; break;
                case ",": if (fresh) { entry = "0"; fresh = false; } if (!entry.Contains(",")) entry += ","; break;
                case "÷": case "×": case "−": case "+": case "=":
                    if (pending != null && !fresh) stored = Apply(stored, Value(entry), pending);
                    else if (pending == null) stored = Value(entry);
                    pending = key == "=" ? null : key;
                    entry = Format(stored);
                    fresh = true;
                    break;
                default:
                    entry = fresh || entry == "0" ? key : entry.Length < 14 ? entry + key : entry;
                    fresh = false;
                    break;
            }
            display.text = entry;
        }

        static double Value(string text) =>
            double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

        static double Apply(double a, double b, string op) => op switch
        {
            "÷" => b == 0 ? double.NaN : a / b,
            "×" => a * b,
            "−" => a - b,
            _ => a + b,
        };

        static string Format(double v) =>
            double.IsNaN(v) || double.IsInfinity(v) ? "Ошибка" : v.ToString("0.########", CultureInfo.InvariantCulture).Replace('.', ',');
    }

    /// <summary>Заметки героини: одна заметка, сохраняется между запусками.</summary>
    public sealed class NotesApp : PhoneApp
    {
        const string NotesKey = "sa.notes";
        const string DefaultNotes = "Купить: молоко, батарейки, хлеб\n" +
                                    "Вернуть Ане конспект по статистике\n" +
                                    "Позвонить маме в воскресенье\n\n" +
                                    "Третий вечер подряд кто-то стоит под фонарём напротив. Наверное, показалось.";
        static readonly Color Background = new Color(0.12f, 0.11f, 0.08f);

        readonly InputField text;

        public bool InputFocused => text.isFocused;

        public NotesApp(RectTransform area, Action back)
        {
            Root = Page("Notes", area, Background);
            Header(Root, "Заметки", () => back(), new Color(0.2f, 0.18f, 0.12f));
            text = Ui.Input("Note", Root, "Новая заметка…", 18);
            Ui.Stretch(text.GetComponent<RectTransform>(), 12f, 12f, 12f, 68f);
            text.lineType = InputField.LineType.MultiLineNewline;
            text.textComponent.alignment = TextAnchor.UpperLeft;
            text.textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.placeholder.GetComponent<Text>().alignment = TextAnchor.UpperLeft;
            text.characterLimit = 2000;
            text.text = PlayerPrefs.HasKey(NotesKey) ? PlayerPrefs.GetString(NotesKey) : DefaultNotes;
            text.onEndEdit.AddListener(_ => Save());
        }

        void Save()
        {
            PlayerPrefs.SetString(NotesKey, text.text);
            PlayerPrefs.Save();
        }

        protected override void OnHide()
        {
            if (text.isFocused) text.DeactivateInputField();
            Save();
        }
    }
}
