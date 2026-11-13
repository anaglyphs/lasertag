using Anaglyph.XR.DepthKit.EnvScanning;
using Anaglyph.XR.VisualLocalization;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// Late reverb shaped by the space around the listener: rays measure how far the scanned
	/// surfaces are and how much of the view is open, which set one global reverb's decay
	/// and level. Where the scan doesn't reach, <see cref="CoarseEnvMap"/> knows how far the
	/// space runs empty and where far surfaces stand, and <see cref="SkyView"/> tells open
	/// sky from far buildings. Early echoes come from <see cref="EnvSoundReflections"/>, so
	/// the reverb's own early reflections are off
	/// </summary>
	public class EnvReverb : MonoBehaviour
	{
		private const float SpeedOfSound = 343f;
		private const float SabineConstant = 0.161f;

		[SerializeField] private int probeCount = 32;
		[SerializeField] private float maxProbeDistance = 30f;
		[SerializeField] private float secondsBetweenProbes = 0.25f;
		[SerializeField] private float secondsToSettle = 1f;

		// share of sound a typical furnished room's surfaces absorb per bounce
		[SerializeField, Range(0.01f, 0.9f)] private float surfaceAbsorption = 0.15f;
		[SerializeField] private Vector2 decaySecondsRange = new(0.1f, 4f);
		[SerializeField] private float enclosedLevelMillibels = -600f;

		// sound that goes up into open sky never returns, so no smooth tail builds under it:
		// what the walls around still send back is short, quiet, grainy and dull
		[Header("Under open sky")]
		[SerializeField] private float openSkyLongestDecaySeconds = 0.5f;
		[SerializeField] private float openSkyLevelCutMillibels = 1000f;
		[SerializeField, Range(0f, 100f)] private float openSkyDensity = 20f;
		[SerializeField, Range(0f, 100f)] private float openSkyDiffusion = 20f;
		[SerializeField] private float openSkyHighsMillibels = -3000f;
		[SerializeField, Range(0.1f, 2f)] private float openSkyHighsDecayRatio = 0.3f;

		private const float RoofedDensity = 100f, RoofedDiffusion = 100f, RoofedHighsMillibels = -500f;
		private float roofedHighsDecayRatio;

		private AudioReverbZone zone;
		private AudioListener listener;
		private Vector3[] probeDirections;
		private bool[] probeHit;
		private bool[] probeHitFloor;
		private float[] probeDistance;
		// for a probe that hit nothing: how far its way is known to be empty
		private float[] probeKnownEmptyMeters;
		private float nextProbeTime;
		private int envMeshLayerMask;

		private float targetDecaySeconds;
		private float targetLevelMillibels = -10000f;
		private float targetLateDelaySeconds;
		private float targetDensity = RoofedDensity, targetDiffusion = RoofedDiffusion;
		private float targetHighsMillibels = RoofedHighsMillibels, targetHighsDecayRatio;

		/// <summary>How much of the view overhead is open sky, 0 under a roof to 1 under none.</summary>
		public float OpenSkyOverhead { get; private set; }

		public float EstimatedDecaySeconds => targetDecaySeconds;
		public float EstimatedLevelMillibels => targetLevelMillibels;

		private void Awake()
		{
			envMeshLayerMask = LayerMask.GetMask(EnvMesher.EnvironmentMeshLayerName);
			probeDirections = EvenlySpreadDirections(probeCount);
			probeHit = new bool[probeCount];
			probeHitFloor = new bool[probeCount];
			probeDistance = new float[probeCount];
			probeKnownEmptyMeters = new float[probeCount];

			zone = gameObject.AddComponent<AudioReverbZone>();
			zone.reverbPreset = AudioReverbPreset.User;
			zone.minDistance = 10000f;
			zone.maxDistance = 10001f;
			zone.reflections = -10000;
			zone.reverb = 0;
			zone.diffusion = RoofedDiffusion;
			zone.density = RoofedDensity;
			zone.roomHF = Mathf.RoundToInt(RoofedHighsMillibels);
			zone.room = -10000;
			roofedHighsDecayRatio = targetHighsDecayRatio = zone.decayHFRatio;
		}

		private void OnEnable() => zone.enabled = true;
		private void OnDisable() => zone.enabled = false;

		private void Update()
		{
			if (Time.unscaledTime >= nextProbeTime)
			{
				nextProbeTime = Time.unscaledTime + secondsBetweenProbes;
				ProbeRoom();
			}

			float settle = 1 - Mathf.Exp(-Time.unscaledDeltaTime / secondsToSettle);
			zone.decayTime = Mathf.Lerp(zone.decayTime, targetDecaySeconds, settle);
			zone.reverbDelay = Mathf.Lerp(zone.reverbDelay, targetLateDelaySeconds, settle);
			zone.room = Mathf.RoundToInt(Mathf.Lerp(zone.room, targetLevelMillibels, settle));
			zone.density = Mathf.Lerp(zone.density, targetDensity, settle);
			zone.diffusion = Mathf.Lerp(zone.diffusion, targetDiffusion, settle);
			zone.roomHF = Mathf.RoundToInt(Mathf.Lerp(zone.roomHF, targetHighsMillibels, settle));
			zone.decayHFRatio = Mathf.Lerp(zone.decayHFRatio, targetHighsDecayRatio, settle);
		}

		private void ProbeRoom()
		{
			if (!listener || !listener.isActiveAndEnabled)
				listener = FindAnyObjectByType<AudioListener>();
			if (!listener) return;

			Vector3 listenerPos = listener.transform.position;
			CoarseEnvMap coarseMap = CoarseEnvScanner.Instance ? CoarseEnvScanner.Instance.Map : null;
			int upwardProbeCount = 0, ceilingHitCount = 0;
			float wallDistanceSum = 0;
			int wallHitCount = 0;
			for (int i = 0; i < probeDirections.Length; i++)
			{
				probeHit[i] = Physics.Raycast(listenerPos, probeDirections[i], out RaycastHit hit, maxProbeDistance,
					envMeshLayerMask, QueryTriggerInteraction.Ignore);
				probeDistance[i] = hit.distance;
				probeHitFloor[i] = probeHit[i] && hit.normal.y > 0.7f;
				bool hitCeiling = probeHit[i] && hit.normal.y < -0.7f;
				probeKnownEmptyMeters[i] = 0;

				// past the fine scan the coarse map has no normals: what a steep ray meets is
				// floor or ceiling
				if (!probeHit[i] && coarseMap != null)
				{
					CoarseEnvMap.Cell stoppedBy = coarseMap.Cast(listenerPos, probeDirections[i], maxProbeDistance, out float meters);
					if (stoppedBy == CoarseEnvMap.Cell.Solid)
					{
						probeHit[i] = true;
						probeDistance[i] = meters;
						probeHitFloor[i] = probeDirections[i].y < -0.5f;
						hitCeiling = probeDirections[i].y >= 0.5f;
					}
					else
					{
						probeKnownEmptyMeters[i] = meters;
					}
				}

				if (probeHit[i] && !probeHitFloor[i])
				{
					wallDistanceSum += probeDistance[i];
					wallHitCount++;
				}

				if (probeDirections[i].y < 0.5f) continue;
				upwardProbeCount++;
				if (hitCeiling) ceilingHitCount++;
			}

			float roofedShare = upwardProbeCount > 0 ? ceilingHitCount / (float)upwardProbeCount : 0;
			// a way nothing is known of is taken to be like the ways that are: as far as their
			// surfaces on average, and no nearer than it is known to be empty
			float typicalSurfaceMeters = wallHitCount > 0 ? wallDistanceSum / wallHitCount : maxProbeDistance;

			float hitDistanceSum = 0, hitWeight = 0;
			float enclosingWeight = 0, enclosingProbeCount = 0;
			float openOverheadSum = 0, knownOverheadCount = 0;
			for (int i = 0; i < probeDirections.Length; i++)
			{
				bool known = TryGetSurfaceShare(i, listenerPos, out float surfaceShare);
				if (!known) surfaceShare = roofedShare;
				float surfaceMeters = probeHit[i] ? probeDistance[i] : Mathf.Max(probeKnownEmptyMeters[i], typicalSurfaceMeters);
				hitDistanceSum += surfaceShare * surfaceMeters;
				hitWeight += surfaceShare;
				if (known && probeDirections[i].y >= 0.5f)
				{
					knownOverheadCount++;
					openOverheadSum += 1 - surfaceShare;
				}

				// a floor neither encloses nor opens
				if (probeDirections[i].y < -0.3f || probeHitFloor[i]) continue;
				enclosingProbeCount++;
				enclosingWeight += surfaceShare;
			}

			// judged only from overhead directions scanned or seen: until one is, it sounds roofed
			OpenSkyOverhead = knownOverheadCount > 0 ? openOverheadSum / knownOverheadCount : 0;
			targetDensity = Mathf.Lerp(RoofedDensity, openSkyDensity, OpenSkyOverhead);
			targetDiffusion = Mathf.Lerp(RoofedDiffusion, openSkyDiffusion, OpenSkyOverhead);
			targetHighsMillibels = Mathf.Lerp(RoofedHighsMillibels, openSkyHighsMillibels, OpenSkyOverhead);
			targetHighsDecayRatio = Mathf.Lerp(roofedHighsDecayRatio, openSkyHighsDecayRatio, OpenSkyOverhead);

			if (enclosingWeight < 0.5f || hitWeight <= 0)
			{
				targetLevelMillibels = -10000f;
				return;
			}

			float openShare = 1 - enclosingWeight / enclosingProbeCount;

			// open directions absorb everything that reaches them
			float meanFreePath = hitDistanceSum / hitWeight;
			float absorption = Mathf.Min(openShare + (1 - openShare) * surfaceAbsorption, 0.99f);
			float eyringDecay = SabineConstant * meanFreePath / 4f / -Mathf.Log(1 - absorption);

			float roofedDecay = Mathf.Clamp(eyringDecay, decaySecondsRange.x, decaySecondsRange.y);
			targetDecaySeconds = Mathf.Lerp(roofedDecay, Mathf.Min(roofedDecay, openSkyLongestDecaySeconds), OpenSkyOverhead);
			targetLateDelaySeconds = Mathf.Clamp(meanFreePath / SpeedOfSound, 0f, 0.1f);
			targetLevelMillibels = enclosedLevelMillibels + 2000f * Mathf.Log10(Mathf.Max(1 - openShare, 0.01f))
			                       - openSkyLevelCutMillibels * OpenSkyOverhead;
		}

		// how much of a probe's direction is surface: all of it where the scan has one, and past
		// the scan's reach what the camera sees there that isn't sky. False where neither knows
		private bool TryGetSurfaceShare(int probe, Vector3 listenerPos, out float surfaceShare)
		{
			surfaceShare = 1;
			if (probeHit[probe]) return true;

			SkyView skyView = SkyView.Instance;
			if (!skyView || !skyView.isActiveAndEnabled ||
			    !skyView.TryGetSkyShare(listenerPos, probeDirections[probe], out float skyShare)) return false;
			surfaceShare = 1 - skyShare;
			return true;
		}

		private static Vector3[] EvenlySpreadDirections(int count)
		{
			var directions = new Vector3[count];
			float goldenAngle = Mathf.PI * (3 - Mathf.Sqrt(5));
			for (int i = 0; i < count; i++)
			{
				float y = 1 - 2 * (i + 0.5f) / count;
				float radius = Mathf.Sqrt(1 - y * y);
				float angle = goldenAngle * i;
				directions[i] = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
			}

			return directions;
		}
	}
}
