using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Anaglyph.LaserTag.Maps;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;

namespace Anaglyph.LaserTag.Tests
{
	public class MapCatalogOperationTests
	{
		private string directory;
		private MapStore mapStore;
		private MapSpaceStore spaceStore;
		private MapWorkingCopy mapDocument;
		private MapSpaceWorkingCopy spaceDocument;
		private MapSpace space;
		private GameMap map;
		private object controller, visit;
		private int retryRequests;
		private readonly List<object> changes = new();
		private static string Id() => Guid.NewGuid().ToString("N");
		private static object Property(object target, string name) => target.GetType().GetProperty(name).GetValue(target);
		private object Call(string name, params object[] args) => controller.GetType().GetMethod(name).Invoke(controller, args);
		private CatalogOperationResult Operation(string name, params object[] args) => (CatalogOperationResult)Call(name, args);
		private void OnCommitted(object change) => changes.Add(change);
		private string JournalBlocker => Path.Combine(directory, "pending-catalog-operation.json.tmp");

		[SetUp]
		public void SetUp()
		{
			directory = Path.Combine(Path.GetTempPath(), "lasertag-catalog-controller-" + Id());
			mapStore = new(Path.Combine(directory, "maps"));
			spaceStore = new(Path.Combine(directory, "spaces"));
			space = MapSpace.Create("Room");
			map = new() { id = Id(), version = Id(), name = "Layout", storageFrameId = space.storageFrameId };
			space.mapIds.Add(map.id);
			Assert.That(mapStore.Save(map), Is.True);
			Assert.That(spaceStore.Save(space), Is.True);
			var assembly = typeof(MapSpace).Assembly;
			visit = Activator.CreateInstance(assembly.GetType("Anaglyph.LaserTag.Maps.MapVisitContext", true));
			controller = Activator.CreateInstance(assembly.GetType("Anaglyph.LaserTag.Maps.MapCatalog", true),
				mapStore, spaceStore, directory, visit);
			mapDocument = (MapWorkingCopy)Property(controller, "MapDocument");
			spaceDocument = (MapSpaceWorkingCopy)Property(controller, "SpaceDocument");
			mapDocument.Load(map);
			spaceDocument.Load(space);
			var committed = controller.GetType().GetEvent("Committed");
			committed.AddEventHandler(controller, Delegate.CreateDelegate(committed.EventHandlerType, this,
				GetType().GetMethod(nameof(OnCommitted), BindingFlags.NonPublic | BindingFlags.Instance)));
			controller.GetType().GetEvent("RetryRequested").AddEventHandler(controller, (Action)(() => retryRequests++));
			changes.Clear();
			retryRequests = 0;
		}

		[TearDown]
		public void TearDown()
		{
			if (Directory.Exists(directory)) Directory.Delete(directory, true);
		}

		private void BlockJournal()
		{
			Directory.CreateDirectory(JournalBlocker);
			LogAssert.Expect(LogType.Exception, new Regex("[\\s\\S]*"));
		}

		[Test]
		public void CompletionPublishesPersistedDocumentsWithoutSelectingThem()
		{
			var result = Operation("NewMap");

			Assert.That(result.IsCompleted, Is.True);
			Assert.That(changes.Count, Is.EqualTo(1));
			Assert.That(Property(changes[0], "Kind").ToString(), Is.EqualTo("MapCreated"));
			var created = (GameMap)Property(changes[0], "Map");
			Assert.That(mapStore.TryGet(created.id, out _), Is.True);
			Assert.That(spaceStore.TryGet(space.id, out var savedSpace), Is.True);
			Assert.That(savedSpace.mapIds, Does.Contain(created.id));
			Assert.That(mapDocument.CurrentId, Is.EqualTo(map.id));
			Assert.That(spaceDocument.CurrentSpace.mapIds, Is.EqualTo(new[] { map.id }));
		}

		[Test]
		public void FailedJournalWriteReportsPendingAndCompletesOnceWithItsOriginalVisit()
		{
			var mapContext = Property(visit, "MapContext");
			var referenceContext = Property(visit, "ReferenceContext");
			BlockJournal();

			Assert.That(Operation("NewMap").IsPending, Is.True);
			Assert.That(Property(controller, "HasPending"), Is.True);
			Assert.That(changes, Is.Empty);
			Assert.That(retryRequests, Is.EqualTo(1));
			Assert.That(Operation("NewMap").Status, Is.EqualTo(CatalogOperationStatus.Rejected));
			Assert.That(Call("SaveDocuments", false), Is.False);
			visit.GetType().GetProperty("MapContext").SetValue(visit, Guid.NewGuid());
			visit.GetType().GetProperty("ReferenceContext").SetValue(visit, Guid.NewGuid());
			Directory.Delete(JournalBlocker);

			Assert.That(Call("RetryPending"), Is.True);
			Assert.That(Call("RetryPending"), Is.True);
			Assert.That(changes.Count, Is.EqualTo(1));
			Assert.That(Property(changes[0], "MapContext"), Is.EqualTo(mapContext));
			Assert.That(Property(changes[0], "ReferenceContext"), Is.EqualTo(referenceContext));
			Assert.That(Property(controller, "HasPending"), Is.False);
			Assert.That(mapStore.Maps.Count, Is.EqualTo(2));
			Assert.That(mapDocument.CurrentId, Is.EqualTo(map.id));
		}

