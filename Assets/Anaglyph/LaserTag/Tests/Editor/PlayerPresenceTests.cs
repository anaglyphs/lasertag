using System;
using System.Collections.Generic;
using System.Reflection;
using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.Maps;
using Anaglyph.LaserTag.Objects.Gameplay.Control_Point;
using Anaglyph.LaserTag.Player;
using Anaglyph.LaserTag.Player.Teams;
using Anaglyph.Netcode;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Anaglyph.LaserTag.Tests
{
	public class PlayerPresenceTests
	{
		private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
		private readonly List<GameObject> objects = new();
		private Dictionary<ulong, PlayerAvatar> previousPlayers;
		private PlayerAvatar previousLocal;
		private ColocationManager previousColocation;
		private LaserTagMapCoordinator previousCoordinator;
		private Guid mapId;

		[SetUp]
		public void SetUp()
		{
			previousPlayers = PlayerAvatar.All;
			previousLocal = PlayerAvatar.Local;
			previousColocation = ColocationManager.Instance;
			previousCoordinator = LaserTagMapCoordinator.Instance;
			SetStatic(typeof(PlayerAvatar), "All", new Dictionary<ulong, PlayerAvatar>());
			SetStatic(typeof(PlayerAvatar), "Local", null);
			mapId = Guid.NewGuid();
			var context = Create("Presence context");
			var colocation = context.AddComponent<ColocationManager>();
			Set(colocation, "offlineMethod", ColocationManager.ColocationMethod.SystemDetermined);
			SetStatic(typeof(ColocationManager), "Instance", colocation);
			var coordinator = context.AddComponent<LaserTagMapCoordinator>();
			var spaces = new MapSpaceManager(new MapSpaceStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
			spaces.Load(MapSpace.Create("Presence test")); Set(coordinator, "spaces", spaces);
			((MapWorkflow)Get(coordinator, "workflow")).BeginHosting(false, 0);
			SetStatic(typeof(LaserTagMapCoordinator), "Instance", coordinator);
		}

		[TearDown]
		public void TearDown()
		{
			SetStatic(typeof(PlayerAvatar), "All", previousPlayers);
			SetStatic(typeof(PlayerAvatar), "Local", previousLocal);
			SetStatic(typeof(ColocationManager), "Instance", previousColocation);
			SetStatic(typeof(LaserTagMapCoordinator), "Instance", previousCoordinator);
			foreach (GameObject owner in objects)
			{
				foreach (NetworkBehaviour component in owner.GetComponentsInChildren<NetworkBehaviour>(true))
					Spawned(component, false);
				Object.DestroyImmediate(owner);
			}
			objects.Clear();
		}

		[Test]
		public void ConnectedObjectSurvivesSittingOutBeforeFirstAlignment()
		{
			var owner = Create("Connected identity");
			var networkObject = owner.AddComponent<NetworkObject>();
			var spawner = owner.AddComponent<PlayerAvatarSpawner>();
			Set(spawner, "spawned", networkObject);
			var state = typeof(NetcodeManagement).GetField("_state", BindingFlags.Static | BindingFlags.NonPublic);
			object previous = state.GetValue(null);
			try
			{
				state.SetValue(null, NetcodeState.Connected);
				spawner.SetIsParticipating(false);
				Invoke(spawner, "Handle");
				Assert.That(Get(spawner, "spawned"), Is.SameAs(networkObject));
				spawner.SetIsParticipating(true);
				Invoke(spawner, "Handle");
				Assert.That(Get(spawner, "spawned"), Is.SameAs(networkObject));
			}
			finally { state.SetValue(null, previous); }
		}

		[Test]
		public void ReadinessIsAvailableBeforeAlignmentAndRequiresNativeCapability()
		{
			var readiness = Ready(false);
			Assert.That(readiness.CanMintSharedAnchors, Is.True);
			Assert.That(readiness.IsAlignedTo(mapId, LaserTagMapCoordinator.Instance.CanonicalFrameId, LaserTagMapCoordinator.Instance.ReferenceContext), Is.False);
			readiness.isOperator = true;
			Assert.That(readiness.CanMintSharedAnchors, Is.False);
			readiness.isOperator = false;
			readiness.supportsSharedAnchors = false;
			Assert.That(readiness.CanMintSharedAnchors, Is.False);
			readiness = Ready(false);
			readiness.isHeadTracked = false;
			Assert.That(readiness.CanMintSharedAnchors, Is.False);
			readiness = Ready(false);
			readiness.isFocused = false;
			Assert.That(readiness.CanMintSharedAnchors, Is.False);
		}

		[Test]
		public void PreviousMapAndPreviousMethodCannotMakeAnAvatarPresent()
		{
			PlayerAvatar player = Player(1);
			Assert.That(player.HasSpatialPresence, Is.True);
			var readiness = Ready(true);
			readiness.mapId = Guid.NewGuid();
			NetworkValue(player.HeadsetStatus, "readinessSync", readiness);
			Assert.That(player.HasSpatialPresence, Is.False);
			readiness = Ready(true);
			readiness.frameId = Guid.NewGuid();
			NetworkValue(player.HeadsetStatus, "readinessSync", readiness);
			Assert.That(player.HasSpatialPresence, Is.False);
		}

		[Test]
		public void AlignmentLossPreservesLifeTeamScoreAndEliminationMembership()
		{
			PlayerAvatar player = Player(1);
			player.scoreSync = new NetworkVariable<int>(42);
			int deaths = 0;
			player.Killed += () => deaths++;
			Invoke(player, "RefreshParticipation");
			NetworkValue(player.HeadsetStatus, "readinessSync", Ready(false));
			Invoke(player, "RefreshParticipation");
			Assert.That(player.CanInteract, Is.False);
			Assert.That(player.IsRoundParticipant, Is.True);
			Assert.That(player.IsAlive, Is.True);
			Assert.That(player.Health, Is.EqualTo(MatchSettings.MaxHealth));
			Assert.That(player.Team, Is.EqualTo(1));
			Assert.That(player.Score, Is.EqualTo(42));
			Assert.That(deaths, Is.Zero);
			NetworkValue(player.HeadsetStatus, "readinessSync", Ready(true));
			Invoke(player, "RefreshParticipation");
			Assert.That(player.CanInteract, Is.True);
		}

		[Test]
		public void DeadPlayersKeepSpatialBasePresenceButCannotInteract()
		{
			PlayerAvatar player = Player(1);
			NetworkValue(player, "isAliveSync", false);
			Assert.That(player.HasSpatialPresence, Is.True);
			Assert.That(player.CanInteract, Is.False);
			Assert.That(player.IsRoundParticipant, Is.True);
		}

		[Test]
		public void WaitingPlayersDoNotChangeEliminationAndAlignmentLossIsNotDeath()
		{
			PlayerAvatar first = Player(1);
			PlayerAvatar second = Player(2);
			PlayerAvatar waiting = Player(0, enteredPlay: false);
			NetworkValue(waiting.HeadsetStatus, "readinessSync", Ready(false));
			Assert.That(Winner(out _), Is.False);
			NetworkValue(second.HeadsetStatus, "readinessSync", Ready(false));
			Assert.That(Winner(out _), Is.False);
			NetworkValue(second, "isAliveSync", false);
			Assert.That(Winner(out byte team), Is.True);
			Assert.That(team, Is.EqualTo(first.Team));
		}

		[Test]
		public void SittingOutPlayersNeitherBlockMusterNorProduceAnEliminationDraw()
		{
			Player(0, participating: false, enteredPlay: false);
			Assert.That(typeof(MatchReferee).GetMethod("AllPlayersInBase", BindingFlags.Static | BindingFlags.NonPublic)
				.Invoke(null, null), Is.False);
			Assert.That(Winner(out _), Is.False);
		}

		[Test]
		public void PresentationHidesWithoutDisablingNetworkComponentsOrOverridingOwnerVisibility()
		{
			PlayerAvatar player = Player(1);
			var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
			body.transform.SetParent(player.transform);
			var renderer = body.GetComponent<Renderer>();
			var collider = body.GetComponent<Collider>();
			renderer.enabled = false; // equivalent to DontRenderIfOwner
			var presentation = player.gameObject.AddComponent<AvatarPresentation>();
			Invoke(presentation, "Awake");
			Assert.That(renderer.forceRenderingOff, Is.False);
			Assert.That(renderer.enabled, Is.False);
			Assert.That(collider.enabled, Is.True);
			NetworkValue(player.HeadsetStatus, "readinessSync", Ready(false));
			Invoke(presentation, "Refresh");
			Assert.That(renderer.forceRenderingOff, Is.True);
			Assert.That(collider.enabled, Is.False);
			Assert.That(player.enabled && player.HeadsetStatus.enabled, Is.True);
			Assert.That(player.IsSpawned, Is.True);
			NetworkValue(player.HeadsetStatus, "readinessSync", Ready(true));
			Invoke(presentation, "Refresh");
			Assert.That(renderer.forceRenderingOff, Is.False);
			Assert.That(renderer.enabled, Is.False);
			Assert.That(collider.enabled, Is.True);
		}

		[Test]
		public void DespawnDoesNotReportGameplayDeath()
		{
			PlayerAvatar player = Player(1);
			int deaths = 0;
			player.Killed += () => deaths++;
			player.OnNetworkDespawn();
			Assert.That(deaths, Is.Zero);
		}

		[Test]
		public void PrefabKeepsStatusAndNetworkTransformsEnabled()
		{
			var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Anaglyph/LaserTag/Player/Avatar.prefab");
			Assert.That(prefab.GetComponent<PlayerHeadsetStatus>(), Is.Not.Null);
			Assert.That(prefab.GetComponent<AvatarPresentation>(), Is.Not.Null);
			foreach (NetworkBehaviour component in prefab.GetComponentsInChildren<NetworkBehaviour>(true))
			{
				Assert.That(component.enabled, Is.True, component.GetType().Name);
				for (Transform parent = component.transform; parent != null; parent = parent.parent)
					Assert.That(parent.gameObject.activeSelf, Is.True, parent.name);
			}
		}

		[Test]
		public void TrackingCannotShowWeaponsWhileSpatialPresenceIsHidden()
		{
			PlayerAvatar player = Player(1);
			var weapon = player.gameObject.AddComponent<AvatarWeaponVisual>();
			Invoke(weapon, "Awake");
			Spawned(weapon, true);
			NetworkValue(weapon, "shownSync", true);
			var wrapper = (GameObject)Get(weapon, "presentationRoot");
			var instance = new GameObject("Tracked weapon");
			instance.transform.SetParent(wrapper.transform);
			Set(weapon, "instance", instance);
			Invoke(weapon, "ApplySyncedState");
			Assert.That(wrapper.activeSelf, Is.True);
			NetworkValue(player.HeadsetStatus, "readinessSync", Ready(false));
			Invoke(weapon, "ApplySyncedState");
			instance.SetActive(false);
			instance.SetActive(true); // a tracking callback only controls this child
			Assert.That(wrapper.activeSelf, Is.False);
			Assert.That(weapon.enabled, Is.True);
		}

		[Test]
		public void StaleControlPointOccupancyCannotCaptureWhileUnaligned()
		{
			PlayerAvatar player = Player(1);
			var point = Create("Control point").AddComponent<ControlPoint>();
			((HashSet<PlayerAvatar>)Get(point, "playersInside")).Add(player);
			var contains = typeof(ControlPoint).GetMethod("CheckIfPlayerIsInside", Private);
			Assert.That(contains.Invoke(point, new object[] { player }), Is.True);
			NetworkValue(player.HeadsetStatus, "readinessSync", Ready(false));
			Assert.That(contains.Invoke(point, new object[] { player }), Is.False);
		}

		private GameObject Create(string name)
		{
			var owner = new GameObject(name);
			owner.SetActive(false);
			objects.Add(owner);
			return owner;
		}

		private PlayerAvatar Player(byte team, bool participating = true, bool enteredPlay = true)
		{
			var owner = Create("Player presence test");
			owner.AddComponent<NetworkObject>();
			var teams = owner.AddComponent<TeamOwner>();
			NetworkValue(teams, "teamSync", team);
			var player = owner.AddComponent<PlayerAvatar>();
			Set(player, "teamOwner", teams);
			Invoke(player, "Awake");
			Spawned(player, true);
			Spawned(player.HeadsetStatus, true);
			Set(player.HeadsetStatus, "currentMapId", mapId);
			NetworkValue(player.HeadsetStatus, "readinessSync", Ready(true));
			object state = Activator.CreateInstance(typeof(PlayerAvatar).GetNestedType("ParticipationState", BindingFlags.NonPublic));
			state.GetType().GetField("participating").SetValue(state, participating);
			state.GetType().GetField("enteredPlay").SetValue(state, enteredPlay);
			NetworkValue(player, "participationSync", state);
			PlayerAvatar.All[(ulong)PlayerAvatar.All.Count + 1] = player;
			return player;
		}

		private HeadsetReadiness Ready(bool aligned) => new()
		{
			hasAnchorRuntime = true, supportsSharedAnchors = true, isHeadTracked = true,
			isFocused = true, aligned = aligned, mapId = mapId, frameId = LaserTagMapCoordinator.Instance.CanonicalFrameId, referenceContext = LaserTagMapCoordinator.Instance.ReferenceContext,
			method = ColocationManager.ColocationMethod.SystemDetermined
		};

		private static bool Winner(out byte team)
		{
			object[] args = { (byte)0 };
			bool won = (bool)typeof(MatchReferee).GetMethod("TryGetEliminationWinner", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
			team = (byte)args[0];
			return won;
		}

		// Populate received values without starting a transport or invoking a native XR runtime.
		private static void NetworkValue(object owner, string field, object value)
		{
			object variable = owner.GetType().GetField(field, Private | BindingFlags.Public).GetValue(owner);
			variable.GetType().GetField("m_InternalValue", Private).SetValue(variable, value);
		}
		private static void Spawned(NetworkBehaviour component, bool value) => typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(component, value);
		private static object Get(object owner, string field) => owner.GetType().GetField(field, Private).GetValue(owner);
		private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Private).SetValue(owner, value);
		private static void SetStatic(Type type, string property, object value) => type.GetProperty(property).SetValue(null, value);
		private static void Invoke(object owner, string method) => owner.GetType().GetMethod(method, Private).Invoke(owner, null);
	}
}
