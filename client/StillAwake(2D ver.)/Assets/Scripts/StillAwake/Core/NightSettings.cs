using System;
using UnityEngine;

namespace StillAwake
{
    /// <summary>Баланс ночи. Редактируется в инспекторе объекта Game на сцене Game.unity.</summary>
    [Serializable]
    public sealed class NightSettings
    {
        /// <summary>Ночь — с 12 AM до 6 AM, как во FNAF.</summary>
        public const int Hours = 6;

        [Header("Время")]
        [Tooltip("Сколько реальных секунд длится игровой час. Во FNAF — около 90: ночь ≈ 9 минут.")]
        public float secondsPerHour = 90f;

        [Tooltip("Через сколько секунд после начала приходит первое сообщение.")]
        public float firstMessageAt = 30f;

        [Tooltip("Доля ночи, после которой сталкер может подойти к двери и войти.")]
        [Range(0f, 1f)] public float threatPhaseAt = 0.3f;

        [Tooltip("Доля ночи, после которой сталкер двигается быстрее.")]
        [Range(0f, 1f)] public float dangerPhaseAt = 0.7f;

        [Header("Сталкер")]
        [Tooltip("Сколько секунд сталкер стоит на улице или у окна (мин, макс).")]
        public Vector2 outsideStepSeconds = new Vector2(10f, 16f);

        [Tooltip("Время от появления у незапертой двери до входа — столько есть, чтобы спрятаться.")]
        public float doorWarningSeconds = 7f;

        [Tooltip("Сколько он дополнительно возится с запертой дверью.")]
        public float lockedDoorExtraSeconds = 12f;

        [Tooltip("Сколько секунд он обыскивает комнату.")]
        public float roomSearchSeconds = 10f;

        [Tooltip("Пауза после ухода из комнаты.")]
        public float visitCooldownSeconds = 30f;

        [Header("Переписка")]
        [Tooltip("Сколько ждать сообщение от модели, когда сталкер пишет первым; дальше — скриптовая реплика.")]
        public float modelEventTimeout = 45f;

        [Tooltip("Сколько ждать ответ модели на сообщение игрока (сервер ждёт модель до 100 с).")]
        public float modelReplyTimeout = 90f;

        public float NightSeconds => secondsPerHour * Hours;

        [Tooltip("Множитель скорости сталкера снаружи в опасной фазе.")]
        public float dangerSpeedMultiplier = 1.4f;

        [Header("Шум")]
        [Tooltip("Скорость затухания шума (единиц в секунду, шкала 0–100).")]
        public float noiseDecayPerSecond = 5f;

        [Header("Отладка")]
        [Tooltip("Сразу показать области взаимодействия (F1) и состояние сталкера (F2).")]
        public bool debugOverlay;
    }
}
