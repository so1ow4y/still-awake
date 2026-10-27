using System.Collections.Generic;
using UnityEngine;

namespace StillAwake
{
    // Действия с объектами комнаты и предметами. Каждое действие возвращает мысль героини — она идёт в субтитры.
    public sealed partial class NightController
    {
        List<RoomAction> GetActions(HotspotDef hotspot)
        {
            var actions = new List<RoomAction> { new RoomAction("Осмотреть", () => hotspot.Description) };

            if (ctx.RoomView.IsAuthored)
            {
                ViewId? destination = null;
                switch (hotspot.Id)
                {
                    case RoomContent.WindowGlass: case RoomContent.WindowTable: destination = ViewId.Window; break;
                    case RoomContent.Door: case RoomContent.Desk: case RoomContent.Laptop: destination = ViewId.Door; break;
                    case RoomContent.Bed: destination = ViewId.UnderBed; break;
                    case RoomContent.Wardrobe: case RoomContent.Nightstand: case RoomContent.Mirror: case RoomContent.Fridge: destination = ViewId.Wardrobe; break;
                }
                if (destination.HasValue)
                {
                    ViewId target = destination.Value;
                    Vector2 focus = FrameSpace.ToWorld(hotspot.Area).center;
                    actions.Add(new RoomAction("Подойти: " + RoomContent.Get(target).Title, () =>
                    {
                        GoTo(target, focus);
                        return null;
                    }));
                }
            }

            switch (hotspot.Id)
            {
                case RoomContent.WindowGlass:
                    actions.Add(new RoomAction("Посмотреть в окно", LookOutside));
                    if (ctx.RoomView.IsAuthored) actions.Add(new RoomAction(Room.CurtainsClosed ? "Раздвинуть шторы" : "Задёрнуть шторы", ToggleCurtains));
                    break;
                case RoomContent.CurtainLeft:
                case RoomContent.CurtainRight:
                    actions.Add(new RoomAction(Room.CurtainsClosed ? "Раздвинуть шторы" : "Задёрнуть шторы", ToggleCurtains));
                    break;
                case RoomContent.WindowTable:
                    actions.Add(Search(hotspot.Id, "Обыскать ящик", ItemCatalog.Flashlight, "В ящике — фонарик. Батарейки вроде живые."));
                    actions.Add(new RoomAction(Room.TableMoved ? "Вернуть стол на место" : "Передвинуть стол", MoveTable));
                    break;

                case RoomContent.Door:
                    actions.Add(new RoomAction("Прислушаться", ListenAtDoor));
                    if (ctx.RoomView.IsAuthored) actions.Add(new RoomAction("Посмотреть в глазок", LookThroughPeephole));
                    actions.Add(new RoomAction(Room.DoorLocked ? "Отпереть дверь" : "Запереть дверь", ToggleDoorLock));
                    break;
                case RoomContent.Peephole:
                    actions.Add(new RoomAction("Посмотреть в глазок", LookThroughPeephole));
                    break;
                case RoomContent.LightSwitch:
                    actions.Add(new RoomAction(Room.LightOn ? "Выключить свет" : "Включить свет", ToggleLight));
                    break;
                case RoomContent.Hanger:
                    actions.Add(Search(hotspot.Id, "Обыскать карманы", ItemCatalog.Key, "В кармане куртки — ключ от комнаты."));
                    break;
                case RoomContent.Desk:
                    if (ctx.RoomView.IsAuthored) actions.Add(Search(hotspot.Id, "Обыскать ящик", ItemCatalog.Key, "В ящике стола — ключ от комнаты."));
                    break;
                case RoomContent.Nightstand:
                    if (ctx.RoomView.IsAuthored) actions.Add(Search(hotspot.Id, "Обыскать тумбу", ItemCatalog.Powerbank, "В тумбе — пауэрбанк."));
                    break;
                case RoomContent.Laptop:
                    actions.Add(new RoomAction("Открыть ноутбук", ReadLaptop));
                    break;

                case RoomContent.Bed:
                case RoomContent.UnderBedGap:
                    actions.Add(new RoomAction("Залезть под кровать", () => Hide(HideSpot.UnderBed)));
                    break;
                case RoomContent.Box:
                    actions.Add(Search(hotspot.Id, ctx.RoomView.IsAuthored ? "Открыть книгу" : "Открыть коробку", ItemCatalog.Note, "Между страницами — сложенная записка."));
                    break;

                case RoomContent.Wardrobe:
                    actions.Add(new RoomAction("Спрятаться в шкафу", () => Hide(HideSpot.Wardrobe)));
                    break;
                case RoomContent.WardrobeShelf:
                    actions.Add(Search(hotspot.Id, "Обыскать полку", ItemCatalog.Powerbank, "Под свитерами — пауэрбанк."));
                    break;
                case RoomContent.Mirror:
                    actions.Add(new RoomAction("Посмотреть в зеркало", LookInMirror));
                    break;
                case RoomContent.Fridge:
                    actions.Add(new RoomAction("Открыть холодильник", OpenFridge));
                    break;
            }

            // Каждое выбранное действие — событие для аналитики ночи.
            for (int i = 0; i < actions.Count; i++)
            {
                RoomAction action = actions[i];
                actions[i] = new RoomAction(action.Title, () =>
                {
                    Track("action", ("hotspot", hotspot.Id), ("action", action.Title));
                    return action.Run();
                });
            }
            return actions;
        }

