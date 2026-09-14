# MapSpace implementation

Implemented from the approved [MapSpaces plan](map-spaces-implementation-plan.md). The original plan remains the design record. This document describes the code and the validation boundary.

## Documents and coordinate frames

`MapSpace` stores alignment settings, saved tag/anchor targets, known map IDs, frame identities and the persisted `canonicalFromStorage` rigid pose. `GameMap` stores a layout and its storage-frame ID. A map has one parent space; copying a layout creates a new map ID. The separate JSON files are:

```text
Application.persistentDataPath/map-catalog-v2/
  spaces/<space-id>.json
  maps/<map-id>.json
  pending-catalog-operation.json   # present only during a multi-file operation
  session-cache/<remote-space-id>.json
```

This fresh catalog does not load or migrate the old map directory. It does not delete that directory.

`JsonDocumentStore` provides detached reads, schema validation, temporary files, backup recovery and atomic replacement. It distinguishes unavailable reads from missing or invalid documents. `MapCatalogJournal` records a recoverable operation before writing layouts and their membership. Recovery precedes membership pruning. Pruning removes bad, duplicate or missing IDs, preserves uncertain reads, and leaves unrelated layout files alone. Native anchor saves are erased only when no readable catalog owner references them; an unavailable catalog prevents erasure.

Every saved layout pose, shared anchor target, registered tag target and private tag-anchor target uses the stable storage basis:

```text
canonicalPose = canonicalFromStorage * storedPose
storedPose    = inverse(canonicalFromStorage) * canonicalPose
```

`MapSceneObjectDirector` projects scene placements; `MapSpaceColocationAdapter` projects provider targets. `MapSpaceReconciler` projects incoming canonical DTOs into local storage. A pure rebase changes only the space document. It preserves map files, target poses, object revisions, tag IDs and anchor UUIDs. Undo restores the previous offset/frame pair. The transform has unit scale, yaw and translation; it is not applied to the tracking rig a second time.

Shared definitions, private tag-anchor realizations, retained source revisions and foreign associations are separate collections. A private realization records the tag size and definition it realizes, preventing an old UUID from being reused for a conflicting imported tag. Incoming host definitions become active while conflicting local definitions remain retained.

## Lifecycles and discovery

`LaserTagMapCoordinator` composes `MapWorkingCopy`, `MapSpaceWorkingCopy`, persistence, network DTOs, discovery and alignment. Map changes within a space replace gameplay objects while keeping references, the active observer and scan. Deleting the last map leaves the space. Space settings include rename, deletion and explicit alignment reset. Operators can also create spaces. Rebase undo remains an internal API and is not exposed in the menus.

Headsets probe saved space anchors automatically. Every matching space remains a distinct candidate; the most recently used match wins startup. A healthy completed probe with no matches creates a space and baseline layout, reusing an unfinished automatic draft where possible. Shared spatial anchors are the default when sharing is supported. Otherwise, including on managed headsets without a Meta account, startup activates registered AprilTags and resumes the normal first-tag registration transition. This does not require a shared-anchor minter or manual space creation. Local anchor probing still runs when available, even without sharing support. A confirmed lack of anchor support can start the AprilTag path without waiting for an impossible probe; unknown capability, canceled probes and inconclusive queries retry without creating a space. Session selection supersedes startup. Operator and Editor simulation restoration use separate paths.

Shared-anchor initialization checks runtime support and also confirms a successful upload before committing its first reference, including during standalone startup. A definitive cloud-storage or permission denial switches an unfinished automatic draft to AprilTags; transient network/localization errors retry. This fallback keeps the draft frame and map files, and never resets an established reference frame.

Editor simulation restores the most recently used space through the normal activation path, including its saved alignment method and reference services. It does not replace a restored provider with System determined. A new simulation space explicitly saves System determined as its initial method, keeping the saved preference, active provider and auto-hosted selection consistent.

## Alignment transitions

Saved anchors and registered tags are reference-based. Two AprilTags and System determined are provisional. Provisional alignment permits layout editing, but cannot authorize persistent reference creation when any saved reference-based definition exists, including retained definitions. A space with no saved references permits its designated initialization headset to establish the frame.

