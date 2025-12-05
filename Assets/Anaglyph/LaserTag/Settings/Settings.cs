using Anaglyph.DepthKit.Meshing;
using Anaglyph.XRTemplate;
using Anaglyph.XRTemplate.SharedSpaces;
using UnityEngine;
using VariableObjects;

namespace Anaglyph.Lasertag
{
	public class Settings : MonoBehaviour
	{
		[SerializeField] private BoolObject aprilTagColocation;
		[SerializeField] private FloatObject aprilTagSize;
		[SerializeField] private BoolObject boundary;
		[SerializeField] private BoolObject damagedRedVision;
		[SerializeField] private BoolObject lightEffects;
		[SerializeField] private BoolObject relay;

		[SerializeField] private BoolObject drawScanMesh;
		[SerializeField] private BoolObject pauseScanning;

		private void Start()
		{
			aprilTagColocation.AddChangeListenerAndCheck(b =>
			{
				if (b)
					ColocationManager.Instance.methodHostSetting =
						ColocationManager.ColocationMethod.AprilTag;
				else
					ColocationManager.Instance.methodHostSetting =
						ColocationManager.ColocationMethod.MetaSharedAnchor;
			});

			aprilTagSize.AddChangeListenerAndCheck(s =>
			{
				if (aprilTagColocation.Value)
					TagColocator.Instance.tagSizeCmHostSetting = s;
			});

			// boundary.AddChangeListenerAndCheck(b =>
			// {
			// });

			damagedRedVision.AddChangeListenerAndCheck(b =>
			{
				if (MainPlayer.Instance != null)
					MainPlayer.Instance.redDamagedVision = b;
			});

			lightEffects.AddChangeListenerAndCheck(b => { DepthLight.SetGloballyEnabled(b); });

			// relay.AddChangeListenerAndCheck(b =>
			// {
			// });

			drawScanMesh.AddChangeListenerAndCheck(b => { MeshChunk.SetDebugRenderingEnabled(b); });

			pauseScanning.AddChangeListenerAndCheck(b => { EnvironmentMapper.Instance.enabled = !b; });
		}
	}
}