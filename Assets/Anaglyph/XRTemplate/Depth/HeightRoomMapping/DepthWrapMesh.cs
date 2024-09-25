using Unity.XR.Oculus;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR;

namespace Anaglyph.XRTemplate
{
    public class DepthWrapMesh : MonoBehaviour
    {
        [SerializeField] private Material mat;
		public UnityEvent<RenderTexture> onNewFrameAvailable = new();

		private XRDisplaySubsystem xrDisplay;

		private void Start()
		{
			xrDisplay = OVRManager.GetCurrentDisplaySubsystem();
		}

		private void Update()
        {
			if (xrDisplay == null) return;

			uint id = 0;
            if(Utils.GetEnvironmentDepthTextureId(ref id))
            {
				var desc = Utils.GetEnvironmentDepthFrameDesc(0);

				RenderTexture rt = xrDisplay.GetRenderTexture(id);
				mat.mainTexture = rt;
				transform.position = desc.createPoseLocation;
				transform.rotation = new Quaternion(desc.createPoseRotation.x, desc.createPoseRotation.y, desc.createPoseRotation.z, desc.createPoseRotation.w);

				onNewFrameAvailable.Invoke(rt);
			}
		}
    }
}
