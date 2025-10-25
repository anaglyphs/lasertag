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

		private void Start()
		{
			aprilTagColocation.AddChangeListenerAndCheck(b =>
			{
				if (b)
					ColocationManager.Instance.HostColocationMethod = ColocationManager.Method.AprilTag;
				else
					ColocationManager.Instance.HostColocationMethod = ColocationManager.Method.MetaSharedAnchor;
			});

			aprilTagSize.AddChangeListenerAndCheck(s =>
			{
				if (aprilTagColocation.Value)
					ColocationManager.Instance.HostAprilTagSize = s;
			});

			// boundary.AddChangeListenerAndCheck(b =>
			// {
			// });

			damagedRedVision.AddChangeListenerAndCheck(b =>
			{
				if(Player.Instance != null)
					Player.Instance.redDamagedVision = b;
			});

			lightEffects.AddChangeListenerAndCheck(b =>
			{
				DepthLight.SetGloballyEnabled(b);
			});

			// relay.AddChangeListenerAndCheck(b =>
			// {
			// });
		}
	}
}
