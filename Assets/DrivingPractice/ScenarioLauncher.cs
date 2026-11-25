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
        static DrivingPractice stagedScenario;
        static Scene previousScene;
        static float nextStart; // world z where the next scenario begins: the current one's end

        internal static int CompletedCount { get; private set; }
        internal static int ScenarioCount => session.Count;
        internal static bool HasRemaining => CompletedCount < ScenarioCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession()
        {
            SceneManager.sceneLoaded -= StageLoadedScenario;
            stagedScenario = null;
            previousScene = default;
            nextStart = 0f;
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
            SceneManager.LoadSceneAsync(session[0], LoadSceneMode.Single);
        }

        internal static bool IsCurrentScenario(string path) =>
            HasRemaining && session[CompletedCount] == path;

        internal static bool HasFollowingScenario(string path) =>
            IsCurrentScenario(path) && CompletedCount + 1 < ScenarioCount;

        internal static void PreloadFollowing(DrivingPractice current)
        {
            if (!HasFollowingScenario(current.gameObject.scene.path)) return;
            nextStart = current.WorldEnd;
            SceneManager.sceneLoaded += StageLoadedScenario;
            SceneManager.LoadSceneAsync(session[CompletedCount + 1], LoadSceneMode.Additive);
        }

        static void StageLoadedScenario(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Additive || CompletedCount + 1 >= ScenarioCount ||
                scene.path != session[CompletedCount + 1]) return;

            SceneManager.sceneLoaded -= StageLoadedScenario;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!root.TryGetComponent<DrivingPractice>(out var next)) continue;
                next.StageForSession(nextStart);
                stagedScenario = next;
                return;
            }

            Debug.LogError($"No Driving Practice controller found in {scene.path}.");
        }

        internal static bool TryAdvance(DrivingPractice current)
        {
            if (!stagedScenario || !HasFollowingScenario(current.gameObject.scene.path)) return false;

            var next = stagedScenario;
            stagedScenario = null;
            CompletedCount++;
            current.LeaveSessionSection();
            SceneManager.SetActiveScene(next.gameObject.scene);
            next.EnterSessionSection(current);
            if (previousScene.IsValid()) SceneManager.UnloadSceneAsync(previousScene);
            previousScene = current.gameObject.scene;
            PreloadFollowing(next);
            return true;
        }

        internal static bool TryCompleteFinalScenario(string path)
        {
            if (!IsCurrentScenario(path) || CompletedCount + 1 != ScenarioCount) return false;
            CompletedCount++;
            return true;
        }

        internal static void RestartSession()
        {
            if (launcherScene != null)
                SceneManager.LoadSceneAsync(launcherScene, LoadSceneMode.Single);
        }
    }
}
