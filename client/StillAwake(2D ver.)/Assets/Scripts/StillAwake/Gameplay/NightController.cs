using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

namespace StillAwake
{
    public enum NightPhase
    {
        /// <summary>Спокойное начало: осмотреться в комнате.</summary>
        Calm,
        /// <summary>Пришло первое сообщение, сталкер бродит снаружи.</summary>
        Strange,
        /// <summary>Сталкер может подойти к двери и войти.</summary>
        Threat,
        /// <summary>Ближе к рассвету — быстрее и настойчивее.</summary>
        Danger,
    }

    /// <summary>Ссылки на системы одной ночи; собирается в GameBootstrap.</summary>
    public sealed class GameContext
    {
        public NightSettings Settings;
        /// <summary>Какая это ночь (уровень).</summary>
        public NightDef Night;
        public RoomState Room;
        public Inventory Inventory;
        public Phone Phone;
        public RoomView RoomView;
        public RoomCamera Camera;
        public HotspotPicker Picker;
        public Stalker Stalker;
        public StalkerView StalkerView;
        public Hud Hud;
        public PhoneView PhoneView;
        public InventoryView InventoryView;
        public ActionMenu Menu;
        public Subtitles Subtitles;
        public EndScreen EndScreen;
        public PauseMenu Pause;
        /// <summary>Связь с сервером; null — играем без него.</summary>
        public BackendClient Backend;
        public Action Restart;
        /// <summary>Выход в главное меню; null — сцены меню нет в Build Settings.</summary>
        public Action ToMenu;
        /// <summary>Перейти к следующей ночи.</summary>
        public Action NextNight;
    }

    /// <summary>
    /// Один игровой цикл — одна ночь с 12 AM до 6 AM (час ≈ 90 секунд, как во FNAF): спокойное начало →
    /// первое сообщение → сталкер снаружи → визиты в комнату → рассвет (победа) или поимка (проигрыш).
    /// Клик по объекту — меню действий у курсора, результат — мысль героини в субтитрах. Предметы из хотбара
    /// можно перетащить на объект. Действия с объектами — NightController.Actions.cs, переписка — NightController.Chat.cs.
    /// </summary>
    public sealed partial class NightController : MonoBehaviour
    {
        const float TransitionSeconds = 0.32f;

        GameContext ctx;
        bool over;
        bool firstMessageSent;
        bool debugStalker;
        bool transitioning;
        /// <summary>Диспетчер принял вызов: в 6 утра приедет полиция.</summary>
        bool rescueCalled;
        float nextFlicker = 45f;
        Hotspot dropTarget;

        public float Elapsed { get; private set; }
        public NightPhase Phase { get; private set; } = NightPhase.Calm;
        public bool Paused { get; private set; }

        NightSettings Settings => ctx.Settings;
        RoomState Room => ctx.Room;
        Stalker Stalker => ctx.Stalker;

        /// <summary>Точное время ночи «01:37» — для сообщений в телефоне.</summary>
        string ClockText
        {
            get
            {
                int minutes = Mathf.Clamp((int)(Elapsed / Settings.NightSeconds * NightSettings.Hours * 60f), 0, NightSettings.Hours * 60);
                return $"{minutes / 60:00}:{minutes % 60:00}";
            }
        }

        /// <summary>Часы на экране, как во FNAF: только целые часы — 12 AM, 1 AM … 6 AM.</summary>
        string HourText
        {
            get
            {
                int hour = Mathf.Clamp((int)(Elapsed / Settings.secondsPerHour), 0, NightSettings.Hours);
                return (hour == 0 ? 12 : hour) + " AM";
            }
        }

