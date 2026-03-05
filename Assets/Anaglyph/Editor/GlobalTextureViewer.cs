#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public class GlobalTextureViewer : EditorWindow
{
	private string texturePropertyName = "_GlobalTexture";
	private Texture globalTexture;

	[MenuItem("Window/Global Texture Viewer")]
	public static void ShowWindow()
	{
		GetWindow<GlobalTextureViewer>("Global Texture Viewer");
	}

	private void OnEnable()
	{
		EditorApplication.update += Update;
	}

	private void OnDisable()
	{
		EditorApplication.update -= Update;
	}

	private void Update()
	{
		RefreshTexture();
		Repaint();
	}

	private void OnGUI()
	{
		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Global Texture Viewer", EditorStyles.boldLabel);

		EditorGUI.BeginChangeCheck();
		texturePropertyName = EditorGUILayout.TextField("Texture Property Name", texturePropertyName);
		if (EditorGUI.EndChangeCheck()) RefreshTexture();

		EditorGUILayout.Space();

		if (globalTexture != null)
		{
			float aspect = (float)globalTexture.width / globalTexture.height;
			float previewWidth = Mathf.Min(position.width - 20f, 512f);
			float previewHeight = previewWidth / aspect;

			Rect rect = GUILayoutUtility.GetRect(previewWidth, previewHeight, GUILayout.ExpandWidth(false));
			EditorGUI.DrawPreviewTexture(rect, globalTexture);

			EditorGUILayout.LabelField($"Resolution: {globalTexture.width} x {globalTexture.height}");
		}
		else
		{
			EditorGUILayout.HelpBox("No global texture found with that property name.", MessageType.Info);
		}
	}

	private void RefreshTexture()
	{
		if (string.IsNullOrEmpty(texturePropertyName))
		{
			globalTexture = null;
			return;
		}

		int propertyID = Shader.PropertyToID(texturePropertyName);
		globalTexture = Shader.GetGlobalTexture(propertyID);
	}
}
#endif