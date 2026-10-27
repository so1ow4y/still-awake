using UnityEngine;

namespace StillAwake
{
    /// <summary>
    /// Сталкер на кадре. Пока это красный квадрат (или Art/Resources/Stalker/stalker.png, если он есть),
    /// который перескакивает между кадрами вслед за Stalker.Node и ходит между двумя точками внутри кадра.
    /// </summary>
    public sealed class StalkerView : MonoBehaviour
    {
        const float WanderSeconds = 3.5f;
        static readonly Color PlaceholderColor = new Color(0.75f, 0.05f, 0.05f);

        Stalker stalker;
        RoomView view;
        RoomState room;
        SpriteRenderer spriteRenderer;
        Color baseColor;
        /// <summary>Сдвиг по маршруту и «приближение» после мигания света.</summary>
        float wanderOffset, scale = 1f, targetScale = 1f;

        /// <summary>Сталкер виден в кадре и точка попадает в его силуэт.</summary>
        public bool Contains(Vector2 worldPoint) =>
            spriteRenderer != null && spriteRenderer.enabled && spriteRenderer.bounds.Contains(new Vector3(worldPoint.x, worldPoint.y, spriteRenderer.bounds.center.z));

        public void Init(Stalker logic, RoomView roomView, RoomState roomState)
        {
            stalker = logic;
            view = roomView;
            room = roomState;

            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            spriteRenderer.sortingOrder = 10;
            Sprite art = ArtLibrary.Load(ArtPaths.Stalker);
            spriteRenderer.sprite = art != null ? art : ArtLibrary.White;
            baseColor = art != null ? Color.white : PlaceholderColor;
            spriteRenderer.enabled = false;
        }

        /// <summary>Мигание света: в темноте силуэт рывком переносится в другую точку маршрута и становится больше.</summary>
        public void Lurch()
        {
            wanderOffset = Random.Range(0f, WanderSeconds * 2f);
            scale = Random.Range(1.1f, 1.3f);
            targetScale = scale;
        }

        /// <summary>Свет снова ровный: силуэт медленно «отступает» к обычному размеру.</summary>
        public void Settle() => targetScale = 1f;

        void LateUpdate()
        {
            scale = Mathf.MoveTowards(scale, targetScale, Time.deltaTime * 0.05f);
            StalkerSpot[] spots = view.GetStalkerSpots(stalker.Node);
            bool outside = stalker.Node == StalkerNode.Street || stalker.Node == StalkerNode.Window;
            bool behindCurtains = outside && room.CurtainsClosed && view.Current == ViewId.Window;

            spriteRenderer.enabled = spots != null && spots.Length > 0 && !behindCurtains;
            if (!spriteRenderer.enabled) return;

            StalkerSpot from = spots[0];
            StalkerSpot to = spots[spots.Length - 1];
            float t = Mathf.SmoothStep(0f, 1f, Mathf.PingPong((Time.time + wanderOffset) / WanderSeconds, 1f));

            Rect a = FrameSpace.ToWorld(from.Area);
            Rect b = FrameSpace.ToWorld(to.Area);
            Vector2 size = Vector2.Lerp(a.size, b.size, t) * scale;
            Vector2 spriteSize = spriteRenderer.sprite.bounds.size;

            transform.localPosition = Vector2.Lerp(a.center, b.center, t);
            transform.localScale = new Vector3(size.x / spriteSize.x, size.y / spriteSize.y, 1f);

            Color color = baseColor;
            color.a *= Mathf.Lerp(from.Alpha, to.Alpha, t);
            spriteRenderer.color = color;
        }
    }
}
