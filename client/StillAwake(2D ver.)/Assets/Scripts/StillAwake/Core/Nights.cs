using UnityEngine;

namespace StillAwake
{
    /// <summary>Одна ночь (уровень): название, вступление и насколько она тяжелее базовой.</summary>
    public sealed class NightDef
    {
        public int Number;
        public string Title;
        /// <summary>Вступление в меню выбора ночи и тост в начале.</summary>
        public string Intro;
        /// <summary>Делитель пауз сталкера снаружи и между визитами: 1.3 — на треть быстрее.</summary>
        public float Pace = 1f;
        /// <summary>Сколько секунд от появления у двери до входа (0 — как в настройках сцены).</summary>
        public float DoorWarningSeconds;
        /// <summary>Сколько секунд он обыскивает комнату (0 — как в настройках сцены).</summary>
        public float RoomSearchSeconds;
        /// <summary>Доли ночи, с которых он входит и спешит (0 — как в настройках сцены).</summary>
        public float ThreatAt, DangerAt;
        /// <summary>Ночь начинается без света — электричество «моргнуло».</summary>
        public bool StartDark;
    }

    /// <summary>
    /// Ночи по порядку и прогресс игрока (PlayerPrefs): какая выбрана и до какой открыто. Следующая ночь
    /// открывается, если пережить предыдущую (любая концовка, где героиня жива).
    /// </summary>
    public static class Nights
    {
        const string SelectedKey = "sa.night.selected";
        const string UnlockedKey = "sa.night.unlocked";

        public static readonly NightDef[] All =
        {
            new NightDef
            {
                Number = 1, Title = "Первая ночь",
                Intro = "Пятница, 13 ноября. Кто-то третий вечер подряд стоит под фонарём напротив.",
            },
            new NightDef
            {
                Number = 2, Title = "Он вернулся",
                Intro = "Он знает, где ты пряталась. И он больше не ждёт полуночи.",
                Pace = 1.25f, DoorWarningSeconds = 6f, ThreatAt = 0.22f, DangerAt = 0.6f,
            },
            new NightDef
            {
                Number = 3, Title = "Без света",
                Intro = "В доме выбило пробки. Только телефон, фонарик — и он.",
                Pace = 1.35f, DoorWarningSeconds = 6f, RoomSearchSeconds = 12f, ThreatAt = 0.2f, DangerAt = 0.55f, StartDark = true,
            },
            new NightDef
            {
                Number = 4, Title = "Последняя ночь",
                Intro = "«Сегодня я не уйду». Он написал это ещё днём.",
                Pace = 1.55f, DoorWarningSeconds = 5f, RoomSearchSeconds = 13f, ThreatAt = 0.15f, DangerAt = 0.45f,
            },
        };

        public static NightDef Get(int number) => All[Mathf.Clamp(number, 1, All.Length) - 1];

        /// <summary>Сколько ночей открыто (минимум первая).</summary>
        public static int Unlocked => Mathf.Clamp(PlayerPrefs.GetInt(UnlockedKey, 1), 1, All.Length);

        /// <summary>Выбранная в меню ночь — её и запускает «Играть».</summary>
        public static NightDef Selected => Get(Mathf.Min(PlayerPrefs.GetInt(SelectedKey, 1), Unlocked));

        public static bool IsUnlocked(int number) => number <= Unlocked;

        public static void Select(int number)
        {
            if (!IsUnlocked(number)) return;
            PlayerPrefs.SetInt(SelectedKey, number);
            PlayerPrefs.Save();
        }

        /// <summary>Пережила ночь: открыть следующую. true — открыта новая.</summary>
        public static bool Complete(int number)
        {
            if (number >= All.Length || number + 1 <= Unlocked) return false;
            PlayerPrefs.SetInt(UnlockedKey, number + 1);
            PlayerPrefs.Save();
            return true;
        }

        public static bool HasNext(int number) => number < All.Length;

        /// <summary>Настройки сцены, подкрученные под ночь. Исходные не меняются.</summary>
        public static NightSettings Apply(NightSettings source, NightDef night)
        {
            NightSettings s = JsonUtility.FromJson<NightSettings>(JsonUtility.ToJson(source));
            s.outsideStepSeconds /= night.Pace;
            s.visitCooldownSeconds /= night.Pace;
            s.firstMessageAt /= night.Pace;
            if (night.DoorWarningSeconds > 0f) s.doorWarningSeconds = night.DoorWarningSeconds;
            if (night.RoomSearchSeconds > 0f) s.roomSearchSeconds = night.RoomSearchSeconds;
            if (night.ThreatAt > 0f) s.threatPhaseAt = night.ThreatAt;
            if (night.DangerAt > 0f) s.dangerPhaseAt = night.DangerAt;
            return s;
        }
    }
}
