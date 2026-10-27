using System;
using UnityEngine;
using UnityEngine.UI;

namespace StillAwake
{
    public enum PhoneMode
    {
        /// <summary>Спрятан целиком, внизу только язычок с числом непрочитанных.</summary>
        Hidden,
        /// <summary>Торчит снизу на четверть: время и уведомление.</summary>
        Peek,
        /// <summary>В руке целиком: рабочий стол и приложения.</summary>
        Full,
    }

    /// <summary>
    /// Телефон героини (корпус — Phone/phone_100.png, чёрный, без полей). Три положения: спрятан, на четверть,
    /// целиком — переключаются Tab (свернуть/открыть) и H (спрятать/показать), телефон плавно выезжает.
    /// На экране — рабочий стол с приложениями: Сообщения (счётчик непрочитанных на иконке), Телефон,
    /// Калькулятор, Заметки.
    /// </summary>
    public sealed class PhoneView : MonoBehaviour
    {
        // Разметка phone_100.png 600×1200 в пикселях от левого верхнего угла.
        const float ImageWidth = 600f;
        const float ImageHeight = 1200f;
        static readonly PxRect ScreenArea = new PxRect(24, 24, 552, 1152);

        const float Scale = 0.75f;
        const float Margin = 40f;
        const float StatusHeight = 30f;
        const float HomeBarHeight = 28f;
        const float SlideSmoothTime = 0.12f;
        const float BannerSeconds = 4f;

        static readonly Color BodyColor = new Color(0.03f, 0.03f, 0.035f);
        static readonly Color WallpaperColor = new Color(0.06f, 0.07f, 0.12f);
        static readonly Color BannerColor = new Color(0.16f, 0.17f, 0.2f, 0.96f);
        static readonly Color HandleColor = new Color(0.05f, 0.05f, 0.06f, 0.92f);

        public PhoneMode Mode { get; private set; } = PhoneMode.Peek;
        /// <summary>Телефон открыт целиком.</summary>
        public bool IsOpen => Mode == PhoneMode.Full;
        /// <summary>Курсор над телефоном или его язычком — камера не двигается.</summary>
        public bool PointerOver =>
            RectTransformUtility.RectangleContainsScreenPoint(body, GameInput.PointerPosition, null) && Mode != PhoneMode.Hidden ||
            handle.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(handle, GameInput.PointerPosition, null);
        /// <summary>Печатают в мессенджере или заметках — клавиши игры не срабатывают.</summary>
        public bool Typing => messenger.InputFocused || notes.InputFocused;
        /// <summary>Игрок набирает сообщение незнакомцу (или только что набирал).</summary>
        public bool PlayerComposing => messenger.Visible && messenger.Composing;

        public event Action<bool> OpenChanged;
        public event Action<int> ReplyChosen;
        /// <summary>Игрок написал своё сообщение и отправил его.</summary>
        public event Action<string> MessageSubmitted;
        /// <summary>Набрали номер и нажали «Вызов».</summary>
        public event Action<string> CallStarted;
        /// <summary>Диспетчер службы спасения принял вызов: помощь приедет к 6 утра.</summary>
        public event Action RescueRequested;
        /// <summary>Диспетчер решил, что это розыгрыш: никто не приедет.</summary>
        public event Action RescueRefused;

        Phone phone;
        RectTransform canvas, body, handle, home, apps, banner, peekCatcher;
        Text clock, bannerText, handleBadge, homeClock, messagesBadge;
        GameObject messagesBadgeObject;
        MessengerApp messenger;
        DialerApp dialer;
        CalculatorApp calculator;
        NotesApp notes;
        SettingsApp settingsApp;
        Text signal;
        PhoneApp[] allApps;
        float slideVelocity, bannerTimer;
        int lastCount;

        public static PhoneView Create(RectTransform canvas, Phone phone)
        {
            RectTransform root = Ui.Stretch(Ui.Node("Phone", canvas));
            var view = root.gameObject.AddComponent<PhoneView>();
            view.phone = phone;
            view.canvas = canvas;
            view.Build(root);

            phone.Changed += view.Refresh;
            view.Refresh();
            view.SetMode(PhoneMode.Peek, instant: true);
            return view;
        }

