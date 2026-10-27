using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Anaglyph.LaserTag.MapEditor;
using Anaglyph.LaserTag.Maps;
using Anaglyph.Netcode;
using Anaglyph.Netcode.SyncVariables;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;
using Space = Anaglyph.LaserTag.Maps.Space;

namespace Anaglyph.LaserTag.Tests.PlayMode
{
	/// <summary>
	/// A two-editor LAN session, one side per editor process: run <see cref="HostSide"/> in one
	/// editor and <see cref="ClientSide"/> in another, both started with
	/// <c>-sessionDir &lt;shared folder&gt;</c>. The sides hand off through marker files in that
	/// folder. Covers join (the client shows the host's space and map), a map switch, a method
	/// switch that moves the frame, a client request the host applies, and the client's
	/// disconnect bringing its own space back. Explicit host/connect calls: the editor's
	/// auto-connect preference must be off for the run. Session state is read from the bus and
	/// NetworkManager, because NetcodeManagement hooks its state callbacks on the first scene
	/// load, which in a test run is the runner's empty scene.
	/// </summary>
	public class SessionSpaceTests
	{
		private const float Startup = 300f, Step = 90f;
		private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

		private static string SessionDir
		{
			get
			{
				string[] args = Environment.GetCommandLineArgs();
				for (int i = 0; i < args.Length - 1; i++)
					if (args[i] == "-sessionDir") return args[i + 1];
				return Path.Combine(Path.GetTempPath(), "lasertag-session");
			}
		}

		private static void Mark(string name, string content = "") => File.WriteAllText(Path.Combine(SessionDir, name), content);
		private static string ReadMark(string name) => File.ReadAllText(Path.Combine(SessionDir, name)).Trim();

		private static IEnumerator WaitFor(Func<bool> condition, float seconds, string what)
		{
			float deadline = Time.realtimeSinceStartup + seconds;
			while (!condition())
			{
				if (Time.realtimeSinceStartup > deadline) Assert.Fail($"timed out after {seconds:F0} s waiting for {what}");
				yield return null;
			}
		}

		private static IEnumerator WaitMark(string name, float seconds) =>
			WaitFor(() => File.Exists(Path.Combine(SessionDir, name)), seconds, "marker " + name);

