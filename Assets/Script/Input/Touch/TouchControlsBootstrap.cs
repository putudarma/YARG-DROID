using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;

namespace YARG.Input.Touch
{
    /// <summary>
    /// Installs the Android touch controls without touching any scene or
    /// prefab: when the Gameplay scene loads on a mobile platform, a
    /// <see cref="TouchControlsManager"/> is added to a fresh GameObject.
    /// On desktop / editor builds this does nothing.
    ///
    /// Two install paths, belt and braces (build #15 phone test: the
    /// sceneLoaded-only install never produced a visible manager):
    ///   - <see cref="SceneManager.sceneLoaded"/> (fires on every scene load), and
    ///   - a persistent watcher (<see cref="TouchControlsWatcher"/>) that
    ///     retries while the Gameplay scene is active, in case the event was
    ///     missed or the manager was destroyed before it could build its UI.
    /// The manager itself is a plain MonoBehaviour (not a GameplayBehaviour),
    /// so it can never be destroyed for a missing GameManager.
    /// </summary>
    [Preserve]
    public static class TouchControlsBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;

            // Persistent watcher: survives scene changes, retries the install.
            var watcherObject = new GameObject("TouchControlsWatcher");
            watcherObject.AddComponent<TouchControlsWatcher>();
            Object.DontDestroyOnLoad(watcherObject);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ApplyAndroidOrientation();
            TryInstall(scene);
        }

        private static void ApplyAndroidOrientation()
        {
            // Landscape lock + never sleep on Android (belt and braces next to
            // the PlayerSettings default orientation, which is already
            // LandscapeRight). Build #15 used LandscapeLeft here and the
            // phone showed the game upside-down (180° reversed).
            if (Application.platform == RuntimePlatform.Android)
            {
                Screen.orientation = ScreenOrientation.LandscapeRight;
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
            }
        }

        internal static void TryInstall(Scene scene)
        {
            if (!Application.isMobilePlatform)
                return;

            if (scene.name != "Gameplay")
                return;

            if (Object.FindAnyObjectByType<TouchControlsManager>() != null)
                return;

            var host = new GameObject("TouchControls");
            host.AddComponent<TouchControlsManager>();
        }
    }

    /// <summary>
    /// Retries the touch-controls install while the Gameplay scene is the
    /// active scene. Cheap: one scene-name check per frame, and the
    /// install itself is a no-op once a manager exists.
    /// </summary>
    [Preserve]
    public class TouchControlsWatcher : MonoBehaviour
    {
        private void Update()
        {
            if (!Application.isMobilePlatform)
            {
                enabled = false;
                return;
            }

            TouchControlsBootstrap.TryInstall(SceneManager.GetActiveScene());
        }
    }
}
