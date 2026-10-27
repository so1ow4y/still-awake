using System.Collections.Generic;
using UnityEngine;

namespace StillAwake
{
    /// <summary>
    /// Процедурные звуки-заглушки, пока нет настоящих: шаги, поворот, щелчки, мигание лампы, телефон,
    /// стук в дверь и тихий гул комнаты. Настоящий звук — заменить клип в нужном методе.
    /// </summary>
    public static class Sfx
    {
        const int SampleRate = 44100;

        static AudioSource source, ambient, ringback, heart, breath;
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();

        public static void Init(AudioSource audioSource)
        {
            source = audioSource;
            ambient = audioSource.gameObject.AddComponent<AudioSource>();
            ambient.loop = true;
            ambient.volume = 0.35f;
            ringback = audioSource.gameObject.AddComponent<AudioSource>();
            ringback.loop = true;
            ringback.volume = 0.5f;
            heart = audioSource.gameObject.AddComponent<AudioSource>();
            heart.loop = true;
            heart.volume = 0f;
            breath = audioSource.gameObject.AddComponent<AudioSource>();
            breath.loop = true;
            breath.volume = 0f;
        }

        /// <summary>
        /// Сердце героини: tension 0–1 — громкость и частота (≈55 → 130 ударов в минуту). holdingBreath —
        /// он в комнате, а она в укрытии: слышно сдавленное дыхание.
        /// </summary>
        public static void SetHeartbeat(float tension, bool holdingBreath)
        {
            if (heart == null) return;
            if (heart.clip == null)
            {
                heart.clip = Get("heartbeat", Heartbeat);
                breath.clip = Get("breath", Breathing);
            }
            float volume = Mathf.Clamp01((tension - 0.08f) * 1.4f) * 0.9f;
            heart.volume = volume;
            heart.pitch = Mathf.Lerp(0.92f, 2.2f, tension);
            if (volume > 0f && !heart.isPlaying) heart.Play();
            else if (volume <= 0f && heart.isPlaying) heart.Stop();

            float breathVolume = holdingBreath ? 0.5f : 0f;
            breath.volume = Mathf.MoveTowards(breath.volume, breathVolume, Time.deltaTime * 0.5f);
            if (breath.volume > 0f && !breath.isPlaying) breath.Play();
            else if (breath.volume <= 0f && breath.isPlaying) breath.Stop();
        }

        public static void Notification() => Play(Get("notification", () => Tones(new[] { 880f, 1320f }, 0.09f, 0.35f)));

        /// <summary>Телефон жужжит на вибрации: два коротких импульса.</summary>
        public static void Vibrate() => Play(Get("vibrate", Vibration), 0.8f);

        public static void Knock() => Play(Get("knock", () => Thumps(3, 0.28f, 70f, 0.8f)));

        /// <summary>Несколько шагов — героиня подходит к объекту.</summary>
        public static void Footsteps() => Play(Get("steps", () => Thumps(3, 0.26f, 55f, 0.35f, noise: 0.6f)), 0.9f);

        /// <summary>Шорох одежды при повороте.</summary>
        public static void Turn() => Play(Get("turn", () => Noise(0.28f, 0.18f, lowpass: 0.08f)));

        public static void Click() => Play(Get("click", () => Tones(new[] { 2100f }, 0.012f, 0.25f)), 0.6f);

        public static void Switch() => Play(Get("switch", () => Thumps(1, 0.05f, 900f, 0.4f, noise: 0.8f)));

        public static void Pickup() => Play(Get("pickup", () => Noise(0.18f, 0.25f, lowpass: 0.25f)));

        /// <summary>Треск и гудение лампы, когда свет мигает.</summary>
        public static void Buzz() => Play(Get("buzz", Buzzing), 0.7f);

