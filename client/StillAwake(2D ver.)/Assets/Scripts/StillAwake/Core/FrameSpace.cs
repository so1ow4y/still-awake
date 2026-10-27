using System;
using UnityEngine;

namespace StillAwake
{
    /// <summary>
    /// Координаты кадра комнаты. Кадры рисуются в 3000×2000 px, разметка (области взаимодействия,
    /// точки сталкера) задаётся в пикселях этого кадра от левого верхнего угла — так её удобно
    /// сверять прямо в Krita/Photoshop.
    /// </summary>
    public static class FrameSpace
    {
        public const float WidthPx = 3000f;
        public const float HeightPx = 2000f;
        public const float PixelsPerUnit = 100f;

        public static readonly Vector2 WorldSize = new Vector2(WidthPx / PixelsPerUnit, HeightPx / PixelsPerUnit);

        public static Vector2 ToWorld(float px, float py) =>
            new Vector2(px / PixelsPerUnit - WorldSize.x / 2f, WorldSize.y / 2f - py / PixelsPerUnit);

        public static Rect ToWorld(PxRect r)
        {
            Vector2 topLeft = ToWorld(r.X, r.Y);
            Vector2 size = new Vector2(r.W, r.H) / PixelsPerUnit;
            return new Rect(topLeft.x, topLeft.y - size.y, size.x, size.y);
        }
    }

    /// <summary>Прямоугольник в пикселях картинки: X, Y — левый верхний угол.</summary>
    [Serializable]
    public struct PxRect
    {
        public float X, Y, W, H;

        public PxRect(float x, float y, float w, float h)
        {
            X = x;
            Y = y;
            W = w;
            H = h;
        }
    }
}
