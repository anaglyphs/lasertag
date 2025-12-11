using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.LaserTag.NPCs
{
	public class ComputeNavMeshButton : MonoBehaviour
	{
		private void Awake()
		{
			TryGetComponent(out Button b);
			b.onClick.AddListener(delegate
			{
				NavMeshSurface s = FindAnyObjectByType<NavMeshSurface>();
				s.UpdateNavMesh(s.navMeshData);
			});
		}
	}
}