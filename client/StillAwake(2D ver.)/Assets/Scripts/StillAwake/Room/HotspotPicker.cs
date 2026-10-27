using System;
using UnityEngine;

namespace StillAwake
{
    /// <summary>
    /// Выбирает область под курсором. Если области накладываются, побеждает та, на которой реально
    /// стоит курсор и которая «точнее»: сначала явный приоритет, затем меньшая площадь
    /// (кружка на столе важнее стола), затем близость центра к курсору.
    /// Силуэт сталкера в окне или в двери считается частью окна или двери — по нему тоже можно кликнуть.
    /// </summary>
    public sealed class HotspotPicker : MonoBehaviour
    {
        /// <summary>Выключается на паузе, во время перехода между кадрами и когда героиня в укрытии.</summary>
        public bool InputEnabled = true;

        public Hotspot Hovered { get; private set; }

        /// <summary>Клик по области: сама область и экранная точка клика (для меню у курсора).</summary>
        public event Action<Hotspot, Vector2> Clicked;

        /// <summary>Попал ли курсор в силуэт сталкера; если да — id области, к которой он относится.</summary>
        public Func<Vector2, string> StalkerAt;

        RoomView view;
        Camera cam;
        Hud hud;

        public void Init(RoomView roomView, Camera worldCamera, Hud headsUp)
        {
            view = roomView;
            cam = worldCamera;
            hud = headsUp;
            view.ViewChanged += _ => SetHovered(null);
        }

        public Vector2 ScreenToWorld(Vector2 screen) => cam.ScreenToWorldPoint(screen);

        void Update()
        {
            Hotspot next = null;
            if (InputEnabled && !GameInput.PointerOverUi)
                next = Pick(ScreenToWorld(GameInput.PointerPosition));

            SetHovered(next);
            hud.SetTooltip(next != null ? next.Def.Title : null);

            if (next != null && GameInput.ClickPressed) Clicked?.Invoke(next, GameInput.PointerPosition);
        }

        public Hotspot PickScreen(Vector2 screen) => Pick(ScreenToWorld(screen));

        public Hotspot Pick(Vector2 worldPoint)
        {
            string stalkerTarget = StalkerAt?.Invoke(worldPoint);
            if (stalkerTarget != null)
                foreach (Hotspot candidate in view.Hotspots)
                    if (candidate.Def.Id == stalkerTarget) return candidate;

            Hotspot best = null;
            foreach (Hotspot candidate in view.Hotspots)
            {
                if (!candidate.Contains(worldPoint)) continue;
                if (best == null || IsBetter(candidate, best, worldPoint)) best = candidate;
            }
            return best;
        }

        static bool IsBetter(Hotspot a, Hotspot b, Vector2 point)
        {
            if (a.Def.Priority != b.Def.Priority) return a.Def.Priority > b.Def.Priority;
            if (!Mathf.Approximately(a.Area, b.Area)) return a.Area < b.Area;
            return (a.Center - point).sqrMagnitude < (b.Center - point).sqrMagnitude;
        }

        void SetHovered(Hotspot hotspot)
        {
            if (Hovered == hotspot) return;
            // Объект мог быть уничтожен при смене кадра — проверка Unity-null это учитывает.
            if (Hovered != null) Hovered.SetHovered(false);
            Hovered = hotspot;
            if (Hovered != null) Hovered.SetHovered(true);
        }
    }
}
