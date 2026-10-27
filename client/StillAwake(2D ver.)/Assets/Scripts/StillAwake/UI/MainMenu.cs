using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace StillAwake
{
    /// <summary>
    /// Главное меню (сцена GameMenu): «Играть», текущая ночь (клик — выбрать другую), «Настройки», «Выход».
    /// Открывается после входа в аккаунт (AuthScreen). Внизу — чей аккаунт и пишет ли сталкеру нейросеть.
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        const float ColumnWidth = 560f;
        static readonly Color Background = new Color(0.025f, 0.025f, 0.035f);
        static readonly Color Shade = new Color(0f, 0f, 0f, 0.55f);

        static readonly Color NightColor = new Color(0.12f, 0.12f, 0.16f, 0.95f);
        static readonly Color LockedColor = new Color(0.08f, 0.08f, 0.1f, 0.9f);

        SettingsPanel settings;
        Text aiLine, footer, accountLine;
        Button play, nightButton;
        RectTransform nightsPanel, nightsList;
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
            Ui.Place(column, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(150f, -340f), new Vector2(ColumnWidth, 500f));
            menu.play = Ui.Size(Ui.Button("Play", column, "Играть", menu.StartNight, 32, Ui.PrimaryButton), 76f);
            menu.nightButton = Ui.Size(Ui.Button("Night", column, "", menu.OpenNights, 24, NightColor), 60f);
            Ui.Size(Ui.Button("Settings", column, "Настройки", () => menu.settings.Open(), 28), 68f);
            Ui.Size(Ui.Button("Quit", column, "Выход", GameFlow.Quit, 28), 68f);
            menu.accountLine = Ui.Size(Ui.Text("Account", column, "", 22, Ui.Muted, TextAnchor.UpperLeft), 32f);
            menu.aiLine = Ui.Size(Ui.Text("AI", column, "", 22, Ui.Muted, TextAnchor.UpperLeft), 70f);

            menu.footer = Ui.Text("Footer", root, "", 18, Ui.Muted, TextAnchor.LowerLeft);
            Ui.Place(menu.footer.rectTransform, Vector2.zero, Vector2.zero, new Vector2(150f, 36f), new Vector2(1000f, 30f));
            Text keys = Ui.Text("Keys", root, "Enter — играть · Esc — назад", 18, Ui.Muted, TextAnchor.LowerRight);
            Ui.Place(keys.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-60f, 36f), new Vector2(700f, 30f));

            menu.BuildNights(root);
            menu.RefreshNight();

            menu.settings = SettingsPanel.Create(root);
            menu.settings.Closed += menu.RefreshStatus;
            menu.RefreshStatus();
            return menu;
        }

        // ------------------------------------------------------------------ выбор ночи

        void BuildNights(RectTransform root)
        {
            Image shade = Ui.Image("Nights", root, new Color(0f, 0f, 0f, 0.75f), null, raycast: true);
            nightsPanel = Ui.Stretch(shade.rectTransform);
            Image window = Ui.Image("Window", nightsPanel, new Color(0.06f, 0.06f, 0.08f, 1f), null, raycast: true);
            Ui.Place(window.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 760f));
            RectTransform column = Ui.Stretch(Ui.Column("Content", window.transform, 12f, 32));
            Ui.Size(Ui.Text("Title", column, "Сменить ночь", 44, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold), 56f);
            Ui.Size(Ui.Text("Hint", column, "Следующая ночь открывается, если пережить предыдущую.", 20, Ui.Muted, TextAnchor.MiddleLeft), 30f);
            nightsList = Ui.Column("List", column, 10f);
            var listSize = nightsList.gameObject.AddComponent<LayoutElement>();
            listSize.flexibleHeight = 1f;
            RectTransform footerRow = Ui.Size(Ui.Row("Footer", column, 12f), 60f);
            Ui.Size(Ui.Button("Back", footerRow, "Назад [Esc]", CloseNights, 24), 60f, 260f);
            nightsPanel.gameObject.SetActive(false);
        }

        void OpenNights()
        {
            Ui.Clear(nightsList);
            NightDef selected = Nights.Selected;
            foreach (NightDef night in Nights.All)
            {
                NightDef captured = night;
                bool unlocked = Nights.IsUnlocked(night.Number);
                bool current = night.Number == selected.Number;
                string label = "<b>Ночь " + night.Number + ". " + night.Title + "</b>" + (current ? "   <color=#ff8a80>• выбрана</color>" : "") +
                               "\n<size=18><color=#ffffffaa>" + (unlocked ? night.Intro : "Закрыто — переживи ночь " + (night.Number - 1)) + "</color></size>";
                Button row = Ui.Size(Ui.Button("Night " + night.Number, nightsList, label, () => PickNight(captured), 24,
                    current ? Ui.PrimaryButton : unlocked ? NightColor : LockedColor), 104f);
                row.interactable = unlocked;
                Text text = row.GetComponentInChildren<Text>();
                text.alignment = TextAnchor.MiddleLeft;
                text.rectTransform.offsetMin = new Vector2(24f, text.rectTransform.offsetMin.y);
            }
            nightsPanel.gameObject.SetActive(true);
            nightsPanel.SetAsLastSibling();
        }

        void PickNight(NightDef night)
        {
            Nights.Select(night.Number);
            RefreshNight();
            CloseNights();
        }

        void CloseNights() => nightsPanel.gameObject.SetActive(false);

        /// <summary>Подписи «Играть» и кнопки ночи — по выбранной ночи.</summary>
        void RefreshNight()
        {
            NightDef night = Nights.Selected;
            Ui.SetLabel(play, "Играть — ночь " + night.Number);
            Ui.SetLabel(nightButton, "Ночь " + night.Number + ": " + night.Title + "   ·   сменить ▸");
        }

        void Update()
        {
            if (GameInput.KeyPressed(Key.Escape))
            {
                if (nightsPanel.gameObject.activeSelf) CloseNights();
                else settings.Back();
                return;
            }
            if (settings.IsOpen || nightsPanel.gameObject.activeSelf) return;
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
            PlayerInfo player = BackendClient.Player;
            accountLine.text = player != null && !string.IsNullOrEmpty(player.username)
                ? "Аккаунт: <b>" + player.username + "</b> · сменить — «Настройки → Аккаунт»"
                : "Без сервера: ночи не сохраняются. Войти — «Настройки → Аккаунт».";
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
