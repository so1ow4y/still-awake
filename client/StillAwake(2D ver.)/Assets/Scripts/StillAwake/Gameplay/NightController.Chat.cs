using System;
using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

namespace StillAwake
{
    // Переписка с незнакомцем в мессенджере. КОГДА он пишет, решает игровая логика (у окна, у двери, вошёл, ушёл),
    // а ЧТО пишет — модель через backend (ключ события + состояние ночи). ИИ выключен в настройках, сервера нет
    // или модель не успела — скриптовая реплика. Игрок отвечает подсказками или любым своим текстом.
    public sealed partial class NightController
    {
        sealed class ChatReply
        {
            public string Text;
            public Func<string> Answer;
            public float Attention;
        }

        static readonly string[] ScriptedAnswers =
        {
            "Я вижу, как ты печатаешь.",
            "Тише. Я слушаю.",
            "Ты правда думаешь, что это поможет?",
            "Мне нравится, когда ты отвечаешь.",
            "Не пиши. Лучше посмотри в окно.",
            "Ты не одна. Помнишь?",
        };

        bool windowMessageSent;
        bool helloSent;
        bool lightOffMessageSent;
        bool curtainsMessageSent;
        bool phoneWarningShown;
        int doorMessages;
        int modelRequests;

        /// <summary>Сообщения пишет модель: ИИ включён в настройках и ночь открыта на сервере.</summary>
        bool ModelOnline => BackendClient.AiEnabled && ctx.Backend != null && ctx.Backend.Online;

        static ChatReply Reply(string text, Func<string> answer, float attention = 0f) =>
            new ChatReply { Text = text, Answer = answer, Attention = attention };

        void FirstContact()
        {
            Say("first_contact", "Ещё не спишь?", 0f,
                Reply("Кто это?", () => "Тот, кто тоже не спит."),
                Reply("Сплю.", () => Room.LightOn ? "Неправда. У тебя горит свет." : "Неправда. Экран телефона светится.", 5f),
                Reply("Не пиши мне.", () => "Как грубо. А я ведь совсем рядом.", 10f));
        }

        void OnStalkerAtWindow()
        {
            if (windowMessageSent) return;
            windowMessageSent = true;
            Say("stalker_at_window", Room.CurtainsClosed ? "Зачем ты задёрнула шторы? Я же просто смотрю." : "Красивая комната. Особенно отсюда.", 1.5f,
                Reply("Где ты?", () => "Ближе, чем ты думаешь."),
                Reply("Я вызову полицию.", () => "Попробуй. Связь здесь плохая.", 10f),
                Reply("Что тебе нужно?", () => "Просто поговорить. Пока что."));
        }

        void OnSeenThroughWindow()
        {
            if (helloSent) return;
            helloSent = true;
            Say("seen_at_window", "Привет :)", 1.5f);
        }

        void OnLightTurnedOff()
        {
            bool outside = Stalker.Node == StalkerNode.Street || Stalker.Node == StalkerNode.Window;
            if (lightOffMessageSent || !outside) return;
            lightOffMessageSent = true;
            Say("light_off", "Выключила свет? Я всё равно тебя вижу.", 2f);
        }

        void OnCurtainsClosed()
        {
            bool outside = Stalker.Node == StalkerNode.Street || Stalker.Node == StalkerNode.Window;
            if (curtainsMessageSent || !outside) return;
            curtainsMessageSent = true;
            Say("curtains_closed", "Шторы тебя не спрячут.", 2f);
        }

        void OnDoorMessage(bool locked)
        {
            doorMessages++;
            string text = doorMessages == 1
                ? locked ? "Заперлась? Умница. Но замки — это ненадолго." : "Ты оставила дверь открытой. Как мило."
                : locked ? "Опять заперто? Я терпеливый." : "Тук-тук.";
            Say(locked ? "door_locked" : "door_open", text, 1f);
        }

        void OnRoomMessage()
        {
            // С первого визита игрок узнаёт про звук телефона, со второго это уже ловушка.
            if (Stalker.Visits >= 2) Say("stalker_entered", "Где же ты прячешься?", 3f);
        }

        void OnSurvivedMessage()
        {
            if (Stalker.Visits == 1)
            {
                Say("visit_survived", "Хорошо прячешься. Но я запомнил, где ты была.", 3f);
                ctx.Hud.ShowToast("Он запомнил укрытие — в следующий раз прячься в другом месте.", 4f);
            }
            else
            {
                Say("visit_survived", "В следующий раз найду.", 3f);
            }
        }

        void OnDangerPhase()
        {
            Say("danger_phase", "Скоро рассвет. Нам надо успеть увидеться.", 0f,
                Reply("Оставь меня в покое.", () => "Не могу.", 5f),
                Reply("Я не открою.", () => "Я и не прошу."));
        }

