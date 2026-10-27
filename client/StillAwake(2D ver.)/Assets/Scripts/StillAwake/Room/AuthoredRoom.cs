using System.Collections.Generic;
using UnityEngine;

namespace StillAwake
{
    // Adapts the editable room objects to the night simulation. No legacy controllers run here.
    public sealed class AuthoredRoom
    {
        /// <summary>Контур меньше этой доли от самого большого контура объекта — случайный мазок, а не часть объекта.</summary>
        const float SpeckShare = 0.02f;

        readonly Transform root;
        readonly Dictionary<string, Collider2D> objects = new Dictionary<string, Collider2D>();
        readonly Dictionary<SpriteRenderer, Color> colors = new Dictionary<SpriteRenderer, Color>();
        public Rect WindowRect { get; private set; }

        public AuthoredRoom(Transform roomRoot)
        {
            root = roomRoot;
            foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>())
                colors[renderer] = renderer.color;
            foreach (var collider in root.GetComponentsInChildren<Collider2D>())
            {
                if (collider is PolygonCollider2D polygon) RemoveSpecks(polygon);
                objects[collider.name] = collider;
            }
            Physics2D.SyncTransforms();
            WindowRect = BoundsOf("Окно");
        }

        /// <summary>
        /// Коллайдеры собраны по альфе спрайтов и цепляют случайные мазки далеко от объекта: такой объект
        /// перехватывал клики и подсвечивался рамкой «не там». Крошечные контуры убираем (только в игре, сцена не меняется).
        /// </summary>
        static void RemoveSpecks(PolygonCollider2D polygon)
        {
            var paths = new List<Vector2[]>();
            float largest = 0f;
            for (int i = 0; i < polygon.pathCount; i++)
            {
                Vector2[] path = polygon.GetPath(i);
                paths.Add(path);
                largest = Mathf.Max(largest, Hotspot.PathArea(path));
            }
            List<Vector2[]> kept = paths.FindAll(path => Hotspot.PathArea(path) >= largest * SpeckShare);
            if (kept.Count == paths.Count) return;
            polygon.pathCount = kept.Count;
            for (int i = 0; i < kept.Count; i++) polygon.SetPath(i, kept[i]);
        }

        public void BuildHotspots(List<Hotspot> output, Transform parent)
        {
            foreach (var pair in objects)
            {
                string id = IdFor(pair.Key);
                HotspotDef template = null;
                foreach (var view in RoomContent.Views)
                    foreach (var def in view.Hotspots)
                        if (def.Id == id) template = def;
                var defn = HotspotDef.Box(id, pair.Key, 0, 0, 1, 1,
                    pair.Key == "Кровать" ? 0 : 2,
                    pair.Key == "Книга" ? "Книга на тумбе. Между страницами что-то лежит." : template != null ? template.Description : "Знакомый предмет в твоей комнате.");
                output.Add(Hotspot.CreateFromCollider(defn, pair.Value, pair.Value.GetComponent<SpriteRenderer>(), parent));
            }
        }

        static string IdFor(string name)
        {
            switch (name)
            {
                case "Окно": return RoomContent.WindowGlass;
                case "Дверь": return RoomContent.Door;
                case "Кровать": return RoomContent.Bed;
                case "Шкаф": return RoomContent.Wardrobe;
                case "Стол у окна": return RoomContent.WindowTable;
                case "Компьютерный стол": return RoomContent.Desk;
                case "Компьютер": return RoomContent.Laptop;
                case "Тумба": return RoomContent.Nightstand;
                case "Книга": return RoomContent.Box;
                case "Торшер": case "Люстра": return RoomContent.LightSwitch;
                case "Зеркало": return RoomContent.Mirror;
                case "Холодильник": return RoomContent.Fridge;
                case "Кресло": return RoomContent.Chair;
                default: return "room_" + name;
            }
        }

        Rect BoundsOf(string name)
        {
            if (!objects.TryGetValue(name, out var collider)) return new Rect(-1, -1, 2, 2);
            Bounds b = collider.bounds;
            return new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
        }

        /// <summary>Свет комнаты; brightness &lt; 1 — мигание лампы.</summary>
        public void SetLight(bool lightOn, bool flashlight, float brightness = 1f)
        {
            Color tint = lightOn ? Color.white * brightness : new Color(0.62f, 0.72f, 1f) * (flashlight ? 0.65f : 0.3f);
            tint.a = 1f;
            foreach (var pair in colors)
                if (pair.Key != null) pair.Key.color = pair.Value * tint;
        }

        // При выгрузке сцены комната может исчезнуть раньше RoomView — тогда трогать её уже нельзя.
        public void SetVisible(bool visible)
        {
            if (root != null) root.gameObject.SetActive(visible);
        }

        public void RestoreColors()
        {
            foreach (var pair in colors)
                if (pair.Key != null) pair.Key.color = pair.Value;
        }

        public StalkerSpot[] StalkerSpots(StalkerNode node)
        {
            Rect r;
            float alpha = 1f;
            switch (node)
            {
                case StalkerNode.Street: r = WindowRect; alpha = 0.6f; break;
                case StalkerNode.Window: r = WindowRect; break;
                case StalkerNode.Door: r = BoundsOf("Дверь"); alpha = 0.35f; break;
                case StalkerNode.Room: r = new Rect(-6, -5, 12, 7); break;
                default: return null;
            }
            float size = Mathf.Min(r.width, r.height) * (node == StalkerNode.Street ? 0.15f : 0.35f);
            return new[] { Spot(r.center + Vector2.left * r.width * 0.15f, size, alpha),
                Spot(r.center + Vector2.right * r.width * 0.15f, size, alpha) };
        }

        static StalkerSpot Spot(Vector2 center, float size, float alpha) => new StalkerSpot(
            new PxRect((center.x - size / 2 + 15) * 100, (10 - center.y - size / 2) * 100, size * 100, size * 100), alpha);
    }
}