        public void Init(GameContext context)
        {
            ctx = context;

            ctx.Picker.Clicked += OnHotspotClicked;
            ctx.Picker.StalkerAt = StalkerTargetAt;
            ctx.Menu.Result += ctx.Subtitles.Show;
            ctx.RoomView.ViewChanged += OnViewChanged;

            ctx.Hud.ReturnClicked += ReturnToSofa;
            ctx.Hud.TurnLeftClicked += () => Turn(false);
            ctx.Hud.TurnRightClicked += () => Turn(true);
            ctx.Hud.UnhideClicked += LeaveHiding;

            ctx.PhoneView.ReplyChosen += index => ctx.Phone.Reply(index, ClockText);
            ctx.PhoneView.MessageSubmitted += OnPlayerMessage;
            ctx.PhoneView.CallStarted += OnCallStarted;
            ctx.PhoneView.RescueRequested += OnRescueRequested;
            ctx.PhoneView.RescueRefused += () =>
            {
                Track("rescue_refused", ("clock", ClockText));
                ctx.Subtitles.Show("Он… повесил трубку. Они не приедут. Надо перезвонить и говорить чётко.");
            };
            ctx.Hud.PeepholeCloseClicked += ClosePeephole;
            ctx.PhoneView.OpenChanged += _ => RefreshInputLocks();

            ctx.InventoryView.UseRequested += UseItem;
            ctx.InventoryView.Dragging += OnItemDragging;
            ctx.InventoryView.Dropped += OnItemDropped;
            ctx.InventoryView.DragEnded += () => SetDropTarget(null);

            ctx.EndScreen.RestartClicked += () => ctx.Restart();
            ctx.EndScreen.MenuClicked += () => ctx.ToMenu?.Invoke();
            ctx.EndScreen.NextClicked += () => ctx.NextNight?.Invoke();
            ctx.Pause.ResumeClicked += () => SetPaused(false);
            ctx.Pause.RestartClicked += () => Leave(ctx.Restart);
            ctx.Pause.MenuClicked += () => Leave(ctx.ToMenu);

            ctx.Phone.Received += OnPhoneMessage;

            Stalker.NodeChanged += OnStalkerMoved;
            Stalker.ArrivedAtDoor += OnStalkerAtDoor;
            Stalker.LockPicked += () => ctx.Hud.ShowToast("Щёлк. Замок поддался...");
            Stalker.EnteredRoom += OnStalkerEntered;
            Stalker.VisitSurvived += OnVisitSurvived;
            Stalker.CaughtPlayer += Lose;

            ctx.RoomView.SetDebugHotspots(Settings.debugOverlay);
            debugStalker = Settings.debugOverlay;
            ctx.Hud.SetClock(HourText);
            ctx.PhoneView.SetClock(ClockText);
            RefreshInputLocks();
            UpdateHint();
            Sfx.StartAmbient();
            ctx.Hud.ShowToast("Ночь " + ctx.Night.Number + ". " + ctx.Night.Title + "\n" + ctx.Night.Intro, 6f);

            if (ctx.Backend != null)
            {
                ctx.Backend.StartSession();
                Track("night_start", ("hours", NightSettings.Hours), ("seconds_per_hour", Settings.secondsPerHour),
                    ("ai", BackendClient.AiEnabled), ("platform", Application.platform.ToString()));
            }
        }

        void OnDestroy()
        {
            if (Paused) Time.timeScale = 1f;
            Sfx.StopRingback();
            if (ctx != null && ctx.Backend != null) ctx.Backend.EndSession();
        }

        void Update()
        {
            HandleHotkeys();
            // Курсор над телефоном или хотбаром, открыто меню объекта, тащат предмет, смотрят в глазок — взгляд стоит на месте.
            ctx.Camera.Hold = ctx.PhoneView.PointerOver || ctx.InventoryView.PointerOver || ctx.Menu.IsOpen ||
                              ctx.InventoryView.IsDragging || ctx.Hud.PeepholeOpen;
            ctx.Hud.SetCloseUpStalker(Stalker.Node == StalkerNode.Door, Stalker.Node == StalkerNode.Room);
            if (over || Paused) return;

            float dt = Time.deltaTime;
            Elapsed += dt;
            Room.Noise = Mathf.Max(0f, Room.Noise - Settings.noiseDecayPerSecond * dt);

            UpdatePhase();
            UpdateFlicker(dt);
            ctx.Hud.SetClock(HourText);
            ctx.PhoneView.SetClock(ClockText);
            ctx.Hud.SetDebug(debugStalker ? DebugText() : null);

            if (Elapsed >= Settings.NightSeconds) Dawn();
        }

