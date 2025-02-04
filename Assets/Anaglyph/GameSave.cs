using System.IO;
using UnityEngine;

namespace Anaglyph
{
	public static class GameSave
	{
		public static void ReadFile<T>(string nameAndExtension, out T obj) where T : struct
		{
			string path = GetPath(nameAndExtension);
			if (File.Exists(path))
			{
				string fileContents = File.ReadAllText(path);
				obj = JsonUtility.FromJson<T>(fileContents);
				return;
			}

			obj = new();
		}

		public static void WriteFile<T>(string nameAndExtension, T obj) where T : struct
		{
			string jsonString = JsonUtility.ToJson(obj);
			string path = GetPath(nameAndExtension);
			File.WriteAllText(path, jsonString);
		}

		private static string GetPath(string nameAndExtension)
		{
			return Path.Combine(Application.persistentDataPath, nameAndExtension);
		}
	}
}
