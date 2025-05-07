using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.Lasertag.Gallery
{
    public class SimpleTarget : NetworkBehaviour, IBulletHitHandler
    {
		public int pointValue;
		public float openAtZ;
		public float closeAtZ;

		public UnityEvent OnScore = new();
		public UnityEvent OnHit = new();
		public UnityEvent OnOpen = new();
		public UnityEvent OnClose = new();

		NetworkVariable<bool> openSync = new(false);

		private void Awake()
		{
			openSync.OnValueChanged += delegate (bool previousValue, bool newValue)
			{
				if(newValue)
					OnOpen.Invoke();
				else
					OnClose.Invoke();
			};
		}

		private float prevZ = Mathf.Infinity;
		private void Update()
		{
			float z = transform.position.z;
			if(IsOwner)
			{
				if (prevZ > openAtZ && z < openAtZ)
					openSync.Value = true;
				else if(prevZ > closeAtZ && z < closeAtZ)
					openSync.Value = false;
			}

			prevZ = transform.position.z;
		}

		public void OnOwnedBulletHit(Bullet bullet, Vector3 worldHitPoint)
		{
			if (!openSync.Value)
				return;

			MainPlayer.Instance.avatar.scoreSync.Value += pointValue;
			OnScore.Invoke();
			BulletHitRpc();
		}

		[Rpc(SendTo.Everyone)]
		private void BulletHitRpc()
		{
			OnHit.Invoke();

			if (IsOwner)
			{
				openSync.Value = false;
			}
		}


	}
}
