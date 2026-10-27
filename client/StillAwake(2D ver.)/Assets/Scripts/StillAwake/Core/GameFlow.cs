using UnityEngine;
using UnityEngine.SceneManagement;

namespace StillAwake
{
    /// <summary>
    /// Переходы между сценами: GameMenu (главное меню) → Game (ночь) → обратно в меню.
    /// Если сцены меню нет в Build Settings, игра работает как раньше — сразу с ночи.
    /// </summary>
    public static class GameFlow
    {
        public const string MenuScene = "GameMenu";
        public const string GameScene = "Game";

        /// <summary>Ночь запущена из меню — GameBootstrap не отправит обратно в меню.</summary>
        public static bool CameFromMenu { get; private set; }

        public static bool HasMenu => Application.CanStreamedLevelBeLoaded(MenuScene);

        public static void StartNight()
        {
            CameFromMenu = true;
            Time.timeScale = 1f;
            SceneManager.LoadScene(GameScene);
        }

        public static void OpenMenu()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(MenuScene);
        }

        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // Enter Play Mode без перезагрузки домена: статика переживает запуски, сбрасываем вручную.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => CameFromMenu = false;
    }
}
