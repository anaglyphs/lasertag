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
		public static ColocationManager Instance { get; private set; }

		[Serializable]
		public enum ColocationMethod
		{
			MetaSharedAnchor,
			AprilTag,
		}

		[SerializeField] private BoolObject useAprilTagColocation;
		[SerializeField] private FloatObject aprilTagSizeOption;

		private NetworkVariable<ColocationMethod> colocationMethodSync = new(0);
		public void SetColocationMethod(ColocationMethod colocationMethod)
			=> colocationMethodSync.Value = colocationMethod;

		private NetworkVariable<float> aprilTagSizeSync = new();

		[SerializeField] private MetaAnchorColocator metaAnchorColocator;
		[SerializeField] private AprilTagColocator aprilTagColocator;
        [SerializeField] private DualTagColocator dualTagColocator;

		private void Awake()
        {
            Instance = this;
        }
        
        

        private void OnColocated(bool isColocated)
        {
            if (isColocated)
            {
                EnvironmentMapper.Instance.Clear();
            }
        }

		public override async void OnNetworkSpawn()
		{
            Colocation.IsColocatedChange += OnColocated;
            
			await Awaitable.EndOfFrameAsync();

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
					Colocation.SetActiveColocator(dualTagColocator);
                    dualTagColocator.tagSize = aprilTagSizeSync.Value;
					break;
			}

			Colocation.ActiveColocator.Colocate();
		}

		public override void OnNetworkDespawn()
		{
            Colocation.IsColocatedChange -= OnColocated;
            
			Colocation.ActiveColocator.StopColocation();

			MainXRRig.TrackingSpace.position = Vector3.zero;
			MainXRRig.TrackingSpace.rotation = Quaternion.identity;
		}
	}
}
