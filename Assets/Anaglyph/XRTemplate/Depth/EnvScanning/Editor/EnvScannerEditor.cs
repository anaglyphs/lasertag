#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Anaglyph.DepthKit.EnvScanning
{
	/// <summary>
	/// Disclosure: fully written by Claude Code
	/// Custom inspector for <see cref="EnvScanner"/> that estimates the GPU memory the
	/// chunkTable ComputeBuffer and chunkData RenderTexture will allocate for the currently
	/// entered dimension parameters. Mirrors the allocations in EnvScanner.Setup() and
	/// updates live as the fields are edited.
	/// </summary>
	[CustomEditor(typeof(EnvScanner))]
	public class EnvScannerEditor : Editor
	{
		// chunkTable stores one int (chunk slot index) per cell of the chunk table grid
		private const int ChunkTableBytesPerCell = sizeof(int);

		public override void OnInspectorGUI()
		{
			DrawDefaultInspector();

			SerializedProperty voxPerChunkDimProp = serializedObject.FindProperty("voxPerChunkDim");
			SerializedProperty chunkTableDimsProp = serializedObject.FindProperty("chunkTableDims");
			SerializedProperty chunkDataDimsProp = serializedObject.FindProperty("chunkDataDims");

			if (voxPerChunkDimProp == null || chunkTableDimsProp == null || chunkDataDimsProp == null)
				return;

			int voxPerChunkDim = voxPerChunkDimProp.intValue;
			Vector3Int chunkTableDims = ReadInt3(chunkTableDimsProp);
			Vector3Int chunkDataDims = ReadInt3(chunkDataDimsProp);

			// chunkTable: one int per cell of the chunkTableDims grid
			long chunkTableCells = (long)chunkTableDims.x * chunkTableDims.y * chunkTableDims.z;
			long chunkTableBytes = chunkTableCells * ChunkTableBytesPerCell;

			// chunkData: a 3D atlas of chunkDataDims slots, each voxPerChunkDim^3 voxels, R8G8_SNorm
			long dataBytesPerVoxel = GraphicsFormatUtility.GetBlockSize(GraphicsFormat.R8G8_SNorm);
			long dataWidth = (long)chunkDataDims.x * voxPerChunkDim;
			long dataHeight = (long)chunkDataDims.y * voxPerChunkDim;
			long dataDepth = (long)chunkDataDims.z * voxPerChunkDim;
			long chunkDataBytes = dataWidth * dataHeight * dataDepth * dataBytesPerVoxel;

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Estimated GPU Memory", EditorStyles.boldLabel);

			EditorGUILayout.LabelField("chunkTable buffer",
				$"{FormatBytes(chunkTableBytes)}  ({chunkTableCells:N0} cells x {ChunkTableBytesPerCell} B)");

			EditorGUILayout.LabelField("chunkData texture",
				$"{FormatBytes(chunkDataBytes)}  ({dataWidth}x{dataHeight}x{dataDepth} x {dataBytesPerVoxel} B)");

			EditorGUILayout.LabelField("Total", FormatBytes(chunkTableBytes + chunkDataBytes));
			if (GUILayout.Button(new GUIContent("Open Scan Viewer", "Open the scan viewer debug window"),
				    EditorStyles.miniButton))
			{
				EnvScannerDebugWindow.ShowWindow();
			}
		}

		private static Vector3Int ReadInt3(SerializedProperty int3Prop)
		{
			return new Vector3Int(
				int3Prop.FindPropertyRelative("x").intValue,
				int3Prop.FindPropertyRelative("y").intValue,
				int3Prop.FindPropertyRelative("z").intValue);
		}

		private static string FormatBytes(long bytes)
		{
			const long kb = 1024;
			const long mb = kb * 1024;
			const long gb = mb * 1024;

			if (bytes >= gb) return $"{bytes / (double)gb:0.##} GB";
			if (bytes >= mb) return $"{bytes / (double)mb:0.##} MB";
			if (bytes >= kb) return $"{bytes / (double)kb:0.##} KB";
			return $"{bytes} B";
		}
	}
}
#endif
