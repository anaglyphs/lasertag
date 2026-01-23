using System;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	[CreateAssetMenu(fileName = "GameObjectMenu", menuName = "Lasertag/GameObjectMenu")]
	public class GameObjectMenu : ScriptableObject
	{
		[Serializable]
		public struct Entry
		{
			public string name;
			public Sprite icon;
			public GameObject prefab;
		}

		public Entry[] entries;
	}
}