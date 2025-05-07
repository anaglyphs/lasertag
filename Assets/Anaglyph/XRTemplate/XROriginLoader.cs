using UnityEngine;

namespace Anaglyph.XRTemplate
{
	public static class XROriginLoader
	{
		private const string objectName = "XR Origin";

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void OnFirstSceneLoad()
		{
			Object globalObject = Object.Instantiate(Resources.Load(objectName));
			Object.DontDestroyOnLoad(globalObject);
		}
	}
}
