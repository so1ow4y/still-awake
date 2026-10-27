using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;

namespace StillAwake
{
    /// <summary>
    /// Точка входа сцены Game.unity. Собирает ночь кодом (камера, свет, сталкер, UI) поверх комнаты,
    /// собранной на сцене (authoredRoom), и связывает системы. Баланс ночи — в инспекторе этого компонента.
    /// Если сцена GameMenu есть в Build Settings, а ночь запустили не из меню, сначала открывается меню.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] NightSettings settings = new NightSettings();
        [SerializeField] ViewId startView = ViewId.Lounge;

        [SerializeField] Transform authoredRoom;

        [Tooltip("Play на сцене Game сначала открывает главное меню (если GameMenu есть в Build Settings). Выключите, чтобы сразу проверять ночь.")]
        [SerializeField] bool openMenuFirst = true;

        GameObject session;
        bool restarting;
        bool redirecting;

        void Awake()
        {
            if (!openMenuFirst || GameFlow.CameFromMenu || !GameFlow.HasMenu) return;
            redirecting = true;
            GameFlow.OpenMenu();
        }

        void Start()
        {
            if (!redirecting) Build();
        }

        void Build()
        {
            session = new GameObject("Session");
            Transform root = session.transform;

            Camera worldCamera = CreateCamera(root);
            Light2D globalLight = CreateGlobalLight(root);
            CreateEventSystem(root);

            var room = new RoomState();
            var inventory = new Inventory(InventoryView.SlotCount);
            var phone = new Phone();

            var roomView = Child<RoomView>("Room", root);
            roomView.Init(room, globalLight, authoredRoom);

            // Порядок создания — порядок слоёв: субтитры и хотбар под телефоном, меню действий над ними, пауза сверху.
            bool hasMenu = GameFlow.HasMenu;
            RectTransform canvas = Ui.CreateCanvas(root);
            Hud hud = Hud.Create(canvas);
            Subtitles subtitles = Subtitles.Create(canvas);
            InventoryView inventoryView = InventoryView.Create(canvas, inventory);
            PhoneView phoneView = PhoneView.Create(canvas, phone);
            ActionMenu actionMenu = ActionMenu.Create(canvas);
            EndScreen endScreen = EndScreen.Create(canvas, hasMenu);
            PauseMenu pause = PauseMenu.Create(canvas, hasMenu);
            hud.CreateOverlay(canvas);

            var roomCamera = worldCamera.gameObject.AddComponent<RoomCamera>();

            var picker = session.AddComponent<HotspotPicker>();
            picker.Init(roomView, worldCamera, hud);

            // Ночь, выбранная в меню: её баланс поверх настроек сцены.
            NightDef nightDef = Nights.Selected;
            NightSettings nightSettings = Nights.Apply(settings, nightDef);
            if (nightDef.StartDark) room.LightOn = false;

            var night = session.AddComponent<NightController>();
            var stalker = Child<Stalker>("Stalker (logic)", root);
            stalker.Init(nightSettings, room, () => night.Phase);
            var stalkerView = Child<StalkerView>("Stalker", roomView.transform);
            stalkerView.Init(stalker, roomView, room);
            Child<Atmosphere>("Atmosphere", root).Init(worldCamera, stalker, room);

            night.Init(new GameContext
            {
                Settings = nightSettings,
                Night = nightDef,
                Room = room,
                Inventory = inventory,
                Phone = phone,
                RoomView = roomView,
                Camera = roomCamera,
                Picker = picker,
                Stalker = stalker,
                StalkerView = stalkerView,
                Hud = hud,
                PhoneView = phoneView,
                InventoryView = inventoryView,
                Menu = actionMenu,
                Subtitles = subtitles,
                EndScreen = endScreen,
                Pause = pause,
                Backend = BackendClient.Instance,
                Restart = Restart,
                ToMenu = hasMenu ? GameFlow.OpenMenu : (Action)null,
                NextNight = NextNight,
            });

            roomView.Show(startView);
        }

        /// <summary>Следующая ночь: выбрать её и пересобрать сессию.</summary>
        void NextNight()
        {
            Nights.Select(Nights.Selected.Number + 1);
            Restart();
        }

        /// <summary>Перезапуск без перезагрузки сцены — не зависит от Build Settings.</summary>
        void Restart()
        {
            if (!restarting) StartCoroutine(RestartRoutine());
        }

        IEnumerator RestartRoutine()
        {
            restarting = true;
            Time.timeScale = 1f;
            Destroy(session);
            yield return null;   // старые камера и EventSystem уничтожаются в конце кадра
            Build();
            restarting = false;
        }

        static Camera CreateCamera(Transform root)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(0f, 0f, -10f);

            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = RoomCamera.DefaultSize;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;

            go.AddComponent<AudioListener>();
            Sfx.Init(go.AddComponent<AudioSource>());
            return cam;
        }

        static Light2D CreateGlobalLight(Transform root)
        {
            // Создаём выключенным, чтобы тип Global был задан до регистрации света в URP.
            var go = new GameObject("Global Light 2D");
            go.SetActive(false);
            go.transform.SetParent(root, false);
            var light = go.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.intensity = 1f;
            light.color = Color.white;
            go.SetActive(true);
            return light;
        }

        static void CreateEventSystem(Transform root)
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem");
            go.transform.SetParent(root, false);
            go.AddComponent<EventSystem>();
            // Без своего ассета действий модуль сам подключает DefaultInputActions.
            go.AddComponent<InputSystemUIInputModule>();
        }

        static T Child<T>(string name, Transform parent) where T : Component
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<T>();
        }
    }
}
