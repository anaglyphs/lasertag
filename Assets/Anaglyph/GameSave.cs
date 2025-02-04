using System.IO;
using UnityEngine;

namespace Anaglyph
{
	public static class GameSave
	{
		public static void ReadFile<T>(string nameAndExtension, out T obj) where T : struct
		{
			if (File.Exists(GetPath(nameAndExtension)))
			{
				string fileContents = File.ReadAllText(nameAndExtension);
				obj = JsonUtility.FromJson<T>(fileContents);
				return;
			}

			obj = new();
		}

		public static void WriteFile<T>(string nameAndExtension, T obj) where T : struct
		{
			string jsonString = JsonUtility.ToJson(obj);
			File.WriteAllText(GetPath(nameAndExtension), jsonString);
		}

		private static string GetPath(string nameAndExtension)
		{
			return Path.Combine(Application.persistentDataPath, nameAndExtension);
		}
	}
}
