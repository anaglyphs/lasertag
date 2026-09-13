# MapSpaces implementation plan

Review draft, revised 11 September 2026 after startup, alignment, reconciliation, and space-offset clarifications. This is a design proposal based on the current working tree; no runtime implementation or saved-data deletion is part of this task.

The central change is to give a physical coordinate frame its own lifetime. A `GameMap` becomes a gameplay layout within a `MapSpace`. Loading another map in that space replaces gameplay objects while keeping alignment, registered references, and the live environment scan.

**Decisions confirmed in this conversation**

| Decision | Behavior |
| --- | --- |
| Membership | Each map belongs to exactly one MapSpace. Reusing a layout creates a new map. |
| Existing saves | No legacy migration is required. The new catalog starts fresh. |
| File layout | Keep GameMaps in separate JSON files. MapSpaces list map IDs; prune invalid or missing references. |
| Environment meshes | Retain the scan across maps in the active space; clear it on leaving the space. No disk persistence or inactive-space cache. |
| Headset startup | Probe automatically. Load a matching space, or automatically initialize a space when no match is found: shared spatial anchors when sharing is available, otherwise registered AprilTags. No manual New space command for headsets. |
| Optional setup | Shared spatial anchors are the default. Choosing/configuring other methods is optional. The PC operator retains explicit space creation. |
| Two AprilTags and System determined | Both may be selected in an established space. Treat their alignment as provisional; it does not prove alignment to the saved reference frame. |
| Reference authoring | If any persistent reference-based alignment data exists, new registered tags or shared anchors require current alignment to that frame through reference-based evidence. A provisional method cannot authorize this work. |
| Session adoption | Save the active map and the space's shared alignment data; learn other maps when they are loaded. |
| Multiple probe matches | Automatically select the most recently used matching space, while keeping every matching space available in the picker. |
| Reconciliation | Automatic reconciliation that preserves physical placement is acceptable. Avoid adding a new local space on every host join. |

The revised recommendation is a verified, transactional update to a space's offset into the host's frame, with remembered identity associations. Saved map/reference poses remain in a stable storage frame. This replaces the previous proposal to rewrite all poses during reconciliation. Two AprilTags remains provisional, and System determined remains selectable in established spaces.

Two proposed defaults remain for review: allow gameplay layout edits during provisional alignment while blocking reference authoring; and associate a previously unknown host with the currently active local space after verifying the frame relationship. Neither proposal permits merging every space detected in the same room.

**1. Separate the documents and the identities**

Add `Maps/MapSpace.cs`. Keep it serializable and independent of scene objects, providers, networking, and UI. Its storage document owns the space's ID/name, stable storage-frame identity, current canonical-frame identity and rigid offset, alignment configuration, and a list of map IDs (`List<string> mapIds`). Each GameMap remains a separate JSON file resolved by its ID through `MapStore`; do not embed layouts or store arbitrary file paths. Each map belongs to one space.

Because session adoption transfers one active map at a time, this collection means the maps this device knows belong to the space. It is not an exhaustive catalog shared by every headset. Shared space settings and child map revisions have separate update paths; learning a new child map does not create a conflicting alignment revision. Serialize explicit session DTOs rather than broadcasting the entire storage document.

Simplify [GameMap.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Maps/GameMap.cs:19) to its ID, name, object placements, and map content revision/bookkeeping. Move `tags`, `anchors`, `tagSizeCm`, `DefaultTagSizeCm`, `preferredColocationMethod`, and alignment-setup state out of it. Its `IsEmpty` then means no gameplay objects. A blank map in an aligned space must never be interpreted as an uninitialized coordinate frame.

Keep document identity separate from coordinate and runtime identity:

| Identity | Changes when | Must remain stable when |
| --- | --- | --- |
| Stable `mapId`, with a map content revision | ID on creation; revision on layout/name edits | Another map's objects or the space's references change |
| Stable local `spaceId`, with a space configuration revision | ID on local creation; revision on authored space settings/reference edits | A child map changes; a foreign space is associated with this catalog entry |
| Stable `storageFrameId` | Creation of the local space's storage basis | Reconciliation, method selection, map edits, and imports; every stored pose uses this basis |
| `canonicalFrameId` | Establishing a canonical basis, adopting another basis through verified reconciliation, or deliberately replacing it | A map changes; references are added in the same frame; ordinary tracking is corrected |
| Runtime alignment/scan context | Space entry, effective frame change, provisional reset, or scan reset | An ordinary map change within the active space |

Store every child-map pose and persistent tag/anchor target in that space's stable storage coordinates. Persist one rigid `canonicalFromStorage` pose for the whole local MapSpace, initially identity. There is no per-map placement transform. The offset has unit scale and follows the project's gravity-preserving yaw/translation convention. Different devices may retain different storage bases and offsets while evaluating to the same canonical session frame.

```text
canonicalPose = canonicalFromStorage * storedPose
storedPose = inverse(canonicalFromStorage) * canonicalPose
```

Use a small shared `MapSpaceFrame` value/helper for pose composition, inversion, and context checks. Runtime scene objects, provider targets, and network gameplay continue to use the current canonical/world frame. Apply the offset at storage boundaries, not as another correction every frame. Ordinary colocation moves the tracking rig using those runtime targets; do not apply the storage offset to the rig a second time. Provisional alignment changes physical interpretation temporarily without overwriting the persistent offset or saved targets.

The local storage envelope records foreign space identities, their frame identities/revisions, and their association with this local entry. Receiving a host's ID does not require creating another local entry. The wire identity and the local catalog lookup must therefore be resolved explicitly rather than compared as interchangeable IDs. Retain source revision information for imports; independent devices' configuration counters are not globally ordered.

Keep shared definitions and device-local realizations distinct. The current [GameMap documentation](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Maps/GameMap.cs:6) explicitly permits different tag-anchor UUIDs on different headsets. The new representation should distinguish:

- Shared canonical tag IDs, poses, and physical size.
- Shared-anchor UUIDs and canonical target poses published for the space.
- This device's private tag-anchor realizations, retained local reference definitions after reconciliation, foreign-space associations, and local usage/save bookkeeping.
- Runtime-only tracking objects, anchor leases, scan buffers, meshes, and pending operations.

Persist the first two in `MapSpace` in storage coordinates; keep the third in a typed local-state section of the same envelope. Send explicit session DTOs in canonical coordinates, never another device's storage offset or private realizations as if they were shared frame data. Live meshes remain in the scanner/mesher runtime, associated with the committed space/frame and runtime scan context.

**2. Give storage and document editing clear owners**

Add `MapSpaceManager` as the plain document owner for space settings and membership. Keep [MapManager.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Maps/MapManager.cs:12) as the API for editing the selected child map. MapManager saves layout changes through MapStore; MapSpaceManager saves membership/settings through MapSpaceStore. The space owns the membership list, with a derived reverse index enforcing one space per map. Continue returning detached document snapshots; resolve child maps by ID when needed.

