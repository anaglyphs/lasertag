# Map ownership

`MapManager` owns the editable document. It has no scene, networking, colocation,
match, or editor dependencies. Its reads and the store's reads return detached
snapshots. Authored object/tag/name changes create content revisions. Local anchor
realization updates and repeated disk saves do not.

`MapStore` writes exact snapshots and reports success. Its catalog changes only
after the file operation succeeds. The default store uses the existing `maps`
directory; tests create isolated stores in temporary directories. Existing flat
JSON files and their backup/temp recovery files remain supported. The old unused
`baseVersion` field is ignored when reading legacy files.

`LaserTagMapCoordinator` is the scene component that connects the systems. It owns
match and authority rules, editor prompts, startup selection, map transitions,
autosave, conflict adoption, and orphaned-anchor cleanup. The old MapManager
component GUID and serialized field names were retained on this component, so
existing prefab references continue to resolve.

`MapColocationAdapter` imports references and captures provider state into detached
snapshots. The coordinator commits registered tags through the content update path
and device anchor records through the alignment update path. Anchor saves are
erased only after the updated document is persisted and no stored map references
them. XR providers continue to own anchor operations and reference synchronization.

`MapObjectDirector` manages scene instances and edit requests. Retired objects are
excluded from captures immediately, even when ownership delays their actual
despawn. Replacement completion is observable. Unresolved prefab records survive
capture so loading an older map does not silently delete unsupported placements.

`MapSessionSync` publishes object records before the map identity that commits
those records on SyncBus's ordered channel. Clients persist this complete object
collection, including an empty collection, independently of NGO spawn timing.
They do not save their partial replicated scene. Game policy and adoption are
handled by the coordinator, not by the transport. Object edit requests carry a
map ID so requests from a previous map can be rejected.

On adoption, divergent local content is forked successfully before it can be
replaced, including when the map is already loaded. Failed adoption can be retried
without duplicating the preserved fork. On session shutdown, capture is suspended
until NGO finishes teardown, then the last complete document is instantiated
locally. The workflow operation number invalidates an obsolete rebuild after every await.

# Workflow and decisions

`MapWorkflow` owns the device's lifecycle, pending adoption, and restoration operation.
Its phases describe work in progress; alignment is an independent observation.
`MapPolicy` evaluates the phase and current facts in one place. Both commands and
UI explanations use these rules, including deletion. Neither type reads Unity
singletons or performs I/O. The coordinator exposes `Phase` for inspection.

The normal paths are:

- Offline: `Local` (load, unload, and edit).
- Host: `Hosting -> SwitchingMap -> Hosting`.
- Joiner: `AwaitingSessionMap -> AdoptingSessionMap -> FollowingSession`.
  Later shared revisions pass through adoption again.
- Disconnect: `RestoringLocalMap -> Local`, after network teardown finishes.
- Teardown: `Stopped` disables capture and invalidates pending restoration.

Only local and host scenes are captured into the document. A follower can request
edits and record its device anchors, but shared content comes from the received
document. A map transition blocks edits and tag mutations. Lost alignment blocks
placement and registration, but still allows removing a moved tag. A host can
choose another map during a switch that cannot align. Timeout ends the session
hold without declaring the local frame aligned.

The coordinator performs the effects for these transitions. `SaveCurrentMap`
persists the current document; it never completes an adoption as a side effect.
`CompleteAdoption` owns the save-before-replace sequence and explicitly retries on
storage failure. `RebuildAfterSession` owns restoration and abandons stale work.
The object director no longer maintains a separate following/suspended/quitting
state. The colocation adapter receives an explicit capture selection and does not
infer map permissions from the network role.

All participants in a session must use the updated build: object-list replication
and map-scoped edit requests change the session wire format.

# Validation

Run the `Anaglyph.LaserTag.MapTests` Edit Mode test assembly in Unity's Test Runner.
It exercises snapshot ownership, revision semantics, conflict preservation, empty
collections, save/delete failures, adoption retry, shared-anchor retention, and
legacy/recovery files, lifecycle permissions, transition timeouts, authority promotion,
and stale disconnect continuations without accessing the user's map catalog.

Device validation is still needed for join/switch/disconnect with two headsets,
anchor realization and erasure, tag authoring, and ownership transfers during a
map change. Editor persistence tests do not establish those runtime behaviors.
