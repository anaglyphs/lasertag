using Anaglyph.Audio;
using Anaglyph.XR.DepthKit.EnvScanning;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// Routes pooled sounds around scanned walls: a sound plays from the direction its
	/// shortest route through the room reaches the listener, as far away as that route is
	/// long, muffled by how far the route detours
	/// </summary>
	public class EnvSoundRouting : MonoBehaviour, ISoundRouting
	{
		// routes longer than this are inaudible anyway; the search stops there
		[SerializeField] private float maxRouteMeters = 40f;
		// a detour this much longer than the straight line muffles fully
		[SerializeField] private float detourMetersForFullMuffle = 8f;

		private EnvPathField pathField;
		private EnvMesher pathFieldMesher;
		private AudioListener listener;

		private void OnEnable() => AudioPool.Routing = this;

		private void OnDisable()
		{
			if (ReferenceEquals(AudioPool.Routing, this)) AudioPool.Routing = null;
			DisposePathField();
		}

		private void DisposePathField()
		{
			pathField?.Dispose();
			pathField = null;
			pathFieldMesher = null;
		}

		private void Update()
		{
			if (pathFieldMesher != EnvMesher.Instance || !pathFieldMesher)
			{
				DisposePathField();
				if (!EnvMesher.Instance) return;
				pathFieldMesher = EnvMesher.Instance;
				pathField = new EnvPathField(pathFieldMesher, maxRouteMeters);
			}

			if (!listener || !listener.isActiveAndEnabled)
				listener = FindAnyObjectByType<AudioListener>();
			if (!listener) return;

			pathField.Update(listener.transform.position);
		}

		public bool TryRoute(Vector3 source, out Vector3 apparentSource, out float muffle)
		{
			apparentSource = source;
			muffle = 0;
			if (pathField == null) return false;

			switch (pathField.FindPath(source, out float routeLength, out Vector3 firstTurn))
			{
				case EnvPathResult.Unreachable:
					muffle = 1;
					return true;

				case EnvPathResult.Found:
					Vector3 origin = pathField.Origin;
					Vector3 toFirstTurn = firstTurn - origin;
					if (toFirstTurn.sqrMagnitude > 1e-6f)
						apparentSource = origin + toFirstTurn.normalized * routeLength;
					muffle = Mathf.Clamp01((routeLength - Vector3.Distance(origin, source)) / detourMetersForFullMuffle);
					return true;

				default:
					return false;
			}
		}
	}
}