        /// <summary>
        /// 6 утра. Вызвала помощь — приезжает полиция. Нет — сталкер не уходит с рассветом: последний визит,
        /// из которого живой выйдет только один из них.
        /// </summary>
        void Dawn()
        {
            if (rescueCalled)
            {
                Win();
                return;
            }
            if (Stalker.FinalAssault) return;
            Track("final_assault", ("node", Stalker.Node.ToString()));
            Stalker.StartFinalAssault();
            ctx.Hud.ShowToast("6 AM. Рассвет. Но шаги на лестнице не стихают...", 4f);
            ctx.Subtitles.Show("Никто не приедет. Я никого не позвала.");
            if (Room.LightOn) StartCoroutine(Flicker());
            UpdateHint();
        }

        void UpdatePhase()
        {
            if (!firstMessageSent && Elapsed >= Settings.firstMessageAt)
            {
                firstMessageSent = true;
                FirstContact();
            }

            float progress = Elapsed / Settings.NightSeconds;
            NightPhase next = !firstMessageSent ? NightPhase.Calm
                : progress >= Settings.dangerPhaseAt ? NightPhase.Danger
                : progress >= Settings.threatPhaseAt ? NightPhase.Threat
                : NightPhase.Strange;

            if (next == Phase) return;
            Phase = next;
            Track("phase", ("phase", Phase.ToString().ToLowerInvariant()), ("clock", ClockText));
            if (Phase == NightPhase.Strange) Stalker.Activate();
            if (Phase == NightPhase.Danger) OnDangerPhase();
            UpdateHint();
        }

        void HandleHotkeys()
        {
            if (over)
            {
                if (GameInput.KeyPressed(Key.N) && ctx.EndScreen.NextVisible) ctx.NextNight?.Invoke();
                else if (GameInput.KeyPressed(Key.R) || GameInput.KeyPressed(Key.Enter)) ctx.Restart();
                else if (GameInput.KeyPressed(Key.Escape)) ctx.ToMenu?.Invoke();
                return;
            }

            if (Paused)
            {
                if (GameInput.KeyPressed(Key.Escape) && !ctx.Pause.Back()) SetPaused(false);
                return;
            }

            if (GameInput.KeyPressed(Key.Escape))
            {
                // Esc — всегда пауза, из любого приложения телефона; телефон остаётся как был.
                // Назад по приложениям — кнопка «домой» внизу телефона.
                if (ctx.Hud.PeepholeOpen) ClosePeephole();
                else if (ctx.Menu.IsOpen) ctx.Menu.Close();
                else SetPaused(true);
                return;
            }
            if (ctx.Menu.IsOpen && GameInput.RightClickPressed) ctx.Menu.Close();
            // Пока печатают в телефоне, буквы — это текст, а не действия.
            if (GameInput.Typing || ctx.PhoneView.Typing) return;

            if (GameInput.Pressed(GameAction.Phone)) ctx.PhoneView.Toggle();
            if (GameInput.Pressed(GameAction.PhoneHide)) ctx.PhoneView.ToggleHidden();
            // Кнопки «над телефоном»: M3 прячет его целиком (как H), ПКМ опускает до четверти. Их обычное действие
            // (закрепить камеру) при этом не срабатывает.
            bool phoneClicked = false;
            if (ctx.PhoneView.PointerOver && GameInput.Pressed(GameAction.PhoneHideOnHover))
            {
                ctx.PhoneView.ToggleHidden();
                phoneClicked = true;
            }
            else if (ctx.PhoneView.PointerOver && GameInput.Pressed(GameAction.PhonePeekOnHover))
            {
                ctx.PhoneView.TogglePeek();
                phoneClicked = true;
            }
            if (GameInput.Pressed(GameAction.Inventory)) ctx.InventoryView.Toggle();
            if (GameInput.Pressed(GameAction.CameraLock) && !phoneClicked) SetCameraLocked(!ctx.Camera.Locked);
            if (GameInput.Pressed(GameAction.Unhide))
            {
                if (ctx.Hud.PeepholeOpen) ClosePeephole();
                else LeaveHiding();
            }
            if (GameInput.Pressed(GameAction.DebugAreas)) ctx.RoomView.SetDebugHotspots(!ctx.RoomView.DebugHotspots);
            if (GameInput.Pressed(GameAction.DebugStalker)) debugStalker = !debugStalker;

            if (GameInput.Pressed(GameAction.Sofa)) ReturnToSofa();
            if (GameInput.Pressed(GameAction.Window)) GoToWindow();
            if (GameInput.Pressed(GameAction.TurnLeft)) Turn(false);
            if (GameInput.Pressed(GameAction.TurnRight)) Turn(true);
        }

