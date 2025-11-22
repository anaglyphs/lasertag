using System;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
    public class WorldLockAnchor : MonoBehaviour
    {
	    public Matrix4x4 target = Matrix4x4.identity;

	    private void OnEnable()
	    {
		    OVRManager.display.RecenteredPose += OnRecenteredPose;
	    }

	    private void OnDisable()
	    {
		    OVRManager.display.RecenteredPose -= OnRecenteredPose;
	    }

	    private async void OnRecenteredPose()
	    {
		    await Awaitable.EndOfFrameAsync();
		    var currentMat = transform.localToWorldMatrix;
		    MainXRRig.Instance.AlignSpace(currentMat, target);
	    }
    }
}
