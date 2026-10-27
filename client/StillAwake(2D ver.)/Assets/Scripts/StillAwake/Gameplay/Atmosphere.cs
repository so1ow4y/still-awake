using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StillAwake
{
    /// <summary>
    /// Напряжение ночи на экране и в ушах: чем ближе сталкер, тем темнее края кадра, сильнее зерно и
    /// расслоение цвета, тем бледнее картинка — и тем громче и чаще стучит сердце героини. Когда он в комнате,
    /// к этому добавляется дыхание и лёгкая дрожь камеры. Пост-обработка — свой Volume, собранный кодом.
    /// </summary>
    public sealed class Atmosphere : MonoBehaviour
    {
        const float SmoothSeconds = 1.2f;

        Stalker stalker;
        RoomState room;
        Vignette vignette;
        FilmGrain grain;
        ChromaticAberration aberration;
        ColorAdjustments color;
        Transform shakeTarget;
        float tension, velocity;
        Vector3 shake;

        /// <summary>Напряжение 0–1 — для звука и отладки.</summary>
        public float Tension => tension;

        public void Init(Camera cam, Stalker logic, RoomState state)
        {
            stalker = logic;
            room = state;
            shakeTarget = cam.transform;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            vignette = profile.Add<Vignette>(true);
            vignette.color.Override(Color.black);
            vignette.smoothness.Override(0.45f);
            grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium3);
            aberration = profile.Add<ChromaticAberration>(true);
            color = profile.Add<ColorAdjustments>(true);

            var volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;
            Apply(0f);
        }

        float Target()
        {
            if (!stalker.Active && !stalker.FinalAssault) return 0f;
            float t = stalker.Node switch
            {
                StalkerNode.Street => 0.15f,
                StalkerNode.Window => 0.35f,
                StalkerNode.Door => 0.65f,
                StalkerNode.Room => 1f,
                _ => 0.05f,
            };
            if (stalker.FinalAssault) t = Mathf.Max(t, 0.5f);
            return t;
        }

        void Update()
        {
            tension = Mathf.SmoothDamp(tension, Target(), ref velocity, SmoothSeconds);
            Apply(tension);
            Sfx.SetHeartbeat(tension, room.Hidden != HideSpot.None && stalker.Node == StalkerNode.Room);
        }

        void Apply(float t)
        {
            vignette.intensity.Override(Mathf.Lerp(0.22f, 0.58f, t));
            grain.intensity.Override(Mathf.Lerp(0.18f, 0.75f, t));
            aberration.intensity.Override(Mathf.Lerp(0f, 0.7f, t));
            color.saturation.Override(Mathf.Lerp(-10f, -55f, t));
            color.postExposure.Override(Mathf.Lerp(0f, -0.35f, t));
        }

        // Дрожь камеры поверх RoomCamera: снимаем прошлый сдвиг до её LateUpdate и добавляем новый после.
        void LateUpdate()
        {
            if (shakeTarget == null) return;
            shakeTarget.position -= shake;
            float amount = tension > 0.8f ? (tension - 0.8f) * 0.15f : 0f;
            shake = amount > 0f
                ? new Vector3((Mathf.PerlinNoise(Time.time * 9f, 0f) - 0.5f) * amount, (Mathf.PerlinNoise(0f, Time.time * 9f) - 0.5f) * amount, 0f)
                : Vector3.zero;
            shakeTarget.position += shake;
        }

        void OnDestroy() => Sfx.SetHeartbeat(0f, false);
    }
}