`ReferenceAlignmentTransition` tracks intent, candidate revision, source/target methods and fresh evidence. `MapSpaceAlignmentController` retains immutable source observations, starts an independent target observation lease, and keeps one rig writer. New references cannot participate in their own validating source fit. Setup-only commits without switching methods; setup-and-switch and configured switches transfer control after validation and persistence. Source observation leases survive until the target confirms the handoff, with local fallback if it fails. Peers report their actual source and complete independently. A configured target can recover alignment when the old source is unavailable.

Native services remain active for the source and candidate through a transition, then unused services stop. Independent editor and observation leases can still request tag detection. An operator commits a delegated validation without waiting for a physical rig of its own. Gameplay can continue using the existing trusted frame during validation.

Tag registration and method activation are separate commits. After checking the author, source alignment, operation, reference context and tracking generation, the authority saves each accepted tag definition before publishing it to the provider or other headsets. A failed save cannot display the tag as registered. The same transition then validates a saved target, retaining its observation leases and previous alignment source. Interruption or cancellation does not discard accepted tags; a resumed switch can align to them without requiring the old source. Every additional registration still requires current reference-authoring permission.

Native tag anchors preserve the actual relationship between the detected tag and the anchor returned by the runtime. The initial target uses `canonTag * inverse(observedTag) * observedAnchor`, just like later drift correction. The tag observation is captured in tracking coordinates before minting and projected into the current rig frame afterward. Recenter, focus loss and pause invalidate pending observations, so an old tracking pose cannot define a new anchor target.

Candidate revisions and committed host snapshots retain this headset's compatible private tag anchors, including realizations minted during setup. They remain local to the headset and are saved alongside the host's committed tag definitions. The alignment settings display the transition target and progress separately from the saved space preference.

Setup intent and accepted tag definitions are durable. Live phases, observations, readiness and leases restart after interruption. Network proposals include operation/revision/context and tracking-generation checks; rejections apply only to the originating context. Minter assignment follows space/frame readiness independently of map selection. First-tag initialization uses a separate single-author grant: a headset host may initialize, or an operator may assign a tracked headset without requiring cloud-anchor capability. Once a transition has an author, other headsets cannot initialize its frame.

Imported spaces adopt the host's alignment preference and pending setup intent, replacing obsolete local setup requests. These fields travel with the space snapshot. Automatic anchor updates cannot replace an explicit pending method request or an unfinished handoff.

A method request with no eligible headset is retained as a pending, cancelable selection. It resumes when a headset connects or regains tracking/focus; adding references to an established space still requires a headset aligned to its saved references. An empty space can establish its first tag even while its selected source is shared anchors. Local authoring and network readiness use the same head-tracking check, including Editor simulation. Shared-anchor minting requires runtime persistence and a successful save before publishing a reference; temporary AR Foundation simulation anchors cannot establish a saved frame. These are runtime capability checks, so capable runtimes such as Meta XR Simulator remain supported in the Editor. Saved-reference observers wait for an anchor runtime instead of throwing on an operator or during runtime startup.

Initial thresholds, pending headset measurements:

| Check | Current value |
| --- | --- |
| Candidate observation interval | 0.6 seconds |
| Fit residual / reliable angular error | 5 cm / 5 degrees |
| Agreement with retained reference source | 8 cm / 5 degrees |
| Candidate stability | 2 cm / 1 degree |
| Confirmation after transferring rig control | 0.2 seconds |
| Session frame relationship stability | 1 second |
| Native shared-anchor setup timeout | 20 seconds |

Two tags commits a pair only after usable geometry is observed. The lower ID defines the origin and the higher defines forward. No anchors are created or used. Positions are retained in the headset's current tracking frame, allowing the tags to be scanned individually and then left out of view. Recenter, tracking/alignment loss, sleep and focus loss discard both observations, including detections already in flight; both tags must be seen again. Automatic reacquisition preserves IDs; choosing another pair explicitly clears them. Later unrelated IDs do not silently replace the pair. Both tag methods share the space's printed-size setting.

## Networking and reconciliation

`MapSessionSync` commits canonical shared references and the active layout with a coherent identity header. `MapSpaceTransitionSync` transports staged definitions and validation separately. Only the active layout is learned; other layouts arrive when selected. Protocol version is **8**; peers need matching builds.

Foreign space associations and known frame offsets prevent a new local space on every join. For unknown relationships, independent observers fit both saved reference frames in the same current tracking generation. A stable verified relationship updates the offset and imports references/layouts transactionally. An empty draft with no geometry or references can be reused without a physical transform. Contradictory immutable anchor targets invalidate a remembered mapping.

