using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.LaserTag.Weapons.Bullets
{
	// One light shared by a shot's live bullets. Each client forms the group locally
	// from the bullets' synced shot data, so the group has no network state of its own.
	[DefaultExecutionOrder(100)] // reads member positions after BulletVisuals moves them
	public class BulletGroupLight : MonoBehaviour
	{
		private static readonly Dictionary<(ulong owner, ushort id), BulletGroupLight> activeGroups = new();
		private static readonly Stack<BulletGroupLight> idleGroups = new();

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			activeGroups.Clear();
			idleGroups.Clear();
		}

		private readonly List<Transform> liveMembers = new();
		private (ulong owner, ushort id) key;
		private Light groupLight;
		private int groupSize;
		private float intensityPerMember;
		private float fullGroupRange;

		public static BulletGroupLight Join(ulong owner, ushort id, int groupSize, Light memberLight)
		{
			var key = (owner, id);
			if (!activeGroups.TryGetValue(key, out BulletGroupLight group))
			{
				group = idleGroups.Count > 0 ? idleGroups.Pop() : Create(memberLight);
				group.Begin(key, groupSize, memberLight);
				activeGroups.Add(key, group);
			}

			group.liveMembers.Add(memberLight.transform);
			return group;
		}

		private static BulletGroupLight Create(Light memberLight)
		{
			GameObject lightObject = Instantiate(memberLight.gameObject);
			lightObject.name = "Bullet Group Light";
			DontDestroyOnLoad(lightObject);

			BulletGroupLight group = lightObject.AddComponent<BulletGroupLight>();
			group.groupLight = lightObject.GetComponent<Light>();
			return group;
		}

		private void Begin((ulong owner, ushort id) groupKey, int size, Light memberLight)
		{
			key = groupKey;
			groupSize = size;
			intensityPerMember = memberLight.intensity;
			fullGroupRange = memberLight.range;
			groupLight.color = memberLight.color;
			groupLight.enabled = true;
			gameObject.SetActive(true);
		}

		public void Leave(Transform member)
		{
			liveMembers.Remove(member);
			if (liveMembers.Count > 0) return;

			activeGroups.Remove(key);
			idleGroups.Push(this);
			gameObject.SetActive(false);
		}

		private void LateUpdate()
		{
			Vector3 positionSum = Vector3.zero;
			foreach (Transform member in liveMembers)
				positionSum += member.position;

			int liveCount = liveMembers.Count;
			transform.position = positionSum / liveCount;
			groupLight.intensity = intensityPerMember * liveCount;
			groupLight.range = fullGroupRange * liveCount / groupSize;
		}
	}
}
