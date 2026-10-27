using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace StillAwake.EditorTools
{
    /// <summary>
    /// Меню «Still Awake» и порядок сцен в Build Settings: GameMenu первой (с неё стартует игра), за ней Game.
    /// </summary>
    [InitializeOnLoad]
    static class StillAwakeMenu
    {
        const string MenuScene = "Assets/Scenes/GameMenu.unity";
        const string GameScene = "Assets/Scenes/Game.unity";

        static StillAwakeMenu() => EditorApplication.delayCall += EnsureScenesInBuild;

        [MenuItem("Still Awake/Запустить игру (с меню)")]
        static void PlayFromMenu() => OpenAndPlay(File.Exists(MenuScene) ? MenuScene : GameScene);

        [MenuItem("Still Awake/Открыть сцену меню")]
        static void OpenMenuScene() => Open(MenuScene);

        [MenuItem("Still Awake/Открыть сцену ночи")]
        static void OpenGameScene() => Open(GameScene);

        static void OpenAndPlay(string scene)
        {
            if (Open(scene)) EditorApplication.isPlaying = true;
        }

        static bool Open(string scene)
        {
            if (EditorApplication.isPlaying || !File.Exists(scene) || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            EditorSceneManager.OpenScene(scene);
            return true;
        }

        /// <summary>Меню — первой сценой, ночь — за ним; остальные сцены остаются как были.</summary>
        static void EnsureScenesInBuild()
        {
            var wanted = new List<string>();
            if (File.Exists(MenuScene)) wanted.Add(MenuScene);
            if (File.Exists(GameScene)) wanted.Add(GameScene);
            if (wanted.Count == 0) return;

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            bool inOrder = scenes.Count >= wanted.Count;
            for (int i = 0; inOrder && i < wanted.Count; i++)
                inOrder = scenes[i].path == wanted[i] && scenes[i].enabled;
            if (inOrder) return;

            scenes.RemoveAll(scene => wanted.Contains(scene.path));
            for (int i = wanted.Count - 1; i >= 0; i--)
                scenes.Insert(0, new EditorBuildSettingsScene(wanted[i], true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