        RoomAction Search(string hotspotId, string title, string itemId, string found) =>
            new RoomAction(title, () =>
            {
                if (Room.Looted.Contains(hotspotId)) return "Здесь больше ничего нет.";
                if (!Room.LightOn && !Room.FlashlightOn) return "Слишком темно, ничего не разобрать. Нужен свет.";
                if (!ctx.Inventory.Add(itemId)) return "Инвентарь заполнен.";

                Room.Looted.Add(hotspotId);
                MakeNoise(3f);
                Sfx.Pickup();
                ctx.Hud.ShowToast("Получено: " + ItemCatalog.Get(itemId).Title);
                Track("item_found", ("item", itemId));
                return found;
            });

        string LookOutside()
        {
            if (Room.CurtainsClosed) return "Шторы задёрнуты. Ничего не видно.";

            switch (Stalker.Node)
            {
                case StalkerNode.Street:
                    Stalker.AddAttention(5f);
                    return "Под фонарём на той стороне улицы кто-то стоит. Лица не видно.";
                case StalkerNode.Window:
                    Stalker.AddAttention(15f);
                    OnSeenThroughWindow();
                    return "Прямо за стеклом — силуэт. Он смотрит на тебя.";
                default:
                    return Room.LightOn
                        ? "В стекле отражается освещённая комната. Улицу почти не видно."
                        : "Пустая улица. Ветер качает фонарь.";
            }
        }

        string ToggleCurtains()
        {
            Room.CurtainsClosed = !Room.CurtainsClosed;
            MakeNoise(3f);
            Room.NotifyChanged();
            if (Room.CurtainsClosed) OnCurtainsClosed();
            return Room.CurtainsClosed ? "Шторы задёрнуты. С улицы комнату не видно." : "Шторы раздвинуты.";
        }

        string MoveTable()
        {
            Room.TableMoved = !Room.TableMoved;
            MakeNoise(45f);
            return "Ножки стола со скрежетом проехали по полу. Очень громко.";
        }

        string ListenAtDoor()
        {
            if (Stalker.Node == StalkerNode.Door) return "За дверью кто-то тяжело дышит.";
            return "Тишина. Где-то капает вода.";
        }

        string ToggleDoorLock()
        {
            if (!ctx.Inventory.Has(ItemCatalog.Key)) return "Нужен ключ. Где-то в комнате он должен быть.";

            Room.DoorLocked = !Room.DoorLocked;
            MakeNoise(8f);
            Room.NotifyChanged();
            if (Room.DoorLocked) Stalker.OnDoorLocked();
            return Room.DoorLocked ? "Ключ дважды провернулся в замке. Заперто." : "Замок открыт.";
        }

        string LookThroughPeephole()
        {
            if (Stalker.Node == StalkerNode.Door)
            {
                Stalker.AddAttention(10f);
                return "В глазке — тёмная фигура вплотную к двери.";
            }
            return "Пустой коридор. Лампочка мигает.";
        }

        string ToggleLight()
        {
            Room.LightOn = !Room.LightOn;
            Sfx.Switch();
            MakeNoise(2f);
            Room.NotifyChanged();
            if (!Room.LightOn) OnLightTurnedOff();
            return Room.LightOn ? "Свет включён." : "Свет выключен. Комнату заливает лунный свет.";
        }

        string ReadLaptop()
        {
            const string news = "Новости: «В районе продолжаются поиски пропавшей студентки. " +
                                "Полиция просит жителей запирать двери на ночь».";
            if (Room.LightOn) return news;

            Stalker.AddAttention(5f);
            return news + "\n\nЭкран ярко светится в темноте.";
        }