        void SetCameraLocked(bool locked)
        {
            ctx.Camera.Locked = locked;
            ctx.Hud.SetCameraLocked(locked);
        }

        void RefreshInputLocks()
        {
            bool hidden = Room.Hidden != HideSpot.None;
            bool blocked = over || Paused || transitioning;
            bool peephole = ctx.Hud.PeepholeOpen;
            ctx.Picker.InputEnabled = !blocked && !hidden && !peephole;
            ctx.Camera.LookEnabled = !blocked && !hidden;
            ctx.Camera.LockCentered = hidden;
            ctx.Hud.SetTurnVisible(!hidden && !peephole && !over);
        }

        /// <summary>Пауза: время ночи и сталкер замирают (timeScale = 0), интерфейс живёт по реальному времени.</summary>
        void SetPaused(bool paused)
        {
            if (over || Paused == paused) return;
            Paused = paused;
            Time.timeScale = paused ? 0f : 1f;
            if (paused)
            {
                ctx.Menu.Close();
                ctx.Pause.Open();
            }
            else
            {
                ctx.Pause.Close();
            }
            ctx.Hud.SetTooltip(null);
            RefreshInputLocks();
        }

        /// <summary>Уйти посреди ночи (заново или в меню): ночь на сервере отмечается брошенной.</summary>
        void Leave(Action next)
        {
            if (next == null) return;
            if (!over) Track("night_abandoned", ("clock", ClockText), ("visits", Stalker.Visits));
            next();
        }

        // ------------------------------------------------------------------ перемещение по комнате

        void Turn(bool right)
        {
            if (over || Room.Hidden != HideSpot.None) return;
            ViewDef view = ctx.RoomView.CurrentDef;
            GoTo(right ? view.Right : view.Left, null, turn: true, right: right);
        }

        /// <summary>Подойти к окну (W) с любого места комнаты.</summary>
        void GoToWindow()
        {
            if (over || Room.Hidden != HideSpot.None || ctx.RoomView.Current == ViewId.Window) return;
            GoTo(ViewId.Window, null);
        }

        void ReturnToSofa()
        {
            if (over || Room.Hidden != HideSpot.None || ctx.RoomView.Current == ViewId.Lounge) return;
            GoTo(ViewId.Lounge, null, zoom: 1.35f);
        }

        /// <summary>
        /// Плавный переход к другому кадру: шаги или шорох поворота, камера наезжает на объект (или уходит
        /// в сторону поворота), экран темнеет, новый кадр проявляется и камера «доходит» до обычного вида.
        /// </summary>
        void GoTo(ViewId target, Vector2? focus, bool turn = false, bool right = false, float zoom = 0.62f)
        {
            if (transitioning || over || Paused) return;
            StartCoroutine(TransitionRoutine(target, focus, turn, right, zoom));
        }

