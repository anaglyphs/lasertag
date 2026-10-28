#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Anaglyph.Editor
{
	/// <summary>
	/// Builds Addressables content before a player build only when its inputs changed.
	/// Rebuilding it with every player build costs 3–90 s, mostly recompiling player
	/// scripts, even though the content (localization tables) rarely changes.
	///
	/// Active only while Addressables is set to not build with the player. Runs before
	/// Addressables' own processor (order 1), which still adds the content to the player.
	/// </summary>
	public class AddressablesContentBuild : BuildPlayerProcessor
	{
		public override int callbackOrder => 0;

		private static string HashPath => Addressables.BuildPath + ".contenthash";

		public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
		{
			var settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null || settings.BuildAddressablesWithPlayerBuild !=
			    AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer)
				return;

			string hash = ContentHash(settings);
			bool built = File.Exists(Path.Combine(Addressables.BuildPath, "settings.json"));
			if (built && File.Exists(HashPath) && File.ReadAllText(HashPath) == hash)
				return;

			Debug.Log("[AddressablesContentBuild] Content changed, building Addressables");
			AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
			if (!string.IsNullOrEmpty(result.Error))
				throw new BuildFailedException("Addressables content build failed: " + result.Error);

			File.WriteAllText(HashPath, hash);
		}

		/// <summary>
		/// Every asset the content is built from, its dependencies, the Addressables
		/// settings and groups, the build target, and package versions (serialized
		/// content can depend on package types).
		/// </summary>
		private static string ContentHash(AddressableAssetSettings settings)
		{
			var paths = new List<string> { AssetDatabase.GetAssetPath(settings) };
			var entries = new List<AddressableAssetEntry>();
			foreach (var group in settings.groups)
			{
				if (group == null)
					continue;
				paths.Add(AssetDatabase.GetAssetPath(group));
				foreach (var schema in group.Schemas)
					paths.Add(AssetDatabase.GetAssetPath(schema));
				foreach (var entry in group.entries)
					entry.GatherAllAssets(entries, true, true, false);
			}

			foreach (var entry in entries)
				paths.Add(entry.AssetPath);

			var dependencies = new SortedSet<string>(AssetDatabase.GetDependencies(paths.ToArray(), true));
			dependencies.UnionWith(paths);

			var hash = new Hash128();
			hash.Append(EditorUserBuildSettings.activeBuildTarget.ToString());
			hash.Append(settings.activeProfileId);
			hash.Append(File.ReadAllText("Packages/packages-lock.json"));
			foreach (string path in dependencies)
			{
				if (string.IsNullOrEmpty(path))
					continue;
				hash.Append(path);
				hash.Append(AssetDatabase.GetAssetDependencyHash(path).ToString());
			}

			return hash.ToString();
		}
	}
}
#endif
