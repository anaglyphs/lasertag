## Spaces and maps

A **space** is one physical room: its alignment settings, its references (shared anchors,
registered AprilTags) and its maps, saved together as one JSON document under
`Application.persistentDataPath/spaces-v3`. A **map** is one layout of that room. Poses are in
the space's frame; the frame only changes when it can move (entering or leaving two-tag mode,
resetting alignment), and the maps keep their poses through that. The previous `map-catalog-v2`
catalog is imported once on first launch and left in place. See
[Spaces and maps: implementation](../../../readme/map-spaces.md).

Headsets probe saved spaces on startup: shared anchors first, then the visual map. A matching
space loads automatically, the most recently used one when several match. A healthy probe with
no matches reuses a blank space or makes one with a starter map. The operator loads its most
recent space and can create spaces. Editor simulation restores the last space.

Changing maps within a space keeps alignment and the live environment scan. Leaving the space
clears the scan.

Space alignment offers **Shared spatial anchors**, **AprilTags** and **Visual** (the default).
AprilTags without registered tags runs in two-tag mode: the first two tags seen define a
provisional frame (the lower id is the origin, the higher one forward, gravity up), nothing is
saved, and the space gets a new frame on the way in and out. Both tags must be seen again after
recentering, sleep or tracking loss; space settings can choose another pair. Registered tags and
shared anchors are saved references: the space's frame is the same from one session to the next.

In a session the host's space is the session's. Clients see it, never save it, and ask the host
for changes; the host checks each request against the asking headset's readiness. A PC operator
hosts without a rig: it assigns one ready headset to mint anchors, and its three-step AprilTag
setup (print, connect a headset, review) has that headset measure the printed size and register
the tags on the operator's behalf.
