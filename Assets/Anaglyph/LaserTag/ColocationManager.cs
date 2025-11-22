using Anaglyph.XRTemplate;
using Anaglyph.XRTemplate.SharedSpaces;
using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.XR;

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
		[FormerlySerializedAs("aprilTagColocator")] [SerializeField] private TagColocator tagColocator;

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
					Colocation.SetActiveColocator(tagColocator);
					tagColocator.tagSize = aprilTagSizeSync.Value;
					break;
			}

			Colocation.ActiveColocator.Colocate();
		}

		public override void OnNetworkDespawn()
		{
			Colocation.ActiveColocator.StopColocation();

			if (!XRSettings.enabled)
				return;

			MainXRRig.TrackingSpace.position = Vector3.zero;
			MainXRRig.TrackingSpace.rotation = Quaternion.identity;
		}

		public override void OnDestroy()
		{
			base.OnDestroy();
		}
	}
}