        IEnumerator TransitionRoutine(ViewId target, Vector2? focus, bool turn, bool right, float zoom)
        {
            transitioning = true;
            ctx.Menu.Close();
            RefreshInputLocks();
            if (turn) Sfx.Turn();
            else Sfx.Footsteps();

            StartCoroutine(ctx.Hud.FadeOut(TransitionSeconds));
            if (turn) yield return ctx.Camera.Pan(right ? 1f : -1f, TransitionSeconds);
            else yield return ctx.Camera.Approach(focus ?? (Vector2)ctx.Camera.transform.position, TransitionSeconds, zoom);

            transitioning = false;
            if (!over) ctx.RoomView.Show(target);
            ctx.Camera.Arrive(turn ? new Vector2(right ? -2.5f : 2.5f, 0f) : Vector2.zero, zoom < 1f ? 0.85f : 1.1f);
            RefreshInputLocks();
        }

        void OnViewChanged(ViewId id)
        {
            ctx.Menu.Close();
            ctx.Hud.SetView(ctx.RoomView.CurrentDef);
            ctx.Hud.FlashFromBlack(0.45f);
            ctx.Camera.ResetView(true, id == ViewId.Lounge);
            Track("view", ("view", id.ToString()));
        }

        // ------------------------------------------------------------------ клики и предметы

        void OnHotspotClicked(Hotspot hotspot, Vector2 screen)
        {
            if (transitioning) return;
            if (hotspot.Def.GoTo.HasValue)
            {
                ViewId target = hotspot.Def.GoTo.Value;
                GoTo(target, null, turn: true, right: target == ctx.RoomView.CurrentDef.Right);
                return;
            }

            Vector2 world = ctx.Picker.ScreenToWorld(screen);
            bool onStalker = ctx.StalkerView.Contains(world);
            var actions = GetActions(hotspot.Def);
            if (onStalker) actions.Insert(0, new RoomAction("Посмотреть на него", LookAtStalker));
            Sfx.Click();
            ctx.Menu.Open(hotspot, onStalker ? "Силуэт" : hotspot.Def.Title, actions, screen,
                () => ctx.Picker.InputEnabled && ctx.Picker.PickScreen(GameInput.PointerPosition) == hotspot);
        }

        /// <summary>Курсор на силуэте сталкера: снаружи он — часть окна, за дверью — часть двери.</summary>
        string StalkerTargetAt(Vector2 world)
        {
            if (!ctx.StalkerView.Contains(world)) return null;
            switch (Stalker.Node)
            {
                case StalkerNode.Street:
                case StalkerNode.Window: return RoomContent.WindowGlass;
                case StalkerNode.Door: return RoomContent.Door;
                default: return null;
            }
        }

        void OnItemDragging(Vector2 screen)
        {
            bool canAim = !over && !Paused && Room.Hidden == HideSpot.None;
            SetDropTarget(canAim ? ctx.Picker.PickScreen(screen) : null);
        }

        void SetDropTarget(Hotspot target)
        {
            if (dropTarget == target) return;
            if (dropTarget != null) dropTarget.SetTargeted(false);
            dropTarget = target;
            if (dropTarget != null) dropTarget.SetTargeted(true);
        }

        void OnItemDropped(string itemId, Vector2 screen)
        {
            SetDropTarget(null);
            if (over || Paused || ctx.InventoryView.PointerOver || ctx.PhoneView.PointerOver) return;
            Hotspot target = Room.Hidden == HideSpot.None ? ctx.Picker.PickScreen(screen) : null;
            if (target == null || target.Def.GoTo.HasValue)
            {
                ctx.Subtitles.Show("Сюда это не приложишь.");
                return;
            }
            Track("item_applied", ("item", itemId), ("hotspot", target.Def.Id));
            string thought = ApplyItem(itemId, target.Def);
            if (!string.IsNullOrEmpty(thought)) ctx.Subtitles.Show(thought);
        }