        /// <summary>Тональный сигнал кнопки номеронабирателя.</summary>
        public static void Dtmf(string digit)
        {
            string keys = "123456789*0#";
            int index = keys.IndexOf(digit);
            if (index < 0) return;
            float[] rows = { 697f, 770f, 852f, 941f };
            float[] cols = { 1209f, 1336f, 1477f };
            Play(Get("dtmf" + digit, () => Dual(rows[index / 3], cols[index % 3], 0.12f, 0.25f)));
        }

        /// <summary>Длинные гудки, пока никто не берёт трубку.</summary>
        public static void StartRingback()
        {
            if (ringback == null) return;
            ringback.clip = Get("ringback", () => Ringback());
            ringback.Play();
        }

        public static void StopRingback()
        {
            if (ringback != null) ringback.Stop();
        }

        /// <summary>Тихий гул комнаты и холодильника — тишина ночи не бывает абсолютной.</summary>
        public static void StartAmbient()
        {
            if (ambient == null) return;
            ambient.clip = Get("ambient", RoomTone);
            ambient.Play();
        }

        static void Play(AudioClip clip, float volume = 1f)
        {
            if (source != null) source.PlayOneShot(clip, volume);
        }

        static AudioClip Get(string name, System.Func<float[]> build)
        {
            if (clips.TryGetValue(name, out AudioClip clip) && clip != null) return clip;
            float[] data = build();
            clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            clips[name] = clip;
            return clip;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            source = ambient = ringback = heart = breath = null;
            clips.Clear();
        }

        static float[] Tones(float[] frequencies, float noteSeconds, float volume)
        {
            int perNote = (int)(SampleRate * noteSeconds);
            var data = new float[perNote * frequencies.Length];
            for (int n = 0; n < frequencies.Length; n++)
            for (int i = 0; i < perNote; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = Mathf.Sin(Mathf.PI * i / perNote);
                data[n * perNote + i] = Mathf.Sin(2f * Mathf.PI * frequencies[n] * t) * envelope * volume;
            }
            return data;
        }

        static float[] Dual(float a, float b, float seconds, float volume)
        {
            int length = (int)(SampleRate * seconds);
            var data = new float[length];
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = Mathf.Min(1f, Mathf.Min(i, length - i) / 300f);
                data[i] = (Mathf.Sin(2f * Mathf.PI * a * t) + Mathf.Sin(2f * Mathf.PI * b * t)) * 0.5f * envelope * volume;
            }
            return data;
        }

