#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Anaglyph.LaserTag.Editor
{
	/// <summary>The XR Simulation environment play mode opens, read and set through its preferences asset.</summary>
	public static class SimEnvironmentSetting
	{
		private const string PreferencesPath = "Assets/XR/UserSimulationSettings/Resources/XRSimulationPreferences.asset";
		private const string EnvironmentsFolder = "Assets/Anaglyph/LaserTag/SimulatedEnvironments";
		private const string EnvironmentField = "m_EnvironmentPrefab";

		/// <summary>The environment's prefab path, empty when none is set.</summary>
		public static string Current
		{
			get
			{
				UnityEngine.Object preferences = AssetDatabase.LoadMainAssetAtPath(PreferencesPath);
				if (preferences == null) return "";
				UnityEngine.Object prefab = new SerializedObject(preferences).FindProperty(EnvironmentField).objectReferenceValue;
				return prefab ? AssetDatabase.GetAssetPath(prefab) : "";
			}
		}

		/// <summary>Sets the environment the next play mode opens, by prefab path. Returns why not, or null.</summary>
		public static string Set(string prefabPath)
		{
			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
			if (prefab == null) return $"no prefab at {prefabPath}";
			return Assign(prefab);
		}

		/// <summary>Removes the environment, so play mode opens AR Foundation's fallback. Returns why not, or null.</summary>
		public static string Clear() => Assign(null);

		private static string Assign(GameObject prefab)
		{
			UnityEngine.Object preferences = AssetDatabase.LoadMainAssetAtPath(PreferencesPath);
			if (preferences == null) return $"no XR Simulation preferences at {PreferencesPath}";

			SerializedObject fields = new(preferences);
			fields.FindProperty(EnvironmentField).objectReferenceValue = prefab;
			if (fields.ApplyModifiedPropertiesWithoutUndo()) AssetDatabase.SaveAssetIfDirty(preferences);
			return null;
		}

		/// <summary>
		/// A prefab path from a path, or from part of one under the environments folder ("crypt",
		/// "sophia"). Only prefabs carrying a baked nav-mesh count, since the walkers need one.
		/// </summary>
		public static bool TryFind(string nameOrPath, out string prefabPath, out string problem)
		{
			prefabPath = null;
			problem = null;
			if (AssetDatabase.LoadAssetAtPath<GameObject>(nameOrPath) != null)
			{
				prefabPath = nameOrPath;
				return true;
			}

			string[] matches = AssetDatabase.FindAssets("t:Prefab", new[] { EnvironmentsFolder })
				.Select(AssetDatabase.GUIDToAssetPath)
				.Where(path => path.IndexOf(nameOrPath, StringComparison.OrdinalIgnoreCase) >= 0 && HasNavMesh(path))
				.ToArray();
			if (matches.Length == 1)
			{
				prefabPath = matches[0];
				return true;
			}

			problem = matches.Length == 0
				? $"no environment with a nav-mesh matches \"{nameOrPath}\""
				: $"\"{nameOrPath}\" matches {matches.Length} environments: {string.Join(", ", matches)}";
			return false;
		}

		// a NavMeshSurface stores its baked data in this field, and a bare scan prefab has none
		private static bool HasNavMesh(string prefabPath) =>
			File.ReadLines(prefabPath).Any(line => line.Contains("m_NavMeshData: {fileID: ") && !line.Contains("m_NavMeshData: {fileID: 0}"));
	}
}
#endif
