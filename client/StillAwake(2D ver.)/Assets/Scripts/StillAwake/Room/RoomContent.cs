using System.Collections.Generic;

namespace StillAwake
{
    /// <summary>Кадры комнаты (локации из п.1 художника). Поворот головы ходит по кругу.</summary>
    public enum ViewId
    {
        Window,
        Door,
        UnderBed,
        Wardrobe,
        Lounge,
    }

    public enum HideSpot
    {
        None,
        Wardrobe,
        UnderBed,
    }

    public enum HotspotShape
    {
        Rect,
        Circle,
    }

    /// <summary>Где сейчас сталкер.</summary>
    public enum StalkerNode
    {
        Away,
        Street,
        Window,
        Door,
        Room,
    }

    public sealed class ViewDef
    {
        public ViewId Id;
        public string Title;
        public string Sprite;
        /// <summary>Кадр при выключенном свете (п.3 художника). Если его нет, кадр просто затемняется.</summary>
        public string DarkSprite;
        public ViewId Left;
        public ViewId Right;
        public HotspotDef[] Hotspots;
    }

    public sealed class HotspotDef
    {
        public string Id;
        public string Title;
        public HotspotShape Shape;
        /// <summary>Пиксели кадра 3000×2000 от левого верхнего угла. Для круга — описанный квадрат.</summary>
        public PxRect Area;
        /// <summary>Чем выше, тем важнее, когда области накладываются (кружка на столе, глазок на двери).</summary>
        public int Priority;
        public string Description;
        /// <summary>Зона поворота: клик переключает на этот кадр.</summary>
        public ViewId? GoTo;

        public static HotspotDef Box(string id, string title, float x, float y, float w, float h, int priority, string description) =>
            new HotspotDef { Id = id, Title = title, Shape = HotspotShape.Rect, Area = new PxRect(x, y, w, h), Priority = priority, Description = description };

        public static HotspotDef Round(string id, string title, float cx, float cy, float radius, int priority, string description) =>
            new HotspotDef { Id = id, Title = title, Shape = HotspotShape.Circle, Area = new PxRect(cx - radius, cy - radius, radius * 2f, radius * 2f), Priority = priority, Description = description };

        public static HotspotDef Turn(string id, string title, float x, float y, float w, float h, ViewId target) =>
            new HotspotDef { Id = id, Title = title, Shape = HotspotShape.Rect, Area = new PxRect(x, y, w, h), Priority = 0, GoTo = target };
    }

    public readonly struct StalkerSpot
    {
        public readonly PxRect Area;
        public readonly float Alpha;

        public StalkerSpot(PxRect area, float alpha)
        {
            Area = area;
            Alpha = alpha;
        }
    }

    /// <summary>
    /// Разметка комнаты: кадры, области взаимодействия и точки сталкера. Координаты совпадают с тем,
    /// что нарисовано на заглушках в Assets/Art/Resources/Rooms. Художник поменял кадр — поправь цифры
    /// здесь; F1 в игре подсвечивает все области.
    /// </summary>
    public static class RoomContent
    {
        public const string WindowGlass = "window";
        public const string CurtainLeft = "curtain_left";
        public const string CurtainRight = "curtain_right";
        public const string WindowTable = "window_table";
        public const string Mug = "mug";
        public const string Chair = "chair";

        public const string Desk = "desk";
        public const string Laptop = "laptop";
        public const string LightSwitch = "light_switch";
        public const string Door = "door";
        public const string Peephole = "peephole";
        public const string Hanger = "hanger";

        public const string Bed = "bed";
        public const string Pillow = "pillow";
        public const string UnderBedGap = "under_bed";
        public const string Box = "box";

        public const string Fridge = "fridge";
        public const string Mirror = "mirror";
        public const string Nightstand = "nightstand";
        public const string Wardrobe = "wardrobe";
        public const string WardrobeShelf = "wardrobe_shelf";

