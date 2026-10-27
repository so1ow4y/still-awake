using System.Collections.Generic;
using UnityEngine;

namespace StillAwake
{
    /// <summary>Пути к арту внутри Assets/Art/Resources (без расширения).</summary>
    public static class ArtPaths
    {
        /// <summary>Корпус телефона 600×1200, без полей: экран — прямоугольник 24,24 – 576,1176.</summary>
        public const string Phone = "Phone/phone_100";
        public const string Stalker = "Stalker/stalker";
        /// <summary>Фон главного меню (1920×1080). Пока его нет — тёмный экран.</summary>
        public const string MenuBackground = "UI/menu_background";

        public static string Item(string itemId) => "Items/" + itemId;
        public static string CloseUp(string hotspotId) => "CloseUps/" + hotspotId;
    }

    /// <summary>
    /// Загрузка арта из Assets/Art/Resources. Художник заменяет PNG с тем же именем — код менять не нужно.
    /// Если файла нет, Load возвращает null, и вызывающий код рисует простую заглушку.
    /// </summary>
    public static class ArtLibrary
    {
        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        static Sprite white, circle;

        /// <summary>Белый квадрат 1×1 юнит — для заглушек и подсветки.</summary>
        public static Sprite White => white != null ? white : (white = CreateSolid());

        /// <summary>Белый круг диаметром 1 юнит.</summary>
        public static Sprite Circle => circle != null ? circle : (circle = CreateCircle());

        public static Sprite Load(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (Cache.TryGetValue(path, out Sprite cached)) return cached;

            Sprite sprite = Resources.Load<Sprite>(path);
            if (sprite == null)
            {
                // Картинка импортирована не как Sprite — собираем спрайт сами.
                var texture = Resources.Load<Texture2D>(path);
                if (texture != null)
                    sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f), FrameSpace.PixelsPerUnit);
            }

            Cache[path] = sprite;
            return sprite;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Cache.Clear();
            white = null;
            circle = null;
        }

        static Sprite CreateSolid()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "White", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        }

        static Sprite CreateCircle()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Circle", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float r = size / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                byte a = (byte)(Mathf.Clamp01(r - d) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
