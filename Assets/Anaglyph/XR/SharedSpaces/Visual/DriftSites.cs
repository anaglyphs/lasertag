using System;
using System.Collections.Generic;
using Anaglyph.Debugging.Visuals;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Unity.Mathematics;
using UnityEngine;
using Utilities.XR;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>
	/// What this headset has measured of its drift, as colocation constraints: every revisit
	/// (<see cref="ConstraintSites"/>), and where this session first mapped each place. The
	/// colocator's filter over them aligns the rig; the maps themselves never move.
	/// </summary>
	public sealed class DriftSites
	{
		[Serializable]
		public sealed class Settings
		{
			[Tooltip("Least yaw sigma a site carries, degrees: the solver's own runs several times too small against ground truth (0.6–0.9° errors at ±0.1–0.3°)")]
			public float minYawSigmaDegrees = 0.5f;
			[Tooltip("Yaw sigma a site's features add at one meter from the camera, degrees, falling with distance: on device, solves on features under 5 m away scattered 1.6-3.8° at the same claimed sigma as the 0.2-0.6° of farther ones. The translation at the features stays good")]
			public float closeYawDegrees = 2f;
			[Tooltip("Least translation sigma a site carries, meters: likewise several times too small against ground truth")]
			public float minTranslationSigma = 0.03f;
			[Tooltip("A measurement this close to a site joins it, meters: a few sites per room, each holding its revisits")]
			public float siteRadius = 8f;

			[Header("Mapped places")]
			[Tooltip("Meters apart, at least, the places kept whose first mapping stands as a measurement: the correction views were placed with there, which the map agrees with there by construction")]
			public float mappedSpacing = 2f;
			public int mappedCapacity = 128;

			[Header("Debug")]
			public Color debugRevisitColor = new(1f, 0.8f, 0.2f);
			public Color debugCrossLinkColor = new(0.2f, 0.9f, 1f);
			[Tooltip("The site measured most recently")]
			public Color debugLatestColor = new(0.2f, 1f, 0.3f);
			[Tooltip("Size of the markers drawn at the features each site's latest measurement stood on, meters")]
			public float debugFeatureSize = 0.03f;
		}

		private readonly Settings settings;
		private readonly ConstraintSites sites = new();
		// where this session's map was first made, and the correction its views were placed with there
		private readonly List<ConstraintSites.Measurement> mapped = new();
		private int lastSiteId;

		public DriftSites(Settings settings) => this.settings = settings;

		public ConstraintSites Sites => sites;

		/// <summary>Where this session first mapped each place and the correction it was mapped with, for diagnosis.</summary>
		public IReadOnlyList<ConstraintSites.Measurement> Mapped => mapped;

		public void Clear()
		{
			sites.Clear();
			mapped.Clear();
		}

		/// <summary>An agreed drift measurement joins the site at its features, or starts one.</summary>
		public ConstraintSites.Site Add(in MeasurementGate.Agreed agreed, float3 camera, float yawSigma, float path, float time, bool crossLink, ulong anchor)
		{
			sites.SiteRadius = settings.siteRadius;
			ConstraintSites.Site site = sites.Add(agreed.focus, camera, agreed.correction, math.max(agreed.sigmaTranslation, settings.minTranslationSigma),
				SiteYawSigma(yawSigma, camera, agreed.focus), path, time, crossLink, agreed.features, anchor);
			lastSiteId = site.id;
			return site;
		}

		private float SiteYawSigma(float yawSigma, float3 camera, float3 focus)
		{
			float floor = math.max(yawSigma, math.radians(settings.minYawSigmaDegrees));
			float close = math.radians(settings.closeYawDegrees) / math.max(math.distance(camera.xz, focus.xz), 0.5f);
			return math.sqrt(floor * floor + close * close);
		}

		/// <summary>
		/// A keyframe where this session's map had none near yet: the map there was made with
		/// its segment's correction, so that correction stands as a measurement of the
		/// alignment there, as old as the walk since. Tracking errs by place as much as by
		/// walk, so back at a place, the correction it was mapped with is the best guess until
		/// a revisit measures it, better than one measured across the room. Later views of a
		/// place were placed by whatever the fit was then, and are what revisits measure
		/// against rather than evidence of their own.
		/// </summary>
		public void RecordMapped(float3 at, Pose4 correction, float path, float time)
		{
			foreach (ConstraintSites.Measurement m in mapped)
				if (math.distance(m.focus, at) < settings.mappedSpacing)
					return;

			if (mapped.Count >= settings.mappedCapacity) mapped.RemoveAt(0);
			mapped.Add(new ConstraintSites.Measurement
			{
				camera = at, focus = at, correction = correction,
				sigmaTranslation = settings.minTranslationSigma, sigmaYaw = math.radians(settings.minYawSigmaDegrees),
				path = path, time = time,
			});
		}

		/// <summary>
		/// Every site as an anchor would be: where the map has it now, locked to its node and
		/// moved only by the graph (canon), and where its latest measurement says the uncorrected
		/// alignment places it, seen through the tracking space as it stands (observed), turned so
		/// that one alone still fixes rotation. The colocator's filter runs over them in the order
		/// they were measured, and the latest revisit says where the rig belongs now.
		/// </summary>
		public void AddConstraints(List<ColocationConstraint> results, in Matrix4x4 worldFromPlaced)
		{
			Quaternion placedRotation = worldFromPlaced.rotation;
			foreach (ConstraintSites.Site site in sites.Sites)
			{
				ConstraintSites.Measurement latest = site.Latest;
				Pose4 world = sites.WorldOf(site.anchor);
				float3 placed = latest.fromPlaced.ApplyInverse(site.point);
				results.Add(new ColocationConstraint(new Pose(worldFromPlaced.MultiplyPoint3x4(placed), placedRotation),
					new Pose(world.Apply(site.point), (world * latest.fromPlaced).Rotation), true,
					latest.sigmaTranslation, latest.sigmaYaw, latest.path));
			}
		}

		// every site's latest measurement as an axis where the map has its features (green for the
		// one measured last), a line from there to where the rig's current alignment puts it:
		// how far the fit leaves each one, and the features that measurement stood on
		public void DrawDebug(in Matrix4x4 worldFromPlaced)
		{
			foreach (ConstraintSites.Site site in sites.Sites)
			{
				Color color = site.id == lastSiteId ? settings.debugLatestColor : site.crossLink ? settings.debugCrossLinkColor : settings.debugRevisitColor;
				ConstraintSites.Measurement latest = site.Latest;
				Pose4 world = sites.WorldOf(site.anchor);
				Vector3 at = world.Apply(site.point);
				DebugAxisVisual.DrawDebugAxis(at, (world * latest.fromPlaced).Rotation, color, 0.3f);
				XRGizmos.DrawLine(at, worldFromPlaced.MultiplyPoint3x4(latest.fromPlaced.ApplyInverse(site.point)), color);
				// the features its latest measurement stood on, where the map has them
				if (site.features != null) XRGizmos.DrawPointSet(site.features, color, settings.debugFeatureSize);
			}
		}
	}
}