		[Test]
		public void InvalidCommandIsRejectedWithoutCompletionOrRetry()
		{
			Assert.That(Operation("CreateSpace", false, (Method)999).Status, Is.EqualTo(CatalogOperationStatus.Rejected));
			Assert.That(Property(controller, "HasPending"), Is.False);
			Assert.That(changes, Is.Empty);
			Assert.That(retryRequests, Is.Zero);
			Assert.That(Call("RetryPending"), Is.True);
			Assert.That(changes, Is.Empty);
		}

		[Test]
		public void DuplicationPreservesCanonicalPlacementAcrossDifferentStorageFrames()
		{
			var sourceSpace = MapSpace.Create("Source");
			sourceSpace.canonicalFrameId = space.canonicalFrameId;
			sourceSpace.canonicalFromStorage = new Pose(Vector3.right * 3, Quaternion.Euler(0, 90, 0));
			var source = map.Clone();
			source.id = Id();
			source.storageFrameId = sourceSpace.storageFrameId;
			source.objects.Add(new() { prefabId = "Base", pose = new Pose(Vector3.forward * 2, Quaternion.identity) });
			sourceSpace.mapIds.Add(source.id);
			Assert.That(mapStore.Save(source), Is.True);
			Assert.That(spaceStore.Save(sourceSpace), Is.True);

			Assert.That(Operation("DuplicateMap", source.id).IsCompleted, Is.True);

			var copy = (GameMap)Property(changes[0], "Map");
			Assert.That(Property(changes[0], "Kind").ToString(), Is.EqualTo("Membership"));
			Assert.That(MapSpaceFrame.Near(space.Frame.ToCanonical(copy.objects[0].pose),
				sourceSpace.Frame.ToCanonical(source.objects[0].pose)), Is.True);
			Assert.That(copy.storageFrameId, Is.EqualTo(space.storageFrameId));
			Assert.That(mapDocument.CurrentId, Is.EqualTo(map.id));
		}

		[Test]
		public void SpaceDeletionQueuesNativeCleanupOnlyAfterDurableCompletion()
		{
			string uniqueAnchor = Id(), sharedAnchor = Id();
			space.SetAnchorWithTag(uniqueAnchor, Pose.identity, -1);
			space.SetAnchorWithTag(sharedAnchor, Pose.identity, -1);
			Assert.That(spaceStore.Save(space), Is.True);
			var other = MapSpace.Create("Other");
			other.SetAnchorWithTag(sharedAnchor, Pose.identity, -1);
			Assert.That(spaceStore.Save(other), Is.True);
			var erased = new List<string>();
			BlockJournal();

			Assert.That(Operation("DeleteSpace", space.id).IsPending, Is.True);
			Call("EraseUnreferencedAnchors", (Action<string>)erased.Add);
			Assert.That(erased, Is.Empty);
			Directory.Delete(JournalBlocker);
			Assert.That(Call("RetryPending"), Is.True);
			Call("EraseUnreferencedAnchors", (Action<string>)erased.Add);

			Assert.That(erased, Is.EqualTo(new[] { uniqueAnchor }));
			Assert.That(spaceStore.TryGet(space.id, out _), Is.False);
			Assert.That(mapStore.TryGet(map.id, out _), Is.False);
			Assert.That(Property(changes[0], "Kind").ToString(), Is.EqualTo("SpaceDeleted"));
		}

		[Test]
		public void UnavailableCatalogRetainsCleanupCandidatesUntilOwnershipCanBeChecked()
		{
			string anchor = Id();
			string invalid = Path.Combine(directory, "spaces", Id() + ".json");
			File.WriteAllText(invalid, "{}");
			spaceStore.Refresh();
			var erased = new List<string>();
			Call("QueueAnchorErasure", anchor);

			Call("EraseUnreferencedAnchors", (Action<string>)erased.Add);
			Assert.That(erased, Is.Empty);
			File.Delete(invalid);
			spaceStore.Refresh();
			Call("EraseUnreferencedAnchors", (Action<string>)erased.Add);

			Assert.That(erased, Is.EqualTo(new[] { anchor }));
		}

		[Test]
		public void AutomaticAprilTagSpacePersistsSetupIntentBeforeActivation()
		{
			Assert.That(Operation("CreateSpace", true, Method.AprilTag).IsCompleted, Is.True);

			var created = (MapSpace)Property(changes[0], "Space");
			Assert.That(spaceStore.TryGet(created.id, out var saved), Is.True);
			Assert.That(saved.hasPendingSetup, Is.True);
			Assert.That(saved.pendingSetupMethod, Is.EqualTo(Method.AprilTag));
			Assert.That(saved.initializationPending, Is.True);
			Assert.That(Property(changes[0], "Kind").ToString(), Is.EqualTo("SpaceActivated"));
			Assert.That(spaceDocument.CurrentId, Is.EqualTo(space.id));
		}
	}
}
