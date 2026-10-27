using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace StillAwake
{
    [Serializable]
    public sealed class PlayerInfo
    {
        public string id;
        public string display_name;
        public string phone;
        public string installation_id;
        public string created_at;
    }

    /// <summary>
    /// Состояние ночи для сервера. Сервер берёт отсюда только известные значения и сам пишет по ним
    /// «ситуацию» для модели — текст из игры в системный промпт не попадает.
    /// </summary>
    [Serializable]
    public sealed class NightState
    {
        public string clock;
        public string phase;
        public string stalker;
        public bool light_on;
        public bool curtains_closed;
        public bool door_locked;
        public string hidden;
        public int visits;
        public bool phone_sound;
        public string[] items;
    }

    // Поля DTO названы как в JSON backend: сервер отвергает незнакомые поля. Ответы заполняет JsonUtility.
#pragma warning disable CS0649
    [Serializable]
    sealed class RegisterRequest
    {
        public string phone;
        public string display_name;
        public string installation_id;
        public string game_version;
        public string platform;
    }

    [Serializable]
    sealed class RegisterResponse
    {
        public string token;
        public bool created;
        public PlayerInfo user;
    }

    [Serializable]
    sealed class SessionRequest
    {
        public string game_version;
        public string scenario;
    }

    [Serializable]
    sealed class SessionResponse
    {
        public string session_id;
    }

    [Serializable]
    sealed class ChatRequest
    {
        public string message;
        public string @event;
        public NightState state;
        public int client_seq;
    }

    [Serializable]
    sealed class ChatResponse
    {
        public string reply;
    }

    [Serializable]
    sealed class ModelResponse
    {
        public bool online;
        public string mode;
        public string model;
        public bool loaded;
        public bool found;
        public string note;
        public string error;
    }

    [Serializable]
    sealed class ErrorResponse
    {
        public string error;
        public string code;
    }
#pragma warning restore CS0649

    /// <summary>
    /// Связь игры с backend: игра сама входит по ключу установки (игроку ничего вводить не нужно), открывает
    /// сессию на ночь, шлёт её события и переписку с моделью. Без сервера или с выключенным ИИ сообщения пишет
    /// скрипт. Один объект на всю игру (DontDestroyOnLoad), чтобы запросы не обрывались при смене сцены.
    /// </summary>
    public sealed class BackendClient : MonoBehaviour
    {
        public const string DefaultServer = "http://127.0.0.1:8080";
        const string ServerKey = "sa.server";
        const string TokenKey = "sa.token";
        const string PlayerKey = "sa.player";
        const string InstallationKey = "sa.installation";
        const string AiKey = "sa.ai";
        const int DefaultTimeoutSeconds = 10;

        /// <summary>Сообщения пишет нейросеть (настройка игрока). Выключено — заготовленные реплики, без запросов к модели.</summary>
        public static bool AiEnabled
        {
            get => PlayerPrefs.GetInt(AiKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(AiKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Ключ этой установки игры: по нему сервер узнаёт игрока. Создаётся при первом запуске.</summary>
        public static string InstallationId
        {
            get
            {
                string id = PlayerPrefs.GetString(InstallationKey, "");
                if (id.StartsWith("inst_") && id.Length >= 21) return id;
                id = "inst_" + Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(InstallationKey, id);
                PlayerPrefs.Save();
                return id;
            }
        }

        static BackendClient instance;

        public static BackendClient Instance
        {
            get
            {
                if (instance != null) return instance;
                var go = new GameObject("Backend client");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<BackendClient>();
                return instance;
            }
        }

        public static string ServerUrl
        {
            get => PlayerPrefs.GetString(ServerKey, DefaultServer);
            set
            {
                PlayerPrefs.SetString(ServerKey, NormalizeUrl(value));
                PlayerPrefs.Save();
            }
        }

        public static bool IsRegistered => !string.IsNullOrEmpty(PlayerPrefs.GetString(TokenKey, ""));

        public static PlayerInfo Player =>
            IsRegistered ? JsonUtility.FromJson<PlayerInfo>(PlayerPrefs.GetString(PlayerKey, "{}")) : null;

        /// <summary>Забыть вход (сервер сказал, что токен больше не действует) — в следующий раз игра войдёт заново.</summary>
        public static void Forget()
        {
            PlayerPrefs.DeleteKey(TokenKey);
            PlayerPrefs.DeleteKey(PlayerKey);
            PlayerPrefs.Save();
        }

        /// <summary>«127.0.0.1:8080/» → «http://127.0.0.1:8080».</summary>
        public static string NormalizeUrl(string value)
        {
            value = (value ?? "").Trim().TrimEnd('/');
            if (value.Length == 0) return DefaultServer;
            return value.Contains("://") ? value : "http://" + value;
        }

        public string SessionId { get; private set; }

        /// <summary>Ночь идёт с сервером: ответы пишет модель. Иначе — скриптовые реплики.</summary>
        public bool Online => sessionOpen;

        readonly List<string> pending = new List<string>();
        bool sessionOpen;
        int generation;
        int seq;

        public void CheckHealth(string server, Action<bool, string> done)
        {
            StartCoroutine(Send("GET", "/healthz", null, false, (ok, body, error) => done(ok, ok ? "Сервер на связи" : error),
                DefaultTimeoutSeconds, NormalizeUrl(server)));
        }

        /// <summary>Войти, если ещё не вошли: по ключу установки, без участия игрока. done(ok, ошибка).</summary>
        public void EnsureAccount(Action<bool, string> done) => StartCoroutine(EnsureAccountRoutine(done));

        IEnumerator EnsureAccountRoutine(Action<bool, string> done)
        {
            if (IsRegistered)
            {
                done(true, null);
                yield break;
            }
            var request = new RegisterRequest
            {
                installation_id = InstallationId,
                game_version = Application.version,
                platform = Application.platform.ToString(),
            };
            bool ok = false;
            string error = null;
            yield return Send("POST", "/v1/game/register", JsonUtility.ToJson(request), false, (success, body, message) =>
            {
                ok = success;
                error = message;
                if (!success) return;
                var response = JsonUtility.FromJson<RegisterResponse>(body);
                PlayerPrefs.SetString(TokenKey, response.token);
                PlayerPrefs.SetString(PlayerKey, JsonUtility.ToJson(response.user));
                PlayerPrefs.Save();
            });
            done(ok, error);
        }

        /// <summary>Проверить, что сервер видит модель: для кнопки в настройках. done(ok, текст для игрока).</summary>
        public void CheckModel(Action<bool, string> done) => StartCoroutine(CheckModelRoutine(done));

        IEnumerator CheckModelRoutine(Action<bool, string> done)
        {
            bool signedIn = false;
            string error = null;
            yield return EnsureAccountRoutine((ok, message) => { signedIn = ok; error = message; });
            if (!signedIn)
            {
                done(false, error);
                yield break;
            }
            yield return Send("GET", "/v1/game/model", null, true, (ok, body, message) =>
            {
                if (!ok)
                {
                    done(false, message);
                    return;
                }
                var model = JsonUtility.FromJson<ModelResponse>(body);
                if (!model.online) done(false, string.IsNullOrEmpty(model.error) ? "Сервер модели недоступен" : model.error);
                else if (model.mode == "mock") done(true, model.note);
                else if (string.IsNullOrEmpty(model.model)) done(false, "В LM Studio нет ни одной модели");
                else done(true, "Модель " + model.model + (model.loaded ? " загружена и готова" : " доступна — загрузится при первом сообщении"));
            });
        }

        /// <summary>Пробное сообщение от модели (отдельная сессия): показывает, сколько она реально думает.</summary>
        public void TestChat(Action<bool, string> done) => StartCoroutine(TestChatRoutine(done));

        IEnumerator TestChatRoutine(Action<bool, string> done)
        {
            bool signedIn = false;
            string error = null;
            yield return EnsureAccountRoutine((ok, message) => { signedIn = ok; error = message; });
            if (!signedIn)
            {
                done(false, error);
                yield break;
            }
            string session = null;
            var request = new SessionRequest { game_version = Application.version, scenario = "settings-test" };
            yield return Send("POST", "/v1/game/sessions", JsonUtility.ToJson(request), true, (ok, body, message) =>
            {
                if (ok) session = JsonUtility.FromJson<SessionResponse>(body).session_id;
                else error = message;
            });
            if (session == null)
            {
                done(false, error);
                yield break;
            }
            float started = Time.realtimeSinceStartup;
            var chat = new ChatRequest { message = "", @event = "first_contact", state = new NightState { clock = "00:30", phase = "strange", stalker = "street", light_on = true, hidden = "none" } };
            yield return Send("POST", "/v1/game/sessions/" + session + "/chat", JsonUtility.ToJson(chat), true, (ok, body, message) =>
            {
                float seconds = Time.realtimeSinceStartup - started;
                if (ok) done(true, "«" + JsonUtility.FromJson<ChatResponse>(body).reply + "» — за " + seconds.ToString("0") + " с");
                else done(false, message);
            }, 120);
        }

        /// <summary>Начать ночь на сервере: войти при необходимости и открыть сессию. События до ответа ждут в очереди.</summary>
        public void StartSession(string scenario = "night-1")
        {
            EndSession();
            sessionOpen = true;
            StartCoroutine(StartSessionRoutine(generation, scenario));
        }

        IEnumerator StartSessionRoutine(int current, string scenario)
        {
            bool signedIn = false;
            yield return EnsureAccountRoutine((ok, message) => signedIn = ok);
            if (current != generation) yield break;   // ночь уже закончилась или началась новая
            if (!signedIn)
            {
                EndSession();   // сервера нет — играем без него
                yield break;
            }
            var request = new SessionRequest { game_version = Application.version, scenario = scenario };
            yield return Send("POST", "/v1/game/sessions", JsonUtility.ToJson(request), true, (ok, body, error) =>
            {
                if (current != generation) return;
                if (!ok)
                {
                    Debug.LogWarning("Still Awake: сессия на сервере не создана — " + error);
                    EndSession();
                    return;
                }
                SessionId = JsonUtility.FromJson<SessionResponse>(body).session_id;
                foreach (string json in pending) Post(json);
                pending.Clear();
            });
        }

        /// <summary>Ночь закончилась: дальнейшие события и ответы этой ночи не нужны.</summary>
        public void EndSession()
        {
            generation++;
            sessionOpen = false;
            SessionId = null;
            pending.Clear();
            seq = 0;
        }

        /// <summary>Событие ночи: тип и данные (JSON-объект, см. Json).</summary>
        public void SendEvent(string type, string payloadJson = "{}")
        {
            if (!sessionOpen) return;
            string json = "{\"client_seq\":" + (++seq).ToString(CultureInfo.InvariantCulture) +
                          ",\"type\":" + Quote(type) + ",\"payload\":" + (string.IsNullOrEmpty(payloadJson) ? "{}" : payloadJson) + "}";
            if (SessionId == null) pending.Add(json);
            else Post(json);
        }

        void Post(string json)
        {
            StartCoroutine(Send("POST", "/v1/game/sessions/" + SessionId + "/events", json, true, (ok, _, error) =>
            {
                if (!ok) Debug.LogWarning("Still Awake: событие не отправлено — " + error);
            }));
        }

        /// <summary>
        /// Сообщение сталкера от модели: ответ на реплику игрока (message) или повод написать первым (eventId).
        /// done(null) — сервера нет, модель не успела за timeout или ночь уже закончилась: нужна скриптовая реплика.
        /// </summary>
        public void Chat(string message, string eventId, NightState state, float timeout, Action<string> done)
        {
            if (!sessionOpen)
            {
                done(null);
                return;
            }
            StartCoroutine(ChatRoutine(message, eventId, state, timeout, done));
        }

        IEnumerator ChatRoutine(string message, string eventId, NightState state, float timeout, Action<string> done)
        {
            int current = generation;
            float deadline = Time.realtimeSinceStartup + timeout;
            while (SessionId == null && sessionOpen && current == generation && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (SessionId == null || current != generation)
            {
                done(null);
                yield break;
            }

            var request = new ChatRequest { message = message ?? "", @event = eventId ?? "", state = state, client_seq = ++seq };
            string reply = null;
            int seconds = Mathf.Max(1, Mathf.CeilToInt(deadline - Time.realtimeSinceStartup));
            yield return Send("POST", "/v1/game/sessions/" + SessionId + "/chat", JsonUtility.ToJson(request), true, (ok, body, error) =>
            {
                if (ok) reply = JsonUtility.FromJson<ChatResponse>(body).reply;
                else Debug.LogWarning("Still Awake: модель не ответила — " + error);
            }, seconds);
            done(current == generation && !string.IsNullOrWhiteSpace(reply) ? reply : null);
        }

        IEnumerator Send(string method, string path, string json, bool auth, Action<bool, string, string> done,
            int timeoutSeconds = DefaultTimeoutSeconds, string server = null)
        {
            string baseUrl = server ?? ServerUrl;
            using var request = new UnityWebRequest(baseUrl + path, method);
            if (json != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = timeoutSeconds;
            if (auth) request.SetRequestHeader("Authorization", "Bearer " + PlayerPrefs.GetString(TokenKey, ""));

            yield return request.SendWebRequest();

            string body = request.downloadHandler != null ? request.downloadHandler.text : "";
            if (request.result == UnityWebRequest.Result.Success)
            {
                done(true, body, null);
                yield break;
            }

            string error = request.responseCode == 0 ? "Сервер недоступен: " + baseUrl : request.error;
            if (!string.IsNullOrEmpty(body) && body.TrimStart().StartsWith("{"))
            {
                var parsed = JsonUtility.FromJson<ErrorResponse>(body);
                if (!string.IsNullOrEmpty(parsed?.error)) error = parsed.error;
            }
            // Токен отозван или аккаунт заблокирован — дальше без аккаунта, пока игрок не войдёт снова.
            if (auth && request.responseCode == 401)
            {
                Forget();
                EndSession();
            }
            done(false, body, error);
        }

        /// <summary>JSON-объект из пар «ключ — значение» (строки, числа, bool): JsonUtility не умеет словари.</summary>
        public static string Json(params (string Key, object Value)[] fields)
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Quote(fields[i].Key)).Append(':').Append(ValueToJson(fields[i].Value));
            }
            return sb.Append('}').ToString();
        }

        static string ValueToJson(object value) => value switch
        {
            null => "null",
            bool b => b ? "true" : "false",
            int or long or float or double => Convert.ToString(value, CultureInfo.InvariantCulture),
            _ => Quote(value.ToString()),
        };

        static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;
    }
}
