using Anaglyph.LaserTag.Weapons;
using Anaglyph.Netcode;
using Anaglyph.XR.Input;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public class Shotgun : MonoBehaviour, IWeapon
{
	private HandSubject hand;

	[SerializeField] private GameObject boltPrefab;
	[SerializeField] private Transform muzzle;
	[SerializeField] private WeaponVisual visual;
	public GameObject VisualObject => visual.gameObject;
	public UnityEvent onFire = new();

	[SerializeField] private int numBullets;
	[FormerlySerializedAs("angleSpread")] [SerializeField] private float bulletSpread;

	private void Awake()
	{
		TryGetComponent(out hand);
	}

	private void OnEnable()
	{
		hand.Bind(nameof(OnFire), OnFire);
	}

	private void OnDisable()
	{
		hand.Unbind(nameof(OnFire), OnFire);
	}

	public void OnFire(InputAction.CallbackContext context)
	{
		if (context.performed && context.ReadValueAsButton())
			Fire();
	}

	public void Fire()
	{
		if (!NetworkManager.Singleton.IsConnectedClient || !WeaponsManagement.CanFire)
			return;

		for (int i = 0; i < numBullets; i++)
		{
			float r = Random.Range(0, Mathf.PI * 2);
			float s = Mathf.Sin(r);
			float c = Mathf.Cos(r);
			
			float spread = Random.Range(0, bulletSpread);
			
			Vector3 offs = (muzzle.up * s + muzzle.right * c) * spread;
			Vector3 v = muzzle.forward + offs;
			
			Quaternion rot = Quaternion.LookRotation(v);
			
			NetworkObject n = NetworkObjectPool.Instance.GetNetworkObject(
				boltPrefab, muzzle.position, rot);
			
			n.SpawnWithOwnership(NetworkManager.Singleton.LocalClientId);
		}

		visual.PlayFire();
		onFire.Invoke();
	}
}
