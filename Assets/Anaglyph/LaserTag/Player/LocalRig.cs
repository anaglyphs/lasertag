using UnityEngine;

namespace Anaglyph.Lasertag
{
	/// <summary>
	/// The local player's tracked transforms. Whoever needs to follow the player reads
	/// these - nothing here knows what is following them.
	/// </summary>
	public class LocalRig : MonoBehaviour
	{
		public static LocalRig Instance { get; private set; }

		[SerializeField] private Transform head;
		[SerializeField] private Transform leftHand;
		[SerializeField] private Transform rightHand;

		public Transform Head => head;
		public Transform LeftHand => leftHand;
		public Transform RightHand => rightHand;

		private void Awake()
		{
			Instance = this;
		}

		private void OnDestroy()
		{
			if (Instance == this)
				Instance = null;
		}
	}
}
