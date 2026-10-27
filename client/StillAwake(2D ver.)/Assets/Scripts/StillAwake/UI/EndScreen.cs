using System;
using UnityEngine;
using UnityEngine.UI;

namespace StillAwake
{
    /// <summary>Финал ночи: пережила до рассвета или поймали.</summary>
    public sealed class EndScreen : MonoBehaviour
    {
        static readonly Color WinColor = new Color(0.08f, 0.1f, 0.16f, 0.94f);
        static readonly Color LoseColor = new Color(0.25f, 0.02f, 0.02f, 0.94f);

        public bool IsShown => gameObject.activeSelf;

        public event Action RestartClicked;
        public event Action MenuClicked;
        public event Action NextClicked;

        Image background;
        Text title, body;
        Button next;

        public static EndScreen Create(RectTransform canvas, bool hasMenu)
        {
            Image background = Ui.Image("End screen", canvas, WinColor, null, raycast: true);
            Ui.Stretch(background.rectTransform);

            var screen = background.gameObject.AddComponent<EndScreen>();
            screen.background = background;

            screen.title = Ui.Text("Title", background.transform, "", 64, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Place(screen.title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 290f), new Vector2(1400f, 100f));

            // Концовки — несколько абзацев: место под текст между заголовком и кнопками.
            screen.body = Ui.Text("Body", background.transform, "", 25, new Color(1f, 1f, 1f, 0.85f));
            Ui.Place(screen.body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(1300f, 420f));

            float restartX = hasMenu ? -180f : 0f;
            Button restart = Ui.Button("Restart", background.transform, "Ещё раз [R]", () => screen.RestartClicked?.Invoke(), 28);
            Ui.Place((RectTransform)restart.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(restartX, -290f), new Vector2(320f, 72f));

            if (hasMenu)
            {
                Button menu = Ui.Button("Menu", background.transform, "В меню [Esc]", () => screen.MenuClicked?.Invoke(), 28);
                Ui.Place((RectTransform)menu.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(180f, -290f), new Vector2(320f, 72f));
            }

            // «Следующая ночь» — над остальными кнопками, только когда она открыта.
            screen.next = Ui.Button("Next night", background.transform, "Следующая ночь [N]", () => screen.NextClicked?.Invoke(), 28, Ui.PrimaryButton);
            Ui.Place((RectTransform)screen.next.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -200f), new Vector2(400f, 72f));
            screen.next.gameObject.SetActive(false);

            screen.gameObject.SetActive(false);
            return screen;
        }

        public bool NextVisible => next.gameObject.activeSelf;

        public void Show(string titleText, string bodyText, bool survived, bool canGoNext = false)
        {
            next.gameObject.SetActive(canGoNext);
            background.color = survived ? WinColor : LoseColor;
            title.text = titleText;
            body.text = bodyText;
            gameObject.SetActive(true);
        }
    }
}
