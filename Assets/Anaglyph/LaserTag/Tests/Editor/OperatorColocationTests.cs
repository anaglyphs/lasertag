using Anaglyph.LaserTag.Maps;
using Anaglyph.LaserTag.Operator;
using NUnit.Framework;
using UnityEngine;

namespace Anaglyph.LaserTag.Tests
{
	public class OperatorColocationTests
	{
		[TestCase(ColocationManager.ColocationMethod.SystemDetermined, false, false, true)]
		[TestCase(ColocationManager.ColocationMethod.SystemDetermined, false, true, true)]
		[TestCase(ColocationManager.ColocationMethod.SystemDetermined, true, true, true)]
		[TestCase(ColocationManager.ColocationMethod.AprilTag, false, false, true)]
		[TestCase(ColocationManager.ColocationMethod.AprilTag, false, true, false)]
		[TestCase(ColocationManager.ColocationMethod.MetaSharedAnchor, true, true, true)]
		[TestCase(ColocationManager.ColocationMethod.MetaSharedAnchor, false, true, false)]
		public void StartupAndPickerAcceptSystemMapsWithoutRequiringTags(
			ColocationManager.ColocationMethod method, bool tags, bool anchors, bool expected)
		{
			GameMap map = new() { preferredColocationMethod = method };
			if (tags) map.SetTag(7, Pose.identity);
			if (anchors) map.SetAnchorWithTag(System.Guid.NewGuid().ToString("N"), Pose.identity, -1);
			Assert.That(OperatorHost.CanHostMap(map), Is.EqualTo(expected));
		}

		[TestCase(MapPhase.Local)]
		[TestCase(MapPhase.Hosting)]
		public void OperatorCanChooseAlignmentButHeadsetRequestsRemainBlocked(MapPhase phase)
		{
			var policy = Policy(phase);
			Assert.That(policy.ColocationPreferenceBlockerFor(operatorRequest: true), Is.Null);
			Assert.That(policy.ColocationPreferenceBlockerFor(operatorRequest: false),
				Is.EqualTo("blocker.the-operator-sets-the-alignment-method"));
			Assert.That(policy.ColocationMethodBlocker(targetHasReferences: true), Is.Null);
		}

		[Test]
		public void OperatorCannotChangeAlignmentDuringRoundsOrTransitions()
		{
			Assert.That(Policy(MapPhase.Hosting, playing: true).ColocationPreferenceBlockerFor(true),
				Is.EqualTo("blocker.wait-until-the-round-ends"));
			Assert.That(Policy(MapPhase.Hosting, holding: true).ColocationPreferenceBlockerFor(true), Is.Not.Null);
			Assert.That(Policy(MapPhase.SwitchingMap).ColocationPreferenceBlockerFor(true), Is.Not.Null);
		}

		private static MapPolicy Policy(MapPhase phase, bool playing = false, bool holding = false) => new(
			phase, hasMap: true, empty: false, hasTags: false, frameAgrees: false,
			sessionHolding: holding, roundInProgress: playing, sessionUsesTags: false,
			operatorManagedSession: true, hasAnchors: false);
	}
}
