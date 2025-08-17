using Anaglyph.Lasertag.Logistics;
using Anaglyph.Lasertag.Weapons;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public class Wand : MonoBehaviour
{
	[SerializeField] private GameObject boltPrefab;
	[SerializeField] private GameObject shieldPrefab;
	[SerializeField] private Transform emitFromTransform;
	public UnityEvent onFire = new();

	[SerializeField] private string fireWord = "cast";
	[SerializeField] private string shieldWord = "shield";
	private bool primed = true;

	private VoskRecognition voskRecognition;

	private void Awake()
	{
		voskRecognition = FindAnyObjectByType<VoskRecognition>();
	}

	private void Start()
	{
		
	}

	private void OnEnable()
	{
		if(didStart && !voskRecognition.VoiceProcessor.IsRecording)
			voskRecognition.StartVoskStt();
	}

	private int start = 0;

	private void Update()
	{
		var str = voskRecognition.PartialResult;



		if (str == null || str.Length < start) start = 0;

		string delta = str.Substring(start);
		if (delta.Contains(fireWord))
			Fire(boltPrefab);

		if(delta.Contains(shieldWord))
			Fire(shieldPrefab);

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
