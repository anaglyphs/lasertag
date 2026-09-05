using Anaglyph.LaserTag.Player;
using Anaglyph.LaserTag.Shaders;
using Anaglyph.VariableObjects;
using Anaglyph.XR.SharedSpaces.AprilTags;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	public class LasertagSettings : MonoBehaviour
	{
		[SerializeField] private BoolObject aprilTagColocation;
		[SerializeField] private BoolObject boundary;
		[SerializeField] private BoolObject damagedRedVision;
		[SerializeField] private BoolObject lightEffects;
		[SerializeField] private BoolObject relay;

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
		}
	}
}
