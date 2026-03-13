using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Anaglyph.DepthKit
{
	public class zEnvMapper2 : MonoBehaviour
	{
		public zEnvMapper2 Instance { get; private set; }

		private AROcclusionManager depthMan;

		[SerializeField] private ComputeShader comp;

		private void Awake()
		{
			Instance = this;

			depthMan = FindFirstObjectByType<AROcclusionManager>();
		}

		private void Start()
		{
			// depthMan.frameReceived += OnDepthFrameReceived;
		}

		private void OnEnable()
		{
			if (didStart)
				Start();
		}

		private void OnDisable()
		{
			// depthMan.frameReceived -= OnDepthFrameReceived;
		}

		private void Update()
		{
			bool got = depthMan.TryAcquireEnvironmentDepthCpuImage(out XRCpuImage img);
			if (!got) return;

			XRCpuImage.Plane plane = img.GetPlane(0);

			Debug.Log(plane.pixelStride);

			img.Dispose();
		}
	}
}