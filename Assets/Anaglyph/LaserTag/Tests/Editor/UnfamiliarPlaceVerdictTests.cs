using Anaglyph.XR.SharedSpaces;
using NUnit.Framework;

namespace Anaglyph.LaserTag.Tests
{
	public class UnfamiliarPlaceVerdictTests
	{
		private static UnfamiliarPlaceVerdict NewVerdict()
		{
			UnfamiliarPlaceVerdict verdict = new(new UnfamiliarPlaceVerdict.Settings { seconds = 4f, minViews = 4, minFeatures = 100, inliersThatFoundTheMap = 30, secondsAfterTheMapWasFound = 30f });
			verdict.BeginLooking(0f);
			return verdict;
		}

		[Test]
		public void UsableViewsFindingNothingCallThePlaceUnfamiliarWithinFiveSeconds()
		{
			UnfamiliarPlaceVerdict verdict = NewVerdict();
			for (float t = 1.5f; t < 4f; t += 0.8f) Assert.IsFalse(verdict.Add(t, 400, false, 0), $"at {t} s");
			Assert.IsTrue(verdict.Add(4.7f, 400, true, 8));
		}

		[Test]
		public void AGuessedMapIsGivenUpSooner()
		{
			UnfamiliarPlaceVerdict verdict = new(new UnfamiliarPlaceVerdict.Settings { seconds = 4f, secondsForAGuess = 2f, minViews = 4, minFeatures = 100, inliersThatFoundTheMap = 30 });
			verdict.BeginLooking(0f, mapIsAGuess: true);
			for (float t = 0.5f; t < 2f; t += 0.5f) Assert.IsFalse(verdict.Add(t, 400, false, 0), $"at {t} s");
			Assert.IsTrue(verdict.Add(2f, 400, true, 12));
		}

		[Test]
		public void AfterAViewFindsTheMapTheLookingGoesOnFarLonger()
		{
			UnfamiliarPlaceVerdict verdict = NewVerdict();
			Assert.IsFalse(verdict.Add(1f, 400, true, 61));
			for (float t = 1.5f; t < 31f; t += 0.5f) Assert.IsFalse(verdict.Add(t, 400, true, 12), $"at {t} s");
			Assert.IsTrue(verdict.Add(31f, 400, false, 0));
		}

		[Test]
		public void PlainViewsSayNothing()
		{
			UnfamiliarPlaceVerdict verdict = NewVerdict();
			for (float t = 0; t < 20f; t += 0.5f) Assert.IsFalse(verdict.Add(t, 30, false, 0));
		}

		[Test]
		public void TooFewViewsAreNotEnoughHoweverLongTheyTook()
		{
			UnfamiliarPlaceVerdict verdict = NewVerdict();
			Assert.IsFalse(verdict.Add(5f, 400, false, 0));
			Assert.IsFalse(verdict.Add(10f, 400, false, 0));
			Assert.IsFalse(verdict.Add(20f, 400, false, 0));
			Assert.IsTrue(verdict.Add(30f, 400, false, 0));
		}
	}
}
