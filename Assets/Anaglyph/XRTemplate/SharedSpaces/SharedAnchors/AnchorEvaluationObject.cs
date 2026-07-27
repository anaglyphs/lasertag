using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.OpenXR.Features.Meta;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	/// <summary>
	/// For visually evaluating the performance of anchors.
	/// I.E. I place these down manually and observe how they drift
	/// </summary>
	public class AnchorEvaluationObject : NetworkBehaviour
	{
		private static AnchorRegistry anchorRegistry;
		
		private void Start()
		{
			if (anchorRegistry == null)
			{
				ARAnchorManager anchorManager = FindFirstObjectByType<ARAnchorManager>();
				MetaOpenXRAnchorSubsystem metaAnchorSubsystem = (MetaOpenXRAnchorSubsystem)anchorManager.subsystem;
				anchorRegistry = new AnchorRegistry(anchorManager, metaAnchorSubsystem);
			}
		}

		private AnchorLease anchorLease;

		public override void OnNetworkSpawn()
		{
			if (IsOwner)
			{
				anchorLease = anchorRegistry.Acquire(new SerializableGuid(Guid.NewGuid()), AnchorSource.Local);
			}
		}

		public override void OnNetworkDespawn()
		{
			anchorLease.Dispose();
		}
	}
}

