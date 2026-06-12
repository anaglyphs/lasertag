using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Anaglyph.Netcode
{
	/// <summary>
	/// Keeps scenes that aren't in the build list (build index -1) out of Netcode's
	/// scene management. ARFoundation's XR Simulation additively loads a runtime-created
	/// "Simulated Environment Scene" in the editor, which NetworkSceneManager would
	/// otherwise try to synchronize to joining clients and throw, because it can't
	/// hash a scene that has no build index.
	/// </summary>
	public static class NonBuildSceneSyncFilter
	{
		// statics persist across play sessions while domain reload is disabled
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void OnSceneLoad()
		{
			var manager = NetworkManager.Singleton;
			if (!manager) return;

			// NetworkManager recreates its SceneManager on every session start,
			// clearing these delegates, so they must be reassigned each time
			manager.OnServerStarted += AssignDelegates;
			manager.OnClientStarted += AssignDelegates;
		}

		private static void AssignDelegates()
		{
			var sceneManager = NetworkManager.Singleton.SceneManager;
			sceneManager.VerifySceneBeforeLoading = VerifySceneBeforeLoading;
			sceneManager.VerifySceneBeforeUnloading = VerifySceneBeforeUnloading;
			// don't warn every time a sync skips the simulated environment scene
			sceneManager.DisableValidationWarnings(true);
		}

		// Scene hashes sent over the network always resolve to a valid build index,
		// so this only ever excludes locally created scenes like the XR Simulation
		// environment, never a scene a server legitimately asks a client to load
		private static bool VerifySceneBeforeLoading(int sceneIndex, string sceneName, LoadSceneMode loadSceneMode)
			=> sceneIndex >= 0;

		// Only invoked if PostSynchronizationSceneUnloading is enabled: returning
		// false exempts runtime-created scenes from post-sync cleanup unloading
		private static bool VerifySceneBeforeUnloading(Scene scene)
			=> scene.buildIndex >= 0;
	}
}
