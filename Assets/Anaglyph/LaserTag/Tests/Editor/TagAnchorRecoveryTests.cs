using System;
using System.Collections;
using System.Reflection;
using Anaglyph.XR.SharedSpaces.AprilTags;
using NUnit.Framework;
using UnityEngine;

namespace Anaglyph.LaserTag.Tests
{
	public class TagAnchorRecoveryTests
	{
		private GameObject owner;
		private AprilTagColocationConstraintProvider provider;
		private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
		private static readonly Type ProviderType = typeof(AprilTagColocationConstraintProvider);

		[SetUp]
		public void SetUp()
		{
			// Keep Awake/network registration and all native anchor operations out of these tests.
			owner = new GameObject("Tag anchor recovery test");
			owner.SetActive(false);
			provider = owner.AddComponent<AprilTagColocationConstraintProvider>();
			provider.SetRegisteredTags(new[] { new TagConstraintData(1, Pose.identity) });
		}

		[TearDown]
		public void TearDown() => UnityEngine.Object.DestroyImmediate(owner);

		private object SavedAnchor(Guid guid)
		{
			ProviderType.GetProperty("IsRunning").SetValue(provider, false);
			provider.SetLocalAnchors(new[] { new TaggedAnchorConstraintData(guid, 1, Pose.identity) });
			ProviderType.GetProperty("IsRunning").SetValue(provider, true);
			return ((IDictionary)ProviderType.GetField("localAnchors", PrivateInstance).GetValue(provider))[1];
		}

		private int Generation => (int)ProviderType.GetField("stateGeneration", PrivateInstance).GetValue(provider);

		private bool Commit(Guid guid, int generation, object replacing)
		{
			Type mintedType = ProviderType.Assembly.GetType("Anaglyph.XR.SharedSpaces.SharedAnchors.MintedAnchor", true);
			object minted = Activator.CreateInstance(mintedType,
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
				null, new object[] { null, guid, true }, null);
			return (bool)ProviderType.GetMethod("CommitTagAnchor", PrivateInstance).Invoke(provider,
				new object[] { 1, Pose.identity, generation, minted, replacing });
		}

		private Guid RecordedGuid()
		{
			var records = new System.Collections.Generic.List<TaggedAnchorConstraintData>();
			provider.GetLocalAnchorConstraints(records);
			Assert.That(records.Count, Is.EqualTo(1));
			return records[0].guid;
		}

		[Test]
		public void UnrestoredSavedAnchorCanBeReplacedWithoutDroppingItsTag()
		{
			object saved = SavedAnchor(Guid.NewGuid());
			Guid replacement = Guid.NewGuid();
			int changed = 0;
			provider.AnchorsChanged += () => changed++;
			Assert.That(Commit(replacement, Generation, saved), Is.True);
			Assert.That(RecordedGuid(), Is.EqualTo(replacement));
			Assert.That(provider.RegisteredTags.ContainsKey(1), Is.True);
			Assert.That(changed, Is.EqualTo(1));
		}

		[Test]
		public void StaleMintCannotReplaceAnAnchorFromANewerMapImport()
		{
			object saved = SavedAnchor(Guid.NewGuid());
			int oldGeneration = Generation;
			Guid newer = Guid.NewGuid();
			SavedAnchor(newer);
			Assert.That(Commit(Guid.NewGuid(), oldGeneration, saved), Is.False);
			Assert.That(RecordedGuid(), Is.EqualTo(newer));
		}

		[Test]
		public void MintMustStillReplaceTheSameAnchorEvenWithACurrentGeneration()
		{
			object saved = SavedAnchor(Guid.NewGuid());
			Guid newer = Guid.NewGuid();
			SavedAnchor(newer);
			Assert.That(Commit(Guid.NewGuid(), Generation, saved), Is.False);
			Assert.That(RecordedGuid(), Is.EqualTo(newer));
		}

		[Test]
		public void FreshTagWithoutASavedAnchorStillAcceptsItsFirstMint()
		{
			ProviderType.GetProperty("IsRunning").SetValue(provider, true);
			Guid fresh = Guid.NewGuid();
			Assert.That(Commit(fresh, Generation, null), Is.True);
			Assert.That(RecordedGuid(), Is.EqualTo(fresh));
		}
	}
}