Keep [MapStore.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Maps/MapStore.cs) for separate GameMap JSON files and add `MapSpaceStore` for space JSON files. Use a fresh versioned catalog root with sibling `maps/<mapId>.json` and `spaces/<spaceId>.json` directories, so legacy map files cannot be mistaken for the new schema. No legacy migration or grouping is required. Preserve temporary-file replacement and backup recovery for each document.

Saving a layout writes only its GameMap file. Updating the offset, alignment settings, or membership writes only its MapSpace file. Keep [MapAutosave.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Maps/MapAutosave.cs) coalescing, with independent dirty tracking for maps and spaces.

Validate space membership after map-file recovery on catalog load and refresh. Remove duplicate/malformed IDs and references to missing or invalid map documents, including ID/filename mismatches. Persist the cleaned list without deleting the underlying files. A transient I/O failure is not proof of a missing or corrupt map: leave that reference pending for retry. Enforce one-parent membership through the catalog index; reject conflicting additions rather than assigning an existing map to another space implicitly. Pruning the last reference leaves an empty space with its alignment data intact. Remote catalogs advertising maps not downloaded yet are discovery metadata, not dangling local membership entries.

Separate files need ordered, recoverable operations rather than an assumed atomic save across files:

- Create/import/fork: save the complete map first, then add its ID to the space. A failed membership save leaves a recoverable unreferenced map and a retryable operation, not a broken reference. Preserve its intended space/storage-frame context in recovery metadata; do not guess a new parent for an orphan.
- Delete a map: remove and save membership before deleting its map file. A crash can leave an unreferenced file; an externally missing map is cleaned up by membership pruning. Do not automatically delete orphan files during reference pruning.
- Session adoption involving several documents: stage incoming data and record a recoverable operation before replacing existing files. Commit all required writes before activating the new runtime context. Resume or roll back incomplete operations on startup; do not treat files staged by an unfinished operation as invalid references.
- Pure rebasing changes only the space file: existing map files and stored reference poses remain untouched. Offset, frame identity, and association metadata still commit together in that one file.

An individual failed file write preserves its previous committed contents. Multi-file operation failures remain pending/recoverable; do not claim the whole catalog update is atomic merely because each file replacement is.

Preserve conflict handling at the correct scope, after resolving identities and normalizing coordinate frames:

- A divergent layout forks that map within its existing space.
- A verified change of coordinate basis is not a divergent layout edit. Update the space offset/frame metadata; leave existing map poses, IDs, authored revisions, dirty state, and storage-coordinate conflict baselines unchanged.
- Host configuration governs the session. Keep compatible local references for future localization; preserve conflicting local definitions separately and inactive rather than silently overwriting or averaging them.
- Different space IDs or method preferences alone do not justify another local space. An unverified frame replacement remains staged until reconciliation or an explicit preservation/reset decision is possible.
- Device-local anchor maintenance never forks authored content.
- Failed preservation/adoption stays pending and retryable, without generating another fork on every retry.

A received replacement `canonicalFrameId` is not an ordinary configuration update, even if the advertised space ID matches and local maps are clean. Inactive cached layouts still depend on the old frame. Section 8 defines the verified conversion and failure behavior. A deliberate reset without a conversion changes physical placement and must remain an explicit space-wide operation.

Deleting a map removes only that child. It does not delete the space, tags, or anchors, even when it was the last map. Deleting a space is a separate action that identifies its contained maps. Anchor erasure happens only after the persisted references are removed and no surviving space references the UUID. Retain the cross-document reference check used today for forks.

**3. Split map changes from space changes in the coordinator**

[LaserTagMapCoordinator.ReplaceMap](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/LaserTagMapCoordinator.cs:353) currently rebases and injects references for every map replacement. [OnSessionMapReceived](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/LaserTagMapCoordinator.cs:621) likewise considers a different map to be a different frame. Both decisions must use the space/frame identity instead.

Expose `CurrentSpace` alongside `CurrentMap`, and separate map-content events, space-configuration events, and coordinate-frame invalidation. Keep `LaserTagMapCoordinator` as the composition layer. Extract the method-setup/handoff logic into a focused `MapSpaceAlignmentController` rather than adding every new operation to the existing coordinator.

| Operation | Gameplay objects | Alignment and reference providers | Scan and environment NavMesh |
| --- | --- | --- | --- |
| Select another map in the same space | Save and replace | Keep running with the same reference context | Keep |
| Create a map in the current space | Start an empty layout | Keep | Keep |
| Edit/rename a map | Update that map | Keep | Keep |
| Switch between reference-based methods | Keep poses | Validate alignment to the same canonical frame | Keep; pause integration until aligned |
| Enter/leave/reset provisional alignment | Keep saved poses; physical placement may change | Preserve saved canonical references; change active provider | Clear; start a new runtime scan context |
| Reconcile local space into a different host frame | Update one saved space offset; activate the session layout | Project saved targets through the offset; adopt verified frame | Clear; voxel data is not rebased |
| Select a different space | Save outgoing state; replace selected layout | Cancel outgoing work and load target references | Clear |
| Unload a map but remain in its space | Remove the layout | Keep | Keep |
| Leave/delete/reset the active space | Save or complete explicit deletion | Clear/invalidate the outgoing context | Clear |

An active space may contain zero maps. `NewMap`, `UnloadCurrentMap`, and `EnsureMap` must stop implicitly creating or discarding alignment state. Add space creation/selection/exit commands for the lifecycle controller and operator; headset startup invokes creation automatically, with no manual New space option. Select the last-used map, or create a baseline empty map when entering play requires one. A map-only switch may still hold gameplay while objects are replaced, but it must not restart colocation or imply alignment was lost.

Update [MapWorkflow.cs / MapPolicy](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/MapWorkflow.cs) with separate facts for the active space, its established frame, the active map, and the kind of transition. Preserve generation checks for stale async work, adoption retries, disconnect reconstruction, authority changes, round restrictions, and the ability to escape a space switch that never localizes. A timeout releases a transition hold; it never declares a headset aligned.

On disconnect, rebuild the last complete adopted layout in its adopted space, as the current workflow does for a map. A network-session change invalidates network work independently of whether the physical space/frame changes.

**4. Separate reference-based and provisional alignment**

Use the neutral categories `ReferenceBased` and `Provisional`. Registered AprilTags and persistent spatial anchors are reference-based: they attempt to recover a saved frame. Two AprilTags and System determined are provisional: they can guide play, but do not establish agreement with that saved frame. A reference-based method can still be unlocalized or inaccurate; its category alone is not a readiness signal.

Replace map-based reference policy with a space policy that distinguishes `HasReferenceBasedData`, current alignment evidence, and permission to author references:

| Saved reference-based data | Current authoring headset state | Register tags / mint persistent anchors |
| --- | --- | --- |
| None | Valid initialization source, including provisional alignment | Allow under the single initialization operation |
| Exists | Currently aligned through reference-based evidence to this exact canonical frame | Allow, subject to capability and operation checks |
| Exists | Provisional, searching, lost tracking, or aligned to another frame | Block; recover reference-based alignment first |

