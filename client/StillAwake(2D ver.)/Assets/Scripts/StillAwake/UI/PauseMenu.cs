using System;
using UnityEngine;
using UnityEngine.UI;

namespace StillAwake
{
    /// <summary>Пауза в ночи (Esc): продолжить, настройки, начать заново, выйти в меню.</summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        public bool IsOpen => gameObject.activeSelf;

        public event Action ResumeClicked;
        public event Action RestartClicked;
        public event Action MenuClicked;

        SettingsPanel settings;

        public static PauseMenu Create(RectTransform canvas, bool hasMenu)
        {
            Image background = Ui.Image("Pause", canvas, new Color(0f, 0f, 0f, 0.82f), null, raycast: true);
            Ui.Stretch(background.rectTransform);
            var pause = background.gameObject.AddComponent<PauseMenu>();
            RectTransform root = background.rectTransform;

            Text title = Ui.Text("Title", root, "Пауза", 72, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 250f), new Vector2(800f, 100f));
            Text note = Ui.Text("Note", root, "Часы остановились. Ненадолго.", 26, Ui.Muted, TextAnchor.MiddleCenter, FontStyle.Italic);
            Ui.Place(note.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 180f), new Vector2(800f, 40f));

            RectTransform column = Ui.Column("Buttons", root, 14f);
            Ui.Place(column, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, 120f), new Vector2(480f, 380f));
            Ui.Size(Ui.Button("Resume", column, "Продолжить [Esc]", () => pause.ResumeClicked?.Invoke(), 28, Ui.PrimaryButton), 70f);
            Ui.Size(Ui.Button("Settings", column, "Настройки", () => pause.settings.Open(), 26), 64f);
            Ui.Size(Ui.Button("Restart", column, "Начать ночь заново", () => pause.RestartClicked?.Invoke(), 26), 64f);
            if (hasMenu) Ui.Size(Ui.Button("Menu", column, "В главное меню", () => pause.MenuClicked?.Invoke(), 26), 64f);

            pause.settings = SettingsPanel.Create(root);
            pause.gameObject.SetActive(false);
            return pause;
        }

        public void Open() => gameObject.SetActive(true);

        public void Close()
        {
            settings.Close();
            gameObject.SetActive(false);
        }

        /// <summary>Esc на паузе: сначала закрываются настройки. true — Esc обработан здесь.</summary>
        public bool Back() => settings.Back();
    }
}
