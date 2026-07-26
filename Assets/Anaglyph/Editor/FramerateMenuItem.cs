
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Anaglyph.Editor
{
    public static class FramerateMenuItem
    {
	    [MenuItem("Tools/Framerate/30")]
	    private static void Framerate30()
	    {
		    Application.targetFrameRate = 30;
	    }

	    [MenuItem("Tools/Framerate/60")]
	    private static void Framerate60()
	    {
		    Application.targetFrameRate = 60;
	    }

	    [MenuItem("Tools/Framerate/Unlimited")]
	    private static void FramerateUnlimited()
	    {
		    Application.targetFrameRate = 0;
	    }
    }
}
#endif