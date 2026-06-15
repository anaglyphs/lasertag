//using System.Collections.Generic;
//using System;
//using UnityEngine;

//namespace Anaglyph.XRTemplate.SharedSpaces
//{
//    public static class AnchorGuidSaving
//    {
//		public const string AnchorsSaveFileName = "SavedAnchors.json";
//		private static void Log(string str) => Debug.Log($"[{nameof(AnchorGuidSaving)}] {str}");

//		[Serializable]
//		public struct SavedAnchorGuids
//		{
//			public List<string> guidStrings;
//		}

//		public static SavedAnchorGuids LoadSavedGuids()
//		{
//			Log($"Reading saved anchor uuids...");
//			GameSave.ReadFile(AnchorsSaveFileName, out SavedAnchorGuids localAnchors);
//			if (localAnchors.guidStrings == null)
//				localAnchors.guidStrings = new();
//			return localAnchors;
//		}

//		public static void OverwriteSavedGuids(SavedAnchorGuids savedAnchors)
//		{
//			GameSave.WriteFile(AnchorsSaveFileName, savedAnchors);
//			Log($"Overwrote guid history");
//		}

//		public static void DeleteSavedAnchors()
//		{
//			SavedAnchorGuids saved = LoadSavedGuids();
//			Guid[] guids = new Guid[saved.guidStrings.Count];

//			for (int i = 0; i < saved.guidStrings.Count; i++)
//			{
//				string guidString = saved.guidStrings[i];
//				Guid guid = Guid.Parse(guidString);
//			}

//			OVRAnchor.EraseAsync(null, guids);

//			SavedAnchorGuids empty = new()
//			{
//				guidStrings = new()
//			};

//			OverwriteSavedGuids(empty);
//		}

//		public static void AddAndSaveGuid(Guid uuid)
//		{
//			string uuidString = uuid.ToString();
//			SavedAnchorGuids localAnchors = LoadSavedGuids();

//			if(localAnchors.guidStrings == null)
//				localAnchors.guidStrings = new();

//			if (!localAnchors.guidStrings.Contains(uuidString))
//			{
//				localAnchors.guidStrings.Add(uuidString);
//				OverwriteSavedGuids(localAnchors);
//				Log($"Saved anchor {uuidString} to file!");
//			}
//		}
//	}
//}
