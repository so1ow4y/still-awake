using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace StillAwake
{
    /// <summary>
    /// Вход и регистрация перед главным меню (та же сцена GameMenu, экран собирается кодом). Есть файл аккаунта —
    /// игра сама входит его логином и паролем; удачно — сразу меню. Файла нет или сервер ответил «неверно»
    /// (файл, например, поправили руками) — игрок входит или заводит новый аккаунт. Сервер не отвечает — можно
    /// повторить, поменять адрес или играть без сервера (заготовленные реплики, ночи не сохраняются).
    /// </summary>
    public sealed class AuthScreen : MonoBehaviour
    {
        static readonly Color Background = new Color(0.025f, 0.025f, 0.035f);
        static readonly Color WindowColor = new Color(0.06f, 0.06f, 0.08f, 1f);
        static readonly Color TabColor = new Color(0.12f, 0.12f, 0.15f, 0.95f);

        Action passed;
        RectTransform form, registerOnly, offlineRow;
        Text heading, status;
        Button loginTab, registerTab, submit;
        InputField username, password, repeat, displayName, server;
        bool registerMode, busy;

        /// <summary>Показать вход, если он ещё не проверен в этом запуске; иначе сразу onPassed.</summary>
        public static void Show(RectTransform canvas, Action onPassed)
        {
            if (BackendClient.SessionChecked)
            {
                onPassed();
                return;
            }
            Image background = Ui.Image("Auth", canvas, Background, null, raycast: true);
            Ui.Stretch(background.rectTransform);
            var screen = background.gameObject.AddComponent<AuthScreen>();
            screen.passed = onPassed;
            screen.Build(background.rectTransform);
            screen.Begin();
        }

        void Build(RectTransform root)
        {
            Text title = Ui.Text("Title", root, "STILL AWAKE?", 72, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(1000f, 90f));

            Image window = Ui.Image("Window", root, WindowColor, null, raycast: true);
            Ui.Place(window.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(760f, 800f));
            RectTransform column = Ui.Stretch(Ui.Column("Content", window.transform, 12f, 32));

            heading = Ui.Size(Ui.Text("Heading", column, "", 30, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold), 44f);

            form = Ui.Column("Form", column, 12f);
            Ui.Size(form, 520f);
            RectTransform tabs = Ui.Size(Ui.Row("Tabs", form, 10f), 54f);
            loginTab = Ui.Size(Ui.Button("Login tab", tabs, "Вход", () => SetMode(false), 22, TabColor), 54f, 220f);
            registerTab = Ui.Size(Ui.Button("Register tab", tabs, "Новый аккаунт", () => SetMode(true), 22, TabColor), 54f, 260f);

            username = Ui.Size(Ui.Input("Username", form, "логин: латиница, цифры, . - _ (3–32)", 24), 56f);
            username.characterLimit = 32;
            password = Ui.Size(Ui.Input("Password", form, "пароль (от 8 символов)", 24, InputField.ContentType.Password), 56f);
            password.characterLimit = 128;
            registerOnly = Ui.Column("Register only", form, 12f);
            Ui.Size(registerOnly, 124f);
            repeat = Ui.Size(Ui.Input("Repeat", registerOnly, "пароль ещё раз", 24, InputField.ContentType.Password), 56f);
            repeat.characterLimit = 128;
            displayName = Ui.Size(Ui.Input("Display name", registerOnly, "имя (необязательно)", 24), 56f);
            displayName.characterLimit = 64;
            submit = Ui.Size(Ui.Button("Submit", form, "Войти", Submit, 26, Ui.PrimaryButton), 64f);

            status = Ui.Size(Ui.Text("Status", column, "", 21, Ui.Muted, TextAnchor.UpperLeft), 64f);

            offlineRow = Ui.Size(Ui.Row("Offline", column, 12f), 52f);
            Ui.Size(Ui.Button("Retry", offlineRow, "Повторить", Begin, 21), 52f, 220f);
            Ui.Size(Ui.Button("Offline", offlineRow, "Играть без сервера", PlayOffline, 21), 52f, 300f);

            RectTransform serverRow = Ui.Size(Ui.Row("Server", column, 12f), 46f);
            Ui.Size(Ui.Text("Server label", serverRow, "Сервер", 20, Ui.Muted, TextAnchor.MiddleLeft), 46f, 100f);
            server = Ui.Size(Ui.Input("Server address", serverRow, BackendClient.DefaultServer, 20), 46f);
            server.characterLimit = 200;
        }

        /// <summary>Есть файл аккаунта — войти им; нет — показать форму входа.</summary>
        void Begin()
        {
            server.text = BackendClient.ServerUrl;
            offlineRow.gameObject.SetActive(false);
            SavedAccount saved = AccountFile.Load();
            if (saved == null)
            {
                ShowForm(false, "", "Войдите в аккаунт или заведите новый — с ним сохраняются ваши ночи.", Ui.Muted);
                return;
            }
            heading.text = "Вход";
            form.gameObject.SetActive(false);
            SetStatus("Вхожу как " + saved.username + "…", Ui.Muted);
            busy = true;
            BackendClient.Instance.SignIn(saved.username, saved.password, (result, message) => Done(result, message, saved.username, fromFile: true));
        }

        void ShowForm(bool register, string login, string message, Color color)
        {
            form.gameObject.SetActive(true);
            SetMode(register);
            if (!string.IsNullOrEmpty(login)) username.text = login;
            password.text = repeat.text = "";
            SetStatus(message, color);
            (string.IsNullOrEmpty(username.text) ? username : password).ActivateInputField();
        }

        void SetMode(bool register)
        {
            registerMode = register;
            heading.text = register ? "Новый аккаунт" : "Вход";
            registerOnly.gameObject.SetActive(register);
            Ui.SetLabel(submit, register ? "Создать аккаунт и войти" : "Войти");
            Highlight(loginTab, !register);
            Highlight(registerTab, register);
        }

        static void Highlight(Button button, bool on)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = on ? Ui.PrimaryButton : TabColor;
            colors.selectedColor = colors.normalColor;
            button.colors = colors;
        }

        void Submit()
        {
            if (busy) return;
            string login = username.text.Trim();
            if (login.Length < 3)
            {
                SetStatus("Логин — от 3 символов: латиница, цифры, точка, дефис, подчёркивание.", Ui.ErrorColor);
                return;
            }
            if (password.text.Length < 8)
            {
                SetStatus("Пароль — не короче 8 символов.", Ui.ErrorColor);
                return;
            }
            if (registerMode && password.text != repeat.text)
            {
                SetStatus("Пароли не совпадают.", Ui.ErrorColor);
                return;
            }
            SaveServer();
            busy = true;
            offlineRow.gameObject.SetActive(false);
            SetStatus(registerMode ? "Создаю аккаунт…" : "Вхожу…", Ui.Muted);
            if (registerMode) BackendClient.Instance.Register(login, password.text, displayName.text, (r, m) => Done(r, m, login, fromFile: false));
            else BackendClient.Instance.SignIn(login, password.text, (r, m) => Done(r, m, login, fromFile: false));
        }

        void Done(AuthResult result, string message, string login, bool fromFile)
        {
            if (this == null) return;
            busy = false;
            switch (result)
            {
                case AuthResult.Ok:
                    BackendClient.SessionChecked = true;
                    Destroy(gameObject);
                    passed?.Invoke();
                    return;
                case AuthResult.Rejected when fromFile:
                    // Файл не подошёл (пароль сменили на другом компьютере или правили руками) — забыть его.
                    AccountFile.Delete();
                    ShowForm(false, login, "Сохранённые логин и пароль не подошли (" + message + "). Войдите заново.", Ui.ErrorColor);
                    return;
                case AuthResult.Rejected:
                case AuthResult.Error:
                    ShowForm(registerMode, login, message, Ui.ErrorColor);
                    return;
                default:
                    ShowForm(registerMode, login, "Сервер недоступен: " + BackendClient.ServerUrl + ". Проверьте адрес ниже или запустите сервер.", Ui.ErrorColor);
                    offlineRow.gameObject.SetActive(true);
                    return;
            }
        }

        /// <summary>Сервер не отвечает — играть на заготовленных репликах. Ночи не сохраняются, вход спросят в следующий раз.</summary>
        void PlayOffline()
        {
            BackendClient.SessionChecked = true;
            Destroy(gameObject);
            passed?.Invoke();
        }

        void SaveServer()
        {
            string before = BackendClient.ServerUrl;
            BackendClient.ServerUrl = server.text;
            server.text = BackendClient.ServerUrl;
            if (before != BackendClient.ServerUrl) BackendClient.Forget();
        }

        void SetStatus(string text, Color color)
        {
            status.text = text;
            status.color = color;
        }

        // Enter — отправить, Tab — следующее поле.
        void Update()
        {
            if (busy || !form.gameObject.activeSelf) return;
            if (GameInput.KeyPressed(Key.Enter) || GameInput.KeyPressed(Key.NumpadEnter))
            {
                Submit();
                return;
            }
            if (!GameInput.KeyPressed(Key.Tab)) return;
            InputField[] order = registerMode ? new[] { username, password, repeat, displayName, server } : new[] { username, password, server };
            int focused = Array.FindIndex(order, f => f.isFocused);
            order[(focused + 1) % order.Length].ActivateInputField();
        }
    }
}
