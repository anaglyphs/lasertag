using Anaglyph.Lasertag.Logistics;
using Anaglyph.Lasertag.Weapons;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public class Wand : MonoBehaviour
{
	[SerializeField] private Transform emitFromTransform;
	public UnityEvent onFire = new();

	[SerializeField] private string[] spells;
	[SerializeField] private GameObject[] spellSpawns;
	private bool primed = true;

	private VoskRecognition voskRecognition;

	private void Awake()
	{
		voskRecognition = FindAnyObjectByType<VoskRecognition>();
	}

	private int start = 0;

	private void Update()
	{
		var str = voskRecognition.PartialResult;

		if (str == null || str.Length < start) start = 0;

		if (str == null)
			return;

		string delta = str.Substring(start);

		for (int i = 0; i < spells.Length; i++)
		{
			if (delta.Contains(spells[i]))
			{
				Fire(spellSpawns[i]);
				break;
			}
		}

		if (str.Length > start)
			start = str.Length;
	}

	public void Fire(GameObject obj)
	{
		if (!NetworkManager.Singleton.IsConnectedClient || !WeaponsManagement.canFire)
			return;

		NetworkObject n = NetworkObjectPool.Instance.GetNetworkObject(
			obj, emitFromTransform.position, emitFromTransform.rotation);

		n.SpawnWithOwnership(NetworkManager.Singleton.LocalClientId);

		onFire.Invoke();
	}
}