If geometry cannot establish a relationship, the session remains usable from a separate cache while durable local data stays intact. Provisional-only evidence cannot reconcile saved layouts. Dirty layout conflicts create a local copy within the same transaction; retries reuse the journal instead of generating more copies.

## Environment scans and menus

Scans remain live only for the active space. Space changes, rebase and provisional discontinuities clear scanner GPU state, meshes/colliders and navigation. Ordinary same-space map changes and verified reference-based handoffs preserve the scan. Tracking loss pauses depth integration and outgoing mesh work. Mesh packets and asynchronous completion checks carry space, canonical frame and scan-visit IDs; an A → B → A visit cannot accept packets from the previous A. Late joiners request populated chunks through the bounded encoder queue. Incoming populated and empty chunks update collision and navigation. System determined scans are never merged by assuming common device origins.

The shared headset/operator catalog lists all spaces, each followed by its indented maps. Loading a selected map automatically switches to its parent space if necessary. Space and map rows share one selection. A square edit button appears beside the selected space; it loads that space and its most recently used map before opening space details. The loaded map retains its edit button; deletion appears beside the selected map and requires confirmation. A selected space also has a delete button. Its confirmation modal names the space and warns that all of its saved maps are deleted too; it does not activate the space. Cancel or Back dismisses the request, and confirmation rechecks deletion eligibility. Space details has no delete action. Space settings are a separate page in each menu's parent `NavView` and own all alignment controls. The operator's manual tag size and removal controls also live there. On headsets, opening a tag-based space's settings activates the tag palette; Back or the palette's Done action ends that tool session. Automatic first-tag setup opens the same space page. Map editing contains layout controls only. There is no space dropdown, separate Load Space action, duplicate action or rebase-undo control. Headsets have no New Space action. Alignment settings show the requested method, actual source, transition, provisional category and selected pair. Palette tools identify whether they edit the map or the space. Delayed size/name edits and observations retain the context they started in. The operator catalog scrolls within its resizable sidebar and exposes actual headset sources in a separate table column.

## Verification

Unity 6000.3.20f1 compiles the implementation. The latest EditMode run on 12 September 2026 passed **281 of 282 tests**, including all five simulation-restoration regressions; the existing `UxmlBindingsUpdateNamedVariablesAndLocale` test passed on an isolated rerun. Coverage includes the sharing-unavailable startup fallback, grouped catalog, deferred operator alignment requests, simulated-headset readiness, runtime persistence gates, operator reference observers without an anchor runtime, and anchorless two-tag acquisition, recenter/tracking-loss recovery, stale camera frames, same-space observation preservation and service shutdown. The latest regression checks cover saving tag registrations before publication, failed-save rollback, interrupted-switch recovery, reference-authoring guards, completion of the first-tag handoff, and initial native-anchor pose conversion across rig movement. The assembly exercises persistence, offset projection, conflicts/journal recovery, reference-policy and handoff evidence, overlapping-space discovery, sharing capability and denial handling, first-tag initialization grants, resumable fallback drafts, stale scan contexts, existing tag-anchor recovery, readiness, menu binding lifetimes, and composed UXML. Catalog checks cover loading the exact selected map across spaces, active-row editing, selected-map deletion and membership pruning, navigation and stale-space handling, automatic tag registration through space settings, and tag-tool lifetime across warnings, Back and Done. The headset composition was inspected with `LaserTagRuntimeTheme.tss`; the operator catalog was also inspected in Play Mode, and the short-sidebar layout passed composition checks.

The saved-AprilTag restart failure was reproduced in a live Editor session: AprilTags remained selected while System determined was active, detection was stopped, and environment integration was blocked. After the fix, restarting the same saved space restored AprilTags, detected both registered tags, acquired reference alignment, enabled the scanner and mesher, and cleared the reference-registration blocker. The existing tag definitions and space offset were unchanged.

Physical validation remains required on **two Quests and a PC operator**, on both LAN and relay. In particular: fresh/reloaded automatic startup on both account-enabled and managed headsets without a Meta account, including interrupted tag registration and shared-service permission denial; same-space map changes; every alignment handoff; first pair acquisition and reacquisition; independently created spaces joining repeatedly in both directions; minter replacement and simultaneous setup; sleep/wake/recenter; and stable-scan late joining. Measure physical position and yaw at several points before treating the thresholds above as tuned. Editor checks do not validate Meta anchor services, camera observations, radio/network timing, or physical continuity.
