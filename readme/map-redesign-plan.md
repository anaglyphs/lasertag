# Map system redesign plan

Replaces the 33 scripts in `Assets/Anaglyph/LaserTag/Maps` (~5,800 lines) with 8 files,
roughly 1,800 lines plus `VisualMapPersistence` and a temporary migrator.

## Product decisions (2026-10-26)

| Question | Decision |
|---|---|
| What joiners keep of the host's space/map | **Nothing.** Joiners render the host's synced state. Only device-private data (their own tag-anchor UUIDs) is saved, keyed by the host's space and frame. |
| Changing alignment method mid-session | **Host switches, everyone re-aligns.** No candidate staging, evidence reports or validated handoff. Alignment may drop briefly. |
| Alignment methods | Visual, registered AprilTags, two-tag mode, Meta shared anchors. **Scan registration is dropped.** |
| PC operator flow | **Kept.** A non-host headset can register tags or mint anchors on the host's behalf. |

## What these decisions delete

- **Joiner adoption:** `MapSessionWorkflow`, `MapSpaceReconciler`, map forking, the 1 s dual-observation
  reconciliation, `session-cache/`, pre-session restore, `associations`, `knownFrames`.
- **Frame indirection:** `storageFrameId`/`canonicalFrameId`/`canonicalFromStorage`, Rebase/Undo,
  `previousCanonicalFrameId`. Poses are stored in the space's frame, which is the aligned world frame.
- **Transition protocol:** `ReferenceAlignmentTransition`, `MapSpaceAlignmentController`,
  `MapSpaceReferenceObservation`, candidate/evidence/rejection events, and the workflow state persisted
  in `MapSpace` (`hasPendingSetup`, `pendingSetup*`, `initializationPending`, `referenceDirty`,
  `referenceVersion`, `retainedReferences`, `automaticallyCreated`, `visualMapSaved`).
- **Multi-file transactions:** maps live inside the space file, so `MapCatalogJournal`, membership
  pruning, `FindOwner` and the "Pending" operation result go away.

## Target files

All in `Anaglyph.LaserTag.Maps`.

### 1. `Space.cs`: data only

```csharp
[Serializable] class Space {
    int schemaVersion; string id, name, frameId; long lastUsed;
    AlignmentSettings alignment;            // method, tagSizeCm, pair (first, second)
    List<TagRef> tags; List<AnchorRef> anchors;
    List<Map> maps;
}
[Serializable] class Map { string id, name; List<MapObjectRecord> objects; }
[Serializable] class PrivateReferences { string spaceId, frameId; List<TagAnchorRef> tagAnchors; }
```

- `frameId` changes whenever stored poses stop meaning the same place (alignment reset, two-tag pair
  change). EnvSync and headset readiness key on it instead of the three visit GUIDs.
- `Sanitize()` runs once at load and on received data. It drops individual bad entries with a warning
  and never rejects the whole document. It replaces `IsValid`, `GameMap.Validate`, `MapGuid` and the
  journal's validation.
- The reusable startup draft is derived ("most recent space with no references and only empty maps"),
  not flagged.

### 2. `SpaceStore.cs`: files

- `LoadAll()`, `Save(space)` (write `.tmp` then replace), `Delete(id)`, which also removes the `.vmap`
  and private file, and `LoadPrivate`/`SavePrivate`.
- One unreadable file is skipped and logged. It never makes the catalog "unavailable".
- Plain class with a directory. No static `Default` reset dance.

### 3. `SpaceManager.cs`: the scene component (replaces `LaserTagMapCoordinator`)

- Owns the space and map currently shown, the space list, autosave (a dirty flag and a timer in
  `Update`, no `MapAutosave` class) and startup discovery.
- Role is derived, not tracked: `Offline`, `Host` or `Client` from `SyncBus`, plus `IsChangingMap`.
  This replaces `MapLifecycle` and the seven-state `MapPhase`.
- Commands: `LoadSpace`, `NewSpace`, `DeleteSpace`, `RenameSpace`, `ResetAlignment`, `LoadMap`,
  `NewMap`, `DuplicateMap`, `DeleteMap`, `RenameMap`. Each has a `…Blocker()` that reads state
  directly, replacing `MapPolicy`'s bool-parameter functions.