        public void Open() => SetMode(PhoneMode.Full);
        public void Close() => SetMode(Mode == PhoneMode.Hidden ? PhoneMode.Hidden : PhoneMode.Peek);
        public void Toggle() => SetMode(Mode == PhoneMode.Full ? PhoneMode.Peek : PhoneMode.Full);
        public void ToggleHidden() => SetMode(Mode == PhoneMode.Hidden ? PhoneMode.Peek : PhoneMode.Hidden);
        /// <summary>Опустить до четверти (торчит снизу), а если уже опущен или спрятан — достать целиком.</summary>
        public void TogglePeek() => SetMode(Mode == PhoneMode.Full ? PhoneMode.Peek : PhoneMode.Full);

        /// <summary>
        /// Esc, пока телефон в руке: из чата — к списку, из приложения — на рабочий стол. false — уже на рабочем
        /// столе, шагать некуда: тогда Esc ставит паузу, а телефон остаётся в руке.
        /// </summary>
        public bool Back()
        {
            if (messenger.Visible && messenger.Back()) return true;
            foreach (PhoneApp app in allApps)
            {
                if (!app.Visible) continue;
                GoHome();
                return true;
            }
            return false;
        }

        /// <summary>Кнопка «домой» внизу экрана: из приложения — на рабочий стол, с рабочего стола — опустить до четверти.</summary>
        void HomeButton()
        {
            foreach (PhoneApp app in allApps)
            {
                if (!app.Visible) continue;
                GoHome();
                return;
            }
            SetMode(PhoneMode.Peek);
        }

        /// <summary>Открыть мессенджер (с уведомления или из кода).</summary>
        public void OpenMessenger()
        {
            ShowApp(messenger);
            messenger.OpenChat(phone.Stalker);
            SetMode(PhoneMode.Full);
        }

        /// <summary>Незнакомец «печатает» — пока модель пишет.</summary>
        public void SetTyping(bool value) => phone.SetTyping(value);

        public void SetClock(string time)
        {
            clock.text = time;
            homeClock.text = time;
        }

        public void SetMode(PhoneMode mode, bool instant = false)
        {
            bool wasOpen = IsOpen;
            Mode = mode;
            if (mode != PhoneMode.Full && Typing) EventSystemDeselect();
            if (mode == PhoneMode.Full && messenger.Visible) phone.MarkRead(messenger.Current);
            peekCatcher.gameObject.SetActive(mode == PhoneMode.Peek);
            handle.gameObject.SetActive(mode == PhoneMode.Hidden);
            if (instant) body.anchoredPosition = new Vector2(body.anchoredPosition.x, TargetY);
            Refresh();
            if (wasOpen != IsOpen) OpenChanged?.Invoke(IsOpen);
        }

        float FullHeight => ImageHeight * Scale;

        float TargetY => Mode switch
        {
            PhoneMode.Full => (canvas.rect.height - FullHeight) / 2f,
            PhoneMode.Peek => -FullHeight * 0.75f,
            _ => -FullHeight - 20f,
        };

        static void EventSystemDeselect()
        {
            if (UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        }

        void Build(RectTransform root)
        {
            Sprite art = ArtLibrary.Load(ArtPaths.Phone);
            Image bodyImage = Ui.Image("Phone body", root, art != null ? Color.white : BodyColor, art, raycast: true);
            body = bodyImage.rectTransform;
            Ui.Place(body, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Margin, 0f), new Vector2(ImageWidth, ImageHeight) * Scale);

            Image screen = Ui.Image("Screen", body, WallpaperColor, null, raycast: true);
            Ui.AnchorsPx(screen.rectTransform, ScreenArea, ImageWidth, ImageHeight);
            screen.gameObject.AddComponent<RectMask2D>();

            // Статус-бар: время, связь, батарея.
            clock = Ui.Text("Clock", screen.transform, "00:00", 16, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            Ui.Place(clock.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, 0f), new Vector2(120f, StatusHeight));
            clock.rectTransform.pivot = new Vector2(0f, 1f);
            signal = Ui.Text("Signal", screen.transform, "LTE  73%", 14, Color.white, TextAnchor.MiddleRight);
            Ui.Place(signal.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, 0f), new Vector2(240f, StatusHeight));

            RectTransform content = Ui.Node("Content", screen.transform);
            Ui.Stretch(content, 0f, HomeBarHeight, 0f, StatusHeight);