        void OnPhoneMessage(PhoneMessage message)
        {
            bool hidden = Room.Hidden != HideSpot.None;
            Track("message_received", ("text", message.Text), ("sound", ctx.Phone.SoundOn), ("hidden", hidden));
            if (!ctx.Phone.SoundOn)
            {
                ctx.Hud.ShowToast("Телефон беззвучно мигнул.");
                return;
            }

            Sfx.Notification();
            if (hidden && !phoneWarningShown)
            {
                phoneWarningShown = true;
                ctx.Hud.ShowToast("Телефон пискнул в укрытии! Выключи звук в телефоне.", 4f);
            }
            MakeNoise(hidden ? 30f : 8f);
        }

        /// <summary>Незнакомец пишет первым: текст — от модели по ключу события, иначе fallback.</summary>
        void Say(string eventId, string fallback, float delay, params ChatReply[] replies) =>
            StartCoroutine(SayRoutine(eventId, fallback, delay, replies));

        IEnumerator SayRoutine(string eventId, string fallback, float delay, ChatReply[] replies)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            if (over) yield break;

            string text = fallback;
            // Модель уже занята (локальная отвечает по одному) — событие не ждёт в очереди, пишет скрипт.
            if (ModelOnline && modelRequests == 0)
            {
                string reply = null;
                yield return AskModel(null, eventId, Settings.modelEventTimeout, r => reply = r);
                if (over) yield break;
                if (reply != null) text = reply;
            }
            Receive(text, replies);
        }

        void Receive(string text, ChatReply[] replies)
        {
            if (replies == null || replies.Length == 0)
            {
                ctx.Phone.Receive(text, ClockText);
                return;
            }
            var options = new string[replies.Length];
            for (int i = 0; i < replies.Length; i++) options[i] = replies[i].Text;
            ctx.Phone.Receive(text, ClockText, options, index => OnReplyChosen(replies[index]));
        }

        /// <summary>Подсказка-ответ: влияет на внимание сталкера, а ответ пишет модель (или скрипт).</summary>
        void OnReplyChosen(ChatReply reply)
        {
            Stalker.AddAttention(reply.Attention);
            Track("reply", ("text", reply.Text), ("free", false));
            Answer(reply.Text, reply.Answer?.Invoke());
        }

        /// <summary>Своё сообщение игрока из поля ввода.</summary>
        void OnPlayerMessage(string text)
        {
            if (over || string.IsNullOrWhiteSpace(text)) return;
            ctx.Phone.Send(text, ClockText);
            // Ответила — значит, не спит; свет экрана в укрытии тоже заметен.
            Stalker.AddAttention(Room.Hidden != HideSpot.None ? 6f : 3f);
            Track("reply", ("text", text), ("free", true));
            Answer(text, ScriptedAnswers[Random.Range(0, ScriptedAnswers.Length)]);
        }

        void Answer(string playerText, string fallback) => StartCoroutine(AnswerRoutine(playerText, fallback));

        IEnumerator AnswerRoutine(string playerText, string fallback)
        {
            string text = fallback;
            if (ModelOnline)
            {
                string reply = null;
                yield return AskModel(playerText, null, Settings.modelReplyTimeout, r => reply = r);
                if (reply != null) text = reply;
            }
            else
            {
                yield return new WaitForSeconds(Random.Range(2f, 4f));
            }
            if (!over && !string.IsNullOrEmpty(text)) ctx.Phone.Receive(text, ClockText);
        }

        /// <summary>Запрос к модели с индикатором «печатает…» в телефоне.</summary>
        IEnumerator AskModel(string message, string eventId, float timeout, Action<string> done)
        {
            modelRequests++;
            ctx.PhoneView.SetTyping(true);
            bool finished = false;
            ctx.Backend.Chat(message, eventId, Snapshot(), timeout, reply =>
            {
                finished = true;
                done(reply);
            });
            while (!finished) yield return null;
            modelRequests--;
            if (ctx.PhoneView != null) ctx.PhoneView.SetTyping(modelRequests > 0);
        }

        /// <summary>Состояние ночи для сервера — из него backend собирает «ситуацию» для модели.</summary>
        NightState Snapshot() => new NightState
        {
            clock = ClockText,
            phase = Phase.ToString().ToLowerInvariant(),
            stalker = Stalker.Node.ToString().ToLowerInvariant(),
            light_on = Room.LightOn,
            curtains_closed = Room.CurtainsClosed,
            door_locked = Room.DoorLocked,
            hidden = Room.Hidden == HideSpot.Wardrobe ? "wardrobe" : Room.Hidden == HideSpot.UnderBed ? "under_bed" : "none",
            visits = Stalker.Visits,
            phone_sound = ctx.Phone.SoundOn,
            items = new System.Collections.Generic.List<string>(ctx.Inventory.Items).ToArray(),
        };
    }
}