        string LookInMirror()
        {
            if ((Stalker.Node == StalkerNode.Window || Stalker.Node == StalkerNode.Street) && !Room.CurtainsClosed)
                return "В отражении за спиной — окно. Кажется, там кто-то есть.";
            return "Уставшее лицо. Пора бы спать.";
        }

        string OpenFridge()
        {
            MakeNoise(6f);
            return "Компрессор загудел. Йогурт, вода, лекарства — ничего полезного.";
        }

        void UseItem(string itemId)
        {
            Track("item_used", ("item", itemId));
            string result;
            switch (itemId)
            {
                case ItemCatalog.Key:
                    result = (ctx.RoomView.IsAuthored || ctx.RoomView.Current == ViewId.Door) && Room.Hidden == HideSpot.None
                        ? ToggleDoorLock()
                        : "Ключ от двери. Подойди к двери, чтобы запереть её.";
                    break;

                case ItemCatalog.Flashlight:
                    Room.FlashlightOn = !Room.FlashlightOn;
                    Room.NotifyChanged();
                    if (Room.FlashlightOn && Room.Hidden != HideSpot.None) MakeNoise(15f);
                    if (Room.FlashlightOn && Stalker.Node == StalkerNode.Window && !Room.CurtainsClosed) Stalker.AddAttention(15f);
                    result = !Room.FlashlightOn ? "Фонарик выключен."
                        : Room.LightOn ? "Фонарик включён. При свете от него мало толку."
                        : "Фонарик включён. Луч выхватывает комнату из темноты.";
                    break;

                case ItemCatalog.Note:
                    result = "«Я всегда рядом. Даже когда ты спишь».";
                    break;

                case ItemCatalog.Powerbank:
                    result = "Заряд 100%. Пригодится, если отключат электричество. (заглушка)";
                    break;

                default:
                    result = "Непонятно, что с этим делать.";
                    break;
            }
            ctx.Subtitles.Show(result);
        }

        /// <summary>Предмет перетащили из хотбара на объект комнаты.</summary>
        string ApplyItem(string itemId, HotspotDef target)
        {
            switch (itemId)
            {
                case ItemCatalog.Key:
                    if (target.Id == RoomContent.Door || target.Id == RoomContent.Peephole) return ToggleDoorLock();
                    return "Ключ от двери. Здесь замка нет.";

                case ItemCatalog.Flashlight:
                    return ShineAt(target);

                case ItemCatalog.Note:
                    return target.Id == RoomContent.WindowGlass
                        ? "Приложить записку к стеклу? Чтобы он прочитал, что я её нашла? Нет."
                        : "Не хочу её здесь оставлять. Пусть будет у меня.";

                case ItemCatalog.Powerbank:
                    return target.Id == RoomContent.Laptop
                        ? "Ноутбук и так заряжен. Пауэрбанк лучше оставить для телефона."
                        : "Пауэрбанк тут ни к чему.";

                default:
                    return "Не думаю, что это поможет.";
            }
        }

        /// <summary>Посветить фонариком на объект: в темноте видно подробности, а в окне — его лицо.</summary>
        string ShineAt(HotspotDef target)
        {
            if (target.Id == RoomContent.WindowGlass && !Room.CurtainsClosed &&
                (Stalker.Node == StalkerNode.Window || Stalker.Node == StalkerNode.Street))
            {
                Stalker.AddAttention(20f);
                OnSeenThroughWindow();
                return Stalker.Node == StalkerNode.Window
                    ? "Луч упёрся в стекло — и в чьё-то лицо за ним. Он даже не зажмурился."
                    : "Луч не достаёт до фонаря напротив. Но фигура там повернулась ко мне.";
            }
            if (Room.LightOn) return "Свет и так горит — фонарик тут лишний.";
            Stalker.AddAttention(3f);
            return "Луч выхватывает из темноты: " + target.Description.TrimEnd('.').ToLowerInvariant() + ".";
        }

        /// <summary>Клик прямо по силуэту сталкера.</summary>
        string LookAtStalker()
        {
            Stalker.AddAttention(10f);
            switch (Stalker.Node)
            {
                case StalkerNode.Window:
                    OnSeenThroughWindow();
                    return "Он стоит вплотную к стеклу. Не двигается. Просто смотрит на меня.";
                case StalkerNode.Street:
                    return "Под фонарём напротив кто-то стоит. Лица не разобрать, но он смотрит на моё окно.";
                case StalkerNode.Door:
                    return "В щели под дверью — тень. Кто-то стоит совсем близко.";
                default:
                    return "Показалось?..";
            }
        }
    }
}
