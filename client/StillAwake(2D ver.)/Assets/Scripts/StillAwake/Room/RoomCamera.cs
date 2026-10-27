using System.Collections;
using UnityEngine;

namespace StillAwake
{
    /// <summary>
    /// Осмотр в рамках одного кадра. Кадр 3000×2000, экран по умолчанию показывает его участок
    /// 1920×1080. Взгляд следует за курсором: курсор у края экрана — камера у края кадра, поэтому
    /// точка под курсором всегда соответствует одной и той же точке кадра. Колесо — приближение.
    /// Взгляд можно закрепить (M3) — удобно тащить предметы из инвентаря; переходы между кадрами анимируются.
    /// </summary>
    public sealed class RoomCamera : MonoBehaviour
    {
        /// <summary>1080 px кадра по высоте экрана.</summary>
        public const float DefaultSize = 5.4f;

        /// <summary>Максимальное приближение — ×2 к обычному виду.</summary>
        public const float MinSize = 2.7f;

        const float ZoomStep = 0.85f;
        const float ZoomSmoothTime = 0.12f;
        const float LookSmoothTime = 0.15f;
        /// <summary>После смены кадра камера мягко «доходит» до обычного вида.</summary>
        const float ArriveSeconds = 0.7f;
        const float ArriveSmoothTime = 0.35f;

        /// <summary>Выключается на паузе и в концовке: камера замирает.</summary>
        public bool LookEnabled = true;

        /// <summary>В укрытии взгляд зафиксирован по центру кадра.</summary>
        public bool LockCentered;

        /// <summary>Игрок закрепил взгляд (M3): курсор больше не двигает камеру.</summary>
        public bool Locked;

        /// <summary>Временная остановка взгляда: курсор над телефоном или инвентарём, тащат предмет.</summary>
        public bool Hold;

        float InitialSize => DefaultSize;
        bool waitingForLook;
        bool animating;
        float arriveTimer;
        Vector2 initialPointer;
        Camera cam;
        float targetSize = DefaultSize;
        float sizeVelocity;
        Vector2 look;
        Vector3 velocity;

        void Awake() => cam = GetComponent<Camera>();

        /// <summary>Сбросить приближение и взгляд (при смене кадра).</summary>
        public void ResetView(bool instant, bool fromSofa = false)
        {
            animating = false;
            targetSize = InitialSize;
            look = fromSofa ? new Vector2(0f, -0.8f) : Vector2.zero;
            waitingForLook = fromSofa;
            initialPointer = GameInput.PointerPosition;
            if (!instant) return;
            cam.orthographicSize = Mathf.Min(InitialSize, MaxSize);
            float verticalRange = Mathf.Max(0f, FrameSpace.WorldSize.y / 2f - cam.orthographicSize);
            transform.position = new Vector3(0f, look.y * verticalRange, transform.position.z);
            velocity = Vector3.zero;
            sizeVelocity = 0f;
        }

        /// <summary>Шаг к объекту: камера наезжает на точку и приближает её, пока экран темнеет.</summary>
        public IEnumerator Approach(Vector2 worldPoint, float seconds, float zoom = 0.62f)
        {
            animating = true;
            Vector3 fromPosition = transform.position;
            float fromSize = cam.orthographicSize;
            var toPosition = new Vector3(worldPoint.x, worldPoint.y, fromPosition.z);
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / seconds)
            {
                float k = Mathf.SmoothStep(0f, 1f, t);
                transform.position = Vector3.Lerp(fromPosition, toPosition, k * 0.75f);
                cam.orthographicSize = Mathf.Lerp(fromSize, fromSize * zoom, k);
                yield return null;
            }
        }

        /// <summary>Поворот головы: взгляд уходит в сторону поворота.</summary>
        public IEnumerator Pan(float direction, float seconds)
        {
            animating = true;
            Vector3 from = transform.position;
            Vector3 to = from + new Vector3(direction * 4.5f, 0f, 0f);
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / seconds)
            {
                transform.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
        }

        /// <summary>
        /// После смены кадра: начать немного сбоку и/или ближе и мягко прийти к обычному виду —
        /// так переход читается как шаг или поворот головы, а не как склейка.
        /// </summary>
        public void Arrive(Vector2 offset, float sizeFactor)
        {
            animating = false;
            transform.position += (Vector3)offset;
            cam.orthographicSize = InitialSize * sizeFactor;
            velocity = Vector3.zero;
            sizeVelocity = 0f;
            arriveTimer = ArriveSeconds;
        }

        float MaxSize => Mathf.Min(FrameSpace.WorldSize.y / 2f, FrameSpace.WorldSize.x / (2f * cam.aspect));

        void LateUpdate()
        {
            if (animating) return;
            float maxSize = MaxSize;

            if (LockCentered)
            {
                targetSize = InitialSize;
                look = Vector2.zero;
            }
            else if (LookEnabled && !Locked && !Hold)
            {
                int steps = GameInput.ScrollSteps;
                if (steps != 0) targetSize *= steps > 0 ? ZoomStep : 1f / ZoomStep;
                if (GameInput.ResetZoomPressed) targetSize = InitialSize;
                if (waitingForLook && (GameInput.PointerPosition - initialPointer).sqrMagnitude > 16f) waitingForLook = false;
                if (!waitingForLook) look = GameInput.PointerNormalized;
            }

            arriveTimer -= Time.unscaledDeltaTime;
            float lookSmooth = arriveTimer > 0f ? ArriveSmoothTime : LookSmoothTime;
            float zoomSmooth = arriveTimer > 0f ? ArriveSmoothTime : ZoomSmoothTime;

            targetSize = Mathf.Clamp(targetSize, MinSize, maxSize);
            cam.orthographicSize = Mathf.SmoothDamp(cam.orthographicSize, targetSize, ref sizeVelocity, zoomSmooth, Mathf.Infinity, Time.unscaledDeltaTime);

            Vector2 range = FrameSpace.WorldSize / 2f - new Vector2(cam.orthographicSize * cam.aspect, cam.orthographicSize);
            range = Vector2.Max(range, Vector2.zero);

            var target = new Vector3(look.x * range.x, look.y * range.y, transform.position.z);
            Vector3 position = Vector3.SmoothDamp(transform.position, target, ref velocity, lookSmooth, Mathf.Infinity, Time.unscaledDeltaTime);
            if (arriveTimer <= 0f)
            {
                position.x = Mathf.Clamp(position.x, -range.x, range.x);
                position.y = Mathf.Clamp(position.y, -range.y, range.y);
            }
            transform.position = position;
        }
    }
}
