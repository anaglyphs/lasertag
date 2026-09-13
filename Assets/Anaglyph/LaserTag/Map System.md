## Spaces and maps

A `MapSpace` owns a physical coordinate frame, shared anchor and AprilTag definitions,
alignment preferences, and the IDs of its maps. `GameMap` files hold gameplay layouts.
Both are separate JSON documents under `Application.persistentDataPath/map-catalog-v2`;
legacy maps are not migrated or loaded. See [MapSpace architecture and verification](readme/map-spaces.md).

Headsets probe saved spaces on startup. A matching space loads automatically, choosing the
most recently used match when several localize. A healthy probe with no matches creates
one space and a starter map. Shared spatial anchors are the default when supported;
headsets without sharing access, including managed devices without a Meta account, activate
AprilTags and open tag registration. Local saved anchors can still identify an existing
space on these devices. Unknown capabilities and interrupted or inconclusive probes retry
without creating another space. The operator can create spaces manually.
Editor simulation restores the saved space's alignment method and references. Only a new
simulation space starts with System determined, saved as its preference as well.

Changing maps within a space preserves alignment and the live environment scan. Leaving
the space clears the scan. A verified session reconciliation changes one saved space offset;
it does not rewrite every object or any inactive map file. All saved object and reference
target poses pass through that offset at load/save boundaries. Client reference definitions
and private tag-anchor UUIDs remain saved separately from the host's active definitions.

Space alignment offers **Shared spatial anchors**, **AprilTags**, **System determined**, and
**Two AprilTags**. Saved anchors and registered tags are reference-based methods. Two tags
and System determined are provisional: gameplay editing is allowed, but they cannot authorize
new permanent references when the space already has saved reference-based alignment.
A PC operator assigns a ready headset to mint anchors; the PC retains session authority.
Accepted AprilTag registrations are saved by the authority before they appear as registered.
They survive an interrupted method switch; the previous alignment stays active until the
saved tag target has a stable fit and completes its handoff.

Two AprilTags saves a chosen pair of IDs and the printed tag size, never their first observed
canonical poses. The lower tag defines the origin, the higher defines forward, and gravity
defines up. Tags need at least 10 cm of horizontal separation. No anchors are used: each
headset remembers the observed positions only for its current tracking frame. Scan both
tags again after recentering or losing tracking/alignment, including sleep or focus loss.
Tags can be scanned individually. Space settings can choose another pair; ordinary map
switches keep the pair and its observations.

System determined places the XR origin at identity and is useful for simulation or an
external common-origin system. It does not verify a shared physical frame, so its environment
meshes are not merged across devices.

Reference-based setup and method switching retain the old live alignment source while a
separate observer validates the candidate. Setup-only keeps that source; switching transfers
rig control after validation and persistence. Each peer completes its own handoff. Interrupted
setup can resume its saved intent, but always gathers fresh evidence.