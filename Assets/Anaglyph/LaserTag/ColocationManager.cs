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

		public static ColocationManager Current;
		private NetworkVariable<ColocationMethod> colocationMethodSync = new(0);
		public void SetColocationMethod(ColocationMethod colocationMethod)
			=> colocationMethodSync.Value = colocationMethod;

		private void Start()
		{
			Current = this;
		}

		[SerializeField] private MetaAnchorColocator metaAnchorColocator;
		[SerializeField] private AprilTagColocator aprilTagColocator;

		public override async void OnNetworkSpawn()
		{
			await Awaitable.EndOfFrameAsync();

			EnvironmentMapper.Instance.Clear();

			if (IsOwner)
			{
				colocationMethodSync.Value = useAprilTagColocation.Value ?
					ColocationMethod.AprilTag : ColocationMethod.MetaSharedAnchor;
			}

			switch (colocationMethodSync.Value)
			{
				case ColocationMethod.MetaSharedAnchor:
					Colocation.SetActiveColocator(metaAnchorColocator);
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

			MainXROrigin.Transform.position = Vector3.zero;
			MainXROrigin.Transform.rotation = Quaternion.identity;
		}

		public override void OnDestroy()
		{
			base.OnDestroy();
		}
	}
}