        void MakeNoise(float amount)
        {
            Room.Noise = Mathf.Min(100f, Room.Noise + amount);
            Stalker.HearNoise(amount);
        }

        string Hide(HideSpot spot)
        {
            if (Room.Hidden != HideSpot.None) return null;

            Room.Hidden = spot;
            ctx.Menu.Close();
            ctx.Hud.ShowHidden(spot);
            Track("hide", ("spot", spot.ToString()), ("stalker", Stalker.Node.ToString()));
            MakeNoise(4f);
            if (ctx.Phone.Ring == RingMode.Sound && !ringHintShown)
            {
                ringHintShown = true;
                ctx.Hud.ShowToast("Звук телефона включён — уведомление выдаст тебя.\nТелефон → Настройки → «Без звука».", 5f);
            }
            RefreshInputLocks();
            UpdateHint();
            return null;
        }

        void LeaveHiding()
        {
            if (over || Room.Hidden == HideSpot.None) return;

            Track("unhide", ("spot", Room.Hidden.ToString()), ("stalker", Stalker.Node.ToString()));
            Room.Hidden = HideSpot.None;
            ctx.Hud.ShowHidden(HideSpot.None);
            RefreshInputLocks();
            UpdateHint();
            Stalker.OnPlayerLeftHiding();
            if (!over) MakeNoise(4f);
        }

        void OnCallStarted(string number)
        {
            Track("call", ("number", number), ("hidden", Room.Hidden.ToString()));
            bool emergency = DialerApp.IsEmergency(number);
            ctx.Subtitles.Show(emergency ? "Ответьте. Ну же, ответьте…" : "Возьми трубку. Пожалуйста, возьми трубку.");
            // Голос в укрытии слышно.
            if (Room.Hidden != HideSpot.None) MakeNoise(6f);
        }

        /// <summary>Диспетчер принял вызов. Свободных машин нет — помощь будет к шести утра.</summary>
        void OnRescueRequested()
        {
            if (over) return;
            if (Stalker.FinalAssault)
            {
                ctx.Subtitles.Show("Слишком поздно. Они не успеют.");
                return;
            }
            rescueCalled = true;
            Track("rescue_called", ("clock", ClockText));
            ctx.Hud.ShowToast("Вызов принят. Помощь — не позднее 6 AM.", 4f);
            UpdateHint();
        }

        void ClosePeephole()
        {
            if (!ctx.Hud.PeepholeOpen) return;
            ctx.Hud.ShowPeephole(false);
            RefreshInputLocks();
        }

        // ------------------------------------------------------------------ свет

        void UpdateFlicker(float dt)
        {
            nextFlicker -= dt;
            if (nextFlicker > 0f) return;
            nextFlicker = Phase >= NightPhase.Danger ? Random.Range(12f, 30f) : Random.Range(25f, 60f);
            if (Room.LightOn) StartCoroutine(Flicker());
        }

        /// <summary>Лампа мигает: несколько провалов яркости с треском.</summary>
        IEnumerator Flicker()
        {
            Sfx.Buzz();
            foreach (float brightness in new[] { 0.15f, 1f, 0.05f, 0.75f, 0.25f })
            {
                // В темноте между вспышками силуэт оказывается в другом месте — и чуть ближе.
                if (brightness < 0.2f) ctx.StalkerView.Lurch();
                ctx.RoomView.SetFlicker(brightness);
                yield return new WaitForSeconds(Random.Range(0.04f, 0.14f));
            }
            ctx.RoomView.SetFlicker(1f);
            ctx.StalkerView.Settle();
        }

        // ------------------------------------------------------------------ сталкер

