using System;
using System.Collections.Generic;
using Random = UnityEngine.Random;

namespace StillAwake
{
    public sealed class PhoneMessage
    {
        public readonly bool FromPlayer;
        public readonly string Text;
        public readonly string Time;
        /// <summary>Фото (путь в Art/Resources, например Photos/window_outside) или null.</summary>
        public readonly string Photo;
        /// <summary>Своё сообщение прочитано собеседником (две галочки).</summary>
        public bool Read;

        public PhoneMessage(bool fromPlayer, string text, string time, bool read = false, string photo = null)
        {
            FromPlayer = fromPlayer;
            Text = text;
            Time = time;
            Read = read;
            Photo = photo;
        }
    }

    /// <summary>Режим уведомлений телефона (приложение «Настройки»).</summary>
    public enum RingMode
    {
        /// <summary>Пискнет на всю комнату — в укрытии рядом с ним это смерть.</summary>
        Sound,
        /// <summary>Жужжит: тише, но в тишине слышно.</summary>
        Vibrate,
        /// <summary>Только экран мигнёт.</summary>
        Silent,
    }

    /// <summary>Один чат в мессенджере: незнакомец или кто-то из семьи.</summary>
    public sealed class PhoneChat
    {
        public readonly string Name;
        public readonly string Initial;
        /// <summary>Подпись в шапке, пока собеседник не печатает.</summary>
        public string LastSeen;
        /// <summary>Семья: спят и не отвечают. Только сам незнакомец пишет в ответ.</summary>
        public readonly bool Silent;
        public readonly List<PhoneMessage> Messages = new List<PhoneMessage>();
        public int Unread;
        public bool Typing;

        /// <summary>Таймер «жути» в семейном чате: когда сообщение прочитают и мелькнёт «печатает…».</summary>
        internal float ReadTimer = -1f, TypingTimer = -1f;

        public PhoneChat(string name, string initial, string lastSeen, bool silent)
        {
            Name = name;
            Initial = initial;
            LastSeen = lastSeen;
            Silent = silent;
        }

        public PhoneMessage Last => Messages.Count > 0 ? Messages[Messages.Count - 1] : null;
    }

    /// <summary>
    /// Модель телефона: переписка с незнакомцем, варианты ответа, звук уведомлений, а также чаты с семьёй,
    /// где никто не отвечает. Что пишет незнакомец, решает NightController: модель через backend или скрипт.
    /// Messages, Unread, Typing, Receive… — это чат незнакомца.
    /// </summary>
    public sealed class Phone
    {
        public const string ContactName = "Неизвестный";

        public readonly PhoneChat Stalker = new PhoneChat(ContactName, "?", "был(а) недавно", false);
        public readonly List<PhoneChat> Chats = new List<PhoneChat>();

        public List<PhoneMessage> Messages => Stalker.Messages;
        /// <summary>Непрочитанные в мессенджере — цифра на иконке приложения.</summary>
        public int Unread
        {
            get
            {
                int total = 0;
                foreach (PhoneChat chat in Chats) total += chat.Unread;
                return total;
            }
        }
        public RingMode Ring { get; private set; } = RingMode.Sound;
        public bool SoundOn => Ring == RingMode.Sound;
        /// <summary>Незнакомец «печатает» — пока модель пишет ответ.</summary>
        public bool Typing => Stalker.Typing;
        public IReadOnlyList<string> Replies => replies;

        public event Action Changed;
        /// <summary>Входящее сообщение — для звука уведомления и шума.</summary>
        public event Action<PhoneMessage> Received;

        readonly List<string> replies = new List<string>();
        Action<int> onReply;

        public Phone()
        {
            Chats.Add(Stalker);

            var mom = new PhoneChat("Мама", "М", "была в сети в 22:47", true);
            mom.Messages.Add(new PhoneMessage(false, "Доченька, ты поела? Не сиди допоздна", "21:12"));
            mom.Messages.Add(new PhoneMessage(true, "Поела, мам. Всё хорошо", "21:30", true));
            mom.Messages.Add(new PhoneMessage(false, "Закрой дверь на ночь. В новостях опять про ваш район. Целую", "22:46"));
            Chats.Add(mom);

            var dad = new PhoneChat("Папа", "П", "был в сети вчера в 23:58", true);
            dad.Messages.Add(new PhoneMessage(false, "Запасные ключи у тёти Гали из 12-й", "вчера"));
            dad.Messages.Add(new PhoneMessage(true, "Ок", "вчера", true));
            Chats.Add(dad);

            var sister = new PhoneChat("Лиза", "Л", "была в сети в 00:02", true);
            sister.Messages.Add(new PhoneMessage(false, "ты видела, тебе кто-то лайкает все старые фотки??", "23:40"));
            sister.Messages.Add(new PhoneMessage(false, "аккаунт без аватарки, жуть какая-то", "23:41"));
            sister.Messages.Add(new PhoneMessage(true, "да забей, бот наверное", "23:55", true));
            sister.Messages.Add(new PhoneMessage(false, "ну ладно. я спать, завтра созвонимся", "00:02"));
            Chats.Add(sister);
        }

