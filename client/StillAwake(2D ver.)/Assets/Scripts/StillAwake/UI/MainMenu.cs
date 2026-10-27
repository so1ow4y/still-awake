using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace StillAwake
{
    /// <summary>
    /// Главное меню (сцена GameMenu): «Играть», «Настройки», «Выход». Ни регистрации, ни входа: игра сама
    /// подключается к серверу. Внизу — пишет ли сталкеру нейросеть и доступна ли модель.
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        const float ColumnWidth = 560f;
        static readonly Color Background = new Color(0.025f, 0.025f, 0.035f);
        static readonly Color Shade = new Color(0f, 0f, 0f, 0.55f);

        SettingsPanel settings;
        Text aiLine, footer;
        bool starting;

        public static MainMenu Create(RectTransform canvas)
        {
            Sprite art = ArtLibrary.Load(ArtPaths.MenuBackground);
            Image background = Ui.Image("Main menu", canvas, art != null ? Color.white : Background, art, raycast: true);
            Ui.Stretch(background.rectTransform);
            var menu = background.gameObject.AddComponent<MainMenu>();
            RectTransform root = background.rectTransform;

            // Затемнение слева — под колонкой кнопок, если художник положит фон.
            Ui.Anchors(Ui.Image("Shade", root, Shade).rectTransform, 0f, 0f, 0.42f, 1f);

            Text title = Ui.Text("Title", root, "STILL AWAKE?", 108, Color.white, TextAnchor.LowerLeft, FontStyle.Bold);
            Ui.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(150f, -90f), new Vector2(1300f, 140f));
            Text tagline = Ui.Text("Tagline", root, "Ещё не спишь?", 34, Ui.Accent, TextAnchor.UpperLeft, FontStyle.Italic);
            Ui.Place(tagline.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(156f, -238f), new Vector2(900f, 50f));

            RectTransform column = Ui.Column("Main", root, 16f);
            Ui.Place(column, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(150f, -340f), new Vector2(ColumnWidth, 420f));
            Ui.Size(Ui.Button("Play", column, "Играть", menu.StartNight, 32, Ui.PrimaryButton), 76f);
            Ui.Size(Ui.Button("Settings", column, "Настройки", () => menu.settings.Open(), 28), 68f);
            Ui.Size(Ui.Button("Quit", column, "Выход", GameFlow.Quit, 28), 68f);
            menu.aiLine = Ui.Size(Ui.Text("AI", column, "", 22, Ui.Muted, TextAnchor.UpperLeft), 70f);

            menu.footer = Ui.Text("Footer", root, "", 18, Ui.Muted, TextAnchor.LowerLeft);
            Ui.Place(menu.footer.rectTransform, Vector2.zero, Vector2.zero, new Vector2(150f, 36f), new Vector2(1000f, 30f));
            Text keys = Ui.Text("Keys", root, "Enter — играть · Esc — назад", 18, Ui.Muted, TextAnchor.LowerRight);
            Ui.Place(keys.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-60f, 36f), new Vector2(700f, 30f));

            menu.settings = SettingsPanel.Create(root);
            menu.settings.Closed += menu.RefreshStatus;
            menu.RefreshStatus();
            return menu;
        }

        void Update()
        {
            if (GameInput.KeyPressed(Key.Escape))
            {
                settings.Back();
                return;
            }
            if (settings.IsOpen) return;
            if (GameInput.KeyPressed(Key.Enter) || GameInput.KeyPressed(Key.NumpadEnter)) StartNight();
        }

        void StartNight()
        {
            if (starting) return;
            starting = true;
            GameFlow.StartNight();
        }

        /// <summary>Строка про ИИ: включён ли он и отвечает ли модель на сервере.</summary>
        void RefreshStatus()
        {
            footer.text = "v" + Application.version + " · сервер " + BackendClient.ServerUrl;
            if (!BackendClient.AiEnabled)
            {
                aiLine.text = "ИИ выключен: сообщения — заготовленные реплики. Включить — «Настройки → ИИ и сервер».";
                return;
            }
            aiLine.text = "ИИ включён · проверяю модель…";
            BackendClient.Instance.CheckModel((ok, message) =>
            {
                if (this == null) return;
                aiLine.text = ok
                    ? "ИИ включён · " + message
                    : "ИИ включён, но сервер или модель недоступны (" + message + ") — пока будут заготовленные реплики.";
            });
        }
    }
}
