using System;
using System.Collections.Generic;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>
	/// Whether an applied alignment holds up as the head moves on. A joiner keeps localizing against
	/// other headsets' views: results at the place it was put in confirm it, and a different place
	/// that outgrows it says it is in the wrong one. Until confirmed, what this headset would tell
	/// the others waits: what it has shared can be taken back, but not out of the others' scans.
	/// </summary>
	public sealed class AlignmentWatch
	{
		public enum Verdict { None, Confirmed, Overturned }

		[Serializable]
		public sealed class Settings
		{
			[UnityEngine.Tooltip("Meters walked from where the alignment was found before a result there confirms it: in the sim, a view that resembles another place kept resembling it for over 2 m")]
			public float confirmWalk = 5f;
			[UnityEngine.Tooltip("Seconds what this headset shares waits for confirmation while nothing contradicts it: someone walking into a place no one has mapped can't be confirmed, and is the only one who can share it")]
			public float holdSeconds = 60f;
			[UnityEngine.Tooltip("Distinct views another place needs before it can overturn the alignment")]
			public int overturnViews = 3;
			[UnityEngine.Tooltip("Seconds between checks until confirmed")]
			public float checkSeconds = 1f;
			[UnityEngine.Tooltip("Seconds between checks once confirmed: each is an extraction and up to a query's LighterGlue runs")]
			public float confirmedCheckSeconds = 10f;
		}

		private readonly Settings settings;
		private readonly PlaceVotes votes;
		private Pose4 applied;
		private float beganPath, beganTime;
		private bool confirmed;

		public AlignmentWatch(Settings settings, PlaceVotes.Settings places)
		{
			this.settings = settings;
			votes = new PlaceVotes(places);
		}

		public bool Watching { get; private set; }

		public float CheckSeconds => confirmed ? settings.confirmedCheckSeconds : settings.checkSeconds;

		/// <summary>An alignment was just applied, on what <paramref name="witnesses"/> saw while it was being found.</summary>
		public void Begin(IEnumerable<MeasurementGate.Frame> witnesses, Pose4 place, float path, float time)
		{
			votes.Clear();
			foreach (MeasurementGate.Frame witness in witnesses) votes.Add(witness);
			applied = place;
			beganPath = path;
			beganTime = time;
			confirmed = false;
			Watching = true;
		}

		public void End()
		{
			Watching = false;
			votes.Clear();
		}

		/// <summary>Whether what this headset would share should wait: not confirmed, and either still early or contradicted.</summary>
		public bool HoldsContributions(float time) =>
			Watching && !confirmed && (time - beganTime < settings.holdSeconds || votes.BestAgainst(applied).inliers > votes.For(applied).inliers);

		/// <summary>A result of a check.</summary>
		/// <param name="current">Where the tracking space belongs now, as the rig follows it.</param>
		/// <param name="why">Set whenever the verdict isn't <see cref="Verdict.None"/>.</param>
		public Verdict Add(MeasurementGate.Frame check, Pose4 current, float path, out string why)
		{
			why = null;
			applied = current;
			votes.Add(check);

			if (votes.SamePlace(current, check))
			{
				if (confirmed || path - beganPath < settings.confirmWalk) return Verdict.None;
				confirmed = true;
				why = $"confirmed {path - beganPath:F0} m on";
				return Verdict.Confirmed;
			}

			PlaceVotes.Tally here = votes.For(current), rival = votes.BestAgainst(current);
			if (rival.views < settings.overturnViews || !votes.Beats(rival, here)) return Verdict.None;
			why = $"another place has {rival.views} views and {rival.inliers} inliers against {here.views} and {here.inliers} here";
			return Verdict.Overturned;
		}
	}
}
