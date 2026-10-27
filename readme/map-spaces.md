# Spaces and maps: implementation

The map system after the October 2026 redesign ([plan](map-redesign-plan.md), which records the
product decisions and what they deleted). This document describes the code: the documents, the
scene component and its helpers, sessions, the operator flow, and where data is validated.

## Documents

`Maps/Space.cs` holds plain data; nothing in it touches the scene or the network.

- A **`Space`** is one physical room: `id`, `name`, `frameId`, `lastUsed`, its `alignment`
  settings (method, printed tag size, two-tag pair), its references (`tags`: id and pose;
  `anchors`: shared-anchor GUID, pose and the tag it was realized at, if any) and its `maps`.
- A **`Map`** is one layout of that room: `id`, `name`, `lastUsed`, `objects` (prefab id and
  pose). Maps are embedded in their space; a map has exactly one space.
- Every pose is in the space's frame. **`frameId`** names that frame. It changes only when the
  frame can move: entering or leaving two-tag mode, forgetting the pair while in two-tag mode,
  and resetting alignment. Environment scans and the visual map measured in the old frame are
  discarded then; the maps keep their poses.
- **`PrivateReferences`** (`<space-id>.private.json`) are this device's own realizations of the
  space's tags: anchor GUIDs with the tag pose and size they were realized at. They are never
  shared and are discarded when the host's frame changes.
- `Sanitize()` drops entries it cannot use (bad ids, non-finite poses, duplicate tags) and
  logs them; it never rejects the document. Legacy method values migrate on load: 2 and 3 (system
  frame, separate two-tag method) to AprilTag, 4 (scan registration) to Visual.

Files, under `Application.persistentDataPath/spaces-v3/`:

```text
<space-id>.json           the space with its maps
<space-id>.private.json   this device's tag anchors for it
<space-id>.vmap           its visual map (VisualMapPersistence)
vocabulary.vvoc           the visual vocabulary shared by every map
migrated-from-v2          marker: the v2 catalog was imported once
```

`SpaceStore` writes temp-then-replace, loads every readable file and skips an unreadable or
mismatched one with a warning, so one bad file never blocks the rest. `CatalogV2Migrator` runs
once, when `spaces-v3` is empty and `map-catalog-v2` exists: it embeds each space's maps,
composes stored poses through the v2 `canonicalFromStorage` offset, moves local anchors to the
private file, copies the `.vmap` and vocabulary, and writes the marker. `map-catalog-v2` is left
in place.

## Scene component and helpers

`SpaceManager` (on the MapManager prefab) is the only `MonoBehaviour`. Its `Awake` builds what it
owns; consumers read `SpaceManager.Instance` from `Start` on. It constructs plain classes with
direct references; required scene dependencies are serialized fields that throw when missing.

| Class | Owns |
| --- | --- |
| `SpaceManager` | the shown `Space` and `Map` (read-only to callers), the space list, autosave (dirty flag and debounce), startup discovery, the role (`Offline`, `Host`, `Client`, derived from `SyncBus`), the space and map commands with their `…Blocker()` strings, and the static events `SpaceChanged`, `MapChanged`, `FrameChanged`, `SpacesChanged`, `AlignmentErrorRaised` |
| `SpaceAlignment` | applying a space's settings and references to `ColocationManager` and the providers; authoring (register and remove tags, tag size, method, pair); the host's validation of requests; the anchor provider's `IAnchorSessionPolicy`; minter and author selection |
| `MapObjects` | the scene projection of the shown map: capture, replace, spawn and remove; edit requests in a session carry the map revision |
| `SpaceDiscovery` | which saved spaces are in this room: anchor probe first, then visual voting; presence results for the picker |
| `SpaceSync` | the wire format (below) |
| `TagSetup` | the guided AprilTag setup (below) |
| `VisualMapPersistence` | the `.vmap` per space: sequenced writes so a forced save lands beside an async one, relocalize on activate |

`ColocationManager` is reduced to switching the `Colocator` between the three providers and
tracking the alignment state and tracking generation. It no longer carries a method of its own:
the method is the shown space's (`SpaceAlignment.SelectedMethod`), and `PreferredMethod` falls a
tag or anchor preference back to the kind of reference the space actually has.

## Alignment methods

- **Shared spatial anchors** (`MetaSharedAnchor`): the space's anchors. In a session one headset
  mints and shares (`AnchorMinterPolicy`: the host when it is a headset; for an operator the
  lowest capable headset in the frame, kept while it stays ready). Minted anchors are written to
  the space by the device that owns it.