Count all valid persistent references belonging to the reconciled space, including inactive methods and retained local references. References do not cease defining a frame because they are temporarily unavailable. Map emptiness, a selected enum, and an old `IsAligned` flag cannot authorize setup. Transient native anchors created internally by Two AprilTags retain provisional provenance; their API type does not upgrade them into saved reference-based evidence.

For initial setup, let the current valid frame seed the first persistent reference even if gameplay layouts already exist. In a session the authority grants one initialization operation to one eligible headset. Its first successful reference commit establishes the reference-based frame and invalidates competing initialization grants. The PC operator retains document/session authority and delegates physical work. Automatic standalone anchor creation uses the same operation rules without requiring user setup.

Update [PlayerHeadsetStatus.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Player/PlayerHeadsetStatus.cs), [AnchorMinterPolicy.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/AnchorMinterPolicy.cs), [ReadyForAnchorWork](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/LaserTagMapCoordinator.cs:974), and [ValidateTagRegistration](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/LaserTagMapCoordinator.cs:1070). Validate the actual authoring headset's space/frame, evidence category, tracking generation, and operation. Apply the policy to automatic maintenance/minter paths as well as menu commands. Gameplay readiness and permission to author persistent references are separate.

Generalize `systemFrameForTagSetup` into requested method, active source, and setup operation state. Rename/rework [MapColocationAdapter.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Maps/MapColocationAdapter.cs) as `MapSpaceColocationAdapter`. A same-space map switch must not reset provider snapshots, a tag pair, anchor leases, or the minter. In [ColocationManager.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/ColocationManager.cs), continue distinguishing saved preference, committed session method, active provider, and actual localization state.

Use an explicit reference-alignment transition controller for both setup and method switching, as detailed below. Retain the usable source until the target has been validated and the handoff completes. Failure retains the prior usable configuration. Late native results cannot commit after space exit, tracking/recenter changes, source-method replacement, or authority/minter loss.

Keep [Colocator.cs](/Users/jt/source/lasertag/Assets/Anaglyph/XR/SharedSpaces/Colocator.cs) independent of map classes. Add a non-mutating fit evaluation seam for handoff and reconciliation. Check sufficient, nondegenerate geometry, stability, and residuals; use measured translation/yaw tolerances rather than relying on the current general agreement threshold or a boolean localized flag. Reference-based method handoffs should preserve the canonical frame within those tolerances.

Selecting a configured reference-based method to recover alignment remains allowed. Selecting a provisional method is also allowed in an established space, but can change physical placement. It preserves the saved references for later recovery and disables reference authoring. Proposed default: allow gameplay layout editing under provisional alignment; the resulting saved positions may appear differently in the room after reference-based alignment resumes. Reference protection must not accidentally become a ban on playing or editing.

Audit the no-reference-runtime fallback in `Colocator`: it cannot prove a physical headset is aligned. Explicit simulation must not overwrite real reference records merely because a native provider is unavailable.

**4a. Share an explicit transition lifecycle between setup and switching**

Implement a `ReferenceAlignmentTransition` state machine owned by the proposed `MapSpaceAlignmentController`. It serves setup-only operations, setup followed by switching, and switching between already configured reference-based methods. Setup-only completes after validating and saving the new references while leaving the source method active. Switching skips reference creation when the target is already configured, but still validates its live alignment before handoff.

| Phase | Source alignment | Target work |
| --- | --- | --- |
| Preparing | Keep the previous reference-based source live and driving the rig | Observe/register tags, acquire anchors, or prepare sharing; stage changes separately |
| Validating | Continue aligning from the source | Evaluate the target fit without applying it; require fresh stable observations, adequate geometry, and agreement with the established frame |
| Persisting | Keep source ownership of the rig | Save validated target configuration and prepare the committed session context |
| Handing over, if requested | Retain source observations/leases for recovery | Transfer the single rig-driving role to the validated target; finish only after target alignment is confirmed |
| Completed / canceled / failed | Keep the chosen usable source or target | Release operation-owned resources; preserve committed references |

Keep source method/context, target method, intent (setup-only or activate-target), phase, operation ID, canonical frame/offset revision, source/target reference revisions, authoring client, and session/authority/tracking generations explicit. Live phases, fit evidence, and native leases are runtime state, not proof restored from JSON. Persist durable configuration and any resumable setup intent separately; restart observation/validation after reload.

The invariant is one rig-writing solver, with potentially two reference observation sources. Do not run two normal `Colocator` loops against the rig or combine unvalidated target constraints into the source fit. A frozen last rig transform is not a live reference-based source. The target must support observation/preparation independently of selection; evaluating its proposed transform must not switch the active provider.

Extend the existing handoff in [LaserTagMapCoordinator.ApplyPreferredMethod](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/LaserTagMapCoordinator.cs:823) instead of keeping separate special cases for anchors and initial tag setup. Move its operation ownership into this controller and use it offline too. Change [ColocationManager.UpdateProvider](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/ColocationManager.cs:264) and the lifecycle around `Colocator.SetProvider` so selecting a requested target cannot prematurely stop the source. Add an explicit validated handoff path that transfers solver ownership without an intermediate origin reset or uncontrolled first-fit snap.

Providers need independently owned observation/preparation handles, with shared native resources retained until their last user releases them. Stopping a staged target or retiring the old source must not tear down resources used by the other. This also covers extending the currently active method: stage added tags/anchors against the old reference revision, and admit them to the active constraint set only after validation. Source tracking continues while the target is prepared; unrelated source-definition edits invalidate/revalidate the operation.

During target setup, capture in the live source's canonical frame and inverse-project through the existing space offset when saving. Validate against subsequent observations as well as those used to author the targets. An ordinary reference-based transition changes neither `canonicalFrameId` nor `canonicalFromStorage`; it must not hide a disagreeing target by rebasing the space.

If source tracking is lost, pause reference authoring and target commit; retain the recoverable source configuration and invalidate stale measurement evidence. Resume with fresh evidence or cancel cleanly. Cancellation, preparation/save failure, authority change, or space/frame exit leaves the source active if it is still usable, and never reports stale alignment as valid. Release only uncommitted operation-owned resources. If failure occurs after a session method commit, rollback is an explicit new coordinated commit; individual peers cannot silently rewrite session selection.

Keep initialization and recovery distinct from a normal handoff. A space with no persistent references uses the existing single-author bootstrap exception and may seed its first method from a provisional frame. If persistent references exist but the active method is provisional, recover a saved reference-based method before authoring anything. If the old source cannot localize, switching to an already configured target is still allowed as recovery; do not require two localized methods to escape lost alignment. Recovery supplies no permission to mint/register references until reference-based alignment is established.

Session authority coordinates target configuration and the method commit; every headset independently validates its local handoff. A peer still localizing the target retains its usable source in the same canonical frame and reports that actual source/phase, rather than claiming the requested target is active. A slow peer need not block all other peers indefinitely. Readiness must distinguish committed session method from each headset's actual reference-based source and frame evidence during overlap. Source retention alone does not authorize a local session-method rollback.

