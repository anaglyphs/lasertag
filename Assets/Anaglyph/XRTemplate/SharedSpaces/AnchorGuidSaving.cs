using System.Collections.Generic;
using System;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
    public static class AnchorGuidSaving
    {
		public const string AnchorsSaveFileName = "SavedAnchors.json";
		private static void Log(string str) => Debug.Log($"[{nameof(AnchorGuidSaving)}] {str}");

		[Serializable]
		public struct SavedAnchors
		{
			public List<string> guidStrings;
		}

		public static SavedAnchors GetSavedAnchors()
		{
			Log($"Reading saved anchor uuids...");
			GameSave.ReadFile(AnchorsSaveFileName, out SavedAnchors localAnchors);
			if (localAnchors.guidStrings == null)
				localAnchors.guidStrings = new();
			return localAnchors;
		}

		public static void AddGuid(Guid uuid)
		{
			string uuidString = uuid.ToString();
			SavedAnchors localAnchors = GetSavedAnchors();

			if(localAnchors.guidStrings == null)
				localAnchors.guidStrings = new();

			if (!localAnchors.guidStrings.Contains(uuidString))
			{
				Log($"Saving anchor {uuidString} to file...");
				localAnchors.guidStrings.Add(uuidString);
				GameSave.WriteFile(AnchorsSaveFileName, localAnchors);
				Log($"Saved anchor {uuidString} to file!");
			}
		}
	}
}
