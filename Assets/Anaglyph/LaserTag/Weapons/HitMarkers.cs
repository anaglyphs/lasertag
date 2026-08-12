using Anaglyph.XR;
using UnityEngine;

namespace Anaglyph.LaserTag.Weapons
{
	public class HitMarkers : MonoBehaviour
	{
		private const int MaxSimultaneousMarkers = 32;

		[SerializeField] private Material material;
		[SerializeField] private float lifetimeSeconds = 0.35f;

		[Tooltip("Marker width in meters at one meter away.")]
		[SerializeField] private float sizeAtOneMeter = 0.06f;

		[Tooltip("0 holds a fixed size in the world, 1 holds a fixed size on screen.")]
		[Range(0f, 1f)][SerializeField] private float distanceScaling = 0.6f;

		[SerializeField] private AnimationCurve sizeOverLifetime = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

		[Tooltip("Pushes the marker toward the camera so it doesn't z-fight the surface it's on.")]
		[SerializeField] private float offsetTowardCamera = 0.02f;

		private struct Marker
		{
			public Vector3 position;
			public float spawnTime;
		}

		private readonly Marker[] markers = new Marker[MaxSimultaneousMarkers];
		private int nextMarkerIndex;

		private Mesh quad;
		private RenderParams renderParams;

		private void Awake()
		{
			for (int i = 0; i < markers.Length; i++)
				markers[i].spawnTime = float.NegativeInfinity;

			quad = CreateCameraFacingQuad();

			renderParams = new RenderParams(material)
			{
				layer = gameObject.layer,
				shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
				receiveShadows = false,
			};
		}

		private void OnEnable() => IDamageable.DamageDealt += OnDamageDealt;

		private void OnDisable() => IDamageable.DamageDealt -= OnDamageDealt;

		private void OnDestroy()
		{
			if (quad != null)
				Destroy(quad);
		}

		private void OnDamageDealt(Vector3 position, IDamageable target, IDamageable.Data data)
		{
			markers[nextMarkerIndex] = new Marker
			{
				position = position,
				spawnTime = Time.time,
			};

			nextMarkerIndex = (nextMarkerIndex + 1) % markers.Length;
		}

		private void LateUpdate()
		{
			if (material == null || MainXRRig.Instance == null)
				return;

			Transform cameraTransform = MainXRRig.Camera.transform;
			Vector3 cameraPosition = cameraTransform.position;
			Vector3 cameraUp = cameraTransform.up;

			for (int i = 0; i < markers.Length; i++)
			{
				float age = Time.time - markers[i].spawnTime;
				if (age > lifetimeSeconds)
					continue;

				Vector3 towardCamera = cameraPosition - markers[i].position;
				float distance = towardCamera.magnitude;
				if (distance < 0.001f)
					continue;

				towardCamera /= distance;

				float size = sizeAtOneMeter
					* Mathf.Pow(distance, distanceScaling)
					* sizeOverLifetime.Evaluate(age / lifetimeSeconds);

				Vector3 position = markers[i].position + towardCamera * offsetTowardCamera;
				Quaternion rotation = Quaternion.LookRotation(-towardCamera, cameraUp);
				Matrix4x4 model = Matrix4x4.TRS(position, rotation, Vector3.one * size);

				Graphics.RenderMesh(in renderParams, quad, 0, model);
			}
		}
		
		private static Mesh CreateCameraFacingQuad()
		{
			Mesh mesh = new() { name = "Hit Marker Quad" };

			mesh.SetVertices(new[]
			{
				new Vector3(-0.5f, -0.5f, 0f),
				new Vector3(0.5f, -0.5f, 0f),
				new Vector3(-0.5f, 0.5f, 0f),
				new Vector3(0.5f, 0.5f, 0f),
			});

			mesh.SetUVs(0, new[]
			{
				new Vector2(0f, 0f),
				new Vector2(1f, 0f),
				new Vector2(0f, 1f),
				new Vector2(1f, 1f),
			});

			mesh.SetNormals(new[]
			{
				Vector3.back, Vector3.back, Vector3.back, Vector3.back,
			});

			mesh.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);

			return mesh;
		}
	}
}