		private static IEnumerator Boot()
		{
			LogAssert.ignoreFailingMessages = true;
			Directory.CreateDirectory(SessionDir);
			yield return SceneManager.LoadSceneAsync("MainScene");
			yield return WaitFor(() => SpaceManager.Instance != null && SpaceManager.Instance.Space != null, 60f, "the map manager and a shown space");
			// NetcodeManagement hooks the NetworkManager (connection approval, state) on the first scene
			// load, which here was the test runner's empty scene: hook it now that the main scene is up
			yield return WaitFor(() => NetworkManager.Singleton != null, 30f, "the network manager");
			typeof(NetcodeManagement).GetMethod("OnSceneLoad", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
			NetworkManager.Singleton.LogLevel = LogLevel.Developer;
			Assume.That(SyncBus.Active, Is.False, "the editor must not auto-connect for this test");
			// XR Simulation creates its environment as a runtime scene with no path; NGO's join synchronization
			// hashes every loaded scene's path and throws on it, so scenes outside the build list are skipped
			NetworkManager.Singleton.OnServerStarted += SkipRuntimeScenes;
			NetworkManager.Singleton.OnClientStarted += SkipRuntimeScenes;
		}

		private static void SkipRuntimeScenes()
		{
			if (NetworkManager.Singleton.SceneManager != null)
				NetworkManager.Singleton.SceneManager.VerifySceneBeforeLoading = (buildIndex, name, mode) => buildIndex >= 0;
		}

		// what the network layer is doing, every few seconds, so a stuck connection leaves a trail in the log
		private static IEnumerator WaitForReporting(Func<bool> condition, float seconds, string what, string side)
		{
			float deadline = Time.realtimeSinceStartup + seconds;
			float nextReport = 0;
			while (!condition())
			{
				if (Time.realtimeSinceStartup >= nextReport)
				{
					nextReport = Time.realtimeSinceStartup + 5f;
					NetworkManager network = NetworkManager.Singleton;
					var transport = network != null ? network.NetworkConfig.NetworkTransport as Unity.Netcode.Transports.UTP.UnityTransport : null;
					Debug.Log($"[SessionSpaceTests] {side}: listening={network != null && network.IsListening} client={network != null && network.IsClient} connected={network != null && network.IsConnectedClient} " +
						$"clients={(network != null && network.IsListening ? network.ConnectedClientsIds.Count : -1)} bus={SyncBus.Active} authority={SyncBus.IsAuthority} state={NetcodeManagement.State} " +
						$"transport={(transport != null ? $"{transport.ConnectionData.Address}:{transport.ConnectionData.Port} listen {transport.ConnectionData.ServerListenAddress}" : "none")}");
				}
				if (Time.realtimeSinceStartup > deadline) Assert.Fail($"timed out after {seconds:F0} s waiting for {what}");
				yield return null;
			}
		}

		[UnityTest]
		public IEnumerator HostSide()
		{
			yield return Boot();
			SpaceManager manager = SpaceManager.Instance;
			List<string> created = new();
			try
			{
				Assert.That(manager.NewSpace(), Is.True);
				Space space = manager.Space;
				created.Add(space.id);
				UnityEngine.Object.Instantiate(FirstPrefab(manager).gameObject, new Vector3(1, 0, 2), Quaternion.identity);
				yield return null;
				Assert.That(manager.Save(), Is.True);
				Mark("host-space", $"{space.id}\n{manager.Map.id}\n{space.frameId}");

				NetcodeManagement.Host(NetcodeManagement.Protocol.LAN);
				yield return WaitForReporting(() => NetworkManager.Singleton && NetworkManager.Singleton.IsListening && SyncBus.Active && SyncBus.IsAuthority, Step, "hosting", "host");
				// the client editor already holds the main scene; a Single-mode synchronization would reload it
				// (and trips over the test runner's unnamed scene), so joiners keep what they have
				NetworkManager.Singleton.SceneManager.SetClientSynchronizationMode(LoadSceneMode.Additive);
				Assert.That(manager.Role, Is.EqualTo(SpaceRole.Host));
				yield return WaitFor(() => !manager.IsChangingMap, Step, "the host's map to settle");
				Mark("hosting");

				yield return WaitForReporting(() => File.Exists(Path.Combine(SessionDir, "client-joined")), Startup, "marker client-joined", "host");

				// map switch: a new map in the same space, so the frame stays
				Assert.That(manager.NewMap(), Is.True);
				yield return WaitFor(() => !manager.IsChangingMap, Step, "the map switch to settle");
				Mark("switched-map", manager.Map.id);
				yield return WaitMark("client-saw-map", Step);

				// method switch into two-tag mode: the frame moves, everyone re-aligns
				string frameBefore = space.frameId;
				Assert.That(manager.Alignment.SetMethod(Method.AprilTag), Is.True);
				yield return null;
				Assert.That(space.frameId, Is.Not.EqualTo(frameBefore));
				Assert.That(ColocationManager.Instance.ActiveMethod, Is.EqualTo(Method.AprilTag));
				Mark("switched-method", space.frameId);
				yield return WaitMark("client-saw-method", Step);

				// a client's request, applied here and published back
				yield return WaitFor(() => Mathf.Abs(space.alignment.tagSizeCm - 13f) < .001f, Step, "the client's tag size request");
				Mark("host-saw-size");

				yield return WaitMark("client-done", Step);
				NetcodeManagement.Disconnect();
				yield return WaitFor(() => !SyncBus.Active && manager.Role == SpaceRole.Offline, Step, "the host to go offline");
				Assert.That(manager.Space, Is.SameAs(space), "the host keeps its space after the session");
				Mark("host-done");
			}
			finally
			{
				foreach (string id in created) manager.DeleteSpace(id);
				LogAssert.ignoreFailingMessages = false;
			}
		}

		[UnityTest]
		public IEnumerator ClientSide()
		{
			yield return Boot();
			SpaceManager manager = SpaceManager.Instance;
			List<string> created = new();
			try
			{
				// a local space of its own, to come back to
				Assert.That(manager.NewSpace(), Is.True);
				string localSpace = manager.Space.id;
				created.Add(localSpace);
				Assert.That(manager.Save(), Is.True);

				yield return WaitMark("hosting", Startup);
				string[] host = ReadMark("host-space").Split('\n');
				string hostSpace = host[0].Trim(), hostMap = host[1].Trim(), hostFrame = host[2].Trim();

				NetcodeManagement.ConnectLAN("127.0.0.1");
				// a first attempt can race the host's listen socket; try once more the way the editor's auto-connect does
				float retryAt = Time.realtimeSinceStartup + 30f;
				bool retried = false;
				yield return WaitForReporting(() =>
				{
					if (!retried && Time.realtimeSinceStartup > retryAt && !(NetworkManager.Singleton && NetworkManager.Singleton.IsConnectedClient))
					{
						retried = true;
						Debug.Log("[SessionSpaceTests] client: retrying the connection");
						NetcodeManagement.Disconnect();
						NetcodeManagement.ConnectLAN("127.0.0.1");
					}
					return NetworkManager.Singleton && NetworkManager.Singleton.IsConnectedClient;
				}, Step, "the connection", "client");
				yield return WaitForReporting(() => SyncBus.Active && !SyncBus.IsAuthority, Step, "the bus to spawn on the client", "client");
				Assert.That(manager.Role, Is.EqualTo(SpaceRole.Client));
				yield return WaitFor(() => manager.Space != null && manager.Space.id == hostSpace, Step, "the host's space");
				Assert.That(manager.Space.frameId, Is.EqualTo(hostFrame));
				Assert.That(manager.Map, Is.Not.Null);
				Assert.That(manager.Map.id, Is.EqualTo(hostMap));
				Assert.That(manager.Space.alignment.method, Is.EqualTo(Method.Visual));
				yield return WaitFor(() => !manager.IsChangingMap, Step, "the host's map to settle");
				// both editors share one catalog folder on this machine, so the file itself cannot be checked;
				// what can be is that a client's save is a no-op while it shows the host's space
				long before = Directory.Exists(manager.Store.Directory) ? Directory.GetLastWriteTimeUtc(manager.Store.SpacePath(hostSpace)).Ticks : 0;
				Assert.That(manager.Save(), Is.True);
				Assert.That(Directory.Exists(manager.Store.Directory) ? Directory.GetLastWriteTimeUtc(manager.Store.SpacePath(hostSpace)).Ticks : 0, Is.EqualTo(before),
					"a client's save never writes the host's space");
				Mark("client-joined");

				yield return WaitMark("switched-map", Step);
				string switchedMap = ReadMark("switched-map");
				yield return WaitFor(() => manager.Map != null && manager.Map.id == switchedMap, Step, "the switched map");
				Assert.That(manager.Space.id, Is.EqualTo(hostSpace));
				Assert.That(manager.Space.frameId, Is.EqualTo(hostFrame), "a map switch keeps the frame");
				Mark("client-saw-map");

				yield return WaitMark("switched-method", Step);
				string newFrame = ReadMark("switched-method");
				yield return WaitFor(() => manager.Space.frameId == newFrame && manager.Space.alignment.method == Method.AprilTag, Step, "the method switch");
				yield return null;
				Assert.That(ColocationManager.Instance.ActiveMethod, Is.EqualTo(Method.AprilTag));
				Assert.That(manager.Alignment.UsingTwoTagMode, Is.True);
				Mark("client-saw-method");

				Assert.That(manager.Alignment.TagSizeBlocker(), Is.Null);
				Assert.That(manager.Alignment.SetTagSize(13f), Is.True, "a client asks the host");
				yield return WaitMark("host-saw-size", Step);
				yield return WaitFor(() => Mathf.Abs(manager.Space.alignment.tagSizeCm - 13f) < .001f, Step, "the host's answer");

				NetcodeManagement.Disconnect();
				yield return WaitFor(() => !SyncBus.Active && manager.Role == SpaceRole.Offline && manager.Space != null && manager.Space.id == localSpace, Step, "the local space to come back");
				Mark("client-done");
				yield return WaitMark("host-done", Step);
			}
			finally
			{
				foreach (string id in created) manager.DeleteSpace(id);
				LogAssert.ignoreFailingMessages = false;
			}
		}

		private static MapObject FirstPrefab(SpaceManager manager)
		{
			var database = (MapObjectDatabase)typeof(SpaceManager).GetField("objectDatabase", Private).GetValue(manager);
			foreach (MapObjectDatabase.Category category in database.Categories)
				foreach (MapObjectDatabase.Entry entry in category.Objects)
					if (entry.Prefab != null && !category.DebugOnly) return entry.Prefab;
			return null;
		}
	}
}