        void OnStalkerMoved(StalkerNode node)
        {
            Track("stalker", ("node", node.ToString()), ("clock", ClockText));
            if (node == StalkerNode.Window) OnStalkerAtWindow();
            UpdateHint();
        }

        void OnStalkerAtDoor(bool locked)
        {
            Sfx.Knock();
            ctx.Hud.ShowToast(locked ? "Стук в дверь. Ручку дёргают." : "Стук в дверь.");
            Track("stalker_at_door", ("locked", locked));
            if (Room.LightOn) StartCoroutine(Flicker());
            OnDoorMessage(locked);
        }

        void OnStalkerEntered()
        {
            if (Room.Hidden == HideSpot.Wardrobe && ctx.Inventory.Has(ItemCatalog.Shard))
            {
                SelfDefense();
                return;
            }
            ctx.Hud.ShowToast("Дверь открылась. Шаги в комнате...");
            Track("stalker_entered", ("hidden", Room.Hidden.ToString()), ("visit", Stalker.Visits));
            OnRoomMessage();
        }

        void OnVisitSurvived()
        {
            ctx.Hud.ShowToast("Шаги удаляются.");
            Track("visit_survived", ("visits", Stalker.Visits), ("spot", Room.Hidden.ToString()));
            OnSurvivedMessage();
            UpdateHint();
        }

        void UpdateHint()
        {
            if (over) return;

            bool hidden = Room.Hidden != HideSpot.None;
            if (Stalker.FinalAssault && Stalker.Node != StalkerNode.Room) ctx.Hud.SetHint("6 AM. Он идёт за тобой. Никто не приедет.", true);
            else if (Stalker.Node == StalkerNode.Room) ctx.Hud.SetHint("Он в комнате. Не шевелись.", true);
            else if (Stalker.Node == StalkerNode.Door) ctx.Hud.SetHint("Кто-то у двери! Спрячься: шкаф или под кровать.", true);
            else if (hidden) ctx.Hud.SetHint("Ты в укрытии. " + KeyBindings.Short(GameAction.Unhide) + " — вылезти.", false);
            else if (Phase == NightPhase.Calm) ctx.Hud.SetHint("Ночь. Осмотрись в комнате.", false);
            else if (rescueCalled) ctx.Hud.SetHint("Помощь едет. Продержись до 6 AM.", false);
            else ctx.Hud.SetHint("Продержись до 6 AM.", false);
        }

        /// <summary>Концовка «Синие огни»: вызвала помощь и дожила до шести.</summary>
        void Win()
        {
            Track("night_end", ("result", "police"), ("visits", Stalker.Visits), ("clock", "06:00"));
            Finish();
            ctx.Hud.SetClock("6 AM");
            ctx.EndScreen.Show("Синие огни",
                "Ровно в шесть по потолку поползли синие и красные отсветы. Хлопнули дверцы машин, по лестнице загрохотали шаги — " +
                "на этот раз чужие, тяжёлые, спешащие. Его взяли у подъезда: он стоял под твоим окном и даже не пытался бежать.\n\n" +
                "Тебя вывели под руку, укутали в чьё-то пальто. Ты смотрела, как его, в наручниках, сажают в машину и увозят в участок. " +
                "Он обернулся всего один раз.\n\nВизитов за ночь: " + Stalker.Visits + "." + Survived(), true, CanGoNext);
        }

        /// <summary>Пережила ночь: открыть следующую. Строка для текста концовки.</summary>
        string Survived()
        {
            bool opened = Nights.Complete(ctx.Night.Number);
            return opened ? "\n\nОткрыта ночь " + (ctx.Night.Number + 1) + "." : "";
        }

        bool CanGoNext => ctx.NextNight != null && Nights.HasNext(ctx.Night.Number) && Nights.IsUnlocked(ctx.Night.Number + 1);

