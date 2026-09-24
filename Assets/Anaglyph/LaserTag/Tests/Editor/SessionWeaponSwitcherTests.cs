using System.Reflection;
using Anaglyph.LaserTag.Weapons;
using Anaglyph.Netcode;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR.Input;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Anaglyph.LaserTag.Tests
{
	public class SessionWeaponSwitcherTests
	{
		private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
		private GameObject owner;
		private WeaponSwitcher local;
		private SessionWeaponSwitcher session;
		private WeaponDatabase database;
		private SyncEvent<int> request;

		[SetUp]
		public void SetUp()
		{
			Assert.That(SyncBus.Active, Is.False);
			Assert.That(WeaponSwitcher.Instance, Is.Null);
			Assert.That(SessionWeaponSwitcher.Instance, Is.Null);
			database = AssetDatabase.LoadAssetAtPath<WeaponDatabase>("Assets/Anaglyph/LaserTag/Weapons/Weapon Database.asset");
			owner = new GameObject("Session weapons test");
			owner.SetActive(false);
			local = owner.AddComponent<WeaponSwitcher>();
			session = owner.AddComponent<SessionWeaponSwitcher>();
			var serialized = new SerializedObject(session);
			serialized.FindProperty("database").objectReferenceValue = database;
			serialized.ApplyModifiedPropertiesWithoutUndo();
			typeof(WeaponSwitcher).GetMethod("Awake", Private).Invoke(local, null);
			typeof(SessionWeaponSwitcher).GetMethod("Awake", Private).Invoke(session, null);
			request = (SyncEvent<int>)typeof(SessionWeaponSwitcher).GetField("switchEveryone", Private).GetValue(session);
		}

		[TearDown]
		public void TearDown()
		{
			if (session != null) typeof(SessionWeaponSwitcher).GetMethod("OnDestroy", Private).Invoke(session, null);
			if (local != null) typeof(WeaponSwitcher).GetMethod("OnDestroy", Private).Invoke(local, null);
			Object.DestroyImmediate(owner);
		}

		[Test]
		public void ValidRequestsSwitchBothHandsAndCanRepeatAfterPickup()
		{
			for (int id = 0; id < database.Count; id++)
			{
				request.Raise(id);
				Assert.That(local.GetSelected(Handedness.Left), Is.SameAs(database.GetWeapon(id)));
				Assert.That(local.GetSelected(Handedness.Right), Is.SameAs(database.GetWeapon(id)));
				local.SwitchWeapon(database.GetWeapon((id + 1) % database.Count), Handedness.Left);
				request.Raise(id);
				Assert.That(local.GetSelected(Handedness.Left), Is.SameAs(database.GetWeapon(id)));
			}
		}

		[Test]
		public void InvalidRequestsAreRejectedBeforeChangingWeapons()
		{
			request.Raise(0);
			foreach (int id in new[] { WeaponDatabase.NoWeapon, database.Count, int.MaxValue })
			{
				request.Raise(id);
				Assert.That(local.GetSelected(Handedness.Left), Is.SameAs(database.GetWeapon(0)));
				Assert.That(local.GetSelected(Handedness.Right), Is.SameAs(database.GetWeapon(0)));
			}
		}

		[Test]
		public void BroadcastAppliesOnReceivingPeerWithoutOpeningMenu()
		{
			byte[] data = new byte[sizeof(ulong) + sizeof(int)];
			SyncBytes.Write(data, 0, 123UL);
			SyncBytes.Write(data, sizeof(ulong), database.Count - 1);
			typeof(SyncEventBase).GetMethod("ApplyBroadcast", Private).Invoke(request, new object[] { data });
			Assert.That(local.GetSelected(Handedness.Left), Is.SameAs(database.GetWeapon(database.Count - 1)));
			Assert.That(local.GetSelected(Handedness.Right), Is.SameAs(database.GetWeapon(database.Count - 1)));
		}

		[Test]
		public void DisconnectedCommandsDoNothingAndSelectionsResetWithSession()
		{
			session.SwitchEveryone(0);
			Assert.That(local.GetSelected(Handedness.Left), Is.Null);
			request.Raise(0);
			typeof(WeaponSwitcher).GetMethod("OnNetcodeStateChanged", Private).Invoke(local, new object[] { NetcodeState.Disconnected });
			Assert.That(local.GetSelected(Handedness.Left), Is.Null);
			Assert.That(local.GetSelected(Handedness.Right), Is.Null);
		}
	}
}
