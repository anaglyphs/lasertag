using System;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>
	/// Decides that the map being relocalized against is of some other place: since the looking
	/// began, usable views kept coming and none found the map. A view too plain to match anything
	/// says nothing either way. One view that finds the map says the place is likely the map's:
	/// the looking starts over and goes on far longer, since views that find nothing are as common
	/// in a thinly mapped room as in another place.
	/// </summary>
	public sealed class UnfamiliarPlaceVerdict
	{
		[Serializable]
		public sealed class Settings
		{
			[UnityEngine.Tooltip("Seconds of looking with nothing of the map found before the place is called unfamiliar; the view that says so comes up to a query interval later")]
			public float seconds = 4f;
			[UnityEngine.Tooltip("Seconds instead, counted from the last view that found the map")]
			public float secondsAfterTheMapWasFound = 30f;
			[UnityEngine.Tooltip("Usable views that must have found nothing of the map in that time")]
			public int minViews = 4;
			[UnityEngine.Tooltip("A view with fewer features is too plain to say anything")]
			public int minFeatures = 100;
			[UnityEngine.Tooltip("A solve with at least this many inliers found the map, whether or not it was accepted")]
			public int inliersThatFoundTheMap = 30;
		}

		private readonly Settings settings;
		private int viewsThatFoundNothing;
		private float lookingSince;
		private float patience;

		public UnfamiliarPlaceVerdict(Settings settings) => this.settings = settings;

		public void BeginLooking(float time)
		{
			viewsThatFoundNothing = 0;
			lookingSince = time;
			patience = settings.seconds;
		}

		/// <returns>Whether the place is unfamiliar, counting this view.</returns>
		public bool Add(float time, int features, bool solved, int inliers)
		{
			if (solved && inliers >= settings.inliersThatFoundTheMap)
			{
				BeginLooking(time);
				patience = settings.secondsAfterTheMapWasFound;
				return false;
			}

			if (features < settings.minFeatures) return false;
			viewsThatFoundNothing++;
			return viewsThatFoundNothing >= settings.minViews && time - lookingSince >= patience;
		}
	}
}