        /// <summary>Что закрывают задёрнутые шторы в кадре окна.</summary>
        public static readonly PxRect CurtainsClosedArea = new PxRect(960, 220, 1080, 860);

        public static readonly ViewDef[] Views =
        {
            new ViewDef
            {
                Id = ViewId.Lounge, Title = "На диване",
                Left = ViewId.Door, Right = ViewId.Wardrobe,
                Hotspots = new HotspotDef[0],
            },
            new ViewDef
            {
                Id = ViewId.Window, Title = "У окна", Sprite = "Rooms/window", DarkSprite = "Rooms/window_dark",
                Left = ViewId.Door, Right = ViewId.Wardrobe,
                Hotspots = new[]
                {
                    HotspotDef.Box(WindowGlass, "Окно", 1000, 250, 1000, 800, 1, "Большое окно на улицу. Внизу — тротуар и фонарь."),
                    HotspotDef.Box(CurtainLeft, "Штора", 880, 200, 260, 1000, 2, "Плотные тёмные шторы."),
                    HotspotDef.Box(CurtainRight, "Штора", 1860, 200, 260, 1000, 2, "Плотные тёмные шторы."),
                    HotspotDef.Box(WindowTable, "Стол у окна", 1000, 1200, 1000, 380, 1, "Лёгкий стол. Передвинуть можно, но будет шумно."),
                    HotspotDef.Round(Mug, "Кружка", 1250, 1300, 70, 3, "Остывший чай. Ты так и не допила его."),
                    HotspotDef.Box(Chair, "Стул", 2080, 1050, 320, 650, 1, "Обычный стул. На спинке висит кофта."),
                    HotspotDef.Turn("turn_left", "← К двери", 0, 300, 200, 1400, ViewId.Door),
                    HotspotDef.Turn("turn_right", "К шкафу →", 2800, 300, 200, 1400, ViewId.Wardrobe),
                },
            },
            new ViewDef
            {
                Id = ViewId.Door, Title = "У двери", Sprite = "Rooms/door",
                Left = ViewId.UnderBed, Right = ViewId.Window,
                Hotspots = new[]
                {
                    HotspotDef.Box(Desk, "Компьютерный стол", 250, 1200, 750, 500, 1, "Провода, зарядки, учебники."),
                    HotspotDef.Box(Laptop, "Ноутбук", 380, 950, 450, 280, 2, "Ноутбук. Можно почитать новости."),
                    HotspotDef.Box(LightSwitch, "Выключатель", 1050, 800, 130, 200, 2, "Выключатель основного света."),
                    HotspotDef.Box(Door, "Дверь", 1250, 250, 700, 1450, 1, "Дверь в коридор. Замок запирается ключом изнутри."),
                    HotspotDef.Round(Peephole, "Глазок", 1600, 800, 45, 3, "Дверной глазок."),
                    HotspotDef.Box(Hanger, "Вешалка", 2150, 250, 450, 950, 1, "Куртки и сумка."),
                    HotspotDef.Turn("turn_left", "← К кровати", 0, 300, 200, 1400, ViewId.UnderBed),
                    HotspotDef.Turn("turn_right", "К окну →", 2800, 300, 200, 1400, ViewId.Window),
                },
            },
            new ViewDef
            {
                Id = ViewId.UnderBed, Title = "Кровать", Sprite = "Rooms/under_bed",
                Left = ViewId.Wardrobe, Right = ViewId.Door,
                Hotspots = new[]
                {
                    HotspotDef.Box(Bed, "Кровать", 600, 700, 1800, 750, 1, "Смятое одеяло. Подушка ещё тёплая."),
                    HotspotDef.Round(Pillow, "Подушка", 900, 830, 110, 2, "Ты лежала здесь пять минут назад."),
                    HotspotDef.Box(UnderBedGap, "Под кроватью", 700, 1450, 1600, 250, 2, "Под кроватью хватит места, чтобы спрятаться."),
                    HotspotDef.Box(Box, "Коробка", 2000, 1480, 260, 180, 3, "Коробка из-под обуви."),
                    HotspotDef.Turn("turn_left", "← К шкафу", 0, 300, 200, 1400, ViewId.Wardrobe),
                    HotspotDef.Turn("turn_right", "К двери →", 2800, 300, 200, 1400, ViewId.Door),
                },
            },
            new ViewDef
            {
                Id = ViewId.Wardrobe, Title = "Шкаф", Sprite = "Rooms/wardrobe",
                Left = ViewId.Window, Right = ViewId.UnderBed,
                Hotspots = new[]
                {
                    HotspotDef.Box(Fridge, "Холодильник", 250, 650, 450, 1000, 1, "Маленький холодильник. Тихо гудит."),
                    HotspotDef.Box(Mirror, "Зеркало", 900, 250, 550, 750, 1, "Большое зеркало над тумбами."),
                    HotspotDef.Box(Nightstand, "Тумбы", 850, 1150, 650, 550, 1, "Косметика, фотографии, расчёска."),
                    HotspotDef.Box(Wardrobe, "Шкаф", 1750, 200, 1000, 1550, 1, "Большой шкаф. Внутри можно спрятаться."),
                    HotspotDef.Box(WardrobeShelf, "Верхняя полка", 1850, 300, 800, 220, 2, "Свитера и коробки."),
                    HotspotDef.Turn("turn_left", "← К окну", 0, 300, 200, 1400, ViewId.Window),
                    HotspotDef.Turn("turn_right", "К кровати →", 2800, 300, 200, 1400, ViewId.UnderBed),
                },
            },
        };