        /// <summary>
        /// Осколок и шкаф: он открыл дверцу — и не ожидал удара. Если полиция уже ехала, её найдут рядом с телом;
        /// если нет — никто никогда не узнает, что случилось этой ночью.
        /// </summary>
        void SelfDefense()
        {
            Track("night_end", ("result", rescueCalled ? "killed_prison" : "killed_free"), ("visits", Stalker.Visits), ("clock", ClockText));
            Finish();
            if (rescueCalled)
            {
                ctx.EndScreen.Show("Цена тишины",
                    "Дверца шкафа скрипнула, и в щель упал свет. Ты не помнишь, как рука сама нашла осколок, — помнишь только, " +
                    "что он больше не встал. Стекло порезало ладонь до кости, но боли не было. Была тишина.\n\n" +
                    "Полиция приехала к шести, как и обещали. Тебя нашли на полу, рядом с ним. Наручники щёлкнули так же тихо, " +
                    "как замок в твоей двери. Следствие, суд, «превышение пределов необходимой обороны». Теперь между тобой и миром — решётка. " +
                    "Но по ночам ты, наконец, спишь.", false);
                return;
            }
            ctx.EndScreen.Show("Утро без него",
                "Дверца шкафа скрипнула, и в щель упал свет. Ты ударила раньше, чем успела испугаться. " +
                "Один раз. Этого хватило.\n\nКогда рассвело, в комнате было очень тихо. Никто не звонил в дверь, никто не задавал вопросов — " +
                "ты ведь никого не звала. Телефон больше не вибрировал.\n\nТы выжила. И это — единственное, что теперь имеет значение." + Survived(), true, CanGoNext);
        }

        void Lose(string reason)
        {
            Track("night_end", ("result", "caught"), ("reason", reason), ("visits", Stalker.Visits), ("clock", ClockText));
            Finish();
            ctx.EndScreen.Show("Он нашёл тебя.", reason + "\n\nВремя: " + HourText + ".", false);
        }

        void Finish()
        {
            if (over) return;
            over = true;
            if (Stalker.Active) Stalker.Leave();
            ctx.Menu.Close();
            ctx.Hud.ShowPeephole(false);
            ctx.Subtitles.Clear();
            ctx.PhoneView.Close();
            ctx.PhoneView.SetTyping(false);
            ctx.RoomView.SetFlicker(1f);
            Sfx.StopRingback();
            SetCameraLocked(false);
            ctx.Hud.SetHint("", false);
            ctx.Hud.SetTooltip(null);
            RefreshInputLocks();
            if (ctx.Backend != null) ctx.Backend.EndSession();
        }

        /// <summary>Событие ночи для аналитики на сервере.</summary>
        void Track(string type, params (string, object)[] fields)
        {
            if (ctx.Backend != null) ctx.Backend.SendEvent(type, BackendClient.Json(fields));
        }

        string DebugText() =>
            "Сталкер: " + Stalker.Node + (Stalker.Active ? "" : " (неактивен)") +
            ", таймер " + Stalker.Timer.ToString("0.0") + " c\n" +
            "Внимание: " + Stalker.Attention.ToString("0") + ", шум: " + Room.Noise.ToString("0") +
            ", помнит укрытие: " + Stalker.RememberedSpot + "\n" +
            "Фаза: " + Phase + ", визитов: " + Stalker.Visits + ", " + Elapsed.ToString("0") + "/" + Settings.NightSeconds.ToString("0") + " c" +
            ", " + ClockText + (ModelOnline ? ", модель" : ", скрипт") + "\n" +
            "Свет: " + (Room.LightOn ? "вкл" : "выкл") + ", шторы: " + (Room.CurtainsClosed ? "задёрнуты" : "открыты") +
            ", дверь: " + (Room.DoorLocked ? "заперта" : "открыта") + ", укрытие: " + Room.Hidden + "\n" +
            "Помощь: " + (rescueCalled ? "вызвана" : "нет") + (Stalker.FinalAssault ? ", ФИНАЛЬНЫЙ ВИЗИТ" : "");
    }
}
