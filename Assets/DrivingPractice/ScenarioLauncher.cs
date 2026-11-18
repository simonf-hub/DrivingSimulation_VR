using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BCRoot
{
    public sealed class ScenarioLauncher : MonoBehaviour
    {
        [SerializeField] string[] scenarios;

        static readonly List<string> session = new();
        static string launcherScene;

        internal static int CompletedCount { get; private set; }
        internal static int ScenarioCount => session.Count;
        internal static bool HasRemaining => CompletedCount < ScenarioCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession()
        {
            session.Clear();
            CompletedCount = 0;
            launcherScene = null;
        }

        void Start()
        {
            ResetSession();
            if (scenarios == null || scenarios.Length == 0)
            {
                Debug.LogError("Assign at least one scenario scene path.", this);
                return;
            }

            foreach (var scene in scenarios)
            {
                if (session.Contains(scene)) continue;
                if (SceneUtility.GetBuildIndexByScenePath(scene) < 0 || scene == gameObject.scene.path)
                {
                    Debug.LogError($"Assign an enabled scenario scene. Invalid path: '{scene}'.", this);
                    ResetSession();
                    return;
                }

                session.Add(scene);
            }

            for (var i = session.Count - 1; i > 0; i--)
            {
                var j = Random.Range(0, i + 1);
                (session[i], session[j]) = (session[j], session[i]);
            }

            launcherScene = gameObject.scene.path;
            LoadNext();
        }

        internal static bool IsCurrentScenario(string path) =>
            HasRemaining && session[CompletedCount] == path;

        internal static bool TryCompleteScenario(string path)
        {
            if (!IsCurrentScenario(path)) return false;
            CompletedCount++;
            return true;
        }

        internal static void LoadNext()
        {
            if (HasRemaining)
                SceneManager.LoadSceneAsync(session[CompletedCount], LoadSceneMode.Single);
        }

        internal static void RestartSession()
        {
            if (launcherScene != null)
                SceneManager.LoadSceneAsync(launcherScene, LoadSceneMode.Single);
        }
    }
}
