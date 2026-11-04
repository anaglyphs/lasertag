using System.Collections.Generic;
using System.Text;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Anaglyph.XR.VisualLocalization;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>What every recent localization attempt came to, for the console and <see cref="AlignmentDiagnosis"/>.</summary>
	public sealed class ColocationLog
	{
		private const int Length = 400;

		private readonly Object context;
		private readonly string tag;
		private readonly VisualLocalizer localizer;
		private readonly Queue<string> outcomes = new();

		// the query an outcome is about, so the outcome lands on its frame too
		private VisualQuery considering;

		public ColocationLog(Object context, string tag, VisualLocalizer localizer)
		{
			this.context = context;
			this.tag = tag;
			this.localizer = localizer;
		}

		public string LastOutcome { get; private set; }

		/// <summary>Recent outcomes, oldest first, each stamped with the realtime it happened at.</summary>
		public IEnumerable<string> Recent => outcomes;

		/// <summary>The query the outcomes from here on are about.</summary>
		[System.Diagnostics.Conditional("DEBUG")]
		public void About(VisualQuery query) => considering = query;

		/// <summary>In development builds; a release build doesn't even build the message.</summary>
		[System.Diagnostics.Conditional("DEBUG")]
		public void Outcome(string message)
		{
			LastOutcome = message;
			if (considering != null) localizer.Frames.Note(considering.frame, message);
			outcomes.Enqueue($"{Time.realtimeSinceStartup:F2} {message}");
			while (outcomes.Count > Length) outcomes.Dequeue();
			// several a second while moving: no stack trace, which costs on device
			Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, context, "{0} {1}", tag, message);
		}

		/// <summary>
		/// One greppable line per site measurement ("SiteLap"), for telling a bent map from
		/// drift: each measurement held against the site's previous one and its first, at the
		/// site, with the head travel between them, and against the fit the rig follows, at the
		/// camera. A bent map's revisits repeat themselves however far the head walked between
		/// them; drift's differences grow with it.
		/// </summary>
		[System.Diagnostics.Conditional("DEBUG")]
		public void SiteLap(ConstraintSites.Site site, bool crossLink, Pose4 fit, int siteCount)
		{
			List<ConstraintSites.Measurement> history = site.history;
			ConstraintSites.Measurement latest = site.Latest;
			StringBuilder line = new();
			line.Append($"{tag} SiteLap site={site.id} n={site.measurements} kind={(crossLink ? "CrossLink" : "Loop")} path={latest.path:F1}m " +
			            $"from={math.distance(latest.camera, site.position):F1}m σ={latest.sigmaTranslation * 100:F1}cm/{math.degrees(latest.sigmaYaw):F2}°");
			if (history.Count > 1)
			{
				ConstraintSites.Measurement previous = history[^2];
				line.Append($" | prev walked={latest.path - previous.path:F1}m {Difference(previous.correction, latest.correction, site.position)}");
				if (history.Count > 2)
					line.Append($" | first walked={latest.path - history[0].path:F1}m {Difference(history[0].correction, latest.correction, site.position)}");
			}

			// the fit before this measurement takes effect, where it was measured
			line.Append($" | fit {Difference(fit, latest.correction, latest.focus)} ({siteCount} sites)");
			Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, context, "{0}", line);
		}

		// how far apart two corrections put a point, signed: the yaw and the horizontal offset (cm)
		private static string Difference(Pose4 from, Pose4 to, float3 at)
		{
			float3 offset = to.Apply(at) - from.Apply(at);
			float yaw = math.degrees(Pose4.WrapAngle(to.yaw - from.yaw));
			return $"Δyaw={yaw:+0.00;-0.00}° Δxz=({offset.x * 100:+0.0;-0.0}, {offset.z * 100:+0.0;-0.0})cm |{math.length(offset.xz) * 100:F1}cm|";
		}
	}
}
