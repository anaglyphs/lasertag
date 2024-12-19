using Anaglyph.XRTemplate;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class EnvironmentHeightPlane : MonoBehaviour
{
	private MeshFilter meshFilter;

	[SerializeField] private int verticesPerSide;

	private void Awake()
	{
		meshFilter = GetComponent<MeshFilter>();
	}

	private void Start()
	{
		GeneratePlaneMesh(verticesPerSide, EnvironmentMapper.Instance.EnvironmentSize);
	}

	// Function to create a subdivided plane
	private void GeneratePlaneMesh(int verticesPerSide, float size)
	{
		Mesh mesh = new Mesh();

		mesh.indexFormat = IndexFormat.UInt32;

		// Vertices and UVs
		Vector3[] vertices = new Vector3[verticesPerSide * verticesPerSide];
		Vector2[] uvs = new Vector2[vertices.Length];

		float step = size / (verticesPerSide - 1);
		for (int y = 0; y < verticesPerSide; y++)
		{
			for (int x = 0; x < verticesPerSide; x++)
			{
				int index = x + y * verticesPerSide;
				vertices[index] = new Vector3(x * step - size / 2, 0, y * step - size / 2);
				uvs[index] = new Vector2((float)x / (verticesPerSide - 1), (float)y / (verticesPerSide - 1));
			}
		}

		// Triangles
		int[] triangles = new int[(verticesPerSide - 1) * (verticesPerSide - 1) * 6];
		int t = 0;
		for (int y = 0; y < verticesPerSide - 1; y++)
		{
			for (int x = 0; x < verticesPerSide - 1; x++)
			{
				int i = x + y * verticesPerSide;

				triangles[t++] = i;
				triangles[t++] = i + verticesPerSide;
				triangles[t++] = i + 1;

				triangles[t++] = i + 1;
				triangles[t++] = i + verticesPerSide;
				triangles[t++] = i + verticesPerSide + 1;
			}
		}

		mesh.vertices = vertices;
		mesh.uv = uvs;
		mesh.triangles = triangles;
		mesh.RecalculateNormals();
		mesh.MarkModified();

		meshFilter.mesh = mesh;
	}
}