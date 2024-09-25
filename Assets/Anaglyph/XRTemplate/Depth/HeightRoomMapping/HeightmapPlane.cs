using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Burst;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class HeightmapPlane : MonoBehaviour
{
	public int subdivisions = 10; // Number of subdivisions along each axis
	public float planeSize = 10f; // Size of the plane
	public float heightMultiplier = 2f; // How much the heightmap affects the vertices

	private Mesh mesh;

	private MeshCollider meshCollider;
	private MeshFilter meshFilter;

	private void Awake()
	{
		meshCollider = GetComponent<MeshCollider>();
		meshFilter = GetComponent<MeshFilter>();
	}

	private void Start()
	{
		CreatePlane(subdivisions, planeSize);
	}

	// Function to create a subdivided plane
	private void CreatePlane(int subdivisions, float size)
	{
		// Create arrays for vertices, uvs, and triangles
		Vector3[] vertices = new Vector3[(subdivisions + 1) * (subdivisions + 1)];
		Vector2[] uvs = new Vector2[vertices.Length];
		int[] triangles = new int[subdivisions * subdivisions * 6];

		// Determine step size based on subdivisions and plane size
		float stepSize = size / subdivisions;
		float sizeHalf = size / 2f;

		// Create vertices and UVs
		for (int y = 0; y <= subdivisions; y++)
		{
			for (int x = 0; x <= subdivisions; x++)
			{
				int index = x + y * (subdivisions + 1);
				vertices[index] = new Vector3(x * stepSize - sizeHalf, heightMultiplier, y * stepSize - sizeHalf);
				uvs[index] = new Vector2(x / (float)subdivisions, y / (float)subdivisions);
			}
		}

		// Create triangles
		int triIndex = 0;
		for (int y = 0; y < subdivisions; y++)
		{
			for (int x = 0; x < subdivisions; x++)
			{
				int index = x + y * (subdivisions + 1);

				// Triangle 1
				triangles[triIndex++] = index;
				triangles[triIndex++] = index + subdivisions + 1;
				triangles[triIndex++] = index + subdivisions + 2;

				// Triangle 2
				triangles[triIndex++] = index;
				triangles[triIndex++] = index + subdivisions + 2;
				triangles[triIndex++] = index + 1;
			}
		}

		// Create and assign the mesh
		mesh = new Mesh();
		mesh.vertices = vertices;
		mesh.uv = uvs;
		mesh.triangles = triangles;
		mesh.RecalculateNormals();

		meshFilter.sharedMesh = mesh;
		meshCollider.sharedMesh = mesh;
	}

	// Function to apply heightmap using Burst job
	public void ApplyHeightmap(Texture2D texture)
	{
		Vector3[] vertices = mesh.vertices;
		Vector2[] uvs = mesh.uv;

		// Create NativeArrays to pass data into the job
		NativeArray<Vector3> verticesArray = new NativeArray<Vector3>(vertices, Allocator.TempJob);
		NativeArray<Vector2> uvArray = new NativeArray<Vector2>(uvs, Allocator.TempJob);
		NativeArray<Color> heightmapArray = new NativeArray<Color>(texture.GetPixels(), Allocator.TempJob);

		// Create and schedule the job
		HeightmapJob heightmapJob = new HeightmapJob
		{
			vertices = verticesArray,
			uvs = uvArray,
			heightmap = heightmapArray,
			heightMultiplier = heightMultiplier,
			heightmapWidth = texture.width,
			heightmapHeight = texture.height
		};

		JobHandle jobHandle = heightmapJob.Schedule(verticesArray.Length, 128); // Schedule with a batch size of 64
		jobHandle.Complete(); // Wait for the job to complete
		

		// Apply the new vertices back to the mesh
		mesh.vertices = verticesArray.ToArray();
		mesh.RecalculateBounds();

		// Dispose of NativeArrays
		verticesArray.Dispose();
		uvArray.Dispose();
		heightmapArray.Dispose();
	}

	// Burst-compiled job for applying the heightmap
	//[BurstCompile]
	struct HeightmapJob : IJobParallelFor
	{
		public NativeArray<Vector3> vertices;
		[ReadOnly] public NativeArray<Vector2> uvs;
		[ReadOnly] public NativeArray<Color> heightmap;
		public float heightMultiplier;
		public int heightmapWidth;
		public int heightmapHeight;

		public void Execute(int index)
		{
			// Get the corresponding UV and sample the heightmap
			Vector2 uv = uvs[index];
			int x = (int)(uv.x * heightmapWidth);

			if (x < 0) x = 0;
			else if (x > heightmapWidth) x = heightmapWidth;

			int y = (int)(uv.y * heightmapHeight);

			if (y < 0) y = 0;
			else if (y > heightmapHeight) x = heightmapHeight;

			// Sample the height from the heightmap (using red channel, assuming grayscale)
			float heightValue = heightmap[y * heightmapWidth + x].r;

			// Offset the vertex's Y position based on the heightmap and multiplier
			Vector3 vertex = vertices[index];

			float vHeight = heightValue * heightMultiplier;

			if (vHeight < 0) return;

			if(vertex.y > vHeight)
				vertex.y = vHeight;

			//vertex.y = heightValue * heightMultiplier;

			vertices[index] = vertex;
		}
	}
}