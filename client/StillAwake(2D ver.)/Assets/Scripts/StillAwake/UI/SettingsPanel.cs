using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace StillAwake
{
    /// <summary>
    /// Настройки: управление (переназначение клавиш и кнопок мыши), ИИ (нейросеть или заготовленные реплики,
    /// адрес сервера, проверка модели) и аккаунт (смена пароля, выход, ссылка на свои ночи в панели).
    /// Открывается из главного меню и из паузы. Esc обрабатывает владелец через Back().
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        enum Tab
        {
            Controls,
            Ai,
            Account,
        }

        static readonly Color WindowColor = new Color(0.06f, 0.06f, 0.08f, 1f);
        static readonly Color TabColor = new Color(0.12f, 0.12f, 0.15f, 0.95f);
        static readonly Color KeyColor = new Color(0.18f, 0.19f, 0.23f, 0.95f);
        static readonly Color CaptureColor = new Color(0.45f, 0.12f, 0.12f, 0.95f);

        public bool IsOpen => gameObject.activeSelf;
        public bool IsCapturing => capturing.HasValue;

        public event Action Closed;

        readonly Dictionary<Tab, RectTransform> pages = new Dictionary<Tab, RectTransform>();
        readonly Dictionary<Tab, Button> tabButtons = new Dictionary<Tab, Button>();
        /// <summary>Строка действия, куда перестраиваются кнопки его клавиш.</summary>
        readonly Dictionary<GameAction, RectTransform> keyRows = new Dictionary<GameAction, RectTransform>();
        (GameAction Action, int Slot)? capturing;
        int captureFrame;

        Text serverStatus, accountTitle, accountStatus, accountFile;
        InputField newPassword, repeatPassword;
        bool accountBusy;
        InputField serverInput;
        UnityEngine.UI.Toggle aiToggle;
        bool checking;

        public static SettingsPanel Create(Transform parent)
        {
            Image shade = Ui.Image("Settings", parent, new Color(0f, 0f, 0f, 0.7f), null, raycast: true);
            Ui.Stretch(shade.rectTransform);
            var panel = shade.gameObject.AddComponent<SettingsPanel>();

            Image window = Ui.Image("Window", shade.transform, WindowColor, null, raycast: true);
            Ui.Place(window.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1040f, 960f));
            RectTransform column = Ui.Stretch(Ui.Column("Content", window.transform, 14f, 32));

            Ui.Size(Ui.Text("Title", column, "Настройки", 44, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold), 56f);

            RectTransform tabs = Ui.Size(Ui.Row("Tabs", column, 10f), 58f);
            panel.AddTab(tabs, Tab.Controls, "Управление");
            panel.AddTab(tabs, Tab.Ai, "ИИ и сервер");
            panel.AddTab(tabs, Tab.Account, "Аккаунт");

            RectTransform body = Ui.Node("Body", column);
            var bodySize = body.gameObject.AddComponent<LayoutElement>();
            bodySize.flexibleHeight = 1f;
            bodySize.minHeight = 600f;

            panel.pages[Tab.Controls] = panel.BuildControls(body);
            panel.pages[Tab.Ai] = panel.BuildAi(body);
            panel.pages[Tab.Account] = panel.BuildAccount(body);

            RectTransform footer = Ui.Size(Ui.Row("Footer", column, 12f), 60f);
            Ui.Size(Ui.Button("Back", footer, "Назад [Esc]", panel.Close, 24), 60f, 260f);

            KeyBindings.Changed += panel.RefreshKeys;
            panel.gameObject.SetActive(false);
            return panel;
        }

        void OnDestroy() => KeyBindings.Changed -= RefreshKeys;

        public void Open() => Open(Tab.Controls);


        void Open(Tab tab)
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Show(tab);
        }

        public void Close()
        {
            capturing = null;
            if (!IsOpen) return;
            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        /// <summary>Esc: отменить ожидание клавиши или закрыть настройки. true — Esc обработан.</summary>
        public bool Back()
        {
            if (!IsOpen) return false;
            if (capturing.HasValue)
            {
                capturing = null;
                RefreshKeys();
                return true;
            }
            Close();
            return true;
        }

        void Update()
        {
            if (!capturing.HasValue || Time.frameCount == captureFrame) return;
            Key key = KeyBindings.PressedThisFrame();
            if (key == Key.None || key == Key.Escape) return;   // Esc — отмена, её обрабатывает Back()

            (GameAction action, int slot) = capturing.Value;
            capturing = null;
            if (key == Key.Delete) KeyBindings.Set(action, slot, Key.None);
            else if (KeyBindings.CanBind(key)) KeyBindings.Set(action, slot, key);
            RefreshKeys();
        }

        void AddTab(RectTransform row, Tab tab, string title)
        {
            Button button = Ui.Size(Ui.Button(title, row, title, () => Show(tab), 24, TabColor), 58f, 240f);
            tabButtons[tab] = button;
        }

        void Show(Tab tab)
        {
            capturing = null;
            foreach (var pair in pages) pair.Value.gameObject.SetActive(pair.Key == tab);
            foreach (var pair in tabButtons)
            {
                ColorBlock colors = pair.Value.colors;
                colors.normalColor = pair.Key == tab ? Ui.PrimaryButton : TabColor;
                colors.selectedColor = colors.normalColor;
                pair.Value.colors = colors;
            }
            if (tab == Tab.Controls) RefreshKeys();
            if (tab == Tab.Ai) OpenAi();
            if (tab == Tab.Account) OpenAccount_();
        }

        // ------------------------------------------------------------------ управление

        RectTransform BuildControls(RectTransform body)
        {
            RectTransform page = Ui.Stretch(Ui.Column("Controls", body, 4f));
            Ui.Size(Ui.Text("Hint", page, "Клавиш у действия сколько угодно: «+» — добавить, клик по клавише — заменить, Delete — убрать, Esc — отмена.",
                20, Ui.Muted, TextAnchor.MiddleLeft), 34f);

            foreach (KeyBindings.Definition def in KeyBindings.All)
            {
                RectTransform row = Ui.Size(Ui.Row(def.Action.ToString(), page, 8f), 40f);
                Ui.Size(Ui.Text("Title", row, def.Title, 21, Color.white, TextAnchor.MiddleLeft), 40f);
                RectTransform keys = Ui.Row("Keys", row, 6f);
                keys.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleRight;
                var keysSize = keys.gameObject.AddComponent<LayoutElement>();
                keysSize.preferredWidth = keysSize.minWidth = 560f;
                keysSize.flexibleWidth = 0f;
                keyRows[def.Action] = keys;
            }

            RectTransform resetRow = Ui.Size(Ui.Row("Reset row", page, 10f), 54f);
            Ui.Size(Ui.Button("Reset", resetRow, "Вернуть клавиши по умолчанию", KeyBindings.ResetToDefaults, 22), 54f, 420f);
            return page;
        }

        void StartCapture(GameAction action, int slot)
        {
            capturing = (action, slot);
            captureFrame = Time.frameCount;
            RefreshKeys();
        }

        /// <summary>Перестроить кнопки клавиш: по одной на назначенную клавишу и «+» в конце.</summary>
        void RefreshKeys()
        {
            foreach (var pair in keyRows)
            {
                GameAction action = pair.Key;
                Ui.Clear(pair.Value);
                IReadOnlyList<Key> keys = KeyBindings.GetKeys(action);
                for (int slot = 0; slot <= keys.Count; slot++)
                {
                    int index = slot;
                    bool adding = slot == keys.Count;
                    bool waiting = capturing.HasValue && capturing.Value.Action == action && capturing.Value.Slot == slot;
                    string label = waiting ? "нажми…" : adding ? "+" : KeyBindings.KeyName(keys[slot]);
                    Button button = Ui.Size(Ui.Button("Key " + slot, pair.Value, label, () => StartCapture(action, index), 20, waiting ? CaptureColor : KeyColor),
                        40f, adding && !waiting ? 48f : 118f);
                    ColorBlock colors = button.colors;
                    colors.selectedColor = colors.normalColor;
                    button.colors = colors;
                }
            }
        }

        // ------------------------------------------------------------------ ИИ и сервер

        RectTransform BuildAi(RectTransform body)
        {
            RectTransform page = Ui.Stretch(Ui.Column("AI", body, 14f));
            aiToggle = Ui.Size(Ui.Checkbox("Use AI", page, "Сообщения незнакомца пишет нейросеть", BackendClient.AiEnabled,
                value => BackendClient.AiEnabled = value), 44f);
            Ui.Size(Ui.Text("AI note", page,
                "Выключено — заготовленные реплики и подсказки, запросов к модели нет. Модель работает на сервере игры " +
                "(LM Studio или другой провайдер в его настройках); входить никуда не нужно — игра подключается сама.",
                20, Ui.Muted, TextAnchor.UpperLeft), 76f);

            Ui.Size(Ui.Text("Server title", page, "Адрес сервера", 24, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold), 34f);
            serverInput = Ui.Size(Ui.Input("Address", page, BackendClient.DefaultServer, 26), 58f);
            serverInput.characterLimit = 200;

            RectTransform buttons = Ui.Size(Ui.Row("Server buttons", page, 12f), 56f);
            Ui.Size(Ui.Button("Check", buttons, "Проверить сервер", CheckServer, 22), 56f, 300f);
            Ui.Size(Ui.Button("Save", buttons, "Сохранить адрес", SaveServer, 22, Ui.PrimaryButton), 56f, 300f);

            RectTransform model = Ui.Size(Ui.Row("Model buttons", page, 12f), 56f);
            Ui.Size(Ui.Button("Model", model, "Проверить модель", CheckModel, 22), 56f, 300f);
            Ui.Size(Ui.Button("Test", model, "Пробное сообщение", TestModel, 22), 56f, 300f);

            serverStatus = Ui.Size(Ui.Text("Status", page, "", 22, Ui.Muted, TextAnchor.UpperLeft), 110f);
            return page;
        }

        void OpenAi()
        {
            aiToggle.SetIsOnWithoutNotify(BackendClient.AiEnabled);
            serverInput.text = BackendClient.ServerUrl;
            SetServerStatus("", Ui.Muted);
        }

        void CheckServer() => Check("Проверяю сервер…", done => BackendClient.Instance.CheckHealth(serverInput.text, done));

        void CheckModel()
        {
            SaveServerQuietly();
            Check("Спрашиваю сервер про модель…", done => BackendClient.Instance.CheckModel(done));
        }

        void TestModel()
        {
            SaveServerQuietly();
            Check("Жду сообщение от модели — локальная модель может думать до минуты…", done => BackendClient.Instance.TestChat(done));
        }

        void Check(string waiting, Action<Action<bool, string>> run)
        {
            if (checking) return;
            checking = true;
            SetServerStatus(waiting, Ui.Muted);
            run((ok, message) =>
            {
                if (this == null) return;
                checking = false;
                SetServerStatus(string.IsNullOrEmpty(message) ? (ok ? "Готово" : "Не получилось") : message, ok ? Ui.OkColor : Ui.ErrorColor);
            });
        }

        void SaveServer()
        {
            SaveServerQuietly();
            SetServerStatus("Адрес сохранён.", Ui.OkColor);
        }

        void SaveServerQuietly()
        {
            string before = BackendClient.ServerUrl;
            BackendClient.ServerUrl = serverInput.text;
            serverInput.text = BackendClient.ServerUrl;
            // Вход выдан другим сервером — там он не действует; игра войдёт заново сама.
            if (before != BackendClient.ServerUrl) BackendClient.Forget();
        }

        // ------------------------------------------------------------------ аккаунт

        RectTransform BuildAccount(RectTransform body)
        {
            RectTransform page = Ui.Stretch(Ui.Column("Account", body, 10f));
            accountTitle = Ui.Size(Ui.Text("Who", page, "", 24, Color.white, TextAnchor.MiddleLeft), 40f);
            Ui.Size(Ui.Text("Account note", page,
                "Логин и пароль хранятся в файле игры, и при каждом запуске она входит ими сама. С ними же можно войти " +
                "в игру на другом компьютере и смотреть свои ночи в панели сервера.",
                20, Ui.Muted, TextAnchor.UpperLeft), 56f);
            accountFile = Ui.Size(Ui.Text("File", page, "", 16, Ui.Muted, TextAnchor.UpperLeft), 26f);
            RectTransform links = Ui.Size(Ui.Row("Panel row", page, 12f), 52f);
            Ui.Size(Ui.Button("Open panel", links, "Мои ночи в панели", () => Application.OpenURL(BackendClient.ServerUrl + "/#/my"), 22), 52f, 320f);
            Ui.Size(Ui.Button("Sign out", links, "Выйти из аккаунта", SignOut, 22), 52f, 300f);

            Ui.Size(Ui.Text("Password title", page, "Сменить пароль", 24, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold), 36f);
            RectTransform passwords = Ui.Size(Ui.Row("Passwords", page, 12f), 52f);
            newPassword = Ui.Size(Ui.Input("New password", passwords, "новый пароль (от 8 символов)", 22, InputField.ContentType.Password), 52f, 330f);
            repeatPassword = Ui.Size(Ui.Input("Repeat password", passwords, "ещё раз", 22, InputField.ContentType.Password), 52f, 250f);
            Ui.Size(Ui.Button("Save password", passwords, "Сохранить", SavePassword, 22, Ui.PrimaryButton), 52f, 200f);

            accountStatus = Ui.Size(Ui.Text("Account status", page, "", 22, Ui.Muted, TextAnchor.UpperLeft), 64f);
            return page;
        }

        void OpenAccount_()
        {
            PlayerInfo player = BackendClient.Player;
            accountTitle.text = player != null && !string.IsNullOrEmpty(player.username)
                ? "Вы вошли как <b>" + player.username + "</b>"
                : "Вы играете без сервера — войти можно, выйдя из аккаунта";
            accountFile.text = "Файл аккаунта: " + AccountFile.FilePath;
            newPassword.text = repeatPassword.text = "";
            SetAccountStatus("", Ui.Muted);
        }

        void SavePassword()
        {
            if (accountBusy) return;
            if (newPassword.text.Length < 8)
            {
                SetAccountStatus("Пароль — не короче 8 символов.", Ui.ErrorColor);
                return;
            }
            if (newPassword.text != repeatPassword.text)
            {
                SetAccountStatus("Пароли не совпадают.", Ui.ErrorColor);
                return;
            }
            accountBusy = true;
            SetAccountStatus("Сохраняю пароль…", Ui.Muted);
            BackendClient.Instance.ChangePassword(newPassword.text, (ok, message) =>
            {
                if (this == null) return;
                accountBusy = false;
                if (ok) newPassword.text = repeatPassword.text = "";
                SetAccountStatus(string.IsNullOrEmpty(message) ? (ok ? "Готово" : "Не получилось") : message, ok ? Ui.OkColor : Ui.ErrorColor);
            });
        }

        /// <summary>Выйти: файл аккаунта удаляется, и главное меню снова начинается с экрана входа.</summary>
        void SignOut()
        {
            BackendClient.Instance.SignOut();
            if (GameFlow.HasMenu) GameFlow.OpenMenu();
            else SetAccountStatus("Вы вышли из аккаунта.", Ui.OkColor);
        }

        void SetAccountStatus(string text, Color color)
        {
            accountStatus.text = text;
            accountStatus.color = color;
        }

        void SetServerStatus(string text, Color color)
        {
            serverStatus.text = text;
            serverStatus.color = color;
        }
    }
}
