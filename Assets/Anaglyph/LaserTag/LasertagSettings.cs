using Anaglyph.XRTemplate.SharedSpaces;
using UnityEngine;
using VariableObjects;

namespace Anaglyph.Lasertag
{
	[CreateAssetMenu(fileName = "Lasertag Settings", menuName = "Lasertag/Settings")]
	public class LasertagSettings : ScriptableObject
	{
		[SerializeField] private BoolObject aprilTagColocation;
		[SerializeField] private FloatObject aprilTagSize;
		[SerializeField] private BoolObject boundary;
		[SerializeField] private BoolObject damagedRedVision;
		[SerializeField] private BoolObject lightEffects;
		[SerializeField] private BoolObject relay;

		/// <summary>Applies these settings and listens for subsequent changes.</summary>
		public void Apply()
		{
			RemoveChangeListeners();

			OnAprilTagColocationChanged(aprilTagColocation.Value);
			aprilTagColocation.Changed += OnAprilTagColocationChanged;

			OnAprilTagSizeChanged(aprilTagSize.Value);
			aprilTagSize.Changed += OnAprilTagSizeChanged;

			OnDamagedRedVisionChanged(damagedRedVision.Value);
			damagedRedVision.Changed += OnDamagedRedVisionChanged;

			OnLightEffectsChanged(lightEffects.Value);
			lightEffects.Changed += OnLightEffectsChanged;
		}

		/// <summary>Stops this settings asset from responding to variable changes.</summary>
		public void RemoveChangeListeners()
		{
			if (aprilTagColocation != null)
				aprilTagColocation.Changed -= OnAprilTagColocationChanged;

			if (aprilTagSize != null)
				aprilTagSize.Changed -= OnAprilTagSizeChanged;

			if (damagedRedVision != null)
				damagedRedVision.Changed -= OnDamagedRedVisionChanged;

			if (lightEffects != null)
				lightEffects.Changed -= OnLightEffectsChanged;
		}

		private static void OnAprilTagColocationChanged(bool enabled)
		{
			ColocationManager.Instance.methodHostSetting = enabled
				? ColocationManager.ColocationMethod.AprilTag
				: ColocationManager.ColocationMethod.MetaSharedAnchor;
		}

		private void OnAprilTagSizeChanged(float size)
		{
			if (aprilTagColocation.Value)
				TagConstraintProvider.Instance.tagSizeCmHostSetting = size;
		}

		private static void OnDamagedRedVisionChanged(bool enabled)
		{
			if (MainPlayer.Instance != null)
				MainPlayer.Instance.redDamagedVision = enabled;
		}

		private static void OnLightEffectsChanged(bool enabled)
		{
			DepthLight.SetGloballyEnabled(enabled);
		}
	}
}
