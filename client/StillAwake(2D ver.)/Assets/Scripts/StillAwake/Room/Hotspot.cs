using UnityEngine;

namespace StillAwake
{
    /// <summary>
    /// Область взаимодействия в кадре. В кадрах-заглушках — прямоугольник или круг (попадание считается
    /// геометрически), в нарисованной комнате — полигональный коллайдер объекта. Подсветка у нарисованных
    /// объектов — их собственный силуэт (копия спрайта поверх), поэтому совпадает с рисунком до пикселя.
    /// </summary>
    public sealed class Hotspot : MonoBehaviour
    {
        static readonly Color HoverColor = new Color(1f, 0.92f, 0.6f, 0.36f);
        static readonly Color TargetColor = new Color(0.55f, 0.85f, 1f, 0.35f);
        static readonly Color DebugColor = new Color(0.2f, 1f, 0.4f, 0.22f);
        static readonly Color DebugTurnColor = new Color(0.2f, 0.5f, 1f, 0.22f);

        public HotspotDef Def { get; private set; }
        public Rect WorldRect { get; private set; }
        public Vector2 Center => WorldRect.center;

        /// <summary>Площадь для выбора «более точного» объекта: у коллайдера — сумма площадей контуров.</summary>
        public float Area => polygonArea > 0f ? polygonArea
            : Def.Shape == HotspotShape.Circle ? Mathf.PI * Radius * Radius
            : WorldRect.width * WorldRect.height;

        float Radius => WorldRect.width / 2f;

        Collider2D sourceCollider;
        SpriteRenderer overlay;
        SpriteMask mask;
        float polygonArea;
        bool hovered, targeted, debugVisible;

        public static Hotspot Create(HotspotDef def, Transform parent)
        {
            Hotspot hotspot = CreateBase(def, parent, FrameSpace.ToWorld(def.Area));
            var box = new GameObject("Overlay");
            box.transform.SetParent(hotspot.transform, false);
            box.transform.localScale = new Vector3(hotspot.WorldRect.width, hotspot.WorldRect.height, 1f);
            hotspot.overlay = box.AddComponent<SpriteRenderer>();
            hotspot.overlay.sprite = def.Shape == HotspotShape.Circle ? ArtLibrary.Circle : ArtLibrary.White;
            hotspot.overlay.sortingOrder = 20;
            hotspot.Refresh();
            return hotspot;
        }

        /// <summary>Объект нарисованной комнаты: коллайдер — для попадания, его спрайт — для подсветки.</summary>
        public static Hotspot CreateFromCollider(HotspotDef def, Collider2D collider, SpriteRenderer art, Transform parent)
        {
            Bounds b = collider.bounds;
            def.Area = new PxRect((b.min.x + FrameSpace.WorldSize.x / 2f) * FrameSpace.PixelsPerUnit,
                (FrameSpace.WorldSize.y / 2f - b.max.y) * FrameSpace.PixelsPerUnit,
                b.size.x * FrameSpace.PixelsPerUnit, b.size.y * FrameSpace.PixelsPerUnit);
            Hotspot hotspot = CreateBase(def, parent, new Rect(b.min, b.size));
            hotspot.sourceCollider = collider;
            hotspot.polygonArea = collider is PolygonCollider2D polygon ? PolygonArea(polygon) : b.size.x * b.size.y;

            var box = new GameObject("Overlay");
            box.transform.SetParent(hotspot.transform, false);
            box.transform.localScale = new Vector3(b.size.x, b.size.y, 1f);
            hotspot.overlay = box.AddComponent<SpriteRenderer>();
            hotspot.overlay.sprite = ArtLibrary.White;
            hotspot.overlay.sortingOrder = 20;

            if (art != null && art.sprite != null)
            {
                // Заливка видна только внутри маски по форме спрайта объекта. У каждой области свой диапазон
                // сортировки, чтобы маски соседних объектов не смешивались (в отладке F1 видны все сразу).
                int order = NextMaskOrder();
                var silhouette = new GameObject("Silhouette");
                silhouette.transform.SetParent(hotspot.transform, false);
                silhouette.transform.SetPositionAndRotation(art.transform.position, art.transform.rotation);
                silhouette.transform.localScale = art.transform.lossyScale;
                hotspot.mask = silhouette.AddComponent<SpriteMask>();
                hotspot.mask.sprite = art.sprite;
                hotspot.mask.alphaCutoff = 0.15f;
                hotspot.mask.isCustomRangeActive = true;
                hotspot.mask.backSortingOrder = order - 1;
                hotspot.mask.frontSortingOrder = order;
                hotspot.overlay.sortingOrder = order;
                hotspot.overlay.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            }
            hotspot.Refresh();
            return hotspot;
        }

        static int maskCounter;

        static int NextMaskOrder() => 1000 + (maskCounter++ % 4000) * 2;

        static Hotspot CreateBase(HotspotDef def, Transform parent, Rect worldRect)
        {
            var go = new GameObject("Hotspot " + def.Id);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = worldRect.center;
            var hotspot = go.AddComponent<Hotspot>();
            hotspot.Def = def;
            hotspot.WorldRect = worldRect;
            return hotspot;
        }

        /// <summary>Сумма площадей контуров коллайдера в мировых единицах.</summary>
        public static float PolygonArea(PolygonCollider2D polygon)
        {
            Vector3 scale = polygon.transform.lossyScale;
            float total = 0f;
            for (int p = 0; p < polygon.pathCount; p++) total += PathArea(polygon.GetPath(p));
            return total * Mathf.Abs(scale.x * scale.y);
        }

        public static float PathArea(Vector2[] path)
        {
            float sum = 0f;
            for (int i = 0, j = path.Length - 1; i < path.Length; j = i++)
                sum += (path[j].x + path[i].x) * (path[j].y - path[i].y);
            return Mathf.Abs(sum) * 0.5f;
        }

        public bool Contains(Vector2 point)
        {
            if (sourceCollider != null) return sourceCollider.OverlapPoint(point);
            if (Def.Shape == HotspotShape.Circle) return (point - Center).sqrMagnitude <= Radius * Radius;
            return WorldRect.Contains(point);
        }

        public void SetHovered(bool value)
        {
            if (hovered == value) return;
            hovered = value;
            Refresh();
        }

        /// <summary>Цель для предмета, который тащат из инвентаря.</summary>
        public void SetTargeted(bool value)
        {
            if (targeted == value) return;
            targeted = value;
            Refresh();
        }

        public void SetDebugVisible(bool value)
        {
            debugVisible = value;
            Refresh();
        }

        void Refresh()
        {
            overlay.enabled = hovered || targeted || debugVisible;
            if (mask != null) mask.enabled = overlay.enabled;
            if (targeted) overlay.color = TargetColor;
            else if (hovered) overlay.color = HoverColor;
            else if (debugVisible) overlay.color = Def.GoTo.HasValue ? DebugTurnColor : DebugColor;
        }
    }
}
