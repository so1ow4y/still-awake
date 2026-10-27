using UnityEngine;
using UnityEngine.UI;

namespace StillAwake
{
    /// <summary>
    /// Мысли героини внизу экрана, как в визуальной новелле: текст печатается по буквам, держится
    /// по длине фразы и гаснет. Новая мысль сменяет предыдущую.
    /// </summary>
    public sealed class Subtitles : MonoBehaviour
    {
        const float CharsPerSecond = 55f;
        const float FadeSeconds = 0.4f;
        static readonly Color BandColor = new Color(0f, 0f, 0f, 0.6f);

        public string Current { get; private set; } = "";
        public bool IsShowing => group.alpha > 0.01f;

        Text text;
        CanvasGroup group;
        float typed, hold;

        public static Subtitles Create(RectTransform canvas)
        {
            Image band = Ui.Image("Subtitles", canvas, BandColor);
            Ui.Place(band.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 128f), new Vector2(900f, 96f));
            var subtitles = band.gameObject.AddComponent<Subtitles>();
            subtitles.group = band.gameObject.AddComponent<CanvasGroup>();
            subtitles.group.alpha = 0f;
            subtitles.group.blocksRaycasts = false;

            subtitles.text = Ui.Text("Text", band.transform, "", 26, new Color(1f, 0.96f, 0.9f), TextAnchor.MiddleCenter, FontStyle.Italic);
            Ui.Stretch(subtitles.text.rectTransform, 28f, 8f, 28f, 8f);
            var shadow = subtitles.text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(2f, -2f);
            return subtitles;
        }

        public void Show(string line)
        {
            Current = line ?? "";
            typed = 0f;
            hold = 2.2f + Current.Length * 0.045f;
            text.text = "";
            group.alpha = 1f;
        }

        public void Clear()
        {
            Current = "";
            hold = 0f;
            group.alpha = 0f;
        }

        void Update()
        {
            if (Current.Length == 0 || group.alpha <= 0f) return;
            float dt = Time.unscaledDeltaTime;
            if (typed < Current.Length)
            {
                typed = Mathf.Min(Current.Length, typed + CharsPerSecond * dt);
                text.text = Current.Substring(0, (int)typed);
                return;
            }
            text.text = Current;
            hold -= dt;
            if (hold < 0f) group.alpha = Mathf.Max(0f, group.alpha - dt / FadeSeconds);
        }
    }
}