            home = BuildHome(content);
            apps = Ui.Stretch(Ui.Node("Apps", content));
            messenger = new MessengerApp(apps, phone, GoHome, index => ReplyChosen?.Invoke(index), text => MessageSubmitted?.Invoke(text), () => clock.text);
            dialer = new DialerApp(apps, GoHome, number => CallStarted?.Invoke(number), () => RescueRequested?.Invoke(), () => RescueRefused?.Invoke());
            calculator = new CalculatorApp(apps, GoHome);
            notes = new NotesApp(apps, GoHome);
            settingsApp = new SettingsApp(apps, phone, GoHome);
            allApps = new PhoneApp[] { messenger, dialer, calculator, notes, settingsApp };

            // Полоска «домой» внизу экрана.
            Button homeBar = Ui.Button("Home bar", screen.transform, "", HomeButton, 12, new Color(1f, 1f, 1f, 0f));
            Ui.Place((RectTransform)homeBar.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(200f, HomeBarHeight));
            Image line = Ui.Image("Line", homeBar.transform, new Color(1f, 1f, 1f, 0.7f));
            Ui.Place(line.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(110f, 5f));

            // Уведомление под статус-баром: видно, когда телефон торчит снизу, и коротко поверх других приложений.
            Button bannerButton = Ui.Button("Banner", screen.transform, "", OpenMessenger, 16, BannerColor);
            banner = (RectTransform)bannerButton.transform;
            banner.anchorMin = new Vector2(0f, 1f);
            banner.anchorMax = Vector2.one;
            banner.pivot = new Vector2(0.5f, 1f);
            banner.sizeDelta = new Vector2(-20f, 100f);
            banner.anchoredPosition = new Vector2(0f, -StatusHeight - 4f);
            bannerText = bannerButton.GetComponentInChildren<Text>();
            bannerText.alignment = TextAnchor.UpperLeft;
            bannerText.fontSize = 16;
            bannerText.rectTransform.offsetMin = new Vector2(14f, 8f);
            bannerText.rectTransform.offsetMax = new Vector2(-12f, -8f);

            // Пока телефон торчит снизу, клик по нему открывает его целиком.
            Button catcher = Ui.Button("Peek catcher", body, "", () =>
            {
                if (phone.Stalker.Unread > 0) OpenMessenger();
                else Open();
            }, 12, new Color(1f, 1f, 1f, 0f));
            peekCatcher = (RectTransform)catcher.transform;
            peekCatcher.anchorMin = new Vector2(0f, 0.75f);
            peekCatcher.anchorMax = Vector2.one;
            peekCatcher.offsetMin = peekCatcher.offsetMax = Vector2.zero;

            // Язычок спрятанного телефона.
            Button handleButton = Ui.Button("Phone handle", root, "▲ Телефон", () => SetMode(PhoneMode.Peek), 18, HandleColor);
            handle = (RectTransform)handleButton.transform;
            Ui.Place(handle, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Margin - 60f, 0f), new Vector2(170f, 40f));
            Image badge = Ui.Image("Badge", handle, Ui.Accent, ArtLibrary.Circle);
            Ui.Place(badge.rectTransform, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(-6f, -4f), new Vector2(30f, 30f));
            handleBadge = Ui.Text("Count", badge.transform, "", 16, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(handleBadge.rectTransform);
        }

        RectTransform BuildHome(RectTransform content)
        {
            RectTransform page = Ui.Stretch(Ui.Node("Home", content));
            homeClock = Ui.Text("Big clock", page, "00:00", 64, Color.white, TextAnchor.MiddleCenter);
            Ui.Place(homeClock.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(380f, 90f));
            Text date = Ui.Text("Date", page, "пятница, 13 ноября", 18, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleCenter);
            Ui.Place(date.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -232f), new Vector2(380f, 30f));

            RectTransform row = Ui.Node("Apps row", page);
            Ui.Place(row, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(380f, 110f));
            messagesBadge = AppIcon(row, 0, "Сообщения", "•••", new Color(0.2f, 0.55f, 0.9f), () => ShowApp(messenger));
            messagesBadgeObject = messagesBadge.transform.parent.gameObject;
            AppIcon(row, 1, "Телефон", "#", new Color(0.18f, 0.7f, 0.35f), () => ShowApp(dialer));
            AppIcon(row, 2, "Калькулятор", "±", new Color(0.95f, 0.55f, 0.1f), () => ShowApp(calculator));
            AppIcon(row, 3, "Заметки", "≡", new Color(0.95f, 0.8f, 0.2f), () => ShowApp(notes));

