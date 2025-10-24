using Anaglyph.XRTemplate.SharedSpaces;
using Anaglyph.XRTemplate;
using System;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class ColocationManager : NetworkBehaviour
	{
		public static ColocationManager Instance { get; private set; }

		[Serializable]
		public enum Method
		{
			MetaSharedAnchor = 0,
			AprilTag = 1,
		}

		private NetworkVariable<Method> colocationMethodSync = new(0);
		private NetworkVariable<float> aprilTagSizeSync = new();

		public Method HostColocationMethod;
		public float HostAprilTagSize;

		[SerializeField] private MetaAnchorColocator metaAnchorColocator;
		[SerializeField] private AprilTagColocator aprilTagColocator;

		private void Awake()
		{
			Instance = this;
		}

		public override async void OnNetworkSpawn()
		{
			await Awaitable.EndOfFrameAsync();

			// todo move out of
			EnvironmentMapper.Instance?.Clear();

			if (IsOwner)
			{
				colocationMethodSync.Value = HostColocationMethod;

				aprilTagSizeSync.Value = HostAprilTagSize / 100f;
			}

			switch (colocationMethodSync.Value)
			{
				case Method.MetaSharedAnchor:
					Colocation.SetActiveColocator(metaAnchorColocator);
					break;

				case Method.AprilTag:
					Colocation.SetActiveColocator(aprilTagColocator);
					aprilTagColocator.tagSize = aprilTagSizeSync.Value;
					break;
			}

			Colocation.ActiveColocator.Colocate();
		}

		public override void OnNetworkDespawn()
		{
			Colocation.ActiveColocator.StopColocation();

			MainXRRig.TrackingSpace.position = Vector3.zero;
			MainXRRig.TrackingSpace.rotation = Quaternion.identity;
		}

		public override void OnDestroy()
		{
			base.OnDestroy();
		}
	}
}