        /// <summary>
        /// Где рисуется сталкер-квадрат: (где он, какой кадр) → две точки, между которыми он ходит.
        /// Из укрытий (шкаф, под кроватью) видна только середина кадра — точки подобраны под щель.
        /// </summary>
        static readonly Dictionary<(StalkerNode, ViewId), StalkerSpot[]> Spots = new Dictionary<(StalkerNode, ViewId), StalkerSpot[]>
        {
            { (StalkerNode.Street, ViewId.Window), new[] { Spot(1250, 620, 80, 80), Spot(1650, 600, 90, 90) } },
            { (StalkerNode.Window, ViewId.Window), new[] { Spot(1250, 380, 320, 320), Spot(1450, 400, 300, 300) } },
            // За закрытой дверью — полупрозрачный силуэт.
            { (StalkerNode.Door, ViewId.Door), new[] { Spot(1450, 650, 300, 300, 0.35f) } },
            { (StalkerNode.Room, ViewId.Window), new[] { Spot(500, 800, 450, 450), Spot(1900, 850, 450, 450) } },
            { (StalkerNode.Room, ViewId.Door), new[] { Spot(700, 800, 450, 450), Spot(1500, 850, 450, 450) } },
            { (StalkerNode.Room, ViewId.Wardrobe), new[] { Spot(1100, 800, 380, 380), Spot(1550, 850, 380, 380) } },
            { (StalkerNode.Room, ViewId.UnderBed), new[] { Spot(600, 850, 350, 350), Spot(1950, 880, 350, 350) } },
        };

        public static ViewDef Get(ViewId id)
        {
            foreach (ViewDef view in Views)
                if (view.Id == id) return view;
            return Views[0];
        }

        /// <summary>Точки сталкера в кадре или null, если из этого кадра его не видно.</summary>
        public static StalkerSpot[] StalkerSpots(StalkerNode node, ViewId view) =>
            Spots.TryGetValue((node, view), out StalkerSpot[] spots) ? spots : null;

        static StalkerSpot Spot(float x, float y, float w, float h, float alpha = 1f) => new StalkerSpot(new PxRect(x, y, w, h), alpha);
    }
}
