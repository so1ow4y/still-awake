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
            "ну наконец-то ответила",
            "почему так долго не отвечала?",
            "опять со своей лизой переписывалась да?",
            "мне не нравится когда ты так со мной разговариваешь",
            "я же для тебя стараюсь вообще-то",
            "не делай вид что не знаешь кто я",
            "ты сегодня в серой кофте была. тебе идёт",
        };

        bool windowMessageSent;
        bool helloSent;
        bool lightOffMessageSent;
        bool curtainsMessageSent;
        bool phoneWarningShown;
        bool ringHintShown;

        /// <summary>Шанс, что он перестанет печатать и подождёт её: в начале своего сообщения и ближе к концу.</summary>
        const float WaitChanceEarly = 0.6f;
        const float WaitChanceLate = 0.3f;
        int doorMessages;
        int modelRequests;

        /// <summary>Сообщения пишет модель: ИИ включён в настройках и ночь открыта на сервере.</summary>
        bool ModelOnline => BackendClient.AiEnabled && ctx.Backend != null && ctx.Backend.Online;

        static ChatReply Reply(string text, Func<string> answer, float attention = 0f) =>
            new ChatReply { Text = text, Answer = answer, Attention = attention };

        void FirstContact()
        {
            Say("first_contact", "не спишь? я вижу что ты онлайн", 0f,
                Reply("Кто это?", () => "в смысле кто. я тебе каждый день лайки ставлю"),
                Reply("Сплю.", () => "не ври мне. я же вижу что не спишь", 5f),
                Reply("Не пиши мне.", () => "не надо так со мной. я тебе ничего плохого не сделал", 10f));
        }

        /// <summary>
        /// Фото от незнакомца (Art/Resources/Photos/*.png) с подписью. Шлёт скрипт, без модели: картинки заранее
        /// нарисованы под события. Как и текст, ждёт, пока героиня допишет своё сообщение.
        /// </summary>
        void SendPhoto(string photo, string caption, float delay) => StartCoroutine(PhotoRoutine(photo, caption, delay));

        IEnumerator PhotoRoutine(string photo, string caption, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            while (!over && ctx.PhoneView.PlayerComposing) yield return null;
            if (over) yield break;
            Track("photo", ("photo", photo));
            ctx.Phone.ReceivePhoto(photo, caption, ClockText);
        }

        void OnStalkerAtWindow()
        {
            if (windowMessageSent) return;
            windowMessageSent = true;
            SendPhoto("Photos/window_outside", "это я сейчас снимаю. видишь себя?", 7f);
            Say("stalker_at_window", Room.CurtainsClosed ? "зачем шторы задёрнула? я же просто посмотреть хотел" : "у тебя новые шторы. раньше были синие", 1.5f,
                Reply("Где ты?", () => "рядом. выгляни"),
                Reply("Я вызову полицию.", () => "и что ты им скажешь? что тебе пишет фанат?", 10f),
                Reply("Что тебе нужно?", () => "тебя. просто поговорить нормально, вживую"));
        }

        void OnSeenThroughWindow()
        {
            if (helloSent) return;
            helloSent = true;
            Say("seen_at_window", "привет :)", 1.5f);
        }

        void OnLightTurnedOff()
        {
            bool outside = Stalker.Node == StalkerNode.Street || Stalker.Node == StalkerNode.Window;
            if (lightOffMessageSent || !outside) return;
            lightOffMessageSent = true;
            Say("light_off", "свет выключила. думаешь я уйду?", 2f);
        }

        void OnCurtainsClosed()
        {
            bool outside = Stalker.Node == StalkerNode.Street || Stalker.Node == StalkerNode.Window;
            if (curtainsMessageSent || !outside) return;
            curtainsMessageSent = true;
            Say("curtains_closed", "ну и зачем. я всё равно знаю что ты там", 2f);
        }

        void OnDoorMessage(bool locked)
        {
            doorMessages++;
            if (doorMessages == 1) SendPhoto("Photos/door_outside", "твоя дверь. коврик смешной", 3f);
            string text = doorMessages == 1
                ? locked ? "заперлась. от меня? серьёзно?" : "дверь открыта. ты меня ждала?"
                : locked ? "открой. я никуда не уйду" : "я за дверью";
            Say(locked ? "door_locked" : "door_open", text, 1f);
        }

        void OnRoomMessage()
        {
            // С первого визита игрок узнаёт про звук телефона, со второго это уже ловушка.
            if (Stalker.Visits >= 2) Say("stalker_entered", "ну и где ты", 3f);
        }

        void OnSurvivedMessage()
        {
            if (Stalker.Visits == 1)
            {
                SendPhoto("Photos/her_room", "я был у тебя. у тебя уютно", 6f);
                Say("visit_survived", "ладно. я понял где ты сидела", 3f);
                ctx.Hud.ShowToast("Он запомнил укрытие — в следующий раз прячься в другом месте.", 4f);
            }
            else
            {
                Say("visit_survived", "в следующий раз найду", 3f);
            }
        }

        void OnDangerPhase()
        {
            SendPhoto("Photos/her_sleeping", "это ты вчера. окно было открыто", 9f);
            Say("danger_phase", "скоро утро. я не уйду пока тебя не увижу", 0f,
                Reply("Оставь меня в покое.", () => "нет", 5f),
                Reply("Я не открою.", () => "посмотрим"));
        }

        void OnPhoneMessage(PhoneMessage message)
        {
            bool hidden = Room.Hidden != HideSpot.None;
            Track("message_received", ("text", message.Text), ("ring", ctx.Phone.Ring.ToString()), ("hidden", hidden));
            switch (ctx.Phone.Ring)
            {
                case RingMode.Silent:
                    ctx.Hud.ShowToast("Телефон беззвучно мигнул.");
                    return;
                case RingMode.Vibrate:
                    // Не выдаёт сразу, но шум копится — он может услышать жужжание, когда обыскивает комнату.
                    Sfx.Vibrate();
                    MakeNoise(hidden ? 14f : 4f);
                    return;
            }

            Sfx.Notification();
            if (hidden && !phoneWarningShown)
            {
                phoneWarningShown = true;
                ctx.Hud.ShowToast("Телефон пискнул в укрытии! Телефон → Настройки → «Без звука».", 4f);
            }
            MakeNoise(hidden ? 30f : 8f);
        }

        /// <summary>Незнакомец пишет первым: текст — от модели по ключу события, иначе fallback.</summary>
        void Say(string eventId, string fallback, float delay, params ChatReply[] replies) =>
            StartCoroutine(SayRoutine(eventId, fallback, delay, replies));

        IEnumerator SayRoutine(string eventId, string fallback, float delay, ChatReply[] replies)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            // Ей пишут ему — он не перебивает.
            while (!over && ctx.PhoneView.PlayerComposing) yield return null;
            if (over) yield break;

            string text = fallback;
            float started = Time.time;
            // Модель уже занята (локальная отвечает по одному) — событие не ждёт в очереди, пишет скрипт.
            if (ModelOnline && modelRequests == 0)
            {
                string reply = null;
                yield return AskModel(null, eventId, Settings.modelEventTimeout, r => reply = r);
                if (over) yield break;
                if (reply != null) text = reply;
            }
            yield return TypeOut(text, Time.time - started);
            if (over) yield break;
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
            QueueAnswer(reply.Text, reply.Answer?.Invoke());
        }

        /// <summary>Своё сообщение игрока из поля ввода.</summary>
        void OnPlayerMessage(string text)
        {
            if (over || string.IsNullOrWhiteSpace(text)) return;
            ctx.Phone.Send(text, ClockText);
            // Ответила — значит, не спит; свет экрана в укрытии тоже заметен.
            Stalker.AddAttention(Room.Hidden != HideSpot.None ? 6f : 3f);
            Track("reply", ("text", text), ("free", true));
            QueueAnswer(text, ScriptedAnswers[Random.Range(0, ScriptedAnswers.Length)]);
        }

        readonly System.Collections.Generic.List<string> pendingPlayerTexts = new System.Collections.Generic.List<string>();
        string pendingFallback;
        bool flushing;

        /// <summary>
        /// Сообщения игрока копятся, пока она пишет дальше: незнакомец «видит, что ему печатают», и ждёт.
        /// На сервер уходит всё сразу, только когда она перестала набирать.
        /// </summary>
        void QueueAnswer(string playerText, string fallback)
        {
            pendingPlayerTexts.Add(playerText);
            pendingFallback = fallback;
            if (!flushing) StartCoroutine(FlushAnswers());
        }

        IEnumerator FlushAnswers()
        {
            flushing = true;
            // Кадр, чтобы поле ввода успело очиститься после отправки.
            yield return null;
            while (!over && ctx.PhoneView.PlayerComposing) yield return null;
            flushing = false;
            if (over || pendingPlayerTexts.Count == 0) yield break;

            string joined = string.Join("\n", pendingPlayerTexts);
            pendingPlayerTexts.Clear();
            Answer(joined, pendingFallback);
        }

        void Answer(string playerText, string fallback) => StartCoroutine(AnswerRoutine(playerText, fallback));

        IEnumerator AnswerRoutine(string playerText, string fallback)
        {
            string text = fallback;
            // Сначала он «прочитал» и думает, потом печатает.
            yield return new WaitForSeconds(Random.Range(1f, 2.5f));
            float started = Time.time;
            if (ModelOnline)
            {
                string reply = null;
                yield return AskModel(playerText, null, Settings.modelReplyTimeout, r => reply = r);
                if (reply != null) text = reply;
            }
            if (over || string.IsNullOrEmpty(text)) yield break;
            yield return TypeOut(text, Time.time - started);
            if (!over) ctx.Phone.Receive(text, ClockText);
        }

        /// <summary>
        /// «Печатает…» столько, сколько человек набирал бы это сообщение (~8 символов в секунду, 1.5–12 с).
        /// Время, пока ждали модель, засчитывается — короткий ответ после долгой модели приходит сразу.
        /// </summary>
        IEnumerator TypeOut(string text, float alreadySpent)
        {
            float total = Mathf.Clamp(1f + text.Length / 8f, 1.5f, 12f);
            float duration = total - alreadySpent;
            // Она начала писать, пока он печатает. Как в жизни: кто-то замолкает и ждёт, кто-то дописывает своё.
            // Чем больше он уже набрал, тем вероятнее допишет. Решение — на каждый её «заход», а не на каждый кадр.
            bool? waitsForHer = null;
            while (duration > 0f && !over)
            {
                bool composing = ctx.PhoneView.PlayerComposing;
                if (!composing) waitsForHer = null;
                else if (waitsForHer == null) waitsForHer = Random.value < (duration < total * 0.5f ? WaitChanceLate : WaitChanceEarly);
                bool waiting = composing && waitsForHer == true;
                ctx.PhoneView.SetTyping(!waiting);
                if (!waiting) duration -= Time.deltaTime;
                yield return null;
            }
            if (ctx.PhoneView != null) ctx.PhoneView.SetTyping(modelRequests > 0);
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
