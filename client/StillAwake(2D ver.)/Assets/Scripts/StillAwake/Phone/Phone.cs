using System;
using System.Collections.Generic;

namespace StillAwake
{
    public sealed class PhoneMessage
    {
        public readonly bool FromPlayer;
        public readonly string Text;
        public readonly string Time;

        public PhoneMessage(bool fromPlayer, string text, string time)
        {
            FromPlayer = fromPlayer;
            Text = text;
            Time = time;
        }
    }

    /// <summary>
    /// Модель телефона: переписка с незнакомцем, варианты ответа, звук уведомлений.
    /// Что пишет незнакомец, решает NightController: модель через backend или скрипт без сервера.
    /// </summary>
    public sealed class Phone
    {
        public const string ContactName = "Неизвестный";

        public readonly List<PhoneMessage> Messages = new List<PhoneMessage>();
        /// <summary>Непрочитанные в мессенджере — цифра на иконке приложения.</summary>
        public int Unread { get; private set; }
        public bool SoundOn { get; private set; } = true;
        /// <summary>Незнакомец «печатает» — пока модель пишет ответ.</summary>
        public bool Typing { get; private set; }
        public IReadOnlyList<string> Replies => replies;

        public event Action Changed;
        /// <summary>Входящее сообщение — для звука уведомления и шума.</summary>
        public event Action<PhoneMessage> Received;

        public void SetTyping(bool typing)
        {
            if (Typing == typing) return;
            Typing = typing;
            Changed?.Invoke();
        }

        readonly List<string> replies = new List<string>();
        Action<int> onReply;

        /// <summary>Новое входящее. Непрочитанные варианты ответа на прошлое сообщение сгорают.</summary>
        public void Receive(string text, string time, string[] replyOptions = null, Action<int> onReplyChosen = null)
        {
            var message = new PhoneMessage(false, text, time);
            Messages.Add(message);
            Unread++;

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

            Messages.Add(new PhoneMessage(true, text, time));
            Changed?.Invoke();
            callback?.Invoke(index);
        }

        /// <summary>Игрок написал своё сообщение: варианты ответа больше не нужны.</summary>
        public void Send(string text, string time)
        {
            replies.Clear();
            onReply = null;
            Messages.Add(new PhoneMessage(true, text, time));
            Changed?.Invoke();
        }

        public void MarkRead()
        {
            if (Unread == 0) return;
            Unread = 0;
            Changed?.Invoke();
        }

        public void ToggleSound()
        {
            SoundOn = !SoundOn;
            Changed?.Invoke();
        }
    }
}
