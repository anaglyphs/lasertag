using UnityEngine;

namespace Anaglyph
{
	[ExecuteAlways]
    public class RingMesh : ProgrammaticMesh
    {
		public bool updateOnStart = false;
		public float startRadius = 3f;

		protected override void OnValidate()
		{
			base.OnValidate();

			if(updateOnStart)
			{
				UpdateMesh(startRadius);
			}
		}

		private void Start()
		{
			if (updateOnStart)
			{
				UpdateMesh(startRadius);
			}
		}

		public void UpdateMesh(float radius)
        {
			if (!initializedMesh)
				return;

			ResizeMesh(originalMesh, modifiedMesh, radius);
			meshFilter.mesh = modifiedMesh;
        }

		private static void ResizeMesh(Mesh templateMesh, Mesh resizedMesh, float radius)
		{
			Vector3[] verts = resizedMesh.vertices;

			for (int i = 0; i < verts.Length; i++)
			{
				Vector3 vert = templateMesh.vertices[i];

				Vector2 v2 = new(vert.x, vert.z);
				v2 = v2.normalized * radius;

				vert = new(v2.x, vert.y, v2.y);

				verts[i] = vert;
			}

			resizedMesh.SetVertices(verts);
		}
	}
}