        public void SetTyping(bool typing)
        {
            if (Stalker.Typing == typing) return;
            Stalker.Typing = typing;
            Changed?.Invoke();
        }

        /// <summary>Фото от незнакомца с подписью. Варианты ответа не трогает.</summary>
        public void ReceivePhoto(string photo, string caption, string time)
        {
            var message = new PhoneMessage(false, caption, time, photo: photo);
            Stalker.Messages.Add(message);
            Stalker.Unread++;
            Received?.Invoke(message);
            Changed?.Invoke();
        }

        /// <summary>Новое входящее от незнакомца. Непрочитанные варианты ответа на прошлое сообщение сгорают.</summary>
        public void Receive(string text, string time, string[] replyOptions = null, Action<int> onReplyChosen = null)
        {
            var message = new PhoneMessage(false, text, time);
            Stalker.Messages.Add(message);
            Stalker.Unread++;

            replies.Clear();
            onReply = null;
            if (replyOptions != null)
            {
                replies.AddRange(replyOptions);
                onReply = onReplyChosen;
            }

            Received?.Invoke(message);
            Changed?.Invoke();
        }

        public void Reply(int index, string time)
        {
            if (index < 0 || index >= replies.Count) return;

            string text = replies[index];
            Action<int> callback = onReply;
            replies.Clear();
            onReply = null;

            Stalker.Messages.Add(new PhoneMessage(true, text, time, true));
            Changed?.Invoke();
            callback?.Invoke(index);
        }

        /// <summary>Игрок написал своё сообщение незнакомцу: варианты ответа больше не нужны.</summary>
        public void Send(string text, string time)
        {
            replies.Clear();
            onReply = null;
            Stalker.Messages.Add(new PhoneMessage(true, text, time, true));
            Changed?.Invoke();
        }

        /// <summary>
        /// Сообщение семье. Они спят и не ответят — но через какое-то время сообщение вдруг прочитают,
        /// а в шапке на пару секунд мелькнёт «печатает…». И ничего не придёт.
        /// </summary>
        public void SendToFamily(PhoneChat chat, string text, string time)
        {
            chat.Messages.Add(new PhoneMessage(true, text, time));
            if (chat.ReadTimer < 0f && Random.value < 0.6f) chat.ReadTimer = Random.Range(25f, 70f);
            Changed?.Invoke();
        }

        /// <summary>Тикает таймеры семейных чатов (по игровому времени).</summary>
        public void Tick(float dt, string clock)
        {
            foreach (PhoneChat chat in Chats)
            {
                if (!chat.Silent) continue;
                if (chat.ReadTimer >= 0f)
                {
                    chat.ReadTimer -= dt;
                    if (chat.ReadTimer < 0f)
                    {
                        foreach (PhoneMessage message in chat.Messages) message.Read = true;
                        chat.LastSeen = (chat.Name == "Папа" ? "был" : "была") + " в сети в " + clock;
                        chat.Typing = true;
                        chat.TypingTimer = Random.Range(2.5f, 5f);
                        Changed?.Invoke();
                    }
                }
                if (chat.TypingTimer >= 0f)
                {
                    chat.TypingTimer -= dt;
                    if (chat.TypingTimer < 0f)
                    {
                        chat.Typing = false;
                        Changed?.Invoke();
                    }
                }
            }
        }

        public void MarkRead(PhoneChat chat)
        {
            if (chat == null || chat.Unread == 0) return;
            chat.Unread = 0;
            Changed?.Invoke();
        }

        /// <summary>Прочитать чат незнакомца (уведомление, баннер).</summary>
        public void MarkRead() => MarkRead(Stalker);

        public void SetRing(RingMode mode)
        {
            if (Ring == mode) return;
            Ring = mode;
            Changed?.Invoke();
        }
    }
}