- `Space`/`Map` are exposed as read-only references. No clone on read. Mutation only goes through
  commands.
- Clients call `ShowSessionSpace(Space)` with the in-memory space from `SpaceSync`. It is never saved.
  On disconnect the client reloads its last local space; nothing has to be restored.
- One `CancellationTokenSource` per shown space, cancelled on change. It replaces `MapVisitContext`,
  operation GUIDs and the generation counters.

### 4. `MapObjects.cs`: scene projection

- Keeps today's `MapSceneObjectDirector` logic (retire before instantiate, unresolved prefabs survive
  capture, map-scoped edit requests). The constructor `Func`s become a serialized reference to
  `SpaceManager`.
- No pose conversion, because the space frame is the world frame.

### 5. `SpaceSync.cs`: networking (replaces 3 sync classes and the tag-setup networking)

- **Host to clients:** `SyncList<TagRef>`, `SyncList<AnchorRef>`, `SyncList<MapObjectRecord>`, then a
  `SyncVariable<SpaceHeader>` written last to commit them. The header holds space id/name, frameId,
  alignment settings, map id/name, a revision GUID and the changing flag. This keeps the existing
  ordered-channel trick.
- **Client to host:** a single `SyncEvent<SpaceRequest>` (SetMethod, RegisterTag, RemoveTag,
  SetTagSize, SetPair, ForgetPair, TagSetup Begin/Measure/Finish/Cancel) and a single
  `SyncEvent<RequestRejected>`.
- `MapIdentity`, `SpaceTransitionCommand` and the three duplicate representations go away.

### 6. `SpaceAlignment.cs`: alignment settings and authoring

- Applies `Space.alignment` plus references to `ColocationManager`: activate method, set tags, anchors,
  tag size and pair.
- **Host:** handles `SpaceRequest`s. It validates the sender with `PlayerHeadsetStatus` readiness (same
  space/frame, tracked, and aligned, or the space has no references yet), mutates the space, saves and
  republishes.
- **Every device:** writes realized tag anchors to `PrivateReferences` and minted or persisted shared
  anchors to the space (host only).
- **Method switch:** the host saves the method, bumps `frameId` when the frame can move (to or from
  two-tag mode, pair change), and publishes. Every device calls `ActivateMethod`.
- **Operator:** author selection for tags and anchor minting, plus the tag-setup wizard state (one
  `SyncVariable` in `SpaceSync`).
- Implements one `IAnchorSessionPolicy` interface for the anchor provider (see below).

### 7. `SpaceDiscovery.cs`

Merges `MapSpaceDiscovery` and `VisualSpaceDiscovery`: anchor probe first, then visual. It keeps the
per-space presence results used by the map picker.

### 8. `VisualMapPersistence.cs`: kept

Minor changes: the caller checks authority (drop the `Func<bool>`), and the `Saved` callback goes,
because the `.vmap` file's existence is the record.

## Changes outside `Maps/`

- **AprilTag provider (XR.SharedSpaces):** realizes registered tags through its own local anchors, so
  `ActivateMethod(AprilTag)` works without the map layer's observer (`MapSpaceReferenceObservation`
  today). Drop the 12-observation confirmation gate, which only existed for handoff evidence.
- **Shared-anchor provider:** replace the 9 `Func` properties (`SelectMinter`, `LocalReadinessGate`,
  `ValidateRemoteMint`, …) with one `IAnchorSessionPolicy` interface. An interface is the right tool
  here, because the XR assembly can't reference LaserTag. Drop `ReferenceEditGate`/`RegistrationGate`
  if nothing else uses them.
- **`ColocationManager`:**
  - Remove `ManagedSelection` and its dead branches, and the method `SyncVariable` (`SpaceSync` carries
    the method).
  - Remove `offlineMethod`, `CommitMethod`, `ConfigureSpace(MapSpace, Func)`, `IsSettingUpTags` and the
    `Find`/`GetComponent` fallbacks in Awake.
  - Remove `ScanRegistration`, migrating it to Visual.
