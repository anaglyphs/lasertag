using Anaglyph.XRTemplate.SharedSpaces;
using Anaglyph.XRTemplate;
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
			MetaSharedAnchor,
			AprilTag,
		}

		[SerializeField] private BoolObject useAprilTagColocation;
		[SerializeField] private FloatObject aprilTagSizeOption;

		public static ColocationManager Current;

		private NetworkVariable<ColocationMethod> colocationMethodSync = new(0);
		public void SetColocationMethod(ColocationMethod colocationMethod)
			=> colocationMethodSync.Value = colocationMethod;

		private NetworkVariable<float> aprilTagSizeSync = new();

		private MetaAnchorColocator metaAnchorColocator;
		private AprilTagColocator aprilTagColocator;

		private void Awake()
		{
			Current = this;
			metaAnchorColocator = GetComponent<MetaAnchorColocator>();
			aprilTagColocator = GetComponent<AprilTagColocator>();
		}

		public override async void OnNetworkSpawn()
		{
			await Awaitable.EndOfFrameAsync();

			EnvironmentMapper.Instance.Clear();

			if (IsOwner)
			{
				colocationMethodSync.Value = useAprilTagColocation.Value ?
					ColocationMethod.AprilTag : ColocationMethod.MetaSharedAnchor;

				aprilTagSizeSync.Value = aprilTagSizeOption.Value / 100f;
			}

			switch (colocationMethodSync.Value)
			{
				case ColocationMethod.MetaSharedAnchor:
					Colocation.SetActiveColocator(metaAnchorColocator);
					break;

				case ColocationMethod.AprilTag:
					Colocation.SetActiveColocator(aprilTagColocator);
					aprilTagColocator.tagSize = aprilTagSizeSync.Value;
					break;
			}

			Colocation.ActiveColocator.Colocate();
		}

		public override void OnNetworkDespawn()
		{
			Colocation.ActiveColocator.StopColocation();

			MainXROrigin.Transform.position = Vector3.zero;
			MainXROrigin.Transform.rotation = Quaternion.identity;
		}

		public override void OnDestroy()
		{
			base.OnDestroy();
		}
	}
}
