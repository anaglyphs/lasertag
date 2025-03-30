using Anaglyph.SharedSpaces;
using Anaglyph.XRTemplate;
using Anaglyph.XRTemplate.SharedSpaces;
using System;
using Unity.Netcode;
using UnityEngine;
using VariableObjects;

namespace Anaglyph.Lasertag
{
	public class ColocationManager : NetworkBehaviour
	{
		[Serializable]
		public enum ColocationMethod
		{
			Automatic = 0,
			TrackedKeyboard = 1,
			AprilTag = 2,
		}

		[SerializeField] private BoolObject useKeyboardColocation;

		public static ColocationManager Current;
		private NetworkVariable<ColocationMethod> colocationMethodSync = new(0);
		public void SetColocationMethod(ColocationMethod colocationMethod)
			=> colocationMethodSync.Value = colocationMethod;

		private void Start()
		{
			if (Current == null)
				Current = this;
		}

		private MetaAnchorColocator metaAnchorColocator = new();
		private MetaTrackableColocator metaTrackableColocator = new();
		private AprilTagColocator aprilTagColocator = new();

		protected override void OnNetworkSessionSynchronized()
		{
			EnvironmentMapper.Instance.Clear();

			if (IsOwner)
			{
				colocationMethodSync.Value = useKeyboardColocation.Value ?
					ColocationMethod.AprilTag : ColocationMethod.Automatic;
			}

			switch (colocationMethodSync.Value)
			{
				case ColocationMethod.Automatic:
					Colocation.SetActiveColocator(metaAnchorColocator);
					break;

				case ColocationMethod.TrackedKeyboard:
					Colocation.SetActiveColocator(metaTrackableColocator);
					break;

				case ColocationMethod.AprilTag:
					Colocation.SetActiveColocator(aprilTagColocator);
					break;
			}

			Colocation.ActiveColocator.Colocate();
		}

		public override void OnNetworkDespawn()
		{
			Colocation.ActiveColocator.StopColocation();

			MainXROrigin.Instance.transform.position = Vector3.zero;
			MainXROrigin.Instance.transform.rotation = Quaternion.identity;
		}

		public override void OnDestroy()
		{
			base.OnDestroy();
		}
	}
}
