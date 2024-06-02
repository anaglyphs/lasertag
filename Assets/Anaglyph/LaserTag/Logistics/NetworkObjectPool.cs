using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Pool;

namespace Anaglyph.LaserTag.Logistics
{
	public class NetworkObjectPool : SingletonBehavior<NetworkObjectPool>
	{

		[SerializeField]
		List<PoolConfigObject> PooledPrefabsList;

		HashSet<GameObject> m_Prefabs = new HashSet<GameObject>();

		Dictionary<GameObject, ObjectPool<NetworkObject>> m_PooledObjects = new Dictionary<GameObject, ObjectPool<NetworkObject>>();

		private void Start()
		{
			NetworkManager.Singleton.OnClientStarted += OnSessionStart;
			NetworkManager.Singleton.OnClientStopped += OnSessionStopped;
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();

			if (NetworkManager.Singleton == null)
				return;

			NetworkManager.Singleton.OnClientStarted -= OnSessionStart;
			NetworkManager.Singleton.OnClientStopped -= OnSessionStopped;
		}

		private void OnSessionStart()
		{
			// Registers all objects in PooledPrefabsList to the cache.
			foreach (var configObject in PooledPrefabsList)
			{
				RegisterPrefabInternal(configObject.Prefab, configObject.PrewarmCount);
			}
		}

		private void OnSessionStopped(bool b)
		{
			// Unregisters all objects in PooledPrefabsList from the cache.
			foreach (var prefab in m_Prefabs)
			{
				// Unregister Netcode Spawn handlers
				NetworkManager.Singleton.PrefabHandler.RemoveHandler(prefab);
				m_PooledObjects[prefab].Clear();
			}
			m_PooledObjects.Clear();
			m_Prefabs.Clear();
		}

		public void OnValidate()
		{
			for (var i = 0; i < PooledPrefabsList.Count; i++)
			{
				var prefab = PooledPrefabsList[i].Prefab;
				if (prefab != null)
				{
					Assert.IsNotNull(prefab.GetComponent<NetworkObject>(), $"{nameof(NetworkObjectPool)}: Pooled prefab \"{prefab.name}\" at index {i.ToString()} has no {nameof(NetworkObject)} component.");
				}
			}
		}

		/// <summary>
		/// Gets an instance of the given prefab from the pool. The prefab must be registered to the pool.
		/// </summary>
		/// <remarks>
		/// To spawn a NetworkObject from one of the pools, this must be called on the server, then the instance
		/// returned from it must be spawned on the server. This method will then also be called on the client by the
		/// PooledPrefabInstanceHandler when the client receives a spawn message for a prefab that has been registered
		/// here.
		/// </remarks>
		/// <param name="prefab"></param>
		/// <param name="position">The position to spawn the object at.</param>
		/// <param name="rotation">The rotation to spawn the object with.</param>
		/// <returns></returns>
		public NetworkObject GetNetworkObject(GameObject prefab, Vector3 position, Quaternion rotation)
		{
			var networkObject = m_PooledObjects[prefab].Get();

			var noTransform = networkObject.transform;
			noTransform.SetPositionAndRotation(position, rotation);

			return networkObject;
		}

		/// <summary>
		/// Return an object to the pool (reset objects before returning).
		/// </summary>
		public void ReturnNetworkObject(NetworkObject networkObject, GameObject prefab)
		{
			m_PooledObjects[prefab].Release(networkObject);
		}

		/// <summary>
		/// Builds up the cache for a prefab.
		/// </summary>
		void RegisterPrefabInternal(GameObject prefab, int prewarmCount)
		{
			NetworkObject CreateFunc()
			{
				return Instantiate(prefab).GetComponent<NetworkObject>();
			}

			void ActionOnGet(NetworkObject networkObject)
			{
				networkObject.gameObject.SetActive(true);
			}

			void ActionOnRelease(NetworkObject networkObject)
			{
				networkObject.gameObject.SetActive(false);
			}

			void ActionOnDestroy(NetworkObject networkObject)
			{
				Destroy(networkObject.gameObject);
			}

			m_Prefabs.Add(prefab);

			// Create the pool
			m_PooledObjects[prefab] = new ObjectPool<NetworkObject>(CreateFunc, ActionOnGet, ActionOnRelease, ActionOnDestroy, defaultCapacity: prewarmCount);

			// Populate the pool
			var prewarmNetworkObjects = new List<NetworkObject>();
			for (var i = 0; i < prewarmCount; i++)
			{
				prewarmNetworkObjects.Add(m_PooledObjects[prefab].Get());
			}
			foreach (var networkObject in prewarmNetworkObjects)
			{
				m_PooledObjects[prefab].Release(networkObject);
			}

			// Register Netcode Spawn handlers
			NetworkManager.Singleton.PrefabHandler.AddHandler(prefab, new PooledPrefabInstanceHandler(prefab, this));
		}
	}

	[Serializable]
	struct PoolConfigObject
	{
		public GameObject Prefab;
		public int PrewarmCount;
	}

	class PooledPrefabInstanceHandler : INetworkPrefabInstanceHandler
	{
		GameObject m_Prefab;
		NetworkObjectPool m_Pool;

		public PooledPrefabInstanceHandler(GameObject prefab, NetworkObjectPool pool)
		{
			m_Prefab = prefab;
			m_Pool = pool;
		}

		NetworkObject INetworkPrefabInstanceHandler.Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
		{
			return m_Pool.GetNetworkObject(m_Prefab, position, rotation);
		}

		void INetworkPrefabInstanceHandler.Destroy(NetworkObject networkObject)
		{
			m_Pool.ReturnNetworkObject(networkObject, m_Prefab);
		}
	}

}