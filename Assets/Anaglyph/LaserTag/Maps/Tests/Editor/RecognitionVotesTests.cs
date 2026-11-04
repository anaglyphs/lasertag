using System.Collections.Generic;
using Anaglyph.LaserTag.Maps;
using NUnit.Framework;

namespace Anaglyph.LaserTag.Tests
{
	public class RecognitionVotesTests
	{
		private static Dictionary<string, float> Scores(float mall, float apartment = 0.5f) =>
			new() { ["mall"] = mall, ["apartment"] = apartment };

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