Scope holds to conflicting reference/frame operations, while allowing the registration/measurement actions needed by this setup. Waiting for a person to scan tags is not the existing fixed native-operation timeout: bound native calls, expose cancellation, and keep setup waiting without freezing all interaction. Same-space map changes do not invalidate this space-scoped transition. UI can report, for example, “Preparing AprilTags; aligned using shared anchors,” using the actual source and phase. Preserve the scan during a valid handoff; pause integration if alignment becomes unresolved.

**5. Define durable references and recoverable provisional methods**

| Method | Proposed behavior and implementation location |
| --- | --- |
| Registered AprilTags | [AprilTagColocationConstraintProvider.cs](/Users/jt/source/lasertag/Assets/Anaglyph/XR/SharedSpaces/AprilTags/AprilTagColocationConstraintProvider.cs) receives the space's canonical tag set and size. Capture new poses only under the reference-authoring policy. Private realized anchors remain per device. |
| Shared spatial anchors | [SpatialAnchorColocationConstraintProvider.cs](/Users/jt/source/lasertag/Assets/Anaglyph/XR/SharedSpaces/SharedAnchors/SpatialAnchorColocationConstraintProvider.cs) and its [session partial](/Users/jt/source/lasertag/Assets/Anaglyph/XR/SharedSpaces/SharedAnchors/SpatialAnchorColocationConstraintProvider.Session.cs) provide the default automatic setup. Preserve the delegated minter/prepare-share model, keyed to space/frame rather than map. Published UUIDs and target poses survive child-map changes. |
| Two AprilTags | [TwoAprilTagColocationConstraintProvider.cs](/Users/jt/source/lasertag/Assets/Anaglyph/XR/SharedSpaces/AprilTags/TwoAprilTagColocationConstraintProvider.cs) retains its provisional pair-derived frame. Do not permanently canonize the first observations into the existing space. Persist the chosen pair IDs/size, but keep measured geometry and helper anchor leases transient and recoverable. |
| System determined | [SystemDeterminedColocationConstraintProvider.cs](/Users/jt/source/lasertag/Assets/Anaglyph/XR/SharedSpaces/SystemDeterminedColocationConstraintProvider.cs) trusts the current system origin as a provisional interpretation. Allow explicit selection in established spaces and simulation. It does not overwrite saved canonical targets or prove two devices share a frame. |

For Two AprilTags, the current lower-ID-at-origin / second-tag-defines-direction convention can remain. Automatically select a usable pair if none was chosen, then lock the IDs so a later lower ID cannot unexpectedly replace them. In a session, the authority commits one pair proposed by an observing headset; clients must not independently choose different pairs. Pair replacement remains explicit. Saving chosen IDs is compatible with this policy; saving a supposedly authoritative pair-to-space calibration is not part of the revised design. Even if the same IDs are registered, Two AprilTags remains provisional; using their saved canonical poses belongs to the registered-tag method.

Continue refining transient observations with quality/baseline checks and smoothing, and support reset/reacquisition. A poor first sight may temporarily yield poor alignment, but must not become permanent canonical data. The current provider already averages observations using private anchor leases; retain that recovery mechanism without treating those leases as persistent reference-authoring or probing evidence. A same-space map switch should not require rescanning the pair.

No calibrated system-origin subsystem is planned. Neither provisional method promises continuity with an established reference frame. Returning to a reference-based method localizes its saved targets again. If there are no reference-based records, provisional alignment may seed the first registered tag or persistent anchor under the initialization policy; after that, subsequent reference authoring needs reference-based alignment.

Keep one physical tag-size setting per space initially. Size changes that invalidate registered canonical data require deliberate recalibration; changing a provisional pair's size resets its transient fit and scan context. Supporting mixed printed sizes is separate work.

Removing a moved reference remains possible while unaligned. Deliberately clearing the last persistent reference is a space-wide reset decision: preserve or explicitly reset the existing basis, invalidate pending operations, and clear incompatible scans. Losing tracking, deleting a child map, or failing to load a UUID must never perform that reset implicitly.

**6. Automatically find or initialize a headset's space**

Replace [MapDiscovery.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Maps/MapDiscovery.cs) with `MapSpaceDiscovery`. Build one deduplicated union of the saved, probeable anchor UUIDs belonging to known spaces, perform one localization pass, and score every space independently. Two spaces can both be `Here`, including when they reference the same UUID. No result is keyed by an assumed unique physical-room identity.

Retain `Here`, `Elsewhere`, and `Unknown`, with tested/localized counts and a probe generation. At the operation level return distinct `Matches`, `NoMatches`, `Indeterminate`, and `Canceled` outcomes. No probeable anchors for a particular saved space, an unavailable runtime, and an inconclusive query are not evidence that it is elsewhere. Do not erase references or mutate providers merely because probing failed.

The current native path, through `RefreshLocalizableAsync` and the anchor registry, conflates several unavailable/failed cases with null results, and localization can time out without matches. Give this path explicit outcomes and readiness handling before using it to trigger creation. A successful query followed by a bounded localization window with healthy tracking can return `NoMatches`; a native query error or interrupted tracking returns `Indeterminate`. Do not wait indefinitely for absent anchors in a new room. A completed probe with no matches is the application's bootstrap decision, not proof that this physical room has never been seen. Probing knows only accessible anchor references; it cannot discover every other headset's unknown catalog.

An empty catalog, or a catalog with no probeable candidates, may initialize a new space once native readiness is established; it need not wait for a nonexistent query. Any unprobeable saved spaces remain unknown. Distinguish this case from an attempted query that failed or timed out inconclusively.

Remove the current preferred-method filter from discovery. Retained persistent anchors can locate a space even while its preferred method is Two AprilTags or System determined. Transient two-tag helper anchors do not count. Spaces without accessible persistent anchor realizations can remain unknown; shared-anchor startup normally avoids new anchorless spaces, while devices without sharing access use registered AprilTags and their local anchor realizations when available.

Replace `StartupProbe`'s fixed initial delay with a cancelable lifecycle in the coordinator/alignment controller:

1. Wait for tracking and a known sharing capability, and load the local space catalog. Probe local anchors whenever the runtime supports them, including on managed headsets that cannot share anchors. If anchor support is definitively unavailable, activate the AprilTag path without treating that as a successful room probe.
2. Probe all candidates. If several match, choose the most recently used, with a stable ID tie-breaker. Keep every match available in the picker.
3. Load that space's saved references and use its configured preferred method. When the preferred method is unconfigured or cannot be prepared, retain usable anchor alignment and surface recovery status rather than creating another space. Keep active method and saved preference distinct.
4. If a usable probe finds no match, automatically create one draft space and select/create its baseline map. Shared spatial anchors are the default only when sharing is available; confirm an upload before committing the initial reference, including standalone. If sharing is unsupported or denied (for example on a managed headset without a Meta account), activate registered AprilTags and open the existing first-tag registration flow. Persist that setup intent and grant one tracked headset permission to initialize tags independently of the shared-anchor minter. No manual New space command is required.
5. Reuse the same draft while creation/save/sharing work retries or AprilTag setup is interrupted. A definitive sharing denial retargets only an unfinished automatic draft; it never replaces an established reference frame. Unknown runtime capability or a canceled/inconclusive probe shows progress/recovery and retries; it must not manufacture a fresh durable space each time. If a previous space is subsequently rediscovered, reconcile through section 8 when the relationship is verified.

