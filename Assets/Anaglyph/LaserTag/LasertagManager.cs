using Anaglyph.SharedSpaces;
using Anaglyph.XRTemplate;
using Anaglyph.XRTemplate.SharedSpaces;
using System;
using Unity.Netcode;
using UnityEngine;
using VariableObjects;

namespace Anaglyph.Lasertag
{
	public class LasertagManager : NetworkBehaviour
	{
		[Serializable]
		public enum ColocationMethod
		{
			Automatic = 0,
			TrackedKeyboard = 1,
		}

		[SerializeField] private BoolObject useKeyboardColocation;

		public static LasertagManager Current;
		private NetworkVariable<ColocationMethod> colocationMethodSync = new(0);
		public void SetColocationMethod(ColocationMethod colocationMethod)
			=> colocationMethodSync.Value = colocationMethod;

		private void Start()
		{
			if (Current == null)
				Current = this;
		}

		protected override void OnNetworkPostSpawn()
		{
			EnvironmentMapper.Instance.Clear();

			if (IsOwner)
			{
				colocationMethodSync.Value = useKeyboardColocation.Value ?
					ColocationMethod.TrackedKeyboard : ColocationMethod.Automatic;
			}

			switch (colocationMethodSync.Value)
			{
				case ColocationMethod.Automatic:
					Colocation.SetActiveColocator(new MetaAnchorColocator());
					break;

				case ColocationMethod.TrackedKeyboard:
					Colocation.SetActiveColocator(new MetaTrackableColocator());
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
