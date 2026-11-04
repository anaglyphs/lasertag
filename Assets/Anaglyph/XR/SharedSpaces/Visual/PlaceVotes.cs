using System;
using System.Collections.Generic;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Unity.Mathematics;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>
	/// Recent alignment results, weighed by the place each says the tracking space belongs. A view
	/// that merely resembles another place gives solves as tight as the real one, and keeps giving
	/// them while it stays in sight: so a view counts once, however many frames showed it, and a
	/// place is only as good as how it compares with the others in contention.
	/// </summary>
	public sealed class PlaceVotes
	{
		[Serializable]
		public sealed class Settings
		{
			[UnityEngine.Tooltip("A camera this far from another (m), or turned this far, shows a different view: two frames closer than that share their mistakes")]
			public float distinctViewMeters = 1f;
			public float distinctViewDegrees = 20f;
			[UnityEngine.Tooltip("Results this close (m at the camera, and in yaw) say the same place. Far wider than the agreement that picks the alignment: this only separates places, and has to hold across minutes of drift")]
			public float samePlaceMeters = 1.5f;
			public float samePlaceDegrees = 10f;
			[UnityEngine.Tooltip("A place's inliers must be this many times any other place's to win")]
			public float margin = 1.5f;
		}

		/// <summary>What stands behind one place: its distinct views, and their inliers.</summary>
		public readonly struct Tally
		{
			public readonly int views, inliers;

			public Tally(int views, int inliers)
			{
				this.views = views;
				this.inliers = inliers;
			}
		}

		private const int Capacity = 12;

		private readonly Settings settings;
		private readonly List<MeasurementGate.Frame> frames = new();
		private readonly List<MeasurementGate.Frame> countedViews = new();

		public PlaceVotes(Settings settings) => this.settings = settings;

		public IReadOnlyList<MeasurementGate.Frame> Frames => frames;

		public void Clear() => frames.Clear();

		public void Add(MeasurementGate.Frame frame)
		{
			if (frames.Count == Capacity) frames.RemoveAt(0);
			frames.Add(frame);
		}

		public bool SameView(MeasurementGate.Frame a, MeasurementGate.Frame b) =>
			math.distance(a.cameraInSpace, b.cameraInSpace) < settings.distinctViewMeters &&
			math.dot(a.forwardInSpace, b.forwardInSpace) > math.cos(math.radians(settings.distinctViewDegrees));

		public bool SamePlace(Pose4 place, MeasurementGate.Frame frame) =>
			math.degrees(math.abs(Pose4.WrapAngle(place.yaw - frame.spaceInMap.yaw))) <= settings.samePlaceDegrees &&
			math.distance(place.Apply(frame.cameraInSpace), frame.spaceInMap.Apply(frame.cameraInSpace)) <= settings.samePlaceMeters;

		/// <summary>What stands behind <paramref name="place"/>: each view's strongest frame.</summary>
		public Tally For(Pose4 place)
		{
			countedViews.Clear();
			foreach (MeasurementGate.Frame frame in frames)
				if (SamePlace(place, frame))
					countedViews.Add(frame);
			countedViews.Sort((a, b) => b.solve.inliers.CompareTo(a.solve.inliers));

			int views = 0, inliers = 0;
			for (int i = 0; i < countedViews.Count; i++)
			{
				bool shown = false;
				for (int j = 0; j < i && !shown; j++) shown = SameView(countedViews[i], countedViews[j]);
				if (shown) continue;
				views++;
				inliers += countedViews[i].solve.inliers;
			}

			return new Tally(views, inliers);
		}

		/// <summary>What stands behind the strongest place other than <paramref name="place"/>.</summary>
		public Tally BestAgainst(Pose4 place)
		{
			Tally best = default;
			foreach (MeasurementGate.Frame frame in frames)
			{
				if (SamePlace(place, frame)) continue;
				Tally rival = For(frame.spaceInMap);
				if (rival.inliers > best.inliers) best = rival;
			}

			return best;
		}

		/// <summary>Whether <paramref name="place"/> stands far enough above <paramref name="against"/> to win.</summary>
		public bool Beats(Tally place, Tally against) => place.inliers >= against.inliers * settings.margin;
	}
}