Session selection wins over startup. Cancel outstanding probing/bootstrap work when a session or explicit selection takes control. An empty, uncommitted draft can be discarded or adopted; late native results cannot attach anchors to the selected session space. This prevents startup/join races from leaving unnecessary spaces behind.

Rename `ProbeAllMaps` and [MapProbeBinder.cs](</Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Interface/Main Menu/Game/MapProbeBinder.cs>) to expose space probing. Capture operation/catalog identities before awaiting so stale results cannot replace an active selection. A manual refresh rechecks presence; it does not create another space as a side effect.

Preserve separate startup paths: XR simulation restores the last used space/map pair without a native probe; [OperatorHost.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Operator/OperatorHost.cs:139) restores a supported space/map without pretending the PC can determine physical presence. The operator can explicitly create/manage spaces. Real headset probing remains distinct from network-session discovery, while both coordinate their selection through one lifecycle owner.

**7. Synchronize the space and active layout as one committed context**

[MapSessionSync.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Maps/MapSessionSync.cs) currently puts the preferred method in `MapIdentity` and relies on provider synchronization for references. Move space identity/configuration into `MapSpaceSessionSync`, while retaining map identity and placements as their own content stream.

Define a committed session selection containing the advertised space ID, `canonicalFrameId`, space/reference revisions, active method/operation context, active map ID/revision, and a transition generation. Stage relevant provider/configuration and object-list writes before publishing the selection that commits them. Late joiners and existing clients must never combine a new layout with the previous space's references. Continue accepting a complete empty object list independently of NGO spawn timing. Resolve advertised IDs through local associations before catalog adoption.

For same-space map updates, adopt only the layout while preserving local references and the active solver. For a different space/frame, stage references and layout together and resolve the space offset before permanent adoption. Save-before-adoption, retries, conflict preservation, and stale-disconnect rejection continue to apply to the coherent context. If reconciliation lacks evidence, keep a session snapshot keyed by remote identity without changing the local offset or adding a new normal catalog entry on each join.

Preserve current adoption scope: joiners save the active map plus the space configuration and their own realizations; other locally known maps in that space remain intact. Save the incoming active child as a separate map file, then add its ID to the local space membership list through the recoverable adoption operation. Missing maps in a partial update are not deletions, and the host's other layouts are not downloaded until they become active. This keeps space membership meaningful without requiring every device to have the same catalog.

Session map/reference DTOs carry poses in the committed canonical frame. On publishing, project the active stored map through `canonicalFromStorage`; on permanent adoption, convert incoming poses through its inverse. This walks only the incoming/exported content, never every cached map during a rebase. Native spawned scene objects and edit requests already use canonical/world coordinates, so do not transform those network poses again. Extend `MapSessionSync.Publish`'s map-ID/version early-out to include canonical frame/context: unchanged authored content can need a new projected payload after the frame changes.

Authority handoff keeps the committed session space and canonical frame. A newly promoted authority must not revert to its former offline frame. Network/session operation generations still change independently, invalidating grants and stale writes without requiring another layout rebase.

Add space/frame/operation context to reference-authoring requests and rejections, including tag registration/removal/size requests. The tag provider's current dictionary requests carry only a tag ID/pose, which is insufficient to distinguish a delayed request for a previous space using the same tag ID. Keep generic providers independent of `GameMap` by accepting an opaque reference-context token from the embedding layer. Preserve the anchor provider's existing assignment/context generation checks.

Keep map identity in [MapObjectDirector.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Maps/MapObjectDirector.cs) requests and add the committed operation/frame context where needed to reject stale edits, including switching away and back to the same map. Update readiness, HUD/connection progress, and gameplay gates without treating a map-only transition as loss of physical alignment.

Bump the actual NGO protocol version in [Networking.prefab](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Networking.prefab:53), currently 7. This changes several binary payloads; a display/build-number change alone does not establish network compatibility. Verify both LAN distributed authority and the relay path.

**8. Reconcile spaces using one persisted rigid offset**

Recommendation: when client A joins host B, associate B's advertised space with an appropriate existing local entry, verify the coordinate relationship, and update that entry's `canonicalFromStorage` and canonical-frame metadata. Existing saved object and reference poses remain unchanged in storage coordinates. Their projected physical locations should remain the same within measured alignment accuracy. Reconciliation must solve both identity and geometry: an offset without a saved association would still produce duplicate spaces later.

The earlier proposal would have walked every object in every saved map and rewritten its pose. A single space offset avoids that work and makes reversal simpler. The current code already provides concentrated boundaries in [MapObjectDirector.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Maps/MapObjectDirector.cs), [MapColocationAdapter.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Maps/MapColocationAdapter.cs), and [MapSessionSync.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Maps/MapSessionSync.cs). Keep gameplay, physics, generic providers, and networked transforms in canonical/world coordinates and adapt these document boundaries.

| Boundary | Conversion and implementation |
| --- | --- |
| Saved layout to scene | `MapObjectDirector.Replace`: project each loaded pose through the offset when instantiating. Inactive maps are untouched. |
| Scene to saved layout | `MapObjectDirector.Capture`: use the inverse offset. Preserve unresolved-prefab entries in storage coordinates. Placing/moving live objects and their network requests continue to use world coordinates. |
| Saved references to/from providers | `MapSpaceColocationAdapter.Inject`, local-anchor restoration, and all snapshot paths: project stored targets on injection; inverse-project newly captured or imported targets. UUIDs, tag IDs, and physical tag sizes do not transform. |
| Stored map to/from session DTO | Project on export and inverse-project on adoption. Label wire payloads with the canonical frame/operation; do not interpret a remote device's storage coordinates using this device's offset. |

Use the same `MapSpaceFrame` helper in these paths and in debug visualizations. This does not require parenting all networked objects under a scene root or transforming every object each frame. Activating a changed offset still updates the currently loaded scene and reference targets; loading, capturing, and exporting naturally visit the active map's objects. A rebase itself does not visit poses in inactive maps.

Add a `MapSpaceReconciler` beside the document/store classes. It resolves associations and verified offsets and produces an atomic space-metadata update plus a recoverable multi-file adoption operation when map files also change. The coordinator owns observations and activation. Keep provider-level fitting and anchor observation independent of map classes.

**Resolve the local entry before changing coordinates.** Reuse an existing foreign-space association first. Recognize an already adopted canonical frame and its recorded reconciliation lineage without creating another entry or estimating a new transform. Otherwise, the proposed default is to use the currently active local space once the relationship is verified. If there is no unambiguous candidate, defer association or request a choice. Localization in the same room alone does not prove two intentionally distinct spaces should be combined; other local matches remain separate. Frame compatibility alone is not a blanket instruction to merge catalog membership. Check the map-ID index before importing a child so a known map cannot acquire two parents.

