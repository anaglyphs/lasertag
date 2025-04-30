using UnityEngine;

namespace Anaglyph.Zombies
{
    public class ZombieTarget : MonoBehaviour
    {
		private void Awake()
		{
			Zombie.targets.Add(this);
		}

		private void OnDestroy()
		{
			Zombie.targets.Remove(this);
		}
	}
}
