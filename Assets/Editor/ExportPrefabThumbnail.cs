using UnityEditor;
using UnityEngine;
using System.IO;

public static class ThumbnailSaver
{
	[MenuItem("Assets/Save Prefab Thumbnail")]
	private static void ExportPrefabThumbnail()
	{
		Object selectedObject = Selection.activeObject;

		//Check if the selected object is a prefab
		if (selectedObject != null && PrefabUtility.GetPrefabAssetType(selectedObject) != PrefabAssetType.MissingAsset)
		{
			GameObject prefab = (GameObject)selectedObject;

			//Generate the prefab thumbnail texture
			Texture2D thumbnailTexture = AssetPreview.GetAssetPreview(prefab);
			if (thumbnailTexture != null)
			{
				//Create a new texture with transparent background
				Texture2D transparentTexture = new Texture2D(thumbnailTexture.width, thumbnailTexture.height, TextureFormat.RGBA32, false);
				Color[] pixels = thumbnailTexture.GetPixels();

				//Get the background color
				Color backgroundColor = pixels[0];
				for (int i = 0; i < pixels.Length; i++)
				{
					if (pixels[i] == backgroundColor) pixels[i].a = 0; //If this pixel is exactly the background color, make it transparent
				}
				transparentTexture.SetPixels(pixels);
				transparentTexture.Apply();

				//Save the texture to a user specified folder
				string savePath = EditorUtility.SaveFilePanel("Save Prefab Thumbnail", "", prefab.name + ".png", "png");
				if (!string.IsNullOrEmpty(savePath))
				{
					byte[] pngData = transparentTexture.EncodeToPNG();
					File.WriteAllBytes(savePath, pngData);

					Debug.Log("Prefab thumbnail saved: " + savePath);
				}
			}
			else
			{
				Debug.LogWarning("Could not generate thumbnail for the selected prefab.");
			}
		}
	}

	[MenuItem("Assets/Save Prefab Thumbnail", true)]
	private static bool ValidateSavePrefabThumbnail()
	{
		GameObject selectedPrefab = Selection.activeObject as GameObject;
		return selectedPrefab != null && PrefabUtility.IsPartOfPrefabAsset(selectedPrefab);
	}
}