**Measure the relationship using persistent references.** Localize A's and B's saved reference sets on the joining headset and fit both in the same raw tracking coordinates and tracking generation. Evaluate A's targets using its current offset. They do not need identical anchor UUIDs: independently locating each set in one tracking frame supplies the relationship. Do not derive it merely from the rig's before/after transforms, a host pose, or provisional alignment.

```text
delta = T_hostFromTracking * inverse(T_currentCanonicalFromTracking)
newCanonicalFromStorage = delta * previousCanonicalFromStorage
```

Use the project's gravity-preserving translation/yaw convention, with no scaling. Require adequate geometry, stable fits, and acceptable residuals. Reject inconsistent fits and observations separated by tracking-origin/recenter changes. Persistent references may be observed independently of the active provider; a provisional provider driving play is not itself evidence for the conversion. If either frame has no verifiable reference basis and there is no previously established conversion, do not guess how existing physical layouts relate.

**Commit offset and identity together.** Adopt B's `canonicalFrameId`, retain the local storage frame/catalog ID, and record the new offset and foreign identity association. Existing maps, reference targets, and conflict baselines remain numerically unchanged in storage coordinates. Convert only incoming host map/reference data through the new inverse offset when inserting it into this local document. Native anchor UUIDs and physical anchors do not change or require reminting.

Capture outstanding local edits using the old offset before committing the new one; pause conflicting captures and invalidate old operation contexts. Keep map IDs, authored content revisions, and local dirty/fork state. Track the offset/frame change separately, and publish/swap live state only after the save succeeds. Backups and an idempotent operation ID prevent failed retries from composing the delta twice. Pure rebasing saves only the MapSpace JSON, including offset/frame/association metadata; it neither loads nor rewrites inactive GameMap files. Incoming map files are saved separately under the ordered adoption/recovery rules in section 2.

**Keep rebasing separate from reference adoption.** A rebase changes only coordinate mapping and associated frame metadata. It does not replace, delete, or re-register the client's alignment data. Joining also imports the host's published definitions in a separate source-aware adoption step, expressed in local storage coordinates through the inverse offset. The host's definitions and active method govern the session; the client's existing references remain saved for future probing/localization and may be used where compatible with that frame.

Retain provenance for locally authored/realized references and each imported host definition. Change the adapter's current snapshot/clear behavior so replacing the host's subset cannot erase the client's saved subset. Conflicting tag IDs, sizes, or UUID target definitions remain separate; use the host definition for the session and retain the conflicting local one inactive for resolution. Do not silently average them or publish private client references into the host's authoritative set. Explicit reference editing/removal remains a separate operation. A rebase alone changes no reference membership.

Neither a display-name difference nor a method-preference difference creates another space. Keep the local catalog name and startup preference separate from the session's displayed name/active method where appropriate.

**Avoid repeated estimation.** On the next join to the same canonical frame, the offset is already correct. Later hosting projects this device's stored data into the adopted basis. Reuse recorded offsets/transforms between immutable, verified frame identities when needed, avoiding accumulated estimation error and unnecessary import/export churn for unchanged revisions. Do not infer equivalence merely because IDs or names look similar. If new evidence contradicts a remembered relationship, stop permanent reconciliation and surface the conflict. A genuinely new canonical frame requires a verified relationship again.

**Undo through the same frame transition.** Record the previous offset and target-frame metadata. Undoing the latest rebase restores that pair, without rewriting maps. Setting the offset to identity returns to the original storage basis; it is not necessarily the inverse of the latest rebase after several changes. Restore matching frame context and reactivate alignment, scene poses, and scan lifecycle together. An individual client cannot reset its offset while continuing to claim it uses the host's unchanged frame. Undoing an offset does not undo intervening layout edits, imports, or reference edits.

**Defer safely when evidence is missing.** Keep incoming space/map data in a retryable session cache keyed by its remote identity. Do not change the local offset or append a new ordinary catalog space on each visit. Session play can proceed under the selected method's readiness rules; permanent map adoption waits for reconciliation. On disconnect, retain the cache and recover the last complete committed local context. If there is no existing local candidate at all, adoption may create one entry once. An empty draft with no dependent geometry/references can be reused with the incoming frame as its storage basis and identity offset.

The live voxel grid remains in runtime canonical/world coordinates in this design. Clear it when that frame changes, including a rebase or undo. A storage offset does not by itself move existing voxel indices into a new grid; retaining such scans across a rebase would require additional scanner/mesher work. Ordinary map changes keep the scan.

**9. Bind the live scan to the active space and effective frame**

[EnvMeshSync.OnWorldFrameRebased](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/EnvSyncing/EnvMeshSync.cs:161) already clears the scanner and invalidates async encoding/decoding. Call this behavior for space entry/exit, canonical rebases, and provisional frame discontinuities, not ordinary map changes. Give a small project-layer environment binding explicit responsibility for activating, pausing, and clearing the space's scan.

Keep [EnvScanner.cs](/Users/jt/source/lasertag/Assets/Anaglyph/XR/DepthKit/EnvScanning/EnvScanner.cs) and [EnvMesher.cs](/Users/jt/source/lasertag/Assets/Anaglyph/XR/DepthKit/EnvScanning/EnvMesher.cs) reusable; they need lifecycle/pause/generation hooks, not dependencies on the serializable map types. Pause depth integration as well as meshing and outgoing publication while the active frame is unresolved or tracking is lost. The present gate disables the mesher, which alone does not stop the scanner accumulating samples.

On space exit, clear GPU scan state, chunk meshes/colliders, pending readbacks/meshing/Draco work, and [EnvNavMesher.cs](/Users/jt/source/lasertag/Assets/Anaglyph/XR/DepthKit/EnvScanning/EnvNavMesher.cs) data. On a same-space map change, preserve them. On temporary tracking loss or a verified reference-based handoff, retain geometry and resume integration after recovery.

Entering/leaving provisional alignment, replacing/resetting its pair or origin, or materially changing its effective frame clears the scan and rotates its runtime token. A provisional scan may support play while that frame is usable, but cannot be mixed into a later reference-aligned scan. Ordinary small tracking corrections and observation refinement do not themselves require a reset. Scope synchronized geometry to the committed method/activation as well as the saved canonical frame; the latter remains unchanged while provisional alignment is active. System determined alone cannot prove peers' scans share a physical frame, so merged scanning must not assume that guarantee.

Any scan collected before a space/frame was assigned is only a preview. Do not attach it to a newly initialized or adopted space unless frame compatibility is established; otherwise clear it when committing that frame.

Extend the mesh packet header in [EnvMeshSync.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/EnvSyncing/EnvMeshSync.cs:37) with space/frame and a synchronized scan activation/reset token, in addition to chunk index and revision. The current process-local generation protects a decode already running; it cannot identify an old-space packet that first arrives after the switch. Check context before decoding and again before applying it. Switching A → B → A must not revive geometry from A's previous visit.

