#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

public class GlobalTextureViewer : EditorWindow
{
	private string texturePropertyName = "agDepthTex";
	private Texture globalTexture;
	private int slice;

	private RenderTexture previewRT;

	[MenuItem("Window/Lasertag/Global Texture Viewer")]
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
		if (previewRT != null)
		{
			previewRT.Release();
			DestroyImmediate(previewRT);
			previewRT = null;
		}
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

		if (globalTexture == null)
		{
			EditorGUILayout.HelpBox("No global texture bound to that property name.", MessageType.Info);
			return;
		}

		// How many slices can we scrub through?
		int sliceCount = SliceCount(globalTexture);
		if (sliceCount > 1)
			slice = EditorGUILayout.IntSlider($"Slice (0..{sliceCount - 1})", Mathf.Clamp(slice, 0, sliceCount - 1), 0,
				sliceCount - 1);
		else
			slice = 0;

		EditorGUILayout.LabelField($"Dimension: {globalTexture.dimension}");
		EditorGUILayout.LabelField($"Resolution: {globalTexture.width} x {globalTexture.height}");
		EditorGUILayout.Space();

		// Resolve the (possibly array/3D) texture down to a plain 2D RenderTexture we can draw.
		Texture drawable;
		try
		{
			drawable = ResolveDrawable(globalTexture, slice);
		}
		catch (System.Exception e)
		{
			EditorGUILayout.HelpBox($"Could not preview this texture:\n{e.Message}", MessageType.Error);
			return;
		}

		if (drawable == null)
			return;

		float aspect = (float)globalTexture.width / globalTexture.height;
		float previewWidth = Mathf.Min(position.width - 20f, 512f);
		float previewHeight = previewWidth / aspect;

		Rect rect = GUILayoutUtility.GetRect(previewWidth, previewHeight, GUILayout.ExpandWidth(false));
		EditorGUI.DrawPreviewTexture(rect, drawable);
	}

	private static int SliceCount(Texture tex)
	{
		switch (tex.dimension)
		{
			case TextureDimension.Tex2DArray:
				return tex is RenderTexture rt ? rt.volumeDepth : (tex as Texture2DArray)?.depth ?? 1;
			case TextureDimension.Tex3D:
				return tex is RenderTexture rt3 ? rt3.volumeDepth : (tex as Texture3D)?.depth ?? 1;
			default:
				return 1;
		}
	}

	// Copies the requested slice into a format-matched 2D RenderTexture so DrawPreviewTexture can show it.
	private Texture ResolveDrawable(Texture tex, int sliceIndex)
	{
		// Plain 2D textures can be drawn directly.
		if (tex.dimension == TextureDimension.Tex2D)
			return tex;

		// Build a 2D RenderTexture whose format MATCHES the source. CopyTexture is a raw
		// GPU memory copy (no sampler/shader), so the formats must be identical or it fails.
		GraphicsFormat fmt = tex.graphicsFormat;
		if (previewRT == null || previewRT.width != tex.width || previewRT.height != tex.height
		    || previewRT.graphicsFormat != fmt)
		{
			if (previewRT != null)
			{
				previewRT.Release();
				DestroyImmediate(previewRT);
			}

			previewRT = new RenderTexture(tex.width, tex.height, 0, fmt, 1)
			{
				dimension = TextureDimension.Tex2D
			};
			previewRT.Create();
		}

		// Copy just the requested slice (array element / 3D depth slice) into the 2D target.
		// Unlike Blit, this does not try to sample an array through a sampler2D, so it
		// won't come back white/black.
		Graphics.CopyTexture(tex, sliceIndex, 0, previewRT, 0, 0);
		return previewRT;
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