- **AprilTags** (`AprilTag`): registered tags, realized by every device through its own local
  anchors (`PrivateReferences`). Without registered tags it runs in **two-tag mode**: the first two
  tags seen define a provisional frame, nothing is saved, and the space gets a new `frameId` on
  the way in and out.
- **Visual** (`Visual`, the default): the space's visual map, the provider's own.

## Sessions

The host publishes the shown space and map through `SpaceSync`: three `SyncList`s (tags,
anchors, map objects) followed by one `SyncVariable<SpaceHeader>` that commits them on the bus's
ordered channel. The header carries the space and map identity and names, the frame id, the
alignment settings, a map revision (new each time a map is shown; edit requests from an earlier
showing are refused), a publish revision and the `changing` flag (the host is replacing the
session's objects; readiness and edits wait). The tag setup state rides along as one more
variable.

A client renders whatever the header describes (`SpaceManager.ShowSessionSpace`): a new space
or frame starts colocation over, the same frame with new content is taken in place. It never
saves the host's space, and gets its own last space back once the session is torn down.

Clients ask for changes with one `SyncEvent<SpaceRequest>` (set method, register and remove a
tag, tag size, pair, forget pair, and the four tag-setup steps). The host re-validates every
request against the sender's `PlayerHeadsetStatus` readiness: same space and frame, focused,
head-tracked, not an operator, and aligned unless the space has no references yet. Refusals come
back as one `SyncEvent<RequestRejected>`, surfaced as `AlignmentError.RequestRejected`.

A method switch is the host's: it saves the method, bumps the frame when the frame can move,
publishes, and every device activates the method from the header. In an operator-managed session
only the operator changes the method.

## Operator

An operator machine hosts without a rig. Two things need a headset on its behalf:

- **Anchor minting** goes through `IAnchorSessionPolicy`: `SelectMinter` picks the author above,
  and the anchor provider's preparation and mint proposals are validated by the policy against the
  sender's readiness and tracking generation.
- **Tag registration** runs through `TagSetup`, the three-step wizard (print, connect a headset,
  review). The operator starts it from space settings; the host switches the space to AprilTags
  and publishes a `TagSetupState` naming the operation, the space and frame, and the **author**:
  the headset that measures and registers for it (the requester when a headset asked and is
  ready, else the lowest ready headset in the frame, kept while it stays ready). The author's map
  editor opens on the tag tools: it measures the printed size, continues once the host has
  confirmed that size, then registers tags. The operator reviews the registered tags and
  finishes, which saves the space, or cancels. A setup ends by itself when its space or method
  changes or a round starts; it follows its space through a rebase of its own (the last tag
  removed to measure again).

Offline, and on a headset host, the same `TagSetup` runs on the device itself; in a headset-run
session any ready headset may finish or cancel it.

## Rules the code follows

- Required dependencies are `[SerializeField]` references assigned in the prefab, with no
  `Find`/`GetComponent` fallbacks and no null checks: a missing one throws.
- No `Func`/`Action` wiring inside the LaserTag assembly; direct references, and one interface
  (`IAnchorSessionPolicy`) where the XR assembly boundary forces inversion.
- No `[DefaultExecutionOrder]`; static events for subscribers that attach in `OnEnable`.
- Data is validated where it enters: file load (`Sanitize`) and network receive (`SpaceSync`,
  `HandleRequest`). `Space` and `Map` are handed out as read-only references, never cloned.

## Verification

Implemented and checked in the editor on 26 October 2026 (branch
`claude/suspicious-sammet-e80cff`): the EditMode suites under `Maps/Tests/Editor`
(`Anaglyph.LaserTag.MapTests`: store round trip and migration, two-tag colocation, menu
composition, presence, tag ruler) and `Tests/Editor` (`Anaglyph.LaserTag.EditorTests`), the
offline PlayMode test, and a two-editor PlayMode session test (host and client in separate batch
editors: join, map switch, method switch, a client's tag-size request, disconnect).

Still to check on two Quests (plan step 6): join, switch and disconnect; tag registration by a
joiner and by an operator-assigned headset through the wizard; anchor mint and share with an
operator host; a method switch mid-session; relaunch with a corrupted space file present; and a
first launch that migrates a real `map-catalog-v2`.
