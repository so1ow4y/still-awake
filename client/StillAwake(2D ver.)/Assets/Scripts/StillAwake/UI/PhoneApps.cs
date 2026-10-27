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

    /// <summary>
    /// Мессенджер в духе Telegram: список чатов (незнакомец и семья) и сам чат — шапка с «печатает…», пузыри
    /// с прокруткой, подсказки ответов и своё сообщение. Семья спит и не отвечает.
    /// </summary>
    public sealed class MessengerApp : PhoneApp
    {
        const int MaxMessages = 200;
        const float PhotoWidth = 260f;
        static readonly Color HeaderColor = new Color(0.09f, 0.13f, 0.17f);
        static readonly Color ChatColor = new Color(0.055f, 0.086f, 0.13f);
        static readonly Color RowColor = new Color(1f, 1f, 1f, 0f);
        static readonly Color IncomingColor = new Color(0.094f, 0.145f, 0.2f);
        static readonly Color OutgoingColor = new Color(0.17f, 0.32f, 0.47f);
        static readonly Color AccentColor = new Color(0.32f, 0.53f, 0.76f);
        static readonly Color MutedColor = new Color(1f, 1f, 1f, 0.5f);

        readonly Phone phone;
        readonly Action<int> replyChosen;
        readonly Action<string> submitted;
        readonly Func<string> clock;
        readonly RectTransform listPage, chatPage, chatList, content, replies;
        readonly ScrollRect scroll;
        readonly Text status, name, initial, typingBubble;
        readonly InputField input;
        float dots;
        int shownCount = -1;

        /// <summary>Открытый чат или null — на экране список чатов.</summary>
        public PhoneChat Current { get; private set; }

        /// <summary>Поле ввода в фокусе: клавиши игры не срабатывают.</summary>
        public bool InputFocused => input.isFocused;

        /// <summary>Сколько секунд без нажатий считается, что игрок дописал.</summary>
        const float ComposeIdleSeconds = 2.5f;
        float lastEdit = -100f;

        /// <summary>
        /// Игрок пишет незнакомцу: в поле есть текст или он что-то набирал/стирал последние пару секунд.
        /// Пока так, незнакомец ждёт — «ему же пишут».
        /// </summary>
        public bool Composing => Current != null && !Current.Silent &&
                                 (input.text.Length > 0 || Time.time - lastEdit < ComposeIdleSeconds);

        public MessengerApp(RectTransform area, Phone phone, Action back, Action<int> replyChosen, Action<string> submitted, Func<string> clock)
        {
            this.phone = phone;
            this.replyChosen = replyChosen;
            this.submitted = submitted;
            this.clock = clock;
            Root = Page("Messenger", area, ChatColor);

            // ---- список чатов
            listPage = Ui.Stretch(Ui.Node("Chats", Root));
            Header(listPage, "Чаты", () => back(), HeaderColor, 56f);
            chatList = Ui.Column("List", listPage, 0f);
            Ui.Stretch(chatList, 0f, 0f, 0f, 56f);

            // ---- чат
            chatPage = Ui.Stretch(Ui.Node("Chat", Root));
            RectTransform header = Header(chatPage, null, CloseChat, HeaderColor, 64f);
            Image avatar = Ui.Image("Avatar", header, new Color(0.35f, 0.36f, 0.4f), ArtLibrary.Circle);
            Ui.Place(avatar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(48f, 0f), new Vector2(42f, 42f));
            initial = Ui.Text("Initial", avatar.transform, "?", 22, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(initial.rectTransform);
            name = Ui.Text("Name", header, "", 19, Color.white, TextAnchor.LowerLeft, FontStyle.Bold);
            Ui.Stretch(name.rectTransform, 100f, 32f, 8f, 8f);
            status = Ui.Text("Status", header, "", 15, MutedColor, TextAnchor.UpperLeft);
            Ui.Stretch(status.rectTransform, 100f, 8f, 8f, 34f);

            // Лента с прокруткой колесом и перетаскиванием. Новые снизу; пока игрок внизу — лента едет за ними.
            Image viewport = Ui.Image("Messages", chatPage, new Color(0f, 0f, 0f, 0f), null, raycast: true);
            Ui.Stretch(viewport.rectTransform, 0f, 60f, 0f, 64f);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = Ui.Node("Content", viewport.transform);
            content.anchorMin = Vector2.zero;
            content.anchorMax = new Vector2(1f, 0f);
            content.pivot = new Vector2(0.5f, 0f);
            content.sizeDelta = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.LowerLeft;
            layout.padding = new RectOffset(8, 8, 6, 6);
            layout.spacing = 6f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport.rectTransform;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            scroll.inertia = true;

            typingBubble = Ui.Text("Typing", chatPage, "", 16, MutedColor, TextAnchor.MiddleLeft, FontStyle.Italic);
            typingBubble.gameObject.SetActive(false);

            replies = Ui.Column("Replies", chatPage, 6f);
            replies.anchorMin = new Vector2(0f, 0f);
            replies.anchorMax = new Vector2(1f, 0f);
            replies.pivot = new Vector2(0.5f, 0f);
            replies.anchoredPosition = new Vector2(0f, 60f);
            replies.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(10, 10, 4, 4);

            Image bar = Ui.Image("Input bar", chatPage, HeaderColor, null, raycast: true);
            bar.rectTransform.anchorMin = Vector2.zero;
            bar.rectTransform.anchorMax = new Vector2(1f, 0f);
            bar.rectTransform.pivot = new Vector2(0.5f, 0f);
            bar.rectTransform.sizeDelta = new Vector2(0f, 56f);
            input = Ui.Input("Message", bar.transform, "Сообщение", 18);
            Ui.Stretch(input.GetComponent<RectTransform>(), 8f, 8f, 60f, 8f);
            input.characterLimit = 300;
            input.onValueChanged.AddListener(_ => lastEdit = Time.time);
            input.onEndEdit.AddListener(_ =>
            {
                if (GameInput.KeyPressed(UnityEngine.InputSystem.Key.Enter) || GameInput.KeyPressed(UnityEngine.InputSystem.Key.NumpadEnter)) Submit();
            });
            Button send = Ui.Button("Send", bar.transform, "→", Submit, 26, AccentColor);
            RectTransform sendRect = Ui.Anchors((RectTransform)send.transform, 1f, 0f, 1f, 1f);
            sendRect.pivot = new Vector2(1f, 0.5f);
            sendRect.sizeDelta = new Vector2(48f, -16f);
            sendRect.anchoredPosition = new Vector2(-6f, 0f);

            chatPage.gameObject.SetActive(false);
            phone.Changed += Refresh;
            Refresh();
        }

        /// <summary>Открыть конкретный чат (с уведомления — чат незнакомца).</summary>
        public void OpenChat(PhoneChat chat)
        {
            Current = chat;
            shownCount = -1;
            listPage.gameObject.SetActive(false);
            chatPage.gameObject.SetActive(true);
            Refresh();
        }

        /// <summary>Esc в мессенджере: из чата — к списку. true — шаг назад сделан.</summary>
        public bool Back()
        {
            if (Current == null) return false;
            CloseChat();
            return true;
        }

        void CloseChat()
        {
            if (input.isFocused) input.DeactivateInputField();
            Current = null;
            chatPage.gameObject.SetActive(false);
            listPage.gameObject.SetActive(true);
            Refresh();
        }

        protected override void OnHide()
        {
            if (input.isFocused) input.DeactivateInputField();
        }

        void Submit()
        {
            string text = input.text.Trim();
            if (text.Length == 0 || Current == null) return;
            input.text = "";
            if (Current.Silent) phone.SendToFamily(Current, text, clock());
            else submitted(text);
            input.ActivateInputField();
        }

        // Прочитанными сообщения отмечает PhoneView: только когда телефон в руке и чат открыт.
        public void Refresh()
        {
            if (Current == null) RefreshList();
            else RefreshChat();
        }

        void RefreshList()
        {
            Ui.Clear(chatList);
            foreach (PhoneChat chat in phone.Chats)
            {
                // Незнакомца нет в списке, пока он не написал первым.
                if (!chat.Silent && chat.Messages.Count == 0) continue;
                PhoneChat captured = chat;
                Button row = Ui.Size(Ui.Button(chat.Name, chatList, "", () => OpenChat(captured), 16, RowColor), 68f);
                Image avatar = Ui.Image("Avatar", row.transform, chat.Silent ? new Color(0.3f, 0.42f, 0.36f) : new Color(0.35f, 0.36f, 0.4f), ArtLibrary.Circle);
                Ui.Place(avatar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(46f, 46f));
                Text letter = Ui.Text("Initial", avatar.transform, chat.Initial, 22, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                Ui.Stretch(letter.rectTransform);

                Text title = Ui.Text("Name", row.transform, chat.Name, 18, Color.white, TextAnchor.LowerLeft, FontStyle.Bold);
                Ui.Stretch(title.rectTransform, 70f, 34f, 60f, 8f);
                PhoneMessage last = chat.Last;
                string preview = chat.Typing ? "<color=#6aa6e8>печатает…</color>"
                    : last == null ? "" : (last.FromPlayer ? "Вы: " : "") + (last.Photo != null ? "[фото] " : "") + last.Text;
                if (preview.Length > 42 && !chat.Typing) preview = preview.Substring(0, 40) + "…";
                Text line = Ui.Text("Preview", row.transform, preview, 15, MutedColor, TextAnchor.UpperLeft);
                Ui.Stretch(line.rectTransform, 70f, 8f, 60f, 36f);
                line.horizontalOverflow = HorizontalWrapMode.Overflow;
                Text time = Ui.Text("Time", row.transform, last != null ? last.Time : "", 13, MutedColor, TextAnchor.UpperRight);
                Ui.Stretch(time.rectTransform, 0f, 30f, 10f, 10f);

                if (chat.Unread > 0)
                {
                    Image badge = Ui.Image("Badge", row.transform, AccentColor, ArtLibrary.Circle);
                    Ui.Place(badge.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f, -10f), new Vector2(26f, 26f));
                    Text count = Ui.Text("Count", badge.transform, chat.Unread.ToString(), 14, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                    Ui.Stretch(count.rectTransform);
                }
            }
        }

        void RefreshChat()
        {
            PhoneChat chat = Current;
            name.text = chat.Name;
            initial.text = chat.Initial;

            // Внизу ли лента сейчас — тогда после перестройки остаёмся внизу (новое сообщение видно сразу).
            bool atBottom = shownCount != chat.Messages.Count || scroll.verticalNormalizedPosition <= 0.02f ||
                            content.rect.height <= scroll.viewport.rect.height;
            shownCount = chat.Messages.Count;

            Ui.Clear(content);
            for (int i = Mathf.Max(0, chat.Messages.Count - MaxMessages); i < chat.Messages.Count; i++)
                AddBubble(chat.Messages[i], chat.Silent);

            bool stalker = !chat.Silent;
            Ui.Clear(replies);
            int replyCount = stalker ? phone.Replies.Count : 0;
            for (int i = 0; i < replyCount; i++)
            {
                int index = i;
                Button chip = Ui.Size(Ui.Button("Reply", replies, phone.Replies[i], () => replyChosen(index), 16, new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.35f)), 32f);
                chip.GetComponentInChildren<Text>().fontStyle = FontStyle.Italic;
            }
            float repliesHeight = replyCount > 0 ? replyCount * 38f + 8f : 0f;
            ((RectTransform)scroll.transform).offsetMin = new Vector2(0f, 60f + repliesHeight + (chat.Typing ? 26f : 0f));

            typingBubble.gameObject.SetActive(chat.Typing);
            Ui.Place(typingBubble.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 60f + repliesHeight), new Vector2(300f, 26f));
            UpdateStatus();

            if (atBottom)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                scroll.StopMovement();
                scroll.verticalNormalizedPosition = 0f;
            }
        }

        public override void Tick(float dt)
        {
            if (Current == null || !Current.Typing) return;
            dots += dt;
            UpdateStatus();
        }

        void UpdateStatus()
        {
            if (Current == null) return;
            if (Current.Typing)
            {
                string animated = new string('.', 1 + (int)(dots * 3f) % 3);
                status.text = "<color=#6aa6e8>печатает" + animated + "</color>";
                typingBubble.text = Current.Name + " печатает" + animated;
            }
            else if (!Current.Silent)
            {
                status.text = Current.Messages.Count > 0 ? "был(а) недавно" : "в сети давно не был(а)";
            }
            else
            {
                status.text = Current.LastSeen;
            }
        }

        void AddBubble(PhoneMessage message, bool showDelivery)
        {
            RectTransform row = Ui.Node("Row", content);
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.childAlignment = message.FromPlayer ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            rowLayout.childControlWidth = rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = rowLayout.childForceExpandHeight = false;

            Image bubble = Ui.Image("Bubble", row, message.FromPlayer ? OutgoingColor : IncomingColor);
            var bubbleLayout = bubble.gameObject.AddComponent<VerticalLayoutGroup>();
            bubbleLayout.padding = new RectOffset(10, 10, 6, 6);
            bubbleLayout.spacing = 6f;
            bubbleLayout.childControlWidth = bubbleLayout.childControlHeight = true;
            bubbleLayout.childForceExpandWidth = true;
            bubbleLayout.childForceExpandHeight = false;

            // Фото — сверху пузыря, на всю его ширину (без арта — тёмная плашка «фото»).
            float photoHeight = 0f;
            if (message.Photo != null)
            {
                Sprite sprite = ArtLibrary.Load(message.Photo);
                photoHeight = sprite != null ? PhotoWidth * sprite.rect.height / sprite.rect.width : PhotoWidth * 0.75f;
                Image photo = Ui.Image("Photo", bubble.transform, sprite != null ? Color.white : new Color(0.02f, 0.02f, 0.03f), sprite);
                var photoSize = photo.gameObject.AddComponent<LayoutElement>();
                photoSize.minHeight = photoSize.preferredHeight = photoHeight;
                if (sprite == null)
                {
                    Text missing = Ui.Text("Missing", photo.transform, "[фото]", 16, MutedColor);
                    Ui.Stretch(missing.rectTransform);
                }
                photoHeight += bubbleLayout.spacing;
            }
            // В чатах семьи у своих сообщений видно, прочитали ли их.
            string delivery = showDelivery && message.FromPlayer ? (message.Read ? " · прочитано" : " · доставлено") : "";
            Text text = Ui.Text("Text", bubble.transform,
                message.Text + "  <size=12><color=#ffffff77>" + message.Time + delivery + "</color></size>", 17, Color.white, TextAnchor.UpperLeft);

            // Пузырь по ширине текста, но не шире трёх четвертей экрана. Высоту считаем сами под эту ширину:
            // до первой раскладки Text не знает своей ширины и даёт высоту меньше, чем строк после переноса, —
            // пузыри наезжали друг на друга и обрезали последнюю строку.
            float width = Mathf.Min(300f, text.preferredWidth + bubbleLayout.padding.horizontal + 2f);
            if (message.Photo != null) width = PhotoWidth + bubbleLayout.padding.horizontal;
            float innerWidth = width - bubbleLayout.padding.horizontal;
            float height = text.cachedTextGeneratorForLayout.GetPreferredHeight(text.text,
                text.GetGenerationSettings(new Vector2(innerWidth, 0f))) / text.pixelsPerUnit + 2f;
            var textSize = text.gameObject.AddComponent<LayoutElement>();
            textSize.minHeight = textSize.preferredHeight = height;
            var size = bubble.gameObject.AddComponent<LayoutElement>();
            size.preferredWidth = width;
            size.flexibleWidth = 0f;
            size.minHeight = size.preferredHeight = height + photoHeight + bubbleLayout.padding.vertical;
        }
    }

    /// <summary>
    /// Набор номера и звонок. Обычные номера не отвечают — только гудки. Номера служб спасения со всего мира
    /// (112, 911, 999, 102…) соединяют с диспетчером: короткий диалог с вариантами ответа, который всегда
    /// заканчивается тем, что свободных машин нет и помощь приедет не позднее 6 утра.
    /// </summary>
    public sealed class DialerApp : PhoneApp
    {
        const float RingSeconds = 9f;
        const float ConnectSeconds = 2.5f;
        static readonly Color Background = new Color(0.04f, 0.04f, 0.05f);
        static readonly Color KeyColor = new Color(0.17f, 0.17f, 0.19f);
        static readonly Color CallColor = new Color(0.16f, 0.62f, 0.3f);
        static readonly Color HangUpColor = new Color(0.82f, 0.18f, 0.16f);
        static readonly Color OptionColor = new Color(0.2f, 0.3f, 0.42f);

        /// <summary>Номера экстренных служб: общеевропейский, американский, британский, австралийский, российские, японские…</summary>
        static readonly string[] EmergencyNumbers =
        {
            "112", "911", "999", "000", "111", "110", "119", "100", "101", "102", "103",
            "01", "02", "03", "15", "17", "18", "190", "192", "193", "997", "998", "1122", "10111",
        };

        public static bool IsEmergency(string number) => Array.IndexOf(EmergencyNumbers, number) >= 0;

        /// <summary>
        /// Реплика диспетчера и ответы героини. Next: индекс следующей реплики, End — вызов принят,
        /// Prank — диспетчер решил, что это розыгрыш, и никто не приедет.
        /// </summary>
        sealed class Step
        {
            public const int End = -1;
            public const int Prank = -2;
            public readonly string Line;
            public readonly (string Answer, int Next)[] Options;

            public Step(string line, params (string, int)[] options)
            {
                Line = line;
                Options = options;
            }
        }

        const string FinalLine = "Вызов принят. Но все экипажи сейчас на выездах — ночь тяжёлая. Мы приедем не позднее шести утра. " +
                                 "Заприте дверь, не открывайте никому и не выходите. Продержитесь.";

        const string PrankLine = "Девушка, вы время видели? Ложный вызов — это административная ответственность. " +
                                 "Ложитесь спать и не занимайте линию.";

        // В каждом диалоге есть ответы, после которых диспетчер решает, что это розыгрыш, — и никто не приедет.
        static readonly Step[][] Dialogs =
        {
            new[]
            {
                new Step("Служба спасения, что у вас случилось?",
                    ("Кто-то ходит под моим окном и пишет мне", 1), ("Мне кажется, за мной следят", 1), ("Он хочет войти в квартиру!", 2)),
                new Step("Вы его видите сейчас? Он вам угрожает?",
                    ("Он стоял прямо за окном", 2), ("Он пишет, что знает, где я", 2), ("Ну… он мне лайки ставит. И пишет", 3)),
                new Step("Назовите адрес. Вы одна в квартире?",
                    ("Да, одна. Пожалуйста, приезжайте быстрее", Step.End), ("Одна. Дверь… кажется, не заперта", Step.End)),
                new Step("То есть вам пишет поклонник. Это повод вызывать наряд в час ночи?",
                    ("Он у моего дома! Я видела его!", 2), ("Нет… наверное, нет. Извините", Step.Prank)),
            },
            new[]
            {
                new Step("Дежурная часть, слушаю.",
                    ("У меня под дверью незнакомый мужчина", 1), ("Он прислал мне фото моего окна", 1), ("(шёпотом) помогите…", 4)),
                new Step("Он пытался проникнуть в помещение? Есть оружие, вы видели?",
                    ("Дёргал ручку двери", 2), ("Не знаю… я прячусь", 2), ("Может, я преувеличиваю…", 3)),
                new Step("Оставайтесь на линии. Диктуйте адрес полностью, с номером квартиры.",
                    ("Диктую адрес", Step.End)),
                new Step("Если вы не уверены, позвоните утром участковому. Вы точно в опасности?",
                    ("Да! Пожалуйста, он здесь", 2), ("Не знаю… простите", Step.Prank)),
                new Step("Говорите громче, вас не слышно. Это шутка? Мне сегодня уже трое подростков звонили.",
                    ("Это не шутка! Он ломится в дверь", 2), ("(молчать — он может услышать)", Step.Prank)),
            },
            new[]
            {
                new Step("Экстренная служба. Говорите тише, если вы в опасности. Вам нужна полиция?",
                    ("Да. Полиция", 1), ("Не знаю, кто нужен. Он рядом", 1), ("Ха… да нет, всё нормально", 3)),
                new Step("Он сейчас в доме?",
                    ("Пока снаружи. Ходит вокруг", 2), ("Не знаю. Я слышала шаги", 2), ("Он мне просто пишет. Аноним", 4)),
                new Step("Я записываю. Сколько вам лет и как вас зовут?",
                    ("Называю имя и адрес", Step.End)),
                new Step("Если всё нормально, зачем вы звоните в экстренную службу?",
                    ("Мне страшно. За мной кто-то следит", 1), ("Простите, ошиблась номером", Step.Prank)),
                new Step("Анонимные сообщения — это в киберполицию, заявление онлайн. Вам кто-то угрожает лично?",
                    ("Он стоит у моего окна", 2), ("Нет, пока только пишет", Step.Prank)),
            },
        };

        readonly Action<string> callStarted;
        readonly Action rescueRequested;
        readonly Action pranked;
        readonly Text display, callTitle, callState, operatorText;
        readonly RectTransform keypad, callScreen, options;
        string number = "";
        float callTime = -1f;
        bool emergency, connected;
        Step[] dialog;

        public bool InCall => callTime >= 0f;

        /// <summary>Вызов в службу спасения уже принят: повторный звонок — короткое подтверждение.</summary>
        public bool RescueRequested { get; private set; }

        public DialerApp(RectTransform area, Action back, Action<string> callStarted, Action rescueRequested, Action pranked)
        {
            this.callStarted = callStarted;
            this.rescueRequested = rescueRequested;
            this.pranked = pranked;
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
            Ui.Place(callTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(380f, 50f));
            callState = Ui.Text("State", callScreen, "", 18, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter);
            Ui.Place(callState.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -86f), new Vector2(380f, 34f));

            // Реплика диспетчера сверху, варианты ответа — над кнопкой «Завершить».
            operatorText = Ui.Text("Operator", callScreen, "", 19, Color.white, TextAnchor.UpperLeft, FontStyle.Italic);
            Ui.Stretch(operatorText.rectTransform, 18f, 330f, 18f, 120f);
            options = Ui.Column("Options", callScreen, 8f);
            options.anchorMin = new Vector2(0f, 0f);
            options.anchorMax = new Vector2(1f, 0f);
            options.pivot = new Vector2(0.5f, 0f);
            options.anchoredPosition = new Vector2(0f, 140f);
            options.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(14, 14, 0, 0);
            options.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.LowerCenter;
            options.sizeDelta = new Vector2(0f, 180f);

            Button hangUp = Ui.Button("Hang up", callScreen, "Завершить", HangUp, 22, HangUpColor);
            Ui.Place((RectTransform)hangUp.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(200f, 64f));
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
            emergency = IsEmergency(number);
            connected = false;
            operatorText.text = "";
            Ui.Clear(options);
            callState.text = "Вызов…";
            callStarted(number);
            Sfx.StartRingback();
        }

        void HangUp()
        {
            Sfx.StopRingback();
            callTime = -1f;
            connected = false;
            Ui.Clear(options);
            callScreen.gameObject.SetActive(false);
            keypad.gameObject.SetActive(true);
        }

        protected override void OnHide()
        {
            if (InCall) HangUp();
        }

        void Connect()
        {
            connected = true;
            Sfx.StopRingback();
            callState.text = "Соединено";
            if (RescueRequested)
            {
                ShowLine("Ваш вызов уже зарегистрирован. Экипаж будет не позднее шести утра. Держитесь.",
                    ("Хорошо… Я жду", Step.End));
                return;
            }
            dialog = Dialogs[UnityEngine.Random.Range(0, Dialogs.Length)];
            ShowStep(0);
        }

        void ShowStep(int index)
        {
            Step step = dialog[index];
            ShowLine(step.Line, step.Options);
        }

        void ShowLine(string line, params (string Answer, int Next)[] answers)
        {
            operatorText.text = "— " + line;
            Ui.Clear(options);
            foreach (var option in answers)
            {
                var captured = option;
                Button button = Ui.Size(Ui.Button("Answer", options, captured.Answer, () => Answer(captured.Next), 16, OptionColor), 44f);
                button.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            }
        }

        void Answer(int next)
        {
            Sfx.Click();
            if (!InCall) return;
            if (next == Step.Prank)
            {
                operatorText.text = "— " + PrankLine;
                Ui.Clear(options);
                callState.text = "Диспетчер повесил трубку";
                callTime = RingSeconds;
                pranked?.Invoke();
                return;
            }
            if (next != Step.End)
            {
                ShowStep(next);
                return;
            }

            if (!RescueRequested)
            {
                RescueRequested = true;
                operatorText.text = "— " + FinalLine;
                rescueRequested?.Invoke();
            }
            else
            {
                operatorText.text = "— Оставайтесь в безопасном месте.";
            }
            Ui.Clear(options);
            callState.text = "Диспетчер завершил вызов";
            callTime = RingSeconds;   // кнопка «Завершить» или автозавершение через несколько секунд
        }

        public override void Tick(float dt)
        {
            if (!InCall) return;
            callTime += dt;
            if (emergency)
            {
                if (!connected)
                {
                    if (callTime >= ConnectSeconds) Connect();
                    return;
                }
                // После последней реплики диспетчера звонок сам завершается.
                if (options.childCount == 0 && callTime > RingSeconds + 7f) HangUp();
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

    /// <summary>
    /// Настройки телефона: звук уведомлений. Со звуком сообщение в укрытии слышно на всю комнату,
    /// на вибрации — тише, но шум копится, без звука — только экран мигнёт.
    /// </summary>
    public sealed class SettingsApp : PhoneApp
    {
        static readonly Color Background = new Color(0.06f, 0.065f, 0.08f);
        static readonly Color OptionColor = new Color(0.12f, 0.13f, 0.16f);
        static readonly Color SelectedColor = new Color(0.2f, 0.42f, 0.7f);

        readonly Phone phone;
        readonly Button[] options = new Button[3];

        static readonly (RingMode Mode, string Title, string Note)[] Modes =
        {
            (RingMode.Sound, "Звук", "Уведомления слышно на всю комнату"),
            (RingMode.Vibrate, "Вибрация", "Тише, но в тишине жужжание слышно"),
            (RingMode.Silent, "Без звука", "Только экран мигнёт"),
        };

        public SettingsApp(RectTransform area, Phone phone, Action back)
        {
            this.phone = phone;
            Root = Page("Settings", area, Background);
            Header(Root, "Настройки", () => back(), Background);

            RectTransform column = Ui.Column("Options", Root, 8f, 14);
            Ui.Stretch(column, 0f, 0f, 0f, 60f);
            Ui.Size(Ui.Text("Section", column, "ЗВУКИ И ВИБРАЦИЯ", 14, new Color(1f, 1f, 1f, 0.5f), TextAnchor.LowerLeft, FontStyle.Bold), 28f);
            for (int i = 0; i < Modes.Length; i++)
            {
                RingMode mode = Modes[i].Mode;
                string label = "<b>" + Modes[i].Title + "</b>\n<size=13><color=#ffffff99>" + Modes[i].Note + "</color></size>";
                Button option = Ui.Size(Key(column, label, () => phone.SetRing(mode), OptionColor, 18), 62f);
                Text text = option.GetComponentInChildren<Text>();
                text.alignment = TextAnchor.MiddleLeft;
                text.rectTransform.offsetMin = new Vector2(16f, text.rectTransform.offsetMin.y);
                options[i] = option;
            }

            phone.Changed += Refresh;
            Refresh();
        }

        void Refresh()
        {
            for (int i = 0; i < options.Length; i++)
            {
                ColorBlock colors = options[i].colors;
                colors.normalColor = Modes[i].Mode == phone.Ring ? SelectedColor : OptionColor;
                colors.selectedColor = colors.normalColor;
                colors.highlightedColor = Color.Lerp(colors.normalColor, Color.white, 0.12f);
                options[i].colors = colors;
            }
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
