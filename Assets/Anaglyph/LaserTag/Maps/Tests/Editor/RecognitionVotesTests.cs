using System.Collections.Generic;
using Anaglyph.LaserTag.Maps;
using NUnit.Framework;
using Pose = UnityEngine.Pose;

namespace Anaglyph.LaserTag.Tests
{
	public class RecognitionVotesTests
	{
		private static Dictionary<string, float> Scores(float mall, float apartment = 0.5f) =>
			new() { ["mall"] = mall, ["apartment"] = apartment };

		[Test]
		public void AGuessIsTheBestRankedSpaceWhateverItScores()
		{
			Dictionary<string, float> scores = new() { ["mall"] = 0.61f, ["apartment"] = 0.63f, ["office"] = 0.4f };
			Assert.That(SpaceRanking.BestOfTop(scores, 3), Is.EqualTo("apartment"));
		}

		[Test]
		public void AGuessPrefersWhatThePlayerMadeMoreOfOnlyAmongTheBestRanked()
		{
			Dictionary<string, float> scores = new() { ["mall"] = 0.61f, ["apartment"] = 0.63f, ["office"] = 0.4f };
			System.Func<string, int> priority = id => id == "apartment" ? 0 : 2;
			Assert.That(SpaceRanking.BestOfTop(scores, 3, priority), Is.EqualTo("mall"));
			Assert.That(SpaceRanking.BestOfTop(scores, 2, priority), Is.EqualTo("mall"));
			Assert.That(SpaceRanking.BestOfTop(scores, 1, priority), Is.EqualTo("apartment"));
		}

		[Test]
		public void ThereIsNoGuessWithoutSpaces()
		{
			Assert.That(SpaceRanking.BestOfTop(new Dictionary<string, float>(), 3), Is.Null);
		}

		[Test]
		public void NothingIsRecognizedBeforeTheWindowFills()
		{
			RecognitionVotes votes = new(3);
			Assert.That(votes.Add(Scores(1f), 0.75f), Is.Null);
			Assert.That(votes.Add(Scores(1f), 0.75f), Is.Null);
			Assert.That(votes.Add(Scores(1f), 0.75f), Is.EqualTo("mall"));
		}

		[Test]
		public void OneHighKeyframeAmongLowOnesIsNotEnough()
		{
			RecognitionVotes votes = new(3);
			votes.Add(Scores(0.6f), 0.75f);
			votes.Add(Scores(0.9f), 0.75f);
			Assert.That(votes.Add(Scores(0.6f), 0.75f), Is.Null);
		}

		[Test]
		public void TheHighestSpaceAboveTheMinimumWins()
		{
			RecognitionVotes votes = new(2);
			votes.Add(Scores(0.8f, 0.9f), 0.75f);
			Assert.That(votes.Add(Scores(0.8f, 0.9f), 0.75f), Is.EqualTo("apartment"));
		}

		[Test]
		public void ASpaceThePlayerMadeMoreOfWinsOverAHigherScore()
		{
			RecognitionVotes votes = new(2);
			int Priority(string id) => id == "mall" ? 1 : 0;
			votes.Add(Scores(0.8f, 0.9f), 0.75f, Priority);
			Assert.That(votes.Add(Scores(0.8f, 0.9f), 0.75f, Priority), Is.EqualTo("mall"));
		}

		[Test]
		public void PriorityDoesNotLiftASpaceThatDoesNotLookLikeHere()
		{
			RecognitionVotes votes = new(2);
			int Priority(string id) => id == "mall" ? 2 : 0;
			votes.Add(Scores(0.6f, 0.9f), 0.75f, Priority);
			Assert.That(votes.Add(Scores(0.6f, 0.9f), 0.75f, Priority), Is.EqualTo("apartment"));
		}

		[Test]
		public void PriorityCountsAGivenNameAndPlacedObjects()
		{
			Space generated = Space.Create("Space 12");
			Assert.That(generated.Priority, Is.EqualTo(0));

			Space named = Space.Create("APARTMENT");
			Assert.That(named.Priority, Is.EqualTo(1));

			Map arranged = Map.Create("Map 1");
			arranged.objects.Add(new MapObjectRecord { prefabId = "base", pose = Pose.identity });
			named.maps.Add(arranged);
			Assert.That(named.Priority, Is.EqualTo(2));

			generated.maps.Add(arranged);
			Assert.That(generated.Priority, Is.EqualTo(1));
		}

		[Test]
		public void OldKeyframesLeaveTheWindow()
		{
			RecognitionVotes votes = new(2);
			votes.Add(Scores(0.1f), 0.75f);
			votes.Add(Scores(0.9f), 0.75f);
			Assert.That(votes.Add(Scores(0.9f), 0.75f), Is.EqualTo("mall"));
		}

		[Test]
		public void ClearingForgetsTheWindow()
		{
			RecognitionVotes votes = new(2);
			votes.Add(Scores(0.9f), 0.75f);
			votes.Clear();
			Assert.That(votes.Add(Scores(0.9f), 0.75f), Is.Null);
		}
	}
}
