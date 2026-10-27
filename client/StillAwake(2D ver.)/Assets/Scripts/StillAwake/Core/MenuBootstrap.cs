using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace StillAwake
{
    /// <summary>
    /// Точка входа сцены GameMenu.unity — первой в Build Settings. Собирает меню кодом, как GameBootstrap — ночь.
    /// «Играть» загружает сцену Game.
    /// </summary>
    public sealed class MenuBootstrap : MonoBehaviour
    {
        void Start()
        {
            Time.timeScale = 1f;

            var cameraObject = new GameObject("Menu Camera") { tag = "MainCamera" };
            cameraObject.transform.SetParent(transform, false);
            var cam = cameraObject.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.orthographic = true;
            cameraObject.AddComponent<AudioListener>();

            if (EventSystem.current == null)
            {
                var events = new GameObject("EventSystem");
                events.transform.SetParent(transform, false);
                events.AddComponent<EventSystem>();
                events.AddComponent<InputSystemUIInputModule>();
            }

            MainMenu.Create(Ui.CreateCanvas(transform));
        }
    }
}