Keep session transport generations separate from physical-frame identity. Define initial/resend synchronization for a joining peer so an unchanged, already scanned chunk can be received without requiring someone to rescan it. Bound this transfer and scope it to the active scan token. Received geometry should notify collision/navigation consumers consistently, including empty-chunk updates. Preserve existing meshing thresholds and compression behavior.

**10. Present spaces and maps separately in both interfaces**

Use “Space” as the user-facing noun and `MapSpace` in code. A recommended flow is:

```text
Game
  Spaces: matching, unknown, and other saved spaces
    Space details: name, alignment, maps
      Map: select, create, duplicate, rename, edit layout, delete
      Space alignment: active method, saved preference, setup, tags/pair
```

The space chooser's selected row is a selection, not the active authoring target. Show the active space and map explicitly. Headsets offer “New map in this space” and optional space settings, with no manual New space button; automatic discovery/setup handles the first-run path. The operator retains New space. Reference removal, tag size, and method setup must clearly say they affect every map in that space. A space with no maps still exposes map creation and optional alignment configuration.

Use “Reference-based” and “Provisional” in method details where the distinction matters. Explain reference-authoring blockers at the attempted action, for example “Align using saved tags or anchors before adding references to this space.” Do not turn normal automatic startup or verified reconciliation into a mandatory setup/confirmation flow. Provide useful status for discovery, anchor initialization, waiting for reference alignment, and unresolved reconciliation.

| Files | Planned change |
| --- | --- |
| [GameMenu.cs](</Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Interface/Main Menu/Game/GameMenu.cs>), [GameMenu.uxml](</Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Interface/Main Menu/Game/GameMenu.uxml>), `GameHomePage.uxml`, `GameMapsPage.uxml` | Add space navigation and automatic setup status; show maps within the selected/active space. Omit manual headset space creation. |
| [MapManagerUI.cs](</Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Interface/Main Menu/Game/MapManagerUI.cs>), [MapPickerBinder.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Interface/Shared/Game/MapPickerBinder.cs), `MapCatalog.uxml` | Separate a shared space picker from a map picker. Apply physical presence to spaces, not each child map. Preserve independent selections and binding lifetimes. |
| [GameMapEditingPage.uxml](</Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Interface/Main Menu/Game/GameMapEditingPage.uxml>), [GameMapAlignmentPage.uxml](</Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Interface/Main Menu/Game/GameMapAlignmentPage.uxml>), [MapEditingMenuBinder.cs](</Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Interface/Main Menu/Game/MapEditingMenuBinder.cs>) | Keep map name/layout editing separate from space alignment. Make alignment reachable without requiring a map. |
| [AlignmentMethodBinder.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Interface/Shared/Game/AlignmentMethodBinder.cs), [TagConfigurationBinder.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Interface/Shared/Game/TagConfigurationBinder.cs), their UXML fragments | Bind to `CurrentSpace`. Preserve distinct preferred/active/setup state and coordinator-provided blockers. Add pair selection/reacquisition status, alignment category, and space-wide scope copy. |
| [OperatorMenu.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Operator/OperatorMenu.cs), [OperatorMapsPage.uxml](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Operator/OperatorMapsPage.uxml), [OperatorMapSettingsPage.uxml](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Operator/OperatorMapSettingsPage.uxml), `OperatorMenu.uxml`, `OperatorHost.cs` | Reuse space/map catalog and configuration sections; keep operator layout and startup policy separate. Do not show native probing or physical tag tools on the PC. |
| [MapEditorPalette.uxml](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/MapEditor/MapEditorPalette.uxml), [PaletteMenu.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/MapEditor/PaletteMenu.cs), `MapEditorTagsPage.uxml`, `MapEditorMeasureTagSizePage.uxml` | Label object tools with the map and alignment tools with the space; show the saved two-tag pair and its setup state. |
| [MapEditorTool.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/MapEditor/Tools/MapEditorTool.cs), [MapEditor.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/MapEditor/MapEditor.cs) | Scope measurement/registration to space/frame/reference context. Scope object manipulation to the map. Invalidate pending measurements and delayed size edits on space/frame changes; use IDs, not detached-snapshot object equality. |
| [HUD.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Interface/HUD/HUD.cs), [MapDebugVisuals.cs](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Maps/MapDebugVisuals.cs), connection/telemetry consumers | Read reference state from the space and distinguish loading a layout from aligning to a space. |

Continue using shared sections with page-scoped queries and explicit bind/unbind. Update GameMenu, OperatorMenu, HUDMenu, and PaletteMenu localization entries for actual changes in meaning; keep existing keys where the meaning stays the same. Gate/Connection/Settings/Game ownership remains as documented: spatial catalog/settings belong to Game; network connection and its alignment progress belong to Connection.

Duplicating a map within a space copies only the layout and creates a new map ID. Copying a layout into another unrelated space uses the destination's coordinates, with placement/repositioning made clear; it does not copy alignment data. Whole-space reconciliation during joining follows section 8 and preserves physical placement using the verified transform. A general manual tool for combining arbitrary space catalogs is not required for that workflow.

**11. Implement in reviewable stages**

1. **Documents and persistence:** introduce separate space/map JSON stores, ID-based membership and pruning, recoverable multi-file operations, stable storage basis, rigid offset/helper, child-map ownership, independent revisions, identity associations, fresh catalog, CRUD, and conflict behavior. Add document/store and frame-conversion tests.
2. **Space and map lifecycle:** add active-space state, split transitions/events, adapt workflow/policy, project layouts and provider references at document boundaries, and preserve same-space alignment. Keep the existing scene coordinator GUID and prefab references.
3. **Alignment policy and transitions:** distinguish reference-based and provisional evidence, implement the shared setup/switch state machine with retained source observations and one rig writer, keep two-tag poses transient, and move minter/readiness/request validation to space/frame context. Validate initialization, recovery, handoff continuity, and stale-operation behavior.
4. **Discovery and automatic setup:** add explicit probe outcomes, readiness-based startup, MRU selection, capability-aware automatic creation, and cancellation when session selection wins. Keep the operator/simulation paths distinct.
5. **Session reconciliation:** stage coherent space/map adoption, verify frame relationships, atomically update offsets and identity associations, preserve client references separately from host imports, and support offset undo. Normalize incoming/exported content, defer unresolved adoption without duplicate entries, and update request payloads/readiness and protocol version.
6. **Scan and UI integration:** add effective-frame/scan tokens and late-join mesh transfer; verify A → B → A isolation. Split space/map catalogs and settings across headset/operator/palette, remove manual headset creation, and update localization and documentation.
7. **Integrated verification:** complete Unity Edit Mode/composed-UI checks, then real headset and operator sessions. Intermediate stages are not a playable release until their dependent changes are integrated.

Use the current working-tree versions as the baseline. There are substantial existing edits in the coordinator, providers, player readiness, menus, and tests; preserve those changes rather than rebuilding against an older committed snapshot.