- **`MapBaseRequirements`:** move to `Matches/`.
- **Callers (about 20 runtime files):**

| Today (`LaserTagMapCoordinator.Instance.…`) | New |
|---|---|
| `CurrentSpace`, `CurrentMap`, map/space commands, `SaveCurrentMap`, `Phase`, `IsChangingMap` | `SpaceManager` |
| `ReferenceContext`, `ScanContext`, `CanonicalFrameId`, `SessionSpaceId` | `SpaceManager.Frame` (spaceId, frameId) |
| `CheckWorldFrameIsTrusted`, `CheckReferenceFrameAgreement`, `CanIntegrateEnvironment` | `SpaceAlignment.IsAligned`, `CanIntegrateEnvironment` |
| `CheckCanEditMap`, `RequestPlaceObject`, `RequestRemoveObject`, `CommitObjectMove` | `MapObjects` |
| `TagSetup`, tag/method commands, `Describe*Blocker` | `SpaceAlignment` |
| `AlignmentTransition`, `IsChangingColocation`, `WaitingForAlignmentAuthor`, `SessionIsWaitingOnFirstTag` | removed; UI that showed transition progress is simplified |

  Static events stay static: subscribers can attach in `OnEnable` without depending on Awake order.

## Rules for the new code

- Required dependencies are `[SerializeField]` references, assigned in the prefab. No `Find` or
  `GetComponent` fallbacks, and no null checks on required dependencies: a missing one should throw.
- No `Func`/`Action` wiring within the LaserTag assembly. Use direct references. Use one interface where
  an assembly boundary forces inversion.
- No events that are really calls back to a parent.
- Awake caches only the component's own state. Consumers read `Instance` from `Start` onward. No
  `[DefaultExecutionOrder]`.
- Validate only where data enters (file load, network receive).

## Data migration

- New catalog directory `persistentDataPath/spaces-v3/`. The old `map-catalog-v2/` is left untouched,
  so rollback is possible.
- A one-shot `CatalogV2Migrator` runs when `spaces-v3` is empty:
  - Embeds each space's maps, converting poses through `canonicalFromStorage`.
  - Moves `localAnchors` to the private file.
  - Moves the `.vmap` files (the vocabulary moves too).
  - Drops `retainedReferences`, `associations` and `knownFrames`. Spaces previously adopted from a host
    become plain local spaces.
- The migrator is deleted after one release.

## Implementation order

Branch first. The working tree has unrelated uncommitted changes.

1. **Data and store:** `Space`, `SpaceStore`, `CatalogV2Migrator`, with EditMode tests: round trip, a
   bad entry is dropped, one bad file doesn't block the rest, and a migration fixture from a v2
   catalog.
2. **Offline path:** `SpaceManager`, `MapObjects`, `SpaceDiscovery`, and offline `SpaceAlignment`;
   provider changes (AprilTag self-realization, `IAnchorSessionPolicy`); migrate callers; delete the old
   `Maps/` classes and their internal tests.
   - Rename `LaserTagMapCoordinator` to `SpaceManager` (file and class) so its `.meta` GUID and
     prefab reference survive. Never hand-write `.meta` files.
   - Milestone: load, edit, save and align offline in XR simulation, for each of the four methods.
3. **Sessions:** `SpaceSync` host publish, client render, requests, and the method switch.
   - Milestone: two-editor MPPM session (macOS build profile) with join, map switch, method switch and
     disconnect.
4. **Operator:** author selection, the tag-setup wizard, and remote anchor minting.
5. **Cleanup:** simplify `ColocationManager`, remove `ScanRegistration`, update `Map System.md` and
   `readme/map-spaces.md`, delete `Maps/README.md`.
6. **Device pass on two Quests:** join, switch, disconnect, tag registration by a joiner and by an
   operator-assigned headset, anchor mint and share, method switch mid-session, and relaunch with a
   corrupted space file present.

## Accepted costs

- A mid-session method switch can briefly drop alignment, and objects move if the new frame differs.
- Joiners cannot replay a host's map offline later.
- A joiner's private tag anchors are discarded when the host's `frameId` changes.
- Migrated host-adopted spaces lose their link to that host's space.