        static float[] Thumps(int count, float gapSeconds, float frequency, float volume, float noise = 0.3f)
        {
            int gap = (int)(SampleRate * gapSeconds);
            int length = (int)(SampleRate * 0.12f);
            var data = new float[gap * (count - 1) + length];
            var random = new System.Random(7);
            for (int k = 0; k < count; k++)
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = Mathf.Exp(-t * 35f);
                float n = (float)(random.NextDouble() * 2.0 - 1.0) * noise;
                data[k * gap + i] += (Mathf.Sin(2f * Mathf.PI * frequency * t) + n) * envelope * volume;
            }
            return data;
        }

        static float[] Noise(float seconds, float volume, float lowpass)
        {
            int length = (int)(SampleRate * seconds);
            var data = new float[length];
            var random = new System.Random(11);
            float smooth = 0f;
            for (int i = 0; i < length; i++)
            {
                float white = (float)(random.NextDouble() * 2.0 - 1.0);
                smooth += (white - smooth) * lowpass;
                float envelope = Mathf.Sin(Mathf.PI * i / length);
                data[i] = smooth * envelope * volume * 4f;
            }
            return data;
        }

        static float[] Buzzing()
        {
            int length = (int)(SampleRate * 0.45f);
            var data = new float[length];
            var random = new System.Random(3);
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;
                float saw = 2f * (t * 100f - Mathf.Floor(0.5f + t * 100f));
                float crackle = random.NextDouble() < 0.004 ? (float)(random.NextDouble() * 2.0 - 1.0) : 0f;
                float envelope = Mathf.Sin(Mathf.PI * i / length);
                data[i] = (saw * 0.12f + crackle * 0.6f) * envelope;
            }
            return data;
        }

        static float[] Ringback()
        {
            // 425 Гц: секунда гудка, три секунды тишины.
            int length = SampleRate * 4;
            var data = new float[length];
            int tone = SampleRate;
            for (int i = 0; i < tone; i++)
            {
                float envelope = Mathf.Min(1f, Mathf.Min(i, tone - i) / 400f);
                data[i] = Mathf.Sin(2f * Mathf.PI * 425f * i / SampleRate) * 0.25f * envelope;
            }
            return data;
        }

        static float[] Vibration()
        {
            // 0.3 с жужжания, 0.15 с паузы, ещё 0.3 с: низкий гул с дребезгом о поверхность.
            int pulse = (int)(SampleRate * 0.3f), gap = (int)(SampleRate * 0.15f);
            var data = new float[pulse * 2 + gap];
            for (int k = 0; k < 2; k++)
            for (int i = 0; i < pulse; i++)
            {
                float t = i / (float)SampleRate;
                float envelope = Mathf.Min(1f, Mathf.Min(i, pulse - i) / 300f);
                float rattle = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 170f * t)) * 0.25f + Mathf.Sin(2f * Mathf.PI * 170f * t) * 0.75f;
                data[k * (pulse + gap) + i] = rattle * envelope * 0.35f;
            }
            return data;
        }

        /// <summary>Петля ≈1.1 с: «тук-тук» и пауза — частоту задаёт pitch.</summary>
        static float[] Heartbeat()
        {
            int length = (int)(SampleRate * 1.1f);
            var data = new float[length];
            foreach ((float at, float strength) in new[] { (0f, 1f), (0.24f, 0.7f) })
            {
                int start = (int)(at * SampleRate);
                int beat = (int)(SampleRate * 0.16f);
                for (int i = 0; i < beat && start + i < length; i++)
                {
                    float t = i / (float)SampleRate;
                    float envelope = Mathf.Exp(-t * 28f) * Mathf.Min(1f, i / 60f);
                    float freq = Mathf.Lerp(62f, 40f, t / 0.16f);
                    data[start + i] += Mathf.Sin(2f * Mathf.PI * freq * t) * envelope * strength * 0.9f;
                }
            }
            return data;
        }

        /// <summary>Сдавленное дыхание: медленный шумовой вдох-выдох, петля 3.4 с.</summary>
        static float[] Breathing()
        {
            int length = (int)(SampleRate * 3.4f);
            var data = new float[length];
            var random = new System.Random(19);
            float smooth = 0f;
            for (int i = 0; i < length; i++)
            {
                float phase = i / (float)length;
                // вдох 0–0.35, пауза, выдох 0.5–0.9
                float envelope = phase < 0.35f ? Mathf.Sin(Mathf.PI * phase / 0.35f)
                    : phase > 0.5f && phase < 0.9f ? Mathf.Sin(Mathf.PI * (phase - 0.5f) / 0.4f) * 0.7f : 0f;
                smooth += ((float)(random.NextDouble() * 2.0 - 1.0) - smooth) * 0.12f;
                data[i] = smooth * envelope * 0.6f;
            }
            return data;
        }

        static float[] RoomTone()
        {
            // Петля 4 с: тёмный шум и гул сети 50 Гц; концы сходятся, чтобы петля не щёлкала.
            int length = SampleRate * 4;
            var data = new float[length];
            var random = new System.Random(5);
            float brown = 0f;
            for (int i = 0; i < length; i++)
            {
                brown = Mathf.Clamp(brown + (float)(random.NextDouble() * 2.0 - 1.0) * 0.02f, -1f, 1f) * 0.998f;
                float hum = Mathf.Sin(2f * Mathf.PI * 50f * i / SampleRate) * 0.02f + Mathf.Sin(2f * Mathf.PI * 100f * i / SampleRate) * 0.01f;
                float fade = Mathf.Min(1f, Mathf.Min(i, length - i) / (float)(SampleRate / 10));
                data[i] = (brown * 0.25f + hum) * fade;
            }
            return data;
        }
    }
}