**12. Acceptance and regression coverage**

Extend the existing tests under [Tests/Editor](/Users/jt/source/lasertag/Assets/Anaglyph/LaserTag/Tests/Editor), especially `MapPersistenceTests`, `MapWorkflowTests`, `ColocationMethodHandoffTests`, `AnchorMinterTests`, `TagAnchorRecoveryTests`, `SystemDeterminedColocationTests`, `TwoAprilTagColocationTests`, `PlayerPresenceTests`, `MapMenuCompositionTests`, `MenuBindingLifetimeTests`, and `TagSizeRulerTests`. Add focused space-discovery and mesh-context tests where no equivalent currently exists.

| Scenario | Required result |
| --- | --- |
| Create/switch/delete maps in one space | References, provider selection, trusted frame, and live scan survive; only the intended layout changes. |
| Fresh headset with sharing access and no matching space | Automatically creates one space, saves and shares spatial anchors, and provides a usable map without manual space setup. |
| Managed headset without sharing access and no matching space | Automatically creates/reuses one draft, activates registered AprilTags and opens first-tag registration without requiring a cloud-anchor minter. |
| Unknown capability or inconclusive probe | Waits/retries without creating another space; confirmed lack of sharing does not disable local anchor probing. |
| Probe unavailable, canceled, or inconclusive; native initialization/save retries | Status/retry preserves one pending operation; no repeated durable-space creation. |
| Session join races with startup probe or anchor creation | Session selection wins; late results cannot overwrite it or create orphan references. |
| Blank layout in a space with reference-based data | Cannot authorize new references from an unaligned or provisional headset. |
| Layout objects but no reference-based data | A valid provisional source may seed the first persistent reference. |
| Add anchors to a tag space or tags to an anchor space | The actual authoring headset has current reference-based alignment to the same frame. |
| Two clients try to initialize a space simultaneously | One initialization commits; stale or competing proposals cannot add references in a different frame. |
| Two-tag pair first observed poorly, reset/reacquired, or sees unrelated lower IDs | No permanent canonical poses are written; transient fit can recover and the chosen IDs remain stable. |
| Provisional method selected while saved tags/anchors exist | Selection/play allowed; persistent reference authoring and automatic minting blocked; saved references survive. |
| Return from provisional to reference-based alignment | Saved reference frame is recovered; incompatible scan is cleared. |
| Method setup interrupted by tracking loss, recenter, timeout, space change, or minter/authority loss | No stale result is committed; prior usable state remains recoverable. |
| Setup-only, setup-and-switch, or switching configured reference-based methods | Source continues live alignment through preparation/validation; setup-only retains it, while switching transfers one rig-writing role after validation. |
| Target fit disagrees, or its observation session is canceled | No target-driven rig movement or offset rebase; source resources and committed references survive. |
| Adding references to the currently active method | Staged references cannot contaminate their own validating source fit; commit only after fresh validation. |
| Source unavailable, target already configured | Recovery can select/localize the target without inventing new references or requiring the unavailable source to align first. |
| Different peers complete handoff at different times | Each reports its actual source/phase and frame readiness; retained-source peers do not falsely claim target activation. |
| Several spaces localize in one room | All remain candidates with independent scores and maps. |
| No probeable anchors or no conclusive native answer | Space remains unknown rather than being silently classified elsewhere. |
| Late join or delayed provider/object data | Only a coherent committed space/frame/map is adopted; empty layouts are valid. |
| A joins B with separately created spaces and both reference frames observable | One offset projects all A layouts/reference targets into B's basis while preserving physical placement; existing stored poses stay unchanged. |
| Loading, editing, or importing a map under a nonidentity offset | Pose composition/inversion round-trips correctly; imported content shares the stable storage basis; unresolved prefab placements survive. |
| Provider injection/capture and host-reference import under a nonidentity offset | Targets convert exactly once in each direction; UUIDs, tag IDs, sizes, and existing local saved definitions remain intact. |
| Pure rebase with no reference import | Only the space JSON changes; every GameMap file, reference membership, and stored target pose remains unchanged. |
| Missing, invalid, or duplicate map references | Recover backups first, prune bad membership, and save the space; retain unrelated map files and space alignment. |
| Transient map-read failure or incomplete adoption | Retry/recover before pruning; do not discard valid membership or local edits. |
| Failure between map-file and space-file writes | Recover the staged operation or preserve a retryable orphan; no partial runtime adoption or unintended reparenting. |
| Undo one of several rebases, or return to the original basis | Restore the previous offset/frame pair or the original identity basis deliberately; no saved-map rewrite and no stale session/scan context. |
| Canonical frame changes with unchanged map ID/content revision | Publish the newly projected payload; the content-version early-out cannot suppress it. |
| Repeated joins, host alternation, or already shared canonical frame | No duplicate space and no fresh estimated rebase for a known unchanged basis. |
| Incoming identity is known but its canonical frame changed | Update the offset only with a verified relationship; otherwise defer without changing local data. |
| Several local spaces are present when joining an unknown host | Resolve the intended candidate; other spaces remain distinct despite physical overlap. |
| Frame evidence is missing, provisional-only, inconsistent, or interrupted by recenter | Permanent reconciliation waits; cached session data remains retryable and local data intact. |
| Rebase save fails/crashes/retries | Atomic old-or-new offset/context; no double composition, lost dirty state, or spurious layout fork. |
| Reconciled local anchors differ from host references | Local definitions remain saved; compatible references project into the host frame, and conflicts remain separate with host definitions active for the session. |
| Space/map conflicts or failed writes | Local work survives; retry does not duplicate forks; no premature anchor erasure. |
| A → B → A with delayed packets/readbacks/Draco work | No mesh or reference operation from the prior visit applies to the new active context. |
| Delete a map, then delete a space sharing an anchor with a preserved fork | Map deletion never erases it; space deletion erases only when the last persisted reference is gone. |
| Open/rebind menus, change locale, switch space while editing a size | Correct active target and copy; stale UI work cannot mutate the new space. |

In Unity, import and clone the actual composed UXML documents, check named/type contracts, and inspect the resolved runtime-theme layouts in headset and operator compositions. Run the relevant Edit Mode assembly through Unity, not standalone `dotnet build` or generated project compilation.

Device validation needs at least two Quests and a PC operator: automatic fresh/reloaded startup, same-space map changes, cross-space changes, every method handoff, independent space creation followed by repeated joins in both hosting directions, simultaneous initialization, minter replacement, sleep/wake/recenter, and late joining a stable scan. Exercise LAN and relay separately. Measure physical position/yaw continuity at multiple points in the room before choosing handoff/reconciliation tolerances. Editor tests do not prove these device behaviors.

The proposed defaults for review are: an active space may have no map; deleting its last map keeps the space; full alignment reset remains explicit; provisional alignment permits layout editing; and a new host association uses the verified active local candidate. Physical tolerances need hardware measurements. No runtime behavior has been changed or claimed validated by this planning task.
