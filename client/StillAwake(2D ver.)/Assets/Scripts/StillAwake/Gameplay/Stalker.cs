using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace StillAwake
{
    /// <summary>
    /// Поведение сталкера без LLM: улица → окно → дверь → комната → уходит и возвращается.
    /// Проигрыш только за ошибки игрока: не успела спрятаться, спряталась туда же, где в прошлый раз,
    /// нашумела в укрытии. Так базовый цикл можно проверять до подключения ИИ.
    /// </summary>
    public sealed class Stalker : MonoBehaviour
    {
        public StalkerNode Node { get; private set; } = StalkerNode.Away;
        public bool Active { get; private set; }

        /// <summary>Внимание к героине 0–100: от света в окне, шума и дерзких ответов. Ускоряет его.</summary>
        public float Attention { get; private set; }

        /// <summary>Укрытие, где героиня пряталась в прошлый визит, — туда он заглянет первым.</summary>
        public HideSpot RememberedSpot { get; private set; }

        public int Visits { get; private set; }
        public float Timer => timer;

        public event Action<StalkerNode> NodeChanged;
        /// <summary>Подошёл к двери. Аргумент — заперта ли она.</summary>
        public event Action<bool> ArrivedAtDoor;
        public event Action LockPicked;
        public event Action EnteredRoom;
        public event Action VisitSurvived;
        public event Action<string> CaughtPlayer;

        NightSettings settings;
        RoomState room;
        Func<NightPhase> phase;
        float timer;
        bool lockPicked;

        public void Init(NightSettings nightSettings, RoomState roomState, Func<NightPhase> currentPhase)
        {
            settings = nightSettings;
            room = roomState;
            phase = currentPhase;
        }

        public void Activate()
        {
            if (Active) return;
            Active = true;
            MoveTo(StalkerNode.Street, OutsideStep());
        }

        /// <summary>Рассвет: уходит и больше не возвращается.</summary>
        public void Leave()
        {
            Active = false;
            MoveTo(StalkerNode.Away, 0f);
        }

        public void AddAttention(float amount) => Attention = Mathf.Clamp(Attention + amount, 0f, 100f);

        /// <summary>Шум в комнате. Если он внутри, а звук из укрытия громкий, — сразу идёт на звук.</summary>
        public void HearNoise(float amount)
        {
            if (!Active) return;
            AddAttention(Node == StalkerNode.Door ? amount : amount * 0.5f);
            if (Node == StalkerNode.Room && room.Hidden != HideSpot.None && amount >= 25f)
                Catch("Он услышал звук из укрытия и нашёл тебя.");
        }

        public void OnPlayerLeftHiding()
        {
            if (Active && Node == StalkerNode.Room) Catch("Ты вылезла прямо перед ним.");
        }

        /// <summary>Дверь заперли у него перед носом — придётся повозиться ещё.</summary>
        public void OnDoorLocked()
        {
            if (!Active || Node != StalkerNode.Door) return;
            lockPicked = false;
            timer += settings.lockedDoorExtraSeconds * 0.5f;
        }

        void Update()
        {
            if (!Active) return;

            float dt = Time.deltaTime;
            Attention = Mathf.Max(0f, Attention - dt);

            switch (Node)
            {
                case StalkerNode.Away:
                    timer -= dt * OutsideSpeed;
                    if (timer <= 0f) MoveTo(StalkerNode.Street, OutsideStep());
                    break;

                case StalkerNode.Street:
                    timer -= dt * OutsideSpeed;
                    if (timer <= 0f)
                    {
                        bool rush = CanEnter && (phase() >= NightPhase.Danger || Attention >= 70f) && Random.value < 0.5f;
                        if (rush) ArriveAtDoor();
                        else MoveTo(StalkerNode.Window, OutsideStep());
                    }
                    break;

                case StalkerNode.Window:
                    if (room.LightOn && !room.CurtainsClosed) AddAttention(6f * dt);
                    timer -= dt * OutsideSpeed;
                    if (timer <= 0f)
                    {
                        if (CanEnter) ArriveAtDoor();
                        else MoveTo(StalkerNode.Street, OutsideStep());
                    }
                    break;

                case StalkerNode.Door:
                    timer -= dt;
                    if (room.DoorLocked && !lockPicked && timer <= settings.doorWarningSeconds)
                    {
                        lockPicked = true;
                        room.DoorLocked = false;
                        room.NotifyChanged();
                        LockPicked?.Invoke();
                    }
                    if (timer <= 0f) EnterRoom();
                    break;

                case StalkerNode.Room:
                    if (room.Hidden == HideSpot.None)
                    {
                        Catch("Он вошёл, а ты не успела спрятаться.");
                        return;
                    }
                    timer -= dt;
                    if (timer <= 0f) FinishSearch();
                    break;
            }
        }

        bool CanEnter => phase() >= NightPhase.Threat;

        float OutsideSpeed =>
            (phase() >= NightPhase.Danger ? settings.dangerSpeedMultiplier : 1f) * (Attention >= 60f ? 1.25f : 1f);

        void ArriveAtDoor()
        {
            lockPicked = false;
            MoveTo(StalkerNode.Door, settings.doorWarningSeconds + (room.DoorLocked ? settings.lockedDoorExtraSeconds : 0f));
            ArrivedAtDoor?.Invoke(room.DoorLocked);
        }

        void EnterRoom()
        {
            Visits++;
            MoveTo(StalkerNode.Room, settings.roomSearchSeconds);
            EnteredRoom?.Invoke();
        }

        void FinishSearch()
        {
            if (RememberedSpot != HideSpot.None && RememberedSpot == room.Hidden)
            {
                Catch("Он сразу заглянул туда, где ты пряталась в прошлый раз.");
                return;
            }
            if (Random.value < room.Noise / 100f)
            {
                Catch("Он услышал шорох и заглянул в укрытие.");
                return;
            }

            RememberedSpot = room.Hidden;
            MoveTo(StalkerNode.Away, settings.visitCooldownSeconds);
            VisitSurvived?.Invoke();
        }

        void Catch(string reason)
        {
            if (!Active) return;
            Active = false;
            CaughtPlayer?.Invoke(reason);
        }

        void MoveTo(StalkerNode node, float seconds)
        {
            timer = seconds;
            if (Node == node) return;
            Node = node;
            NodeChanged?.Invoke(node);
        }

        float OutsideStep() => Random.Range(settings.outsideStepSeconds.x, settings.outsideStepSeconds.y);
    }
}
