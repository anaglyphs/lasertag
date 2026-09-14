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
		[TestCase(ColocationManager.ColocationMethod.TwoAprilTags, false, false, true)]
		[TestCase(ColocationManager.ColocationMethod.TwoAprilTags, false, true, true)]
		[TestCase(ColocationManager.ColocationMethod.TwoAprilTags, true, true, true)]
		[TestCase(ColocationManager.ColocationMethod.AprilTag, false, false, true)]
		[TestCase(ColocationManager.ColocationMethod.AprilTag, false, true, true)]
		[TestCase(ColocationManager.ColocationMethod.MetaSharedAnchor, true, true, true)]
		[TestCase(ColocationManager.ColocationMethod.MetaSharedAnchor, false, false, true)]
		[TestCase(ColocationManager.ColocationMethod.MetaSharedAnchor, false, true, true)]
		public void StartupAndPickerAcceptEverySupportedAlignmentMethod(
			ColocationManager.ColocationMethod method, bool tags, bool anchors, bool expected)
		{
			var space = MapSpace.Create("Test"); space.preferredColocationMethod = method;
			Assert.That(ColocationManager.IsValidMethod(space.preferredColocationMethod), Is.EqualTo(expected));
		}

		[Test]
		public void OperatorCanChooseAlignmentButHeadsetRequestsRemainBlocked()
		{
			Assert.That(MapPolicy.ColocationPreferenceBlocker(true, false, true, operatorRequest: true), Is.Null);
			Assert.That(MapPolicy.ColocationPreferenceBlocker(true, false, true, operatorRequest: false),
				Is.EqualTo("blocker.the-operator-sets-the-alignment-method"));
			Assert.That(MapPolicy.ColocationMethodBlocker(ColocationManager.ColocationMethod.AprilTag,
				true, false, true, operatorRequest: false), Is.EqualTo("blocker.the-operator-sets-the-alignment-method"));
		}

		[Test]
		public void AlignmentSelectionRequiresASpaceAndWaitsForTheRoundToEnd()
		{
			Assert.That(MapPolicy.ColocationPreferenceBlocker(true, true, true, operatorRequest: true),
				Is.EqualTo("blocker.wait-until-the-round-ends"));
			Assert.That(MapPolicy.ColocationPreferenceBlocker(true, true, true, operatorRequest: false),
				Is.EqualTo("blocker.wait-until-the-round-ends"));
			Assert.That(MapPolicy.ColocationPreferenceBlocker(false, true, true, operatorRequest: true),
				Is.EqualTo("space.load-first"));
		}
	}
}
