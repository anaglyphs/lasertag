#if UNITY_EDITOR
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Anaglyph.DepthKit.EnvScanning
{
	/// <summary>
	/// Debug window for viewing <see cref="EnvScanner.ChunkData"/>.
	///
	/// Both modes drive Unity's stock object preview via Editor.CreateEditor:
	///  - Single chunk: one chunk's 32^3 TSDF copied into a Texture3D (via the existing ReadbackChunk
	///    path), shown with the Texture3D inspector's Volume / SDF / Slice preview. Correctly scaled,
	///    looks like a real SDF cube.
	///  - Full atlas: the live chunkData RenderTexture previewed directly with the RenderTexture
	///    inspector's volume view - no copy needed, updates every scan. Note this is an ATLAS, not the
	///    scene: up to MaxNumChunks independent 32^3 blobs gridded into the texture, not the room.
	///
	/// Play mode only; chunkData is allocated at runtime in EnvScanner2.Setup().
	/// </summary>
	public class EnvScanner2DebugWindow : EditorWindow
	{
		private enum ViewMode
		{
			SingleChunk,
			FullAtlas
		}

		private ViewMode mode = ViewMode.SingleChunk;
		private int chunkIndex;
		private bool followSelection = true;
		private bool autoRefresh = true;
		private const double RefreshInterval = 0.25;
		private double lastRefreshTime;

		// CPU-fed copy of one chunk's voxels, kept alive so the chunk preview editor keeps its state.
		// Atlas mode previews the live RenderTexture directly, so it needs no copy.
		private Texture3D chunkTex; // voxPerChunkDim^3

		private Editor chunkEditor; // stock Texture3DInspector over chunkTex
		private Editor atlasEditor; // stock RenderTextureEditor over the live scanner.ChunkData

		private ComputeBuffer chunkReadbackBuffer;
		private bool refreshing;
		private string status = "";

		[MenuItem("Window/Lasertag/Scan Data Viewer")]
		public static void ShowWindow()
		{
			GetWindow<EnvScanner2DebugWindow>("Scan Viewer");
		}

		private void OnEnable()
		{
			EditorApplication.update += OnEditorUpdate;
			EditorApplication.playModeStateChanged += OnPlayModeChanged;
		}

		private void OnDisable()
		{
			EditorApplication.update -= OnEditorUpdate;
			EditorApplication.playModeStateChanged -= OnPlayModeChanged;
			Cleanup();
		}

		private void OnPlayModeChanged(PlayModeStateChange change)
		{
			// chunkData is freed when play mode ends; drop our copies so we don't show stale data
			if (change == PlayModeStateChange.ExitingPlayMode)
				Cleanup();
		}

		private void Cleanup()
		{
			// note: atlasEditor targets the scanner's RenderTexture, which we do NOT own - destroying
			// the editor wrapper is correct, but we must not touch the texture itself
			if (chunkEditor != null)
			{
				DestroyImmediate(chunkEditor);
				chunkEditor = null;
			}

			if (atlasEditor != null)
			{
				DestroyImmediate(atlasEditor);
				atlasEditor = null;
			}

			if (chunkTex != null)
			{
				DestroyImmediate(chunkTex);
				chunkTex = null;
			}

			chunkReadbackBuffer?.Dispose();
			chunkReadbackBuffer = null;
		}

		private void OnEditorUpdate()
		{
			if (!Application.isPlaying) return;

			if (autoRefresh && !refreshing &&
			    EditorApplication.timeSinceStartup - lastRefreshTime > RefreshInterval)
				Refresh();

			Repaint(); // keep the orbit / latest data live
		}

		private void OnGUI()
		{
			EnvScanner scanner = Application.isPlaying ? EnvScanner.Instance : null;

			using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
			{
				mode = (ViewMode)EditorGUILayout.EnumPopup(mode, EditorStyles.toolbarPopup, GUILayout.Width(110));
				autoRefresh = GUILayout.Toggle(autoRefresh, "Auto", EditorStyles.toolbarButton, GUILayout.Width(48));
				if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
					Refresh();
				GUILayout.FlexibleSpace();
				GUILayout.Label(status, EditorStyles.miniLabel);
			}

			if (scanner == null || scanner.ChunkData == null)
			{
				EditorGUILayout.HelpBox("Enter play mode with an active EnvScanner2 to view chunk data.",
					MessageType.Info);
				return;
			}

			if (mode == ViewMode.SingleChunk)
				DrawChunkControls(scanner);
			else
				DrawAtlasInfo(scanner);

			Editor previewEditor = GetActiveEditor(scanner);
			if (previewEditor == null || !previewEditor.HasPreviewGUI())
			{
				EditorGUILayout.HelpBox("Waiting for voxel data...", MessageType.None);
				return;
			}

			// stock preview header: Texture3D's Volume/SDF/Slice dropdown, or the RenderTexture's controls
			using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
			{
				GUILayout.FlexibleSpace();
				previewEditor.OnPreviewSettings();
			}

			// fill the rest of the window with the interactive (orbitable) raymarch preview.
			// NB: the min height must stay small - the preview allocates a RenderTexture the size of
			// this rect, so a large min (times retina scale) blows past maxRenderTextureSize.
			// ExpandHeight already grows it to fill the remaining window space.
			Rect r = GUILayoutUtility.GetRect(64, 64,
				GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
			previewEditor.OnInteractivePreviewGUI(r, GUIStyle.none);
		}

		private void DrawChunkControls(EnvScanner scanner)
		{
			if (followSelection && Selection.activeGameObject != null &&
			    Selection.activeGameObject.TryGetComponent(out Chunk selected))
				chunkIndex = selected.chunkIndex;

			using (new EditorGUILayout.HorizontalScope())
			{
				using (new EditorGUI.DisabledScope(followSelection))
				{
					chunkIndex = Mathf.Clamp(EditorGUILayout.IntField("Chunk Index", chunkIndex),
						0, scanner.ChunkTableLength - 1);
				}

				followSelection = GUILayout.Toggle(followSelection, "Follow scene selection",
					GUILayout.Width(150));
			}

			int3 coord = scanner.ChunkIndexToChunkCoord(chunkIndex);
			EditorGUILayout.LabelField("Chunk coord", coord.ToString());
		}

		private void DrawAtlasInfo(EnvScanner scanner)
		{
			RenderTexture cd = scanner.ChunkData;
			EditorGUILayout.LabelField("Atlas",
				$"{cd.width}x{cd.height}x{cd.volumeDepth} - {scanner.MaxNumChunks} slots of {scanner.VoxPerChunkDim}^3");
			EditorGUILayout.HelpBox("This is the packed chunk pool, not the scene. Each populated " +
			                        "slot is an independent chunk's local TSDF.", MessageType.None);
			status = "Atlas (live)";
		}

		// Lazily (re)creates the stock object preview for the active mode's texture. Keyed on the
		// texture instance so swapping modes or rebuilding a texture gets a fresh, correctly-targeted editor.
		private Editor GetActiveEditor(EnvScanner scanner)
		{
			if (mode == ViewMode.SingleChunk)
				return GetEditor(chunkTex, ref chunkEditor);
			// preview the live 3D RenderTexture directly - its inspector already draws the volume view,
			// and sampling the GPU texture means it updates every scan with no copy or readback
			return GetEditor(scanner.ChunkData, ref atlasEditor);
		}

		private Editor GetEditor(Texture tex, ref Editor editor)
		{
			if (tex == null) return null;
			if (editor == null || editor.target != tex)
			{
				if (editor != null) DestroyImmediate(editor);
				editor = Editor.CreateEditor(tex);
			}

			return editor;
		}

		private async void Refresh()
		{
			if (refreshing || !Application.isPlaying) return;
			// atlas mode previews the live RenderTexture directly, so only the chunk copy needs refreshing
			if (mode != ViewMode.SingleChunk) return;

			EnvScanner scanner = EnvScanner.Instance;
			if (scanner == null || scanner.ChunkData == null) return;

			refreshing = true;
			lastRefreshTime = EditorApplication.timeSinceStartup;
			try
			{
				await RefreshChunk(scanner);
			}
			catch (System.Exception e)
			{
				status = e.Message;
			}
			finally
			{
				refreshing = false;
			}
		}

		private async Task RefreshChunk(EnvScanner scanner)
		{
			int vpcd = scanner.VoxPerChunkDim;
			EnsureTex(ref chunkTex, vpcd, vpcd, vpcd, scanner.ChunkData.graphicsFormat);
			chunkReadbackBuffer ??= scanner.CreateChunkReadbackBuffer();

			// ReadbackChunk packs voxels as x + y*dim + z*dim^2, exactly Texture3D's expected order
			EnvScanner.ChunkDataReadbackResult res = await scanner.ReadbackChunk(chunkIndex, chunkReadbackBuffer);
			if (this == null || chunkTex == null) return; // window closed mid-readback
			if (!res.valid)
			{
				status = "Readback invalid";
				return;
			}

			chunkTex.SetPixelData(res.data, 0);
			chunkTex.Apply(false);
			status = $"Chunk {chunkIndex}";
		}

		private void EnsureTex(ref Texture3D tex, int w, int h, int d, GraphicsFormat fmt)
		{
			if (tex != null && (tex.width != w || tex.height != h || tex.depth != d ||
			                    tex.graphicsFormat != fmt))
			{
				DestroyImmediate(tex);
				tex = null;
			}

			if (tex == null)
				tex = new Texture3D(w, h, d, fmt, TextureCreationFlags.None)
				{
					name = "EnvScanner2 Debug",
					hideFlags = HideFlags.HideAndDontSave,
					filterMode = FilterMode.Point,
					wrapMode = TextureWrapMode.Clamp
				};
		}
	}
}
#endif