            // Второй ряд — над первым.
            RectTransform row2 = Ui.Node("Apps row 2", page);
            Ui.Place(row2, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(380f, 110f));
            AppIcon(row2, 0, "Настройки", "⚙", new Color(0.45f, 0.47f, 0.52f), () => ShowApp(settingsApp));
            return page;
        }

        /// <summary>Иконка приложения; возвращает текст бейджа (у остальных он просто не показывается).</summary>
        static Text AppIcon(RectTransform row, int index, string title, string glyph, Color color, UnityEngine.Events.UnityAction open)
        {
            Button icon = Ui.Button(title, row, glyph, open, 30, color);
            Ui.Place((RectTransform)icon.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f + index * 92f, 0f), new Vector2(76f, 76f));
            icon.GetComponentInChildren<Text>().fontStyle = FontStyle.Bold;
            Text label = Ui.Text("Label", icon.transform, title, 13, Color.white, TextAnchor.UpperCenter);
            Ui.Place(label.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(96f, 24f));

            Image badge = Ui.Image("Badge", icon.transform, Ui.Accent, ArtLibrary.Circle);
            Ui.Place(badge.rectTransform, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(-4f, -4f), new Vector2(28f, 28f));
            Text count = Ui.Text("Count", badge.transform, "", 15, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(count.rectTransform);
            badge.gameObject.SetActive(false);
            return count;
        }

        void ShowApp(PhoneApp app)
        {
            home.gameObject.SetActive(false);
            foreach (PhoneApp other in allApps)
                if (other != app) other.Hide();
            app.Show();
            bannerTimer = 0f;
            Refresh();
        }

        void GoHome()
        {
            foreach (PhoneApp app in allApps) app.Hide();
            home.gameObject.SetActive(true);
            Refresh();
        }

        void Refresh()
        {
            if (Mode == PhoneMode.Full && messenger.Visible) phone.MarkRead(messenger.Current);

            // Режим звука виден в статус-баре: «без звука» / «вибро».
            string ring = phone.Ring == RingMode.Silent ? "<color=#ff8a80>без звука</color>   "
                : phone.Ring == RingMode.Vibrate ? "вибро   " : "";
            signal.text = ring + "LTE  73%";

            bool unread = phone.Unread > 0;
            messagesBadgeObject.SetActive(unread);
            messagesBadge.text = phone.Unread.ToString();
            handleBadge.transform.parent.gameObject.SetActive(unread);
            handleBadge.text = phone.Unread.ToString();

            // Новое входящее — показать уведомление на пару секунд, если чат не открыт.
            if (phone.Messages.Count > lastCount && phone.Messages.Count > 0 && !phone.Messages[phone.Messages.Count - 1].FromPlayer)
                bannerTimer = BannerSeconds;
            lastCount = phone.Messages.Count;

            PhoneMessage last = null;
            for (int i = phone.Messages.Count - 1; i >= 0 && last == null; i--)
                if (!phone.Messages[i].FromPlayer) last = phone.Messages[i];
            string preview = last != null ? last.Text : "Нет новых сообщений";
            if (phone.Typing && (last == null || phone.Unread == 0)) preview = "<i>печатает…</i>";
            bannerText.text = "<b>" + Phone.ContactName + "</b>" + (unread ? "  <color=#ff8a80>" + phone.Unread + " новых</color>" : "") +
                              "\n" + preview;
            UpdateBanner();
        }

        void UpdateBanner()
        {
            bool peekNotice = Mode == PhoneMode.Peek && (phone.Unread > 0 || phone.Typing);
            bool headsUp = Mode == PhoneMode.Full && bannerTimer > 0f && !messenger.Visible && phone.Unread > 0;
            banner.gameObject.SetActive(peekNotice || headsUp);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            // Семья «читает» сообщения по игровому времени: на паузе таймеры стоят.
            phone.Tick(Time.deltaTime, clock.text);
            float y = Mathf.SmoothDamp(body.anchoredPosition.y, TargetY, ref slideVelocity, SlideSmoothTime, Mathf.Infinity, dt);
            body.anchoredPosition = new Vector2(body.anchoredPosition.x, y);

            if (bannerTimer > 0f)
            {
                bannerTimer -= dt;
                if (bannerTimer <= 0f) UpdateBanner();
            }
            foreach (PhoneApp app in allApps)
                if (app.Visible) app.Tick(dt);
        }
    }
}
