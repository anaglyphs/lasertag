using Anaglyph.LaserTag.Weapons.Bullets;
using NUnit.Framework;
using UnityEngine;

namespace Anaglyph.LaserTag.Tests
{
	public class ProjectileBarrierHistoryTests
	{
		private static readonly Ray Shot = new(Vector3.zero, Vector3.forward);

		[Test]
		public void ExpiredLocalShieldStillRejectsDelayedHit()
		{
			var history = new ProjectileBarrierHistory();
			history.Record(7, new Vector3(-1, 0, 5), new Vector3(1, 0, 5), 0.2f, 0.4f, 0.65f);
			Assert.That(history.Blocked(7, Shot, 10, 0, 10, 0.01f, 1.5f), Is.True);
		}

		[Test]
		public void ShieldOutsideFlightTimeCannotRejectHit()
		{
			var history = new ProjectileBarrierHistory();
			history.Record(7, new Vector3(-1, 0, 5), new Vector3(1, 0, 5), 0.2f, 0.6f, 0.85f);
			Assert.That(history.Blocked(7, Shot, 10, 0, 10, 0.01f, 1.5f), Is.False);
		}

		[Test]
		public void OtherPlayersShieldsAndShieldsBeyondTargetCannotRejectHit()
		{
			var history = new ProjectileBarrierHistory();
			history.Record(8, new Vector3(-1, 0, 5), new Vector3(1, 0, 5), 0.2f, 0.4f, 0.65f);
			Assert.That(history.Blocked(7, Shot, 10, 0, 10, 0.01f, 1.5f), Is.False);
			Assert.That(history.Blocked(8, Shot, 10, 0, 3, 0.01f, 1.5f), Is.False);
		}

		[Test]
		public void CapsuleAndProjectileRadiiBothCount()
		{
			var history = new ProjectileBarrierHistory();
			history.Record(7, new Vector3(-1, 0.6f, 5), new Vector3(1, 0.6f, 5), 0.2f, 0.4f, 0.65f);
			Assert.That(history.Blocked(7, Shot, 10, 0, 10, 0.5f, 1.5f), Is.True);
			Assert.That(history.Blocked(7, Shot, 10, 0, 10, 0.01f, 1.5f), Is.False);
		}

		[Test]
		public void ParallelAndStationaryCapsulesAreSupported()
		{
			var history = new ProjectileBarrierHistory();
			history.Record(7, new Vector3(0, 0, 4), new Vector3(0, 0, 6), 0.2f, 0.4f, 0.65f);
			history.Record(8, new Vector3(0, 0, 5), new Vector3(0, 0, 5), 0.2f, 0.4f, 0.65f);
			Assert.That(history.Blocked(7, Shot, 10, 0, 10, 0, 1.5f), Is.True);
			Assert.That(history.Blocked(8, Shot, 10, 0, 10, 0, 1.5f), Is.True);
		}

		[Test]
		public void HistoryIsBoundedInTimeAndCapacity()
		{
			var history = new ProjectileBarrierHistory();
			history.Record(7, new Vector3(-1, 0, 5), new Vector3(1, 0, 5), 0.2f, 0.4f, 0.65f);
			Assert.That(history.Blocked(7, Shot, 10, 0, 10, 0, 4), Is.False);
			for (int i = 0; i < 512; i++) history.Record(8, Vector3.zero, Vector3.one, 0.1f, 1, 2);
			Assert.That(history.Blocked(7, Shot, 10, 0, 10, 0, 1.5f), Is.False);
		}

		[Test]
		public void NewSessionClearsOldShieldHistory()
		{
			var history = new ProjectileBarrierHistory();
			history.Record(7, new Vector3(-1, 0, 5), new Vector3(1, 0, 5), 0.2f, 0.4f, 0.65f);
			history.Clear();
			Assert.That(history.Blocked(7, Shot, 10, 0, 10, 0, 1.5f), Is.False);
		}

	}
}
