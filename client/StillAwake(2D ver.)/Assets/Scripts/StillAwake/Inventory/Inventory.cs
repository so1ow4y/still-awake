using System;
using System.Collections.Generic;

namespace StillAwake
{
    public sealed class ItemDef
    {
        public readonly string Id;
        public readonly string Title;
        public readonly string Description;

        public ItemDef(string id, string title, string description)
        {
            Id = id;
            Title = title;
            Description = description;
        }
    }

    /// <summary>Все предметы прототипа. Иконка: Assets/Art/Resources/Items/&lt;id&gt;.png.</summary>
    public static class ItemCatalog
    {
        public const string Key = "key";
        public const string Flashlight = "flashlight";
        public const string Note = "note";
        public const string Powerbank = "powerbank";

        static readonly ItemDef[] All =
        {
            new ItemDef(Key, "Ключ", "Ключ от двери комнаты. Можно запереться изнутри."),
            new ItemDef(Flashlight, "Фонарик", "Светит ярко. В темноте помогает, но свет могут заметить."),
            new ItemDef(Note, "Записка", "Сложенный листок. Почерк незнакомый."),
            new ItemDef(Powerbank, "Пауэрбанк", "Заряжен. Пригодится, если отключат электричество."),
        };

        public static ItemDef Get(string id)
        {
            foreach (ItemDef item in All)
                if (item.Id == id) return item;
            return new ItemDef(id, id, "");
        }
    }

    /// <summary>Небольшой инвентарь: GDD требует, чтобы он не превращался в отдельную сложную систему.</summary>
    public sealed class Inventory
    {
        public readonly int Capacity;
        public IReadOnlyList<string> Items => items;

        public event Action Changed;

        readonly List<string> items = new List<string>();

        public Inventory(int capacity) => Capacity = capacity;

        public bool Has(string id) => items.Contains(id);

        public bool Add(string id)
        {
            if (items.Count >= Capacity || Has(id)) return false;
            items.Add(id);
            Changed?.Invoke();
            return true;
        }

        public bool Remove(string id)
        {
            if (!items.Remove(id)) return false;
            Changed?.Invoke();
            return true;
        }
    }
}
