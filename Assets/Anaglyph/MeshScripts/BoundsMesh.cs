using UnityEngine;

namespace Anaglyph
{
    public class BoundsMesh : ProgrammaticMesh
    {
		private static void ResizeMesh(Mesh templateMesh, Mesh resizedMesh, Vector3 size)
		{
			Vector3[] verts = resizedMesh.vertices;
			Vector3 offset = (size / 2 - Vector3.one);

			for (int i = 0; i < verts.Length; i++)
			{
				Vector3 vert = templateMesh.vertices[i];

				vert.x += offset.x * Mathf.Sign(vert.x);
				vert.y += offset.y * Mathf.Sign(vert.y);
				vert.z += offset.z * Mathf.Sign(vert.z);

				verts[i] = vert;
			}

			resizedMesh.SetVertices(verts);
		}

		public void UpdateMesh(Vector3 size)
		{
			ResizeMesh(originalMesh, modifiedMesh, size);
			meshFilter.mesh = modifiedMesh;
		}
	}
}
