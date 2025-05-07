using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.Lasertag.Gallery
{
    public class SimpleTarget : NetworkBehaviour, IBulletHitHandler
    {
		public int pointValue;
		public float despawnDelay;

		public UnityEvent OnScore = new();
		public UnityEvent OnHit = new();

		public void OnOwnedBulletHit(Bullet bullet, Vector3 worldHitPoint)
		{
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
				StartCoroutine(Delay());
				IEnumerator Delay()
				{
					yield return new WaitForSeconds(despawnDelay);
					NetworkObject.Despawn();
				}
			}
		}


	}
}
