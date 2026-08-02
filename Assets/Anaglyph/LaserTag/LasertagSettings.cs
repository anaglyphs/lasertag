using Anaglyph.XRTemplate.SharedSpaces;
using UnityEngine;
using VariableObjects;

namespace Anaglyph.Lasertag
{
	public class LasertagSettings : MonoBehaviour
	{
		[SerializeField] private BoolObject aprilTagColocation;
		[SerializeField] private FloatObject aprilTagSize;
		[SerializeField] private BoolObject boundary;
		[SerializeField] private BoolObject damagedRedVision;
		[SerializeField] private BoolObject lightEffects;
		[SerializeField] private BoolObject relay;

		private void Start()
		{
			// Keep the provider's offline/host setting current even while shared-anchor
			// colocation is selected. Enabling AprilTags later must not inherit the
			// provider prefab's unconfigured zero value.
			aprilTagSize.AddChangeListenerAndCheck(s =>
				TagConstraintProvider.Instance.HostTagSizeCm = s);